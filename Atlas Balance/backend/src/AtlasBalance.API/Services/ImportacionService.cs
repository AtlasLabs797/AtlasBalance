using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AtlasBalance.API.Services;

public interface IImportacionService
{
    Task<ImportacionContextoResponse> GetContextoAsync(Guid usuarioId, string rol, Guid? paisId, CancellationToken cancellationToken);
    Task<ImportacionValidarResponse> ValidarAsync(Guid usuarioId, string rol, ImportacionValidarRequest request, CancellationToken cancellationToken);
    Task<ImportacionConfirmarResponse> ConfirmarAsync(Guid usuarioId, string rol, ImportacionConfirmarRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<PaginatedResponse<ImportacionLoteResponse>> ListarLotesAsync(Guid usuarioId, string rol, Guid? cuentaId, int page, int pageSize, CancellationToken cancellationToken);
    Task<ImportacionLoteDetalleResponse> CrearLoteAsync(Guid usuarioId, string rol, ImportacionLoteCrearRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ImportacionLoteDetalleResponse> ObtenerLoteAsync(Guid usuarioId, string rol, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImportacionLoteFilaResponse>> ListarLoteFilasAsync(Guid usuarioId, string rol, Guid id, CancellationToken cancellationToken);
    Task<ImportacionConfirmarResponse> ConfirmarLoteAsync(Guid usuarioId, string rol, Guid id, ImportacionLoteConfirmarRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ImportacionLoteResponse> RevertirLoteAsync(Guid usuarioId, string rol, Guid id, ImportacionLoteRevertirRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ImportacionPlazoFijoMovimientoResponse> RegistrarMovimientoPlazoFijoAsync(Guid usuarioId, string rol, ImportacionPlazoFijoMovimientoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
}

public sealed class ImportacionService : IImportacionService
{
    private const int MaxRawDataLength = 5 * 1024 * 1024;
    private const int MaxRows = 50_000;
    private const int MaxExtraColumns = 64;
    private const int MaxExtraColumnNameLength = 80;
    private const int MaxImportedCellLength = 4096;

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy",
        "d/M/yyyy",
        "yyyy-MM-dd",
        "yyyy-M-d",
        "dd-MM-yyyy",
        "d-M-yyyy"
    ];

    private static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IAlertaService? _alertaService;

    private enum ImportacionPermissionMode
    {
        Importar,
        Aprobar,
        Ver
    }

    public ImportacionService(AppDbContext dbContext, IAuditService auditService, IAlertaService? alertaService = null)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _alertaService = alertaService;
    }

    public async Task<ImportacionContextoResponse> GetContextoAsync(Guid usuarioId, string rol, Guid? paisId, CancellationToken cancellationToken)
    {
        var isAdmin = string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase);
        IQueryable<Cuenta> baseQuery = _dbContext.Cuentas.AsNoTracking().Where(c => c.Activa).ApplyPaisScope(paisId);

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

            var hasGlobal = permisosImportacion.Any(p => p.PaisId is null && p.CuentaId is null && p.TitularId is null);
            if (!hasGlobal)
            {
                baseQuery = baseQuery.Where(c =>
                    _dbContext.PermisosUsuario.Any(p =>
                        p.UsuarioId == usuarioId &&
                        p.PuedeImportar &&
                        (p.PaisId == null || p.PaisId == c.PaisId) &&
                        (p.TitularId == null || p.TitularId == c.TitularId) &&
                        (p.CuentaId == null || p.CuentaId == c.Id)));
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
                x.cuenta.PaisId,
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
                PaisId = c.PaisId,
                EsEfectivo = c.EsEfectivo,
                TipoCuenta = c.TipoCuenta.ToString(),
                FormatoId = c.FormatoId,
                FormatoPredefinido = ParseMapeoJson(c.MapeoJson)
            }).ToList()
        };
    }

    public async Task<ImportacionValidarResponse> ValidarAsync(Guid usuarioId, string rol, ImportacionValidarRequest request, CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, ImportacionPermissionMode.Importar, cancellationToken);
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

    public async Task<PaginatedResponse<ImportacionLoteResponse>> ListarLotesAsync(
        Guid usuarioId,
        string rol,
        Guid? cuentaId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (cuentaId.HasValue)
        {
            await EnsureCuentaPermitidaAsync(usuarioId, rol, cuentaId.Value, ImportacionPermissionMode.Ver, cancellationToken);
        }

        var isAdmin = string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase);
        var query = _dbContext.ImportacionLotes.AsNoTracking().AsQueryable();
        if (cuentaId.HasValue)
        {
            query = query.Where(x => x.CuentaId == cuentaId.Value);
        }

        if (!isAdmin)
        {
            query = query.Where(l =>
                _dbContext.Cuentas.Any(c =>
                    c.Id == l.CuentaId &&
                    _dbContext.PermisosUsuario.Any(p =>
                        p.UsuarioId == usuarioId &&
                        (p.PuedeVerCuentas || p.PuedeImportar || p.PuedeRevisarLineas || p.PuedeAprobarImportaciones) &&
                        (p.PaisId == null || p.PaisId == c.PaisId) &&
                        (p.TitularId == null || p.TitularId == c.TitularId) &&
                        (p.CuentaId == null || p.CuentaId == c.Id))));
        }

        var total = await query.CountAsync(cancellationToken);
        var lotes = await query
            .OrderByDescending(x => x.FechaCreacion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var cuentaIds = lotes.Select(x => x.CuentaId).Distinct().ToList();
        var cuentas = await _dbContext.Cuentas
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);

        return new PaginatedResponse<ImportacionLoteResponse>
        {
            Data = lotes.Select(x => MapLote(x, cuentas.GetValueOrDefault(x.CuentaId))).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<ImportacionLoteDetalleResponse> CrearLoteAsync(
        Guid usuarioId,
        string rol,
        ImportacionLoteCrearRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, ImportacionPermissionMode.Importar, cancellationToken);
        EnsureNotPlazoFijoForFormattedImport(cuenta);

        var rawData = request.RawData ?? string.Empty;
        var rawSize = Encoding.UTF8.GetByteCount(rawData);
        var declaredSize = request.TamanioBytes ?? rawSize;
        if (declaredSize < 0)
        {
            throw new ImportacionException("El tamano del archivo no es valido", StatusCodes.Status400BadRequest);
        }

        if (rawSize > MaxRawDataLength || declaredSize > MaxRawDataLength)
        {
            throw new ImportacionException("El archivo supera el limite de 5 MB", StatusCodes.Status413PayloadTooLarge);
        }

        var normalizedMap = NormalizeMap(request.Mapeo);
        var (rows, separator) = ParseRows(rawData, request.Separador);
        var validationRows = ValidateRows(rows, normalizedMap);
        var validRows = validationRows.Where(row => row.Valida).ToList();
        var fingerprintsByIndex = validRows.Count == 0
            ? new Dictionary<int, string>()
            : BuildImportFingerprints(cuenta.Id, validRows);
        var loteHash = validRows.Count == 0
            ? Sha256Hex($"empty|{cuenta.Id:N}|{Sha256Hex(rawData)}")
            : BuildImportBatchHash(validRows, fingerprintsByIndex);

        var lote = new ImportacionLote
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            UsuarioCreadorId = usuarioId,
            TipoOrigen = NormalizeTipoOrigen(request.TipoOrigen),
            NombreArchivo = NormalizeOptionalText(request.NombreArchivo),
            TamanioBytes = declaredSize,
            Sha256 = Sha256Hex(rawData),
            Separador = HumanSeparator(separator),
            MapeoJson = JsonSerializer.Serialize(normalizedMap, SnakeCaseJsonOptions),
            ResumenJson = JsonSerializer.Serialize(new
            {
                filas_total = validationRows.Count,
                filas_validas = validationRows.Count(row => row.Valida),
                filas_error = validationRows.Count(row => !row.Valida),
                filas_advertencia = validationRows.Count(row => row.Advertencias.Count > 0),
                filas_seleccionadas_default = validationRows.Count(row => row.Valida && row.Advertencias.Count == 0)
            }, SnakeCaseJsonOptions),
            ContenidoOriginal = rawData,
            LoteHash = loteHash,
            Estado = validationRows.Any(row => !row.Valida) ? "validado_con_errores" : "validado",
            FilasTotal = validationRows.Count,
            FilasValidas = validationRows.Count(row => row.Valida),
            FilasError = validationRows.Count(row => !row.Valida),
            FilasAdvertencia = validationRows.Count(row => row.Advertencias.Count > 0),
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.ImportacionLotes.Add(lote);
        _dbContext.ImportacionLoteFilas.AddRange(validationRows.Select(row => new ImportacionLoteFila
        {
            Id = Guid.NewGuid(),
            LoteId = lote.Id,
            Indice = row.Indice,
            Valida = row.Valida,
            SeleccionadaDefault = row.Valida && row.Advertencias.Count == 0,
            Estado = ResolveLoteFilaEstado(row),
            DatosJson = JsonSerializer.Serialize(row.Datos, SnakeCaseJsonOptions),
            ErroresJson = JsonSerializer.Serialize(row.Errores, SnakeCaseJsonOptions),
            AdvertenciasJson = JsonSerializer.Serialize(row.Advertencias, SnakeCaseJsonOptions),
            Fingerprint = row.Valida ? fingerprintsByIndex.GetValueOrDefault(row.Indice) : null
        }));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuarioId,
            "importacion_lote_creado",
            "IMPORTACION_LOTES",
            lote.Id,
            httpContext,
            JsonSerializer.Serialize(new
            {
                lote.CuentaId,
                lote.TipoOrigen,
                lote.NombreArchivo,
                lote.TamanioBytes,
                lote.Sha256,
                lote.LoteHash,
                lote.FilasTotal,
                lote.FilasValidas,
                lote.FilasError,
                lote.FilasAdvertencia
            }, SnakeCaseJsonOptions),
            cancellationToken);

        return await BuildLoteDetalleAsync(lote, cuenta.Nombre, cancellationToken);
    }

