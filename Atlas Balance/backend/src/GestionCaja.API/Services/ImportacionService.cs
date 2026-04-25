using System.Globalization;
using System.Text;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GestionCaja.API.Services;

public interface IImportacionService
{
    Task<ImportacionContextoResponse> GetContextoAsync(Guid usuarioId, string rol, CancellationToken cancellationToken);
    Task<ImportacionValidarResponse> ValidarAsync(Guid usuarioId, string rol, ImportacionValidarRequest request, CancellationToken cancellationToken);
    Task<ImportacionConfirmarResponse> ConfirmarAsync(Guid usuarioId, string rol, ImportacionConfirmarRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ImportacionPlazoFijoMovimientoResponse> RegistrarMovimientoPlazoFijoAsync(Guid usuarioId, string rol, ImportacionPlazoFijoMovimientoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
}

public sealed class ImportacionService : IImportacionService
{
    private const int MaxRawDataLength = 5 * 1024 * 1024;
    private const int MaxRows = 50_000;

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy",
        "d/M/yyyy",
        "yyyy-MM-dd",
        "yyyy-M-d",
        "dd-MM-yyyy",
        "d-M-yyyy"
    ];

    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public ImportacionService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<ImportacionContextoResponse> GetContextoAsync(Guid usuarioId, string rol, CancellationToken cancellationToken)
    {
        var isAdmin = string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase);
        IQueryable<Cuenta> baseQuery = _dbContext.Cuentas.AsNoTracking().Where(c => c.Activa);

        if (!isAdmin)
        {
            var permisosImportacion = await _dbContext.PermisosUsuario
                .AsNoTracking()
                .Where(p => p.UsuarioId == usuarioId && p.PuedeImportar)
                .ToListAsync(cancellationToken);

            if (permisosImportacion.Count == 0)
            {
                return new ImportacionContextoResponse { Cuentas = [] };
            }

            var hasGlobal = permisosImportacion.Any(p => p.CuentaId is null && p.TitularId is null);
            if (!hasGlobal)
            {
                var permittedCuentaIds = permisosImportacion
                    .Where(p => p.CuentaId.HasValue)
                    .Select(p => p.CuentaId!.Value)
                    .Distinct()
                    .ToList();

                var permittedTitularIds = permisosImportacion
                    .Where(p => !p.CuentaId.HasValue && p.TitularId.HasValue)
                    .Select(p => p.TitularId!.Value)
                    .Distinct()
                    .ToList();

                baseQuery = baseQuery.Where(c =>
                    permittedCuentaIds.Contains(c.Id) ||
                    permittedTitularIds.Contains(c.TitularId));
            }
        }

        var cuentas = await baseQuery
            .Join(_dbContext.Titulares.AsNoTracking(),
                cuenta => cuenta.TitularId,
                titular => titular.Id,
                (cuenta, titular) => new { cuenta, titular })
            .OrderBy(x => x.titular.Nombre)
            .ThenBy(x => x.cuenta.Nombre)
            .Select(x => new
            {
                x.cuenta.Id,
                x.cuenta.Nombre,
                TitularNombre = x.titular.Nombre,
                x.cuenta.Divisa,
                x.cuenta.EsEfectivo,
                TipoCuenta = x.cuenta.TipoCuenta == TipoCuenta.NORMAL && x.cuenta.EsEfectivo
                    ? TipoCuenta.EFECTIVO
                    : x.cuenta.TipoCuenta,
                x.cuenta.FormatoId,
                MapeoJson = x.cuenta.FormatoId == null
                    ? null
                    : _dbContext.FormatosImportacion
                        .Where(f => f.Id == x.cuenta.FormatoId && f.Activo)
                        .Select(f => f.MapeoJson)
                        .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new ImportacionContextoResponse
        {
            Cuentas = cuentas.Select(c => new CuentaImportacionContextoResponse
            {
                Id = c.Id,
                Nombre = c.Nombre,
                TitularNombre = c.TitularNombre,
                Divisa = c.Divisa,
                EsEfectivo = c.EsEfectivo,
                TipoCuenta = c.TipoCuenta.ToString(),
                FormatoId = c.FormatoId,
                FormatoPredefinido = ParseMapeoJson(c.MapeoJson)
            }).ToList()
        };
    }

    public async Task<ImportacionValidarResponse> ValidarAsync(Guid usuarioId, string rol, ImportacionValidarRequest request, CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, requireImportPermission: true, cancellationToken);
        EnsureNotPlazoFijoForFormattedImport(cuenta);

        var normalizedMap = NormalizeMap(request.Mapeo);
        var (rows, separator) = ParseRows(request.RawData, request.Separador);
        var validationRows = ValidateRows(rows, normalizedMap);

        return new ImportacionValidarResponse
        {
            FilasOk = validationRows.Count(r => r.Valida),
            FilasError = validationRows.Count(r => !r.Valida),
            SeparadorDetectado = HumanSeparator(separator),
            Filas = validationRows,
            Errores = validationRows
                .Where(r => !r.Valida)
                .Select(r => new ErrorFilaResponse
                {
                    FilaIndice = r.Indice,
                    Mensajes = r.Errores
                })
                .ToList()
        };
    }

    public async Task<ImportacionConfirmarResponse> ConfirmarAsync(Guid usuarioId, string rol, ImportacionConfirmarRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, requireImportPermission: true, cancellationToken);
        EnsureNotPlazoFijoForFormattedImport(cuenta);
        var normalizedMap = NormalizeMap(request.Mapeo);
        var (rows, separator) = ParseRows(request.RawData, request.Separador);
        var validationRows = ValidateRows(rows, normalizedMap);
        var allowedRowSet = request.FilasAImportar?.ToHashSet() ?? validationRows.Where(r => r.Valida).Select(r => r.Indice).ToHashSet();

        var selectedValidRows = validationRows
            .Where(r => r.Valida && allowedRowSet.Contains(r.Indice))
            .ToList();

        if (selectedValidRows.Count == 0)
        {
            return new ImportacionConfirmarResponse
            {
                FilasProcesadas = validationRows.Count,
                FilasImportadas = 0,
                FilasConError = validationRows.Count(r => !r.Valida),
                Errores = validationRows
                    .Where(r => !r.Valida)
                    .Select(r => new ErrorFilaResponse
                    {
                        FilaIndice = r.Indice,
                        Mensajes = r.Errores
                    })
                    .ToList()
            };
        }

        var now = DateTime.UtcNow;

        var isRelational = _dbContext.Database.IsRelational();
        IDbContextTransaction? tx = null;
        if (isRelational)
        {
            tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockBytes = cuenta.Id.ToByteArray();
            var lockKey = BitConverter.ToInt64(lockBytes, 0) ^ BitConverter.ToInt64(lockBytes, 8);
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], cancellationToken);
        }

        var maxFila = await _dbContext.Extractos
            .IgnoreQueryFilters()
            .Where(e => e.CuentaId == cuenta.Id)
            .Select(e => (int?)e.FilaNumero)
            .MaxAsync(cancellationToken) ?? 0;

        var selectedValidRowsOrdered = selectedValidRows
            .Select(row =>
            {
                var fecha = ParseDate(row.Datos["fecha"], out _, out var parsedDate)
                    ? parsedDate
                    : throw new InvalidOperationException("Fila validada sin fecha parseable.");

                return new
                {
                    Row = row,
                    Fecha = fecha
                };
            })
            .OrderBy(item => item.Fecha)
            .ThenBy(item => item.Row.Indice)
            .ToList();

        var extractos = new List<Extracto>(selectedValidRowsOrdered.Count);
        var extras = new List<ExtractoColumnaExtra>(selectedValidRowsOrdered.Count * Math.Max(1, normalizedMap.ColumnasExtra.Count));

        foreach (var item in selectedValidRowsOrdered)
        {
            var row = item.Row;
            maxFila += 1;

            var monto = TryParseDecimalSmart(row.Datos["monto"], out var parsedMonto)
                ? parsedMonto
                : throw new InvalidOperationException("Fila validada sin monto parseable.");

            var saldo = TryParseDecimalSmart(row.Datos["saldo"], out var parsedSaldo)
                ? parsedSaldo
                : throw new InvalidOperationException("Fila validada sin saldo parseable.");

            var extracto = new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = item.Fecha,
                Concepto = string.IsNullOrWhiteSpace(row.Datos["concepto"]) ? null : row.Datos["concepto"]!.Trim(),
                Monto = monto,
                Saldo = saldo,
                FilaNumero = maxFila,
                UsuarioCreacionId = usuarioId,
                FechaCreacion = now
            };
            extractos.Add(extracto);

            foreach (var pair in row.Datos.Where(d => d.Key.StartsWith("extra:", StringComparison.OrdinalIgnoreCase)))
            {
                var columnName = pair.Key["extra:".Length..];
                extras.Add(new ExtractoColumnaExtra
                {
                    Id = Guid.NewGuid(),
                    ExtractoId = extracto.Id,
                    NombreColumna = columnName,
                    Valor = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value!.Trim()
                });
            }
        }

        _dbContext.Extractos.AddRange(extractos);
        _dbContext.ExtractosColumnasExtra.AddRange(extras);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsFilaNumeroUniqueViolation(ex))
        {
            throw new ImportacionException(
                "Otra importacion o alta manual asigno los mismos numeros de fila. Vuelve a validar e importar.",
                StatusCodes.Status409Conflict);
        }

        var auditDetails = JsonSerializer.Serialize(new
        {
            cuenta_id = cuenta.Id,
            cuenta = cuenta.Nombre,
            separador = HumanSeparator(separator),
            filas_procesadas = validationRows.Count,
            filas_importadas = extractos.Count,
            filas_con_error = validationRows.Count(r => !r.Valida),
            primeras_filas = selectedValidRowsOrdered.Take(5).Select(item => item.Row.Indice).ToArray()
        });
        await _auditService.LogAsync(usuarioId, "importacion_confirmada", "EXTRACTOS", cuenta.Id, httpContext, auditDetails, cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
            await tx.DisposeAsync();
        }

        return new ImportacionConfirmarResponse
        {
            FilasProcesadas = validationRows.Count,
            FilasImportadas = extractos.Count,
            FilasConError = validationRows.Count(r => !r.Valida),
            Errores = validationRows
                .Where(r => !r.Valida)
                .Select(r => new ErrorFilaResponse
                {
                    FilaIndice = r.Indice,
                    Mensajes = r.Errores
                })
                .ToList()
        };
    }

    public async Task<ImportacionPlazoFijoMovimientoResponse> RegistrarMovimientoPlazoFijoAsync(Guid usuarioId, string rol, ImportacionPlazoFijoMovimientoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, requireImportPermission: true, cancellationToken);
        if (ResolveTipoCuenta(cuenta) != TipoCuenta.PLAZO_FIJO)
        {
            throw new ImportacionException("Esta operacion solo aplica a cuentas de plazo fijo", StatusCodes.Status400BadRequest);
        }

        if (request.Monto <= 0)
        {
            throw new ImportacionException("El monto debe ser mayor que cero", StatusCodes.Status400BadRequest);
        }

        if (request.Fecha == default)
        {
            throw new ImportacionException("La fecha es obligatoria", StatusCodes.Status400BadRequest);
        }

        var tipo = NormalizeMovimientoPlazoFijo(request.TipoMovimiento);
        var signedAmount = tipo == "EGRESO" ? -request.Monto : request.Monto;
        var now = DateTime.UtcNow;

        var isRelational = _dbContext.Database.IsRelational();
        IDbContextTransaction? tx = null;
        if (isRelational)
        {
            tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockBytes = cuenta.Id.ToByteArray();
            var lockKey = BitConverter.ToInt64(lockBytes, 0) ^ BitConverter.ToInt64(lockBytes, 8);
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], cancellationToken);
        }

        var latest = await _dbContext.Extractos
            .IgnoreQueryFilters()
            .Where(e => e.CuentaId == cuenta.Id)
            .OrderByDescending(e => e.Fecha)
            .ThenByDescending(e => e.FilaNumero)
            .Select(e => new { e.Saldo })
            .FirstOrDefaultAsync(cancellationToken);

        var maxFila = await _dbContext.Extractos
            .IgnoreQueryFilters()
            .Where(e => e.CuentaId == cuenta.Id)
            .Select(e => (int?)e.FilaNumero)
            .MaxAsync(cancellationToken) ?? 0;

        var saldoAnterior = latest?.Saldo ?? 0m;
        var saldoActual = saldoAnterior + signedAmount;
        var concepto = NormalizeOptionalText(request.Concepto)
            ?? (tipo == "EGRESO" ? "Salida plazo fijo" : "Entrada plazo fijo");

        var extracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = request.Fecha,
            Concepto = concepto,
            Monto = signedAmount,
            Saldo = saldoActual,
            FilaNumero = maxFila + 1,
            UsuarioCreacionId = usuarioId,
            FechaCreacion = now
        };

        _dbContext.Extractos.Add(extracto);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsFilaNumeroUniqueViolation(ex))
        {
            throw new ImportacionException(
                "Otra operacion asigno el mismo numero de fila. Vuelve a intentarlo.",
                StatusCodes.Status409Conflict);
        }

        var auditDetails = JsonSerializer.Serialize(new
        {
            cuenta_id = cuenta.Id,
            cuenta = cuenta.Nombre,
            tipo_movimiento = tipo,
            monto = signedAmount,
            saldo_anterior = saldoAnterior,
            saldo_actual = saldoActual
        });
        await _auditService.LogAsync(usuarioId, "importacion_plazo_fijo_movimiento", "EXTRACTOS", cuenta.Id, httpContext, auditDetails, cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
            await tx.DisposeAsync();
        }

        return new ImportacionPlazoFijoMovimientoResponse
        {
            ExtractoId = extracto.Id,
            FilaNumero = extracto.FilaNumero,
            Monto = Decimal.Round(signedAmount, 2),
            SaldoAnterior = Decimal.Round(saldoAnterior, 2),
            SaldoActual = Decimal.Round(saldoActual, 2)
        };
    }

    private async Task<Cuenta> EnsureCuentaPermitidaAsync(Guid usuarioId, string rol, Guid cuentaId, bool requireImportPermission, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cuentaId && c.Activa, cancellationToken);

        if (cuenta is null)
        {
            throw new ImportacionException("Cuenta no encontrada o inactiva", StatusCodes.Status404NotFound);
        }

        if (string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return cuenta;
        }

        var permisos = await _dbContext.PermisosUsuario
            .AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        var hasPermission = permisos.Any(p =>
            (!requireImportPermission || p.PuedeImportar) &&
            (p.CuentaId is null || p.CuentaId == cuenta.Id) &&
            (p.TitularId is null || p.TitularId == cuenta.TitularId));

        if (!hasPermission)
        {
            throw new ImportacionException("No tienes permisos para importar en esta cuenta", StatusCodes.Status403Forbidden);
        }

        return cuenta;
    }

    private static bool IsFilaNumeroUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(postgresException.ConstraintName, "ix_extractos_cuenta_id_fila_numero", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void EnsureNotPlazoFijoForFormattedImport(Cuenta cuenta)
    {
        if (ResolveTipoCuenta(cuenta) == TipoCuenta.PLAZO_FIJO)
        {
            throw new ImportacionException("Las cuentas de plazo fijo solo permiten anadir o sacar dinero sin formato de importacion", StatusCodes.Status400BadRequest);
        }
    }

    private static TipoCuenta ResolveTipoCuenta(Cuenta cuenta) =>
        cuenta.TipoCuenta == TipoCuenta.NORMAL && cuenta.EsEfectivo
            ? TipoCuenta.EFECTIVO
            : cuenta.TipoCuenta;

    private static string NormalizeMovimientoPlazoFijo(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "INGRESO" or "ENTRADA" or "ADD" or "ANADIR" => "INGRESO",
            "EGRESO" or "SALIDA" or "REMOVE" or "SACAR" => "EGRESO",
            _ => throw new ImportacionException("Tipo de movimiento invalido", StatusCodes.Status400BadRequest)
        };
    }

    private static MapeoColumnasRequest NormalizeMap(MapeoColumnasRequest? map)
    {
        if (map is null)
        {
            throw new ImportacionException("El mapeo de columnas es obligatorio", StatusCodes.Status400BadRequest);
        }

        var tipoMonto = NormalizeTipoMonto(map.TipoMonto);
        var normalized = new MapeoColumnasRequest
        {
            TipoMonto = tipoMonto,
            Fecha = map.Fecha,
            Concepto = map.Concepto,
            Monto = map.Monto,
            Ingreso = map.Ingreso,
            Egreso = map.Egreso,
            Saldo = map.Saldo,
            ColumnasExtra = (map.ColumnasExtra ?? [])
                .Select(extra => new MapeoColumnaExtraRequest
                {
                    Nombre = extra.Nombre?.Trim() ?? string.Empty,
                    Indice = extra.Indice
                })
                .ToList()
        };

        var baseFields = tipoMonto switch
        {
            "dos_columnas" => new[]
            {
                ("fecha", normalized.Fecha),
                ("concepto", normalized.Concepto),
                ("ingreso", RequireColumnIndex(normalized.Ingreso, "ingreso")),
                ("egreso", RequireColumnIndex(normalized.Egreso, "egreso")),
                ("saldo", normalized.Saldo)
            },
            "tres_columnas" => new[]
            {
                ("fecha", normalized.Fecha),
                ("concepto", normalized.Concepto),
                ("ingreso", RequireColumnIndex(normalized.Ingreso, "ingreso")),
                ("egreso", RequireColumnIndex(normalized.Egreso, "egreso")),
                ("monto", RequireColumnIndex(normalized.Monto, "monto")),
                ("saldo", normalized.Saldo)
            },
            _ => new[]
            {
                ("fecha", normalized.Fecha),
                ("concepto", normalized.Concepto),
                ("monto", RequireColumnIndex(normalized.Monto, "monto")),
                ("saldo", normalized.Saldo)
            }
        };

        var usedIndices = new Dictionary<int, string>();
        foreach (var (fieldName, index) in baseFields)
        {
            ValidateColumnIndex(index, fieldName);
            if (!usedIndices.TryAdd(index, fieldName))
            {
                throw new ImportacionException($"Índice de columna duplicado en mapeo ({index + 1})", StatusCodes.Status400BadRequest);
            }
        }

        var extraNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in normalized.ColumnasExtra)
        {
            if (string.IsNullOrWhiteSpace(extra.Nombre))
            {
                throw new ImportacionException("El nombre de columna extra es obligatorio", StatusCodes.Status400BadRequest);
            }

            ValidateColumnIndex(extra.Indice, $"extra:{extra.Nombre}");
            if (!usedIndices.TryAdd(extra.Indice, $"extra:{extra.Nombre}"))
            {
                throw new ImportacionException($"Índice de columna duplicado en mapeo ({extra.Indice + 1})", StatusCodes.Status400BadRequest);
            }

            if (!extraNames.Add(extra.Nombre))
            {
                throw new ImportacionException($"Nombre de columna extra duplicado ({extra.Nombre})", StatusCodes.Status400BadRequest);
            }
        }

        return normalized;
    }

    private static string NormalizeTipoMonto(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "una_columna";
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized is "una_columna" or "dos_columnas" or "tres_columnas")
        {
            return normalized;
        }

        throw new ImportacionException("Tipo de monto invalido", StatusCodes.Status400BadRequest);
    }

    private static int RequireColumnIndex(int? index, string fieldName)
    {
        if (!index.HasValue)
        {
            throw new ImportacionException($"El indice de {fieldName} es obligatorio", StatusCodes.Status400BadRequest);
        }

        return index.Value;
    }

    private static void ValidateColumnIndex(int index, string fieldName)
    {
        if (index < 0)
        {
            throw new ImportacionException($"El índice de {fieldName} debe ser >= 0", StatusCodes.Status400BadRequest);
        }
    }

    private static (IReadOnlyList<string[]> Rows, char Separator) ParseRows(string rawData, string? separatorHint)
    {
        if (string.IsNullOrWhiteSpace(rawData))
        {
            throw new ImportacionException("No hay datos para importar", StatusCodes.Status400BadRequest);
        }

        if (rawData.Length > MaxRawDataLength)
        {
            throw new ImportacionException("El archivo pegado supera el limite de 5 MB", StatusCodes.Status413PayloadTooLarge);
        }

        var lines = rawData
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            throw new ImportacionException("No hay filas válidas para importar", StatusCodes.Status400BadRequest);
        }

        if (lines.Count > MaxRows)
        {
            throw new ImportacionException($"La importacion supera el limite de {MaxRows} filas", StatusCodes.Status413PayloadTooLarge);
        }

        var separator = ParseSeparator(separatorHint, lines);
        var rows = lines.Select(line => ParseDelimitedLine(line, separator).ToArray()).ToList();
        return (rows, separator);
    }

    private static char ParseSeparator(string? separatorHint, IReadOnlyList<string> lines)
    {
        if (!string.IsNullOrWhiteSpace(separatorHint))
        {
            var normalized = separatorHint.Trim().ToLowerInvariant();
            return normalized switch
            {
                "tab" or "\\t" or "t" => '\t',
                "comma" or "," => ',',
                "semicolon" or ";" => ';',
                _ => DetectSeparator(lines)
            };
        }

        return DetectSeparator(lines);
    }

    private static char DetectSeparator(IReadOnlyList<string> lines)
    {
        var sample = lines.Take(5).ToList();
        var candidates = new[] { '\t', ';', ',' };

        var best = candidates
            .Select(c => new
            {
                Separator = c,
                Score = sample.Sum(line => line.Count(ch => ch == c)),
                NonZero = sample.Count(line => line.Contains(c))
            })
            .OrderByDescending(x => x.NonZero)
            .ThenByDescending(x => x.Score)
            .First();

        return best.NonZero == 0 ? '\t' : best.Separator;
    }

    private static List<string> ParseDelimitedLine(string line, char separator)
    {
        var values = new List<string>();
        var sb = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                    continue;
                }

                insideQuotes = !insideQuotes;
                continue;
            }

            if (ch == separator && !insideQuotes)
            {
                values.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        values.Add(sb.ToString().Trim());
        return values;
    }

    private static List<FilaValidacionResponse> ValidateRows(IReadOnlyList<string[]> rows, MapeoColumnasRequest map)
    {
        var validation = new List<FilaValidacionResponse>(rows.Count);
        string? lastValidDateRaw = null;
        string? lastValidSaldoRaw = null;

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var lineNumber = rowIndex + 1;
            var row = rows[rowIndex];
            var errors = new List<string>();
            var warnings = new List<string>();
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            data["fecha"] = GetCell(row, map.Fecha);
            data["concepto"] = GetCell(row, map.Concepto);
            data["saldo"] = GetCell(row, map.Saldo);
            var hasConcept = !string.IsNullOrWhiteSpace(data["concepto"]);
            var allowIncompleteConceptRow = false;

            if (map.TipoMonto is "dos_columnas" or "tres_columnas")
            {
                data["ingreso"] = GetCell(row, map.Ingreso!.Value);
                data["egreso"] = GetCell(row, map.Egreso!.Value);
                allowIncompleteConceptRow =
                    hasConcept &&
                    string.IsNullOrWhiteSpace(data["fecha"]) &&
                    string.IsNullOrWhiteSpace(data["saldo"]) &&
                    IsBlankAmountRow(data["ingreso"], data["egreso"]);

                if (allowIncompleteConceptRow)
                {
                    data["monto"] = "0";
                    warnings.Add("Importe vacio; se importara como 0.");
                    if (map.TipoMonto == "tres_columnas")
                    {
                        data["monto_banco"] = GetCell(row, map.Monto!.Value);
                    }
                }
                else if (TryBuildSignedMonto(data["ingreso"], data["egreso"], errors, out var signedMonto))
                {
                    data["monto"] = signedMonto.ToString(CultureInfo.InvariantCulture);
                    if (map.TipoMonto == "tres_columnas")
                    {
                        data["monto_banco"] = GetCell(row, map.Monto!.Value);
                        ValidateMontoBanco(data["monto_banco"], signedMonto, errors);
                    }
                }
                else
                {
                    data["monto"] = null;
                    if (map.TipoMonto == "tres_columnas")
                    {
                        data["monto_banco"] = GetCell(row, map.Monto!.Value);
                    }
                }
            }
            else
            {
                data["monto"] = GetCell(row, map.Monto!.Value);
                allowIncompleteConceptRow =
                    hasConcept &&
                    string.IsNullOrWhiteSpace(data["fecha"]) &&
                    string.IsNullOrWhiteSpace(data["saldo"]) &&
                    string.IsNullOrWhiteSpace(data["monto"]);

                if (allowIncompleteConceptRow)
                {
                    data["monto"] = "0";
                    warnings.Add("Monto vacio; se importara como 0.");
                }
                else if (!TryParseDecimalSmart(data["monto"], out _))
                {
                    errors.Add(BuildDecimalError("Monto", data["monto"]));
                }
            }

            foreach (var extra in map.ColumnasExtra)
            {
                data[$"extra:{extra.Nombre.Trim()}"] = GetCell(row, extra.Indice);
            }

            if (!ParseDate(data["fecha"], out var dateError, out _))
            {
                if (allowIncompleteConceptRow && lastValidDateRaw is not null)
                {
                    data["fecha"] = lastValidDateRaw;
                    warnings.Add($"Fecha vacia; se usara la fecha anterior ({lastValidDateRaw}).");
                }
                else
                {
                    errors.Add(dateError!);
                }
            }
            else
            {
                lastValidDateRaw = data["fecha"];
            }

            if (!TryParseDecimalSmart(data["saldo"], out _))
            {
                if (allowIncompleteConceptRow && lastValidSaldoRaw is not null)
                {
                    data["saldo"] = lastValidSaldoRaw;
                    warnings.Add($"Saldo vacio; se usara el saldo anterior ({lastValidSaldoRaw}).");
                }
                else
                {
                    errors.Add(BuildDecimalError("Saldo", data["saldo"]));
                }
            }
            else
            {
                lastValidSaldoRaw = data["saldo"];
            }

            validation.Add(new FilaValidacionResponse
            {
                Indice = lineNumber,
                Valida = errors.Count == 0,
                Datos = data,
                Errores = errors,
                Advertencias = warnings
            });
        }

        return validation;
    }

    private static bool IsBlankAmountRow(params string?[] values) =>
        values.All(string.IsNullOrWhiteSpace);

    private static bool TryBuildSignedMonto(string? rawIngreso, string? rawEgreso, List<string> errors, out decimal monto)
    {
        monto = 0m;
        var hasIngreso = !string.IsNullOrWhiteSpace(rawIngreso);
        var hasEgreso = !string.IsNullOrWhiteSpace(rawEgreso);
        decimal ingreso = 0m;
        decimal egreso = 0m;
        var valid = true;

        if (hasIngreso && !TryParseDecimalSmart(rawIngreso, out ingreso))
        {
            errors.Add(BuildDecimalError("Ingreso", rawIngreso));
            valid = false;
        }

        if (hasEgreso && !TryParseDecimalSmart(rawEgreso, out egreso))
        {
            errors.Add(BuildDecimalError("Egreso", rawEgreso));
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        if (ingreso < 0)
        {
            errors.Add("Ingreso debe ser positivo");
            return false;
        }

        egreso = Math.Abs(egreso);

        if (ingreso > 0 && egreso > 0)
        {
            errors.Add("La fila tiene ingreso y egreso a la vez");
            return false;
        }

        if (ingreso == 0 && egreso == 0)
        {
            errors.Add("La fila no tiene importe");
            return false;
        }

        monto = ingreso > 0 ? ingreso : -egreso;
        return true;
    }

    private static void ValidateMontoBanco(string? rawMontoBanco, decimal signedMonto, List<string> errors)
    {
        if (!TryParseDecimalSmart(rawMontoBanco, out var montoBanco))
        {
            errors.Add(BuildDecimalError("Monto", rawMontoBanco));
            return;
        }

        var matchesSigned = montoBanco == signedMonto;
        var matchesAbsolute = montoBanco > 0 && montoBanco == Math.Abs(signedMonto);
        if (!matchesSigned && !matchesAbsolute)
        {
            errors.Add("Monto no coincide con ingreso/egreso");
        }
    }

    private static string? GetCell(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
        {
            return null;
        }

        return row[index];
    }

    private static bool ParseDate(string? raw, out string? error, out DateOnly date)
    {
        error = null;
        date = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Fecha vacía";
            return false;
        }

        var normalized = raw.Trim();

        if (DateOnly.TryParseExact(normalized, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateOnly.TryParse(normalized, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateOnly.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                var dateTime = DateTime.FromOADate(serial);
                date = DateOnly.FromDateTime(dateTime);
                return true;
            }
            catch
            {
                // ignored
            }
        }

        error = "Fecha inválida";
        return false;
    }

    private static bool TryParseDecimalSmart(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);

        var negativeByParentheses = text.StartsWith('(') && text.EndsWith(')');
        if (negativeByParentheses)
        {
            text = text[1..^1];
        }

        text = text.Replace("MX$", string.Empty, StringComparison.Ordinal)
            .Replace("RD$", string.Empty, StringComparison.Ordinal)
            .Replace("\u20AC", string.Empty, StringComparison.Ordinal)
            .Replace("\u00E2\u201A\u00AC", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains(',') && text.Contains('.'))
        {
            var lastComma = text.LastIndexOf(',');
            var lastDot = text.LastIndexOf('.');
            var decimalSep = lastComma > lastDot ? ',' : '.';
            var thousandSep = decimalSep == ',' ? '.' : ',';
            text = text.Replace(thousandSep.ToString(), string.Empty, StringComparison.Ordinal);
            text = text.Replace(decimalSep, '.');
        }
        else if (text.Contains(','))
        {
            text = NormalizeSingleSeparatorNumber(text, ',');
        }
        else if (text.Contains('.'))
        {
            text = NormalizeSingleSeparatorNumber(text, '.');
        }

        var parsed = decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        if (parsed && negativeByParentheses)
        {
            value *= -1;
        }

        return parsed;
    }

    private static string HumanSeparator(char separator) =>
        separator switch
        {
            '\t' => "tab",
            ';' => "semicolon",
            ',' => "comma",
            _ => separator.ToString()
        };

    private static MapeoColumnasRequest? ParseMapeoJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<MapeoColumnasRequest>(rawJson, options);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDecimalError(string fieldLabel, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return $"{fieldLabel} vacío";
        }

        return fieldLabel switch
        {
            "Monto" => "Monto no numerico",
            "Saldo" => "Saldo no numerico",
            "Ingreso" => "Ingreso no numerico",
            "Egreso" => "Egreso no numerico",
            _ => $"{fieldLabel} invalido"
        };
    }

    private static string NormalizeSingleSeparatorNumber(string text, char separator)
    {
        if (IsThousandsGrouped(text, separator))
        {
            return text.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal);
        }

        var parts = text.Split(separator);
        if (parts.Length == 2 && HasDigitsOnlyIgnoringSign(parts[0]) && HasDigitsOnly(parts[1]) && parts[1].Length is 1 or 2 or 3 or 4)
        {
            return string.Concat(parts[0], ".", parts[1]);
        }

        if (parts.Length > 2 && parts[^1].Length is 1 or 2 && HasDigitsOnlyIgnoringSign(parts[0]) && parts.Skip(1).All(HasDigitsOnly))
        {
            return string.Concat(string.Join(string.Empty, parts[..^1]), ".", parts[^1]);
        }

        return text;
    }

    private static bool IsThousandsGrouped(string text, char separator)
    {
        var parts = text.Split(separator);
        if (parts.Length < 2)
        {
            return false;
        }

        if (!HasDigitsOnlyIgnoringSign(parts[0]) || parts[0].TrimStart('+', '-').Length is < 1 or > 3)
        {
            return false;
        }

        return parts.Skip(1).All(part => part.Length == 3 && HasDigitsOnly(part));
    }

    private static bool HasDigitsOnlyIgnoringSign(string value)
    {
        var normalized = value.TrimStart('+', '-');
        return normalized.Length > 0 && HasDigitsOnly(normalized);
    }

    private static bool HasDigitsOnly(string value) => value.All(char.IsDigit);
}

public sealed class ImportacionException : Exception
{
    public int StatusCode { get; }

    public ImportacionException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