    public async Task<ImportacionLoteDetalleResponse> ObtenerLoteAsync(Guid usuarioId, string rol, Guid id, CancellationToken cancellationToken)
    {
        var lote = await _dbContext.ImportacionLotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lote is null)
        {
            throw new ImportacionException("Lote no encontrado", StatusCodes.Status404NotFound);
        }

        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, lote.CuentaId, ImportacionPermissionMode.Ver, cancellationToken);
        return await BuildLoteDetalleAsync(lote, cuenta.Nombre, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportacionLoteFilaResponse>> ListarLoteFilasAsync(Guid usuarioId, string rol, Guid id, CancellationToken cancellationToken)
    {
        var lote = await _dbContext.ImportacionLotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lote is null)
        {
            throw new ImportacionException("Lote no encontrado", StatusCodes.Status404NotFound);
        }

        await EnsureCuentaPermitidaAsync(usuarioId, rol, lote.CuentaId, ImportacionPermissionMode.Ver, cancellationToken);
        var filas = await _dbContext.ImportacionLoteFilas
            .AsNoTracking()
            .Where(x => x.LoteId == id)
            .OrderBy(x => x.Indice)
            .ToListAsync(cancellationToken);
        return filas.Select(MapLoteFila).ToList();
    }

    public async Task<ImportacionConfirmarResponse> ConfirmarLoteAsync(
        Guid usuarioId,
        string rol,
        Guid id,
        ImportacionLoteConfirmarRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var lote = await _dbContext.ImportacionLotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lote is null)
        {
            throw new ImportacionException("Lote no encontrado", StatusCodes.Status404NotFound);
        }

        await EnsureCuentaPermitidaAsync(usuarioId, rol, lote.CuentaId, ImportacionPermissionMode.Aprobar, cancellationToken);
        if (lote.Estado is "confirmado" or "revertido")
        {
            throw new ImportacionException("El lote ya esta cerrado", StatusCodes.Status409Conflict);
        }

        var filas = await _dbContext.ImportacionLoteFilas
            .Where(x => x.LoteId == id)
            .OrderBy(x => x.Indice)
            .ToListAsync(cancellationToken);
        var filasAImportar = request.FilasAImportar?.ToHashSet() ??
                             filas.Where(x => x.SeleccionadaDefault).Select(x => x.Indice).ToHashSet();
        var filasConAdvertencias = filas
            .Where(x => x.Valida && filasAImportar.Contains(x.Indice) && ParseJsonList(x.AdvertenciasJson).Count > 0)
            .Select(x => x.Indice)
            .ToList();

        if (filasConAdvertencias.Count > 0 && !request.AceptaAdvertencias)
        {
            throw new ImportacionException(
                "El lote contiene filas seleccionadas con advertencias. Confirma acepta_advertencias=true para importarlas.",
                StatusCodes.Status400BadRequest);
        }

        var mapeo = ParseMapeoJson(lote.MapeoJson) ?? throw new ImportacionException("El mapeo guardado del lote no es valido", StatusCodes.Status409Conflict);
        ImportacionConfirmarResponse response;
        try
        {
            response = await ConfirmarAsync(
                usuarioId,
                rol,
                new ImportacionConfirmarRequest
                {
                    CuentaId = lote.CuentaId,
                    RawData = lote.ContenidoOriginal,
                    Separador = lote.Separador,
                    Mapeo = mapeo,
                    FilasAImportar = filasAImportar.ToList(),
                    LoteId = lote.Id
                },
                httpContext,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // H-OPEN-4 (V-02-03): si la confirmacion interna revienta (colision
            // de fingerprint, etc.), el lote debe quedar marcado como "error"
            // para que el usuario sepa que algo fallo, no en "validado" con
            // extractos a medias.
            lote.Estado = "error";
            lote.Notas = $"ConfirmarAsync fallo: {Truncate(ex.Message, 500)}";
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                usuarioId,
                "importacion_lote_error",
                "IMPORTACION_LOTES",
                lote.Id,
                httpContext,
                JsonSerializer.Serialize(new { lote.CuentaId, mensaje = ex.Message }, SnakeCaseJsonOptions),
                cancellationToken);
            throw new ImportacionException(
                "La confirmacion fallo. El lote quedo marcado como 'error'. Revisa la causa y reintenta.",
                StatusCodes.Status409Conflict);
        }

        lote.Estado = "confirmado";
        lote.FechaConfirmacion = DateTime.UtcNow;
        lote.ConfirmadoPorId = usuarioId;
        lote.AdvertenciasAceptadas = request.AceptaAdvertencias;

        foreach (var fila in filas.Where(x => x.Valida))
        {
            fila.Estado = filasAImportar.Contains(fila.Indice) ? "confirmada" : "omitida";
        }

        var warnings = new List<string>();
        if (lote.UsuarioCreadorId == usuarioId)
        {
            warnings.Add("Maker-checker solo aviso: el mismo usuario creo y aprobo este lote.");
            AddAdminNotification(
                "maker_checker_importacion",
                "El mismo usuario creo y aprobo un lote de importacion.",
                new { lote_id = lote.Id, usuario_id = usuarioId, cuenta_id = lote.CuentaId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuarioId,
            "importacion_lote_confirmado",
            "IMPORTACION_LOTES",
            lote.Id,
            httpContext,
            JsonSerializer.Serialize(new
            {
                lote.CuentaId,
                filas_a_importar = filasAImportar.Count,
                response.FilasImportadas,
                response.FilasDuplicadas,
                acepta_advertencias = request.AceptaAdvertencias,
                maker_checker_warning = warnings.Count > 0
            }, SnakeCaseJsonOptions),
            cancellationToken);

        response.Advertencias = warnings;
        return response;
    }

    public async Task<ImportacionLoteResponse> RevertirLoteAsync(
        Guid usuarioId,
        string rol,
        Guid id,
        ImportacionLoteRevertirRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var lote = await _dbContext.ImportacionLotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lote is null)
        {
            throw new ImportacionException("Lote no encontrado", StatusCodes.Status404NotFound);
        }

        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, lote.CuentaId, ImportacionPermissionMode.Aprobar, cancellationToken);
        if (lote.Estado == "revertido")
        {
            throw new ImportacionException("El lote ya fue revertido", StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var extractos = await _dbContext.Extractos
            .IgnoreQueryFilters()
            .Where(x => x.ImportacionLoteId == lote.Id && x.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var extracto in extractos)
        {
            extracto.DeletedAt = now;
            extracto.DeletedById = usuarioId;
        }

        var filas = await _dbContext.ImportacionLoteFilas.Where(x => x.LoteId == lote.Id).ToListAsync(cancellationToken);
        foreach (var fila in filas.Where(x => x.Estado == "confirmada"))
        {
            fila.Estado = "revertida";
        }

        lote.Estado = "revertido";
        lote.FechaReversion = now;
        lote.RevertidoPorId = usuarioId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuarioId,
            "importacion_lote_revertido",
            "IMPORTACION_LOTES",
            lote.Id,
            httpContext,
            JsonSerializer.Serialize(new
            {
                lote.CuentaId,
                extractos_revertidos = extractos.Count,
                motivo = NormalizeOptionalText(request.Motivo)
            }, SnakeCaseJsonOptions),
            cancellationToken);

        return MapLote(lote, cuenta.Nombre);
    }

    public async Task<ImportacionConfirmarResponse> ConfirmarAsync(Guid usuarioId, string rol, ImportacionConfirmarRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, ImportacionPermissionMode.Importar, cancellationToken);
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
        try
        {
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

        // Number from bottom to top so the upper line in the pasted statement remains the latest/highest row.
        var selectedValidRowsForFilaNumbering = selectedValidRows
            .OrderByDescending(row => row.Indice)
            .ToList();

        var fingerprintsByIndex = BuildImportFingerprints(cuenta.Id, selectedValidRows);
        var loteHash = BuildImportBatchHash(selectedValidRows, fingerprintsByIndex);
        var rowCandidates = selectedValidRowsForFilaNumbering
            .Select(row => new ImportRowCandidate(row, fingerprintsByIndex[row.Indice]))
            .ToList();

        var candidateFingerprints = rowCandidates
            .Select(x => x.Fingerprint)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingFingerprints = await _dbContext.Extractos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.CuentaId == cuenta.Id &&
                        e.ImportacionFingerprint != null &&
                        candidateFingerprints.Contains(e.ImportacionFingerprint))
            .Select(e => e.ImportacionFingerprint!)
            .ToListAsync(cancellationToken);

        var existingFingerprintSet = existingFingerprints.ToHashSet(StringComparer.Ordinal);
        var batchFingerprintSet = new HashSet<string>(StringComparer.Ordinal);
        var rowsToImport = rowCandidates
            .Where(candidate => !existingFingerprintSet.Contains(candidate.Fingerprint) &&
                                batchFingerprintSet.Add(candidate.Fingerprint))
            .ToList();

        var duplicateRows = rowCandidates.Count - rowsToImport.Count;

        if (rowsToImport.Count == 0)
        {
            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return new ImportacionConfirmarResponse
            {
                FilasProcesadas = validationRows.Count,
                FilasImportadas = 0,
                FilasDuplicadas = duplicateRows,
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

        var extractos = new List<Extracto>(rowsToImport.Count);
        var extras = new List<ExtractoColumnaExtra>(rowsToImport.Count * Math.Max(1, normalizedMap.ColumnasExtra.Count));

        foreach (var candidate in rowsToImport)
        {
            var row = candidate.Row;
            maxFila += 1;

            var fecha = ParseDate(row.Datos["fecha"], out _, out var parsedDate)
                ? parsedDate
                : throw new InvalidOperationException("Fila validada sin fecha parseable.");

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
                Fecha = fecha,
                Concepto = string.IsNullOrWhiteSpace(row.Datos["concepto"]) ? null : row.Datos["concepto"]!.Trim(),
                Monto = monto,
                Saldo = saldo,
                FilaNumero = maxFila,
                ImportacionFingerprint = candidate.Fingerprint,
                ImportacionLoteHash = loteHash,
                ImportacionLoteId = request.LoteId,
                ImportacionFilaOrigen = row.Indice,
                FechaImportacion = now,
                UsuarioCreacionId = usuarioId,
                FechaCreacion = now
            };
            extractos.Add(extracto);

            foreach (var pair in row.Datos.Where(d => d.Key.StartsWith("extra:", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                var columnName = pair.Key["extra:".Length..];
                extras.Add(new ExtractoColumnaExtra
                {
                    Id = Guid.NewGuid(),
                    ExtractoId = extracto.Id,
                    NombreColumna = columnName,
                    Valor = pair.Value!.Trim()
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
        catch (DbUpdateException ex) when (IsImportFingerprintUniqueViolation(ex))
        {
            throw new ImportacionException(
                "La importacion contiene movimientos ya registrados por huella. Vuelve a validar para ver los duplicados omitidos.",
                StatusCodes.Status409Conflict);
        }

        var auditDetails = JsonSerializer.Serialize(new
        {
            cuenta_id = cuenta.Id,
            cuenta = cuenta.Nombre,
            separador = HumanSeparator(separator),
            filas_procesadas = validationRows.Count,
            filas_importadas = extractos.Count,
            filas_duplicadas = duplicateRows,
            filas_con_error = validationRows.Count(r => !r.Valida),
            lote_hash = loteHash,
            primeras_filas = selectedValidRows.OrderBy(row => row.Indice).Take(5).Select(row => row.Indice).ToArray()
        });
        await _auditService.LogAsync(usuarioId, "importacion_confirmada", "EXTRACTOS", cuenta.Id, httpContext, auditDetails, cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        await EvaluateSaldoAlertAsync(cuenta.Id, usuarioId, cancellationToken);

        return new ImportacionConfirmarResponse
        {
            FilasProcesadas = validationRows.Count,
            FilasImportadas = extractos.Count,
            FilasDuplicadas = duplicateRows,
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
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    public async Task<ImportacionPlazoFijoMovimientoResponse> RegistrarMovimientoPlazoFijoAsync(Guid usuarioId, string rol, ImportacionPlazoFijoMovimientoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, ImportacionPermissionMode.Importar, cancellationToken);
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
        try
        {
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
            .OrderByDescending(e => e.FilaNumero)
            .ThenByDescending(e => e.Fecha)
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
        }

        await EvaluateSaldoAlertAsync(cuenta.Id, usuarioId, cancellationToken);

        return new ImportacionPlazoFijoMovimientoResponse
        {
            ExtractoId = extracto.Id,
            FilaNumero = extracto.FilaNumero,
            Monto = Decimal.Round(signedAmount, 2),
            SaldoAnterior = Decimal.Round(saldoAnterior, 2),
            SaldoActual = Decimal.Round(saldoActual, 2)
        };
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    private async Task<Cuenta> EnsureCuentaPermitidaAsync(Guid usuarioId, string rol, Guid cuentaId, ImportacionPermissionMode permissionMode, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas
            .AsNoTracking()
            .Where(c => c.Id == cuentaId && c.Activa)
            .Where(c => _dbContext.Titulares.Any(t => t.Id == c.TitularId && t.DeletedAt == null))
            .FirstOrDefaultAsync(cancellationToken);

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
            GrantsImportacionPermission(p, permissionMode) &&
            (p.PaisId is null || p.PaisId == cuenta.PaisId) &&
            (p.CuentaId is null || p.CuentaId == cuenta.Id) &&
            (p.TitularId is null || p.TitularId == cuenta.TitularId));

        if (!hasPermission)
        {
            throw new ImportacionException("No tienes permisos para importar en esta cuenta", StatusCodes.Status403Forbidden);
        }

        return cuenta;
    }

    private static bool GrantsImportacionPermission(PermisoUsuario permiso, ImportacionPermissionMode permissionMode)
    {
        return permissionMode switch
        {
            ImportacionPermissionMode.Importar => permiso.PuedeImportar,
            ImportacionPermissionMode.Aprobar => permiso.PuedeAprobarImportaciones || permiso.PuedeImportar,
            ImportacionPermissionMode.Ver => permiso.PuedeVerCuentas ||
                                             permiso.PuedeImportar ||
                                             permiso.PuedeRevisarLineas ||
                                             permiso.PuedeAprobarImportaciones,
            _ => false
        };
    }

    private async Task<ImportacionLoteDetalleResponse> BuildLoteDetalleAsync(ImportacionLote lote, string? cuentaNombre, CancellationToken cancellationToken)
    {
        var filas = await _dbContext.ImportacionLoteFilas
            .AsNoTracking()
            .Where(x => x.LoteId == lote.Id)
            .OrderBy(x => x.Indice)
            .ToListAsync(cancellationToken);
        var filaResponses = filas.Select(MapLoteFila).ToList();

        return new ImportacionLoteDetalleResponse
        {
            Lote = MapLote(lote, cuentaNombre),
            Mapeo = ParseMapeoJson(lote.MapeoJson) ?? new MapeoColumnasRequest(),
            Validacion = new ImportacionValidarResponse
            {
                FilasOk = filaResponses.Count(x => x.Valida),
                FilasError = filaResponses.Count(x => !x.Valida),
                SeparadorDetectado = lote.Separador,
                Filas = filaResponses.Select(x => new FilaValidacionResponse
                {
                    Indice = x.Indice,
                    Valida = x.Valida,
                    Datos = x.Datos,
                    Errores = x.Errores,
                    Advertencias = x.Advertencias
                }).ToList(),
                Errores = filaResponses
                    .Where(x => !x.Valida)
                    .Select(x => new ErrorFilaResponse
                    {
                        FilaIndice = x.Indice,
                        Mensajes = x.Errores
                    })
                    .ToList()
            }
        };
    }

    private static ImportacionLoteResponse MapLote(ImportacionLote lote, string? cuentaNombre)
    {
        return new ImportacionLoteResponse
        {
            Id = lote.Id,
            CuentaId = lote.CuentaId,
            CuentaNombre = cuentaNombre,
            UsuarioCreadorId = lote.UsuarioCreadorId,
            TipoOrigen = lote.TipoOrigen,
            NombreArchivo = lote.NombreArchivo,
            TamanioBytes = lote.TamanioBytes,
            Sha256 = lote.Sha256,
            Separador = lote.Separador,
            LoteHash = lote.LoteHash,
            Estado = lote.Estado,
            FilasTotal = lote.FilasTotal,
            FilasValidas = lote.FilasValidas,
            FilasError = lote.FilasError,
            FilasAdvertencia = lote.FilasAdvertencia,
            AdvertenciasAceptadas = lote.AdvertenciasAceptadas,
            FechaCreacion = lote.FechaCreacion,
            FechaConfirmacion = lote.FechaConfirmacion,
            ConfirmadoPorId = lote.ConfirmadoPorId,
            FechaReversion = lote.FechaReversion,
            RevertidoPorId = lote.RevertidoPorId
        };
    }

    private static ImportacionLoteFilaResponse MapLoteFila(ImportacionLoteFila fila)
    {
        return new ImportacionLoteFilaResponse
        {
            Id = fila.Id,
            LoteId = fila.LoteId,
            Indice = fila.Indice,
            Valida = fila.Valida,
            SeleccionadaDefault = fila.SeleccionadaDefault,
            Estado = fila.Estado,
            Datos = ParseJsonDictionary(fila.DatosJson),
            Errores = ParseJsonList(fila.ErroresJson),
            Advertencias = ParseJsonList(fila.AdvertenciasJson),
            Fingerprint = fila.Fingerprint
        };
    }

    private static string ResolveLoteFilaEstado(FilaValidacionResponse row)
    {
        if (!row.Valida)
        {
            return "error";
        }

        return row.Advertencias.Count > 0 ? "advertencia" : "validada";
    }

    private static string NormalizeTipoOrigen(string? raw)
    {
        var normalized = (raw ?? "PEGADO").Trim().ToUpperInvariant();
        return normalized == "ARCHIVO" ? "ARCHIVO" : "PEGADO";
    }

    private static Dictionary<string, string?> ParseJsonDictionary(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(rawJson, SnakeCaseJsonOptions)
                   ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<string> ParseJsonList(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson, SnakeCaseJsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void AddAdminNotification(string tipo, string mensaje, object details)
    {
        _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Mensaje = mensaje,
            Fecha = DateTime.UtcNow,
            DetallesJson = JsonSerializer.Serialize(details, SnakeCaseJsonOptions)
        });
    }

    private static bool IsFilaNumeroUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(postgresException.ConstraintName, "ix_extractos_cuenta_id_fila_numero", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImportFingerprintUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(postgresException.ConstraintName, "ix_extractos_cuenta_id_importacion_fingerprint", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<int, string> BuildImportFingerprints(Guid cuentaId, IEnumerable<FilaValidacionResponse> rows)
    {
        var result = new Dictionary<int, string>();
        var occurrenceByContent = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows.OrderBy(row => row.Indice))
        {
            var contentFingerprint = BuildImportContentFingerprint(cuentaId, row);
            occurrenceByContent.TryGetValue(contentFingerprint, out var occurrence);
            occurrence += 1;
            occurrenceByContent[contentFingerprint] = occurrence;
            result[row.Indice] = BuildImportFingerprint(cuentaId, contentFingerprint, occurrence);
        }

        return result;
    }

    private static string BuildImportBatchHash(IEnumerable<FilaValidacionResponse> rows, IReadOnlyDictionary<int, string> fingerprintsByIndex)
    {
        var canonicalRows = rows
            .OrderBy(row => row.Indice)
            .Select(row => fingerprintsByIndex[row.Indice]);
        return Sha256Hex(string.Join('\n', canonicalRows));
    }

    private static string BuildImportFingerprint(Guid cuentaId, string contentFingerprint, int occurrence)
    {
        return Sha256Hex($"v2|{cuentaId:N}|{contentFingerprint}|{occurrence.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string BuildImportContentFingerprint(Guid cuentaId, FilaValidacionResponse row)
    {
        var builder = new StringBuilder();
        builder.Append("content-v1|");
        builder.Append(cuentaId.ToString("N"));
        builder.Append('|');
        builder.Append(NormalizeDateForFingerprint(row.Datos.GetValueOrDefault("fecha")));
        builder.Append('|');
        builder.Append(NormalizeDecimalForFingerprint(row.Datos.GetValueOrDefault("monto")));
        builder.Append('|');
        builder.Append(NormalizeDecimalForFingerprint(row.Datos.GetValueOrDefault("saldo")));
        builder.Append('|');
        builder.Append(NormalizeTextForFingerprint(row.Datos.GetValueOrDefault("concepto")));

        return Sha256Hex(builder.ToString());
    }

    private Task EvaluateSaldoAlertAsync(Guid cuentaId, Guid actorUserId, CancellationToken cancellationToken)
    {
        return _alertaService?.EvaluateSaldoPostAsync(cuentaId, actorUserId, cancellationToken) ?? Task.CompletedTask;
    }

    private static string NormalizeDateForFingerprint(string? raw)
    {
        return ParseDate(raw, out _, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : NormalizeTextForFingerprint(raw);
    }

    private static string NormalizeDecimalForFingerprint(string? raw)
    {
        return TryParseDecimalSmart(raw, out var value)
            ? value.ToString("0.####", CultureInfo.InvariantCulture)
            : NormalizeTextForFingerprint(raw);
    }

    private static string NormalizeTextForFingerprint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var parts = raw.Trim()
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', parts);
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
                    Indice = extra.Indice,
                    Etiqueta = extra.Etiqueta?.Trim()
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
        if (normalized.ColumnasExtra.Count > MaxExtraColumns)
        {
            throw new ImportacionException($"El mapeo no puede incluir mas de {MaxExtraColumns} columnas extra", StatusCodes.Status400BadRequest);
        }

        foreach (var (fieldName, index) in baseFields)
        {
            ValidateColumnIndex(index, fieldName);
            if (!usedIndices.TryAdd(index, fieldName))
            {
                throw new ImportacionException($"Índice de columna duplicado en mapeo ({index + 1})", StatusCodes.Status400BadRequest);
            }
        }

        var extraClaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in normalized.ColumnasExtra)
        {
            if (string.IsNullOrWhiteSpace(extra.Nombre))
            {
                throw new ImportacionException("El nombre de columna extra es obligatorio", StatusCodes.Status400BadRequest);
            }

            if (extra.Nombre.Length > MaxExtraColumnNameLength)
            {
                throw new ImportacionException($"El nombre de columna extra no puede superar {MaxExtraColumnNameLength} caracteres", StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrWhiteSpace(extra.Etiqueta) && extra.Etiqueta.Trim().Length > MaxExtraColumnNameLength)
            {
                throw new ImportacionException($"La etiqueta de columna extra no puede superar {MaxExtraColumnNameLength} caracteres", StatusCodes.Status400BadRequest);
            }

            ValidateColumnIndex(extra.Indice, $"extra:{extra.ClaveAlmacenamiento}");
            if (!usedIndices.TryAdd(extra.Indice, $"extra:{extra.ClaveAlmacenamiento}"))
            {
                throw new ImportacionException($"Índice de columna duplicado en mapeo ({extra.Indice + 1})", StatusCodes.Status400BadRequest);
            }

            if (!extraClaves.Add(extra.ClaveAlmacenamiento))
            {
                throw new ImportacionException(
                    $"Clave de columna extra duplicada ({extra.ClaveAlmacenamiento}). Dos columnas con la misma etiqueta generarían la misma columna en extractos.",
                    StatusCodes.Status400BadRequest);
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
            throw new ImportacionException("El archivo no tiene filas importables.", StatusCodes.Status400BadRequest);
        }

        if (rawData.Length > MaxRawDataLength)
        {
            throw new ImportacionException("El archivo pegado supera el limite de 5 MB", StatusCodes.Status413PayloadTooLarge);
        }

        var normalizedRawData = rawData.TrimStart('\uFEFF');

        var lines = normalizedRawData
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
                    EnsureImportedCellLength(sb.Length);
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
            EnsureImportedCellLength(sb.Length);
        }

        values.Add(sb.ToString().Trim());
        return values;
    }

    private static void EnsureImportedCellLength(int length)
    {
        if (length > MaxImportedCellLength)
        {
            throw new ImportacionException($"Una celda importada supera el limite de {MaxImportedCellLength} caracteres", StatusCodes.Status413PayloadTooLarge);
        }
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
                    AddAmbiguousAmountWarning(data["ingreso"], "Ingreso", warnings);
                    AddAmbiguousAmountWarning(data["egreso"], "Egreso", warnings);
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
                else
                {
                    AddAmbiguousAmountWarning(data["monto"], "Monto", warnings);
                }
            }

            foreach (var extra in map.ColumnasExtra)
            {
                data[$"extra:{extra.ClaveAlmacenamiento}"] = GetCell(row, extra.Indice);
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
                AddAmbiguousAmountWarning(data["saldo"], "Saldo", warnings);
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
            return JsonSerializer.Deserialize<MapeoColumnasRequest>(rawJson, SnakeCaseJsonOptions);
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

    // V-02-04 (1.3): un valor con un unico separador y exactamente un grupo de 3
    // digitos (ej. "1,234") es ambiguo: puede ser 1234 (agrupacion de miles) o
    // 1.234 (decimal con 3 decimales). TryParseDecimalSmart elige miles, que suele
    // ser correcto para importes de 2 decimales, pero en modos una_columna/dos_columnas
    // no hay columna de control que lo verifique. Devolvemos un aviso (no bloqueante)
    // para que el usuario revise el formato antes de confirmar. Devuelve null si no
    // hay ambiguedad.
    private static string? BuildAmbiguousAmountWarning(string? raw, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            text = text[1..^1];
        }

        text = text.Replace("MX$", string.Empty, StringComparison.Ordinal)
            .Replace("RD$", string.Empty, StringComparison.Ordinal)
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal);

        var hasComma = text.Contains(',');
        var hasDot = text.Contains('.');
        if (hasComma == hasDot)
        {
            // Ni un solo tipo de separador, o ambos presentes: en esos casos la
            // interpretacion es deterministica y no ambigua.
            return null;
        }

        var separator = hasComma ? ',' : '.';
        var parts = text.Split(separator);
        if (parts.Length != 2 || parts[1].Length != 3 || !IsThousandsGrouped(text, separator))
        {
            return null;
        }

        var comoMiles = text.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal);
        var comoDecimal = text.Replace(separator, '.');
        return $"{fieldLabel} '{raw.Trim()}' es ambiguo: se importara como {comoMiles} (agrupacion de miles). " +
               $"Si el valor real es {comoDecimal}, ajusta el separador decimal del formato.";
    }

    private static void AddAmbiguousAmountWarning(string? raw, string fieldLabel, List<string> warnings)
    {
        var warning = BuildAmbiguousAmountWarning(raw, fieldLabel);
        if (warning is not null)
        {
            warnings.Add(warning);
        }
    }

    private static bool HasDigitsOnlyIgnoringSign(string value)
    {
        var normalized = value.TrimStart('+', '-');
        return normalized.Length > 0 && HasDigitsOnly(normalized);
    }

    private static bool HasDigitsOnly(string value) => value.All(char.IsDigit);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ImportRowCandidate(FilaValidacionResponse Row, string Fingerprint);
}

public sealed class ImportacionException : Exception
{
    public int StatusCode { get; }

    public ImportacionException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
