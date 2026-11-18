using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using AtlasBalance.API.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize]
[Route("api/extractos")]
public sealed class ExtractosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAlertaService _alertaService;
    // M-5: el alcance (que cuentas y titulares ve el actor) lo decide
    // UserAccessService. El controller no lo reimplementa.
    private readonly IUserAccessService _userAccessService;
    private readonly ILogger<ExtractosController> _logger;

    public ExtractosController(
        AppDbContext db,
        IAlertaService alertaService,
        ILogger<ExtractosController> logger,
        IUserAccessService userAccessService)
    {
        _db = db;
        _alertaService = alertaService;
        _userAccessService = userAccessService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string sortBy = "fecha",
        [FromQuery] string sortDir = "desc",
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] Guid? titularId = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        [FromQuery] bool? checkedValue = null,
        [FromQuery] bool? flagged = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? paisId = null,
        [FromQuery] bool incluirEliminados = false,
        [FromQuery] bool soloEliminados = false,
        CancellationToken ct = default)
    {
        // SEC V-02.09: cota del filtro libre que viaja a LIKE/ILike; evita
        // busquedas kilometricas gastando CPU en cada listado.
        if (search is { Length: > 200 })
        {
            search = search[..200];
        }

        if (!TryGetUser(out var actor))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        if (!actor.IsAdmin)
        {
            incluirEliminados = false;
            soloEliminados = false;
        }

        if (!DateRangeValidator.TryValidate(fechaDesde, fechaHasta, out var rangoError))
        {
            return BadRequest(new { error = rangoError });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        // M-5: el alcance lo decide UserAccessService (una sola definicion de la
        // regla para toda la API). El filtro por pais se mantiene encima.
        var userScope = await GetScopeAsync(actor, ct);
        var allowed = await GetAllowedAccountIds(userScope, ct, paisId);
        if (!allowed.Any())
        {
            return Ok(new PaginatedResponse<ExtractoListItemResponse> { Data = [], Total = 0, Page = page, PageSize = pageSize, TotalPages = 0 });
        }

        var desc = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<Extracto> q = incluirEliminados || soloEliminados ? _db.Extractos.IgnoreQueryFilters() : _db.Extractos;

        // F-2: el filtro de solo eliminados se aplica ANTES de contar y paginar,
        // de modo que "total" y las paginas cubren unicamente la papelera.
        if (soloEliminados)
        {
            q = q.Where(x => x.DeletedAt != null);
        }

        q = q.Where(x => allowed.Contains(x.CuentaId));
        if (cuentaId.HasValue) q = q.Where(x => x.CuentaId == cuentaId.Value);
        if (titularId.HasValue)
        {
            var cuentasTitular = await _db.Cuentas.Where(c => c.TitularId == titularId).Select(c => c.Id).ToListAsync(ct);
            q = q.Where(x => cuentasTitular.Contains(x.CuentaId));
        }
        var scopeQuery = q;
        if (fechaDesde.HasValue) q = q.Where(x => x.Fecha >= fechaDesde);
        if (fechaHasta.HasValue) q = q.Where(x => x.Fecha <= fechaHasta);
        if (checkedValue.HasValue) q = q.Where(x => x.Checked == checkedValue);
        if (flagged.HasValue) q = q.Where(x => x.Flagged == flagged);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            q = q.Where(x =>
                (x.Concepto ?? "").ToLower().Contains(term) ||
                (x.Comentarios ?? "").ToLower().Contains(term));
        }

        q = (sortBy.ToLowerInvariant(), desc) switch
        {
            ("fila_numero", true) => q.OrderByDescending(x => x.FilaNumero),
            ("fila_numero", false) => q.OrderBy(x => x.FilaNumero),
            ("monto", true) => q.OrderByDescending(x => x.Monto),
            ("monto", false) => q.OrderBy(x => x.Monto),
            ("saldo", true) => q.OrderByDescending(x => x.Saldo),
            ("saldo", false) => q.OrderBy(x => x.Saldo),
            ("concepto", true) => q.OrderByDescending(x => x.Concepto),
            ("concepto", false) => q.OrderBy(x => x.Concepto),
            ("comentarios", true) => q.OrderByDescending(x => x.Comentarios),
            ("comentarios", false) => q.OrderBy(x => x.Comentarios),
            ("fecha_creacion", true) => q.OrderByDescending(x => x.FechaCreacion),
            ("fecha_creacion", false) => q.OrderBy(x => x.FechaCreacion),
            ("fecha", false) => q.OrderBy(x => x.Fecha).ThenBy(x => x.FilaNumero),
            _ => q.OrderByDescending(x => x.Fecha).ThenByDescending(x => x.FilaNumero)
        };

        // B-15: desempate estable por clave primaria. Sin el, dos filas con el
        // mismo valor de orden pueden caer en paginas distintas o desaparecer
        // entre peticiones. Se aplica DESPUES del campo pedido, sin cambiar el
        // criterio de ordenacion que pide el usuario.
        q = ((IOrderedQueryable<Extracto>)q).ThenBy(x => x.Id);

        var total = await q.CountAsync(ct);
        // S-2: el conjunto efectivo de columnas visibles se calcula POR CUENTA
        // (cada cuenta puede tener su propia regla del administrador). El
        // admin no restringe: el mapa viene vacio y no se filtra nada.
        var conjuntosPorCuenta = await GetConjuntosPorCuentaAsync(actor, allowed, ct);
        var columnasDisponibles = await (
                from extra in _db.ExtractosColumnasExtra
                join extracto in scopeQuery on extra.ExtractoId equals extracto.Id
                where extra.NombreColumna != "" && extra.Valor != null && extra.Valor.Trim() != ""
                select extra.NombreColumna)
            .Distinct()
            .OrderBy(nombre => nombre)
            .ToListAsync(ct);
        // S-2: una columna extra solo se ofrece si la permite el conjunto
        // efectivo de TODAS las cuentas del alcance (la mas restrictiva gana).
        var columnasPermitidas = IntersectConjuntos(conjuntosPorCuenta.Values);
        if (columnasPermitidas is not null)
        {
            columnasDisponibles = [.. columnasDisponibles.Where(columnasPermitidas.Contains)];
        }
        var pageRows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var accountIds = pageRows.Select(x => x.CuentaId).Distinct().ToList();
        var cuentas = await _db.Cuentas.IgnoreQueryFilters().Where(c => accountIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var titularIds = cuentas.Values.Select(c => c.TitularId).Distinct().ToList();
        var titulares = await _db.Titulares.IgnoreQueryFilters().Where(t => titularIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var extractoIds = pageRows.Select(x => x.Id).ToList();
        var extras = await _db.ExtractosColumnasExtra.Where(x => extractoIds.Contains(x.ExtractoId)).ToListAsync(ct);
        var extrasMap = extras.GroupBy(x => x.ExtractoId).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.NombreColumna, v => v.Valor, StringComparer.OrdinalIgnoreCase));
        var desgloseMap = await _db.ExtractosDesgloses
            .Where(x => extractoIds.Contains(x.ExtractoId))
            .GroupBy(x => x.ExtractoId)
            .Select(g => new
            {
                ExtractoId = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.Importe)
            })
            .ToDictionaryAsync(x => x.ExtractoId, ct);

        var data = pageRows.Select(x =>
        {
            var c = cuentas[x.CuentaId];
            var t = titulares[c.TitularId];
            var hasDesglose = desgloseMap.TryGetValue(x.Id, out var desglose);
            var desgloseCount = hasDesglose ? desglose!.Count : 0;
            var desgloseTotal = hasDesglose ? desglose!.Total : 0m;
            // S-2: la fila se filtra con el conjunto efectivo de SU cuenta.
            // "visibles == null" significa que nadie restringe (se muestra todo).
            var visibles = conjuntosPorCuenta.GetValueOrDefault(x.CuentaId)?.EfectivoSet;
            var extrasFila = extrasMap.TryGetValue(x.Id, out var ex) ? ex : [];
            var extrasVisibles = visibles is null
                ? extrasFila
                : extrasFila.Where(kv => visibles.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return new ExtractoListItemResponse
            {
                Id = x.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                BancoNombre = visibles is null || visibles.Contains("banco_nombre") ? c.BancoNombre : null,
                TitularId = t.Id,
                TitularNombre = t.Nombre,
                PaisId = c.PaisId,
                Divisa = c.Divisa,
                Fecha = x.Fecha,
                // S-2: las columnas base que el conjunto efectivo excluye llegan
                // a null para que el VALOR no salga del servidor. Las que no son
                // nullable (fecha, monto, saldo) no se pueden omitir sin romper
                // el contrato de la fila: se dejan tal cual.
                Concepto = visibles is null || visibles.Contains("concepto") ? x.Concepto : null,
                Comentarios = visibles is null || visibles.Contains("comentarios") ? x.Comentarios : null,
                Monto = x.Monto,
                Saldo = x.Saldo,
                FilaNumero = x.FilaNumero,
                Checked = x.Checked,
                CheckedAt = x.CheckedAt,
                CheckedById = x.CheckedById,
                Flagged = x.Flagged,
                FlaggedNota = x.FlaggedNota,
                FlaggedAt = x.FlaggedAt,
                FlaggedById = x.FlaggedById,
                FechaCreacion = x.FechaCreacion,
                FechaModificacion = x.FechaModificacion,
                DeletedAt = x.DeletedAt,
                ColumnasExtra = extrasVisibles,
                DesgloseCount = desgloseCount,
                DesgloseTotal = desgloseTotal,
                DesgloseEstado = GetDesgloseEstado(desgloseCount, desgloseTotal, x.Monto)
            };
        }).ToList();

        return Ok(new PaginatedResponse<ExtractoListItemResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            ColumnasDisponibles = columnasDisponibles
        });
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CreateExtractoRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == req.CuentaId, ct);
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanAdd) return Forbid();

        var isRelational = _db.Database.IsRelational();
        var tx = isRelational ? await _db.Database.BeginTransactionAsync(ct) : null;
        Extracto ex;
        try
        {
            if (isRelational)
            {
                await AcquireFilaNumeroLockAsync(req.CuentaId, ct);
            }

            var maxFila = await _db.Extractos.IgnoreQueryFilters().Where(x => x.CuentaId == req.CuentaId).MaxAsync(x => (int?)x.FilaNumero, ct) ?? 0;
            var fila = req.InsertBeforeFilaNumero.HasValue
                ? Math.Clamp(req.InsertBeforeFilaNumero.Value, 1, maxFila + 1)
                : maxFila + 1;

            if (fila <= maxFila)
            {
                await ShiftFilaNumerosAsync(req.CuentaId, fila, maxFila, ct);
            }

            ex = new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = req.CuentaId,
                Fecha = req.Fecha,
                Concepto = req.Concepto?.Trim(),
                Comentarios = NormalizeOptionalText(req.Comentarios),
                Monto = req.Monto,
                Saldo = req.Saldo,
                FilaNumero = fila,
                UsuarioCreacionId = actor.Id
            };
            _db.Extractos.Add(ex);
            foreach (var item in (req.ColumnasExtra ?? new Dictionary<string, string?>()).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                _db.ExtractosColumnasExtra.Add(new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = ex.Id, NombreColumna = item.Key.Trim(), Valor = item.Value });
            }
            await _db.SaveChangesAsync(ct);

            var changes = new List<(string Col, string? A, string? N)>
            {
                ("fecha", null, ex.Fecha.ToString("dd/MM/yyyy")),
                ("concepto", null, ex.Concepto),
                ("comentarios", null, ex.Comentarios),
                ("monto", null, ex.Monto.ToString()),
                ("saldo", null, ex.Saldo.ToString())
            };
            changes.AddRange((req.ColumnasExtra ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Key)).Select(x => (x.Key.Trim(), (string?)null, x.Value)));
            await SaveCellAudits(ex, actor.Id, "extracto_creado", changes, ct);
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }

        // V-02.08: la evaluacion de alertas es un side effect post-commit; un
        // fallo (p.ej. email de alerta malformado) no debe convertir en 500 una
        // creacion que SI se guardo. Mismo patron que ImportacionService.
        try
        {
            await _alertaService.EvaluateSaldoPostAsync(ex.CuentaId, actor.Id, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception alertEx)
        {
            _logger.LogWarning(alertEx, "Evaluacion de alerta de saldo fallo tras crear extracto {ExtractoId} de cuenta {CuentaId}", ex.Id, ex.CuentaId);
        }

        return Ok(new { id = ex.Id, fila_numero = ex.FilaNumero });
    }

    private async Task AcquireFilaNumeroLockAsync(Guid cuentaId, CancellationToken ct)
        => await AcquireGuidAdvisoryLockAsync(cuentaId, ct);

    private async Task AcquireGuidAdvisoryLockAsync(Guid id, CancellationToken ct)
    {
        var bytes = id.ToByteArray();
        var lockKey = BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], ct);
    }

    private async Task ShiftFilaNumerosAsync(Guid cuentaId, int fromFilaNumero, int maxFilaNumero, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            var offset = maxFilaNumero + 1;
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE ""EXTRACTOS"" SET fila_numero = fila_numero + {offset} WHERE cuenta_id = {cuentaId} AND fila_numero >= {fromFilaNumero}",
                ct);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE ""EXTRACTOS"" SET fila_numero = fila_numero - {maxFilaNumero} WHERE cuenta_id = {cuentaId} AND fila_numero >= {fromFilaNumero + offset}",
                ct);
            return;
        }

        var rows = await _db.Extractos
            .IgnoreQueryFilters()
            .Where(x => x.CuentaId == cuentaId && x.FilaNumero >= fromFilaNumero)
            .OrderByDescending(x => x.FilaNumero)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.FilaNumero += 1;
        }

        await _db.SaveChangesAsync(ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] UpdateExtractoRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        // S-8: el extracto EXISTE (lo acabamos de leer) pero su cuenta no es
        // accesible (borrada): el actor no tiene permiso sobre el. El 404 queda
        // para "no existe".
        if (cuenta is null) return Forbid();
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanEdit) return Forbid();

        var changes = new List<(string Col, string? A, string? N)>();
        var extras = await _db.ExtractosColumnasExtra.Where(x => x.ExtractoId == ex.Id).ToListAsync(ct);
        var extraMap = extras.ToDictionary(x => x.NombreColumna, x => x, StringComparer.OrdinalIgnoreCase);

        try
        {
            if (req.Fecha.HasValue && req.Fecha.Value != ex.Fecha) { EnsureEditable(p, "fecha"); changes.Add(("fecha", ex.Fecha.ToString("dd/MM/yyyy"), req.Fecha.Value.ToString("dd/MM/yyyy"))); ex.Fecha = req.Fecha.Value; }
            if (req.Concepto is not null && !string.Equals(req.Concepto.Trim(), ex.Concepto, StringComparison.Ordinal)) { EnsureEditable(p, "concepto"); changes.Add(("concepto", ex.Concepto, req.Concepto.Trim())); ex.Concepto = req.Concepto.Trim(); }
            if (req.Comentarios is not null)
            {
                var nextComentarios = NormalizeOptionalText(req.Comentarios);
                if (!string.Equals(nextComentarios, ex.Comentarios, StringComparison.Ordinal))
                {
                    EnsureEditable(p, "comentarios");
                    changes.Add(("comentarios", ex.Comentarios, nextComentarios));
                    ex.Comentarios = nextComentarios;
                }
            }
            if (req.Monto.HasValue && req.Monto.Value != ex.Monto) { EnsureEditable(p, "monto"); changes.Add(("monto", ex.Monto.ToString(), req.Monto.Value.ToString())); ex.Monto = req.Monto.Value; }
            if (req.Saldo.HasValue && req.Saldo.Value != ex.Saldo) { EnsureEditable(p, "saldo"); changes.Add(("saldo", ex.Saldo.ToString(), req.Saldo.Value.ToString())); ex.Saldo = req.Saldo.Value; }
            foreach (var kv in (req.ColumnasExtra ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                var key = kv.Key.Trim();
                EnsureEditable(p, key);
                if (extraMap.TryGetValue(key, out var current))
                {
                    if (!string.Equals(current.Valor, kv.Value, StringComparison.Ordinal)) { changes.Add((key, current.Valor, kv.Value)); current.Valor = kv.Value; }
                }
                else
                {
                    changes.Add((key, null, kv.Value));
                    _db.ExtractosColumnasExtra.Add(new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = ex.Id, NombreColumna = key, Valor = kv.Value });
                }
            }
        }
        catch (InvalidOperationException op)
        {
            return BadRequest(new { error = op.Message });
        }

        if (!changes.Any()) return Ok(new { message = "Sin cambios" });
        ex.UsuarioModificacionId = actor.Id;
        ex.FechaModificacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SaveCellAudits(ex, actor.Id, "extracto_celda_actualizada", changes, ct);

        // Mismo patron que Crear: la evaluacion de alertas es un side effect
        // post-guardado. Un fallo (p.ej. email de alerta malformado) no debe
        // convertir en 500 una actualizacion que SI se guardo.
        try
        {
            await _alertaService.EvaluateSaldoPostAsync(ex.CuentaId, actor.Id, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception alertEx)
        {
            _logger.LogWarning(alertEx, "Evaluacion de alerta de saldo fallo tras actualizar extracto {ExtractoId} de cuenta {CuentaId}", ex.Id, ex.CuentaId);
        }

        return Ok(new { message = "Extracto actualizado" });
    }

    [HttpGet("{id:guid}/desglose")]
    public async Task<IActionResult> GetDesglose(Guid id, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        if (!await CanView(actor, ex.CuentaId, ct)) return Forbid();

        var lineas = await _db.ExtractosDesgloses
            .AsNoTracking()
            .Where(x => x.ExtractoId == id)
            .OrderBy(x => x.Orden)
            .Select(x => new ExtractoDesgloseResponse
            {
                Id = x.Id,
                ExtractoId = x.ExtractoId,
                Orden = x.Orden,
                TerceroNombre = x.TerceroNombre,
                Importe = x.Importe,
                Notas = x.Notas,
                FechaCreacion = x.FechaCreacion,
                FechaModificacion = x.FechaModificacion
            })
            .ToListAsync(ct);

        return Ok(BuildDesgloseResumen(ex, lineas));
    }

    [HttpPut("{id:guid}/desglose")]
    public async Task<IActionResult> GuardarDesglose(Guid id, [FromBody] ExtractoDesgloseUpsertRequest req, CancellationToken ct)
    {
        if (req is null || req.Lineas is null) return BadRequest(new { error = "La solicitud de desglose debe incluir lineas." });
        if (string.IsNullOrWhiteSpace(req.Version)) return BadRequest(new { error = "La solicitud de desglose debe incluir version." });
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        // S-8: el extracto EXISTE (lo acabamos de leer) pero su cuenta no es
        // accesible (borrada): el actor no tiene permiso sobre el. El 404 queda
        // para "no existe".
        if (cuenta is null) return Forbid();
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanEdit || !CanEditColumn(p, "desglose")) return Forbid();

        var normalizedLines = new List<NormalizedDesgloseLine>();
        var seenIds = new HashSet<Guid>();
        var requestedLines = req.Lineas;
        if (requestedLines.Count > 500)
        {
            return BadRequest(new { error = "Un desglose no puede superar 500 lineas." });
        }

        for (var i = 0; i < requestedLines.Count; i++)
        {
            var line = requestedLines[i];
            if (line.Id.HasValue && !seenIds.Add(line.Id.Value))
            {
                return BadRequest(new { error = "El desglose contiene lineas duplicadas." });
            }

            var tercero = NormalizeOptionalText(line.TerceroNombre);
            if (tercero is null)
            {
                return BadRequest(new { error = $"La linea {i + 1} necesita nombre de persona o tercero." });
            }

            if (line.Importe == 0m)
            {
                return BadRequest(new { error = $"La linea {i + 1} necesita un importe distinto de cero." });
            }

            normalizedLines.Add(new NormalizedDesgloseLine(line.Id, i + 1, tercero, line.Importe, NormalizeOptionalText(line.Notas)));
        }

        var isRelational = _db.Database.IsRelational();
        var tx = isRelational ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            if (isRelational)
            {
                await AcquireGuidAdvisoryLockAsync(id, ct);
            }

            var current = await _db.ExtractosDesgloses
                .IgnoreQueryFilters()
                .Where(x => x.ExtractoId == id)
                .ToListAsync(ct);
            var activeCurrent = current.Where(x => x.DeletedAt is null).ToList();
            var currentVersion = BuildDesgloseVersion(activeCurrent);
            if (!string.Equals(req.Version, currentVersion, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    error = "El desglose fue modificado por otro usuario. Recarga los datos y vuelve a intentarlo.",
                    code = "desglose_concurrency_conflict"
                });
            }

            var currentById = activeCurrent.ToDictionary(x => x.Id);
            var beforeSummary = BuildDesgloseAuditSummary(ex.Monto, activeCurrent);

            foreach (var requestedId in normalizedLines.Where(x => x.Id.HasValue).Select(x => x.Id!.Value))
            {
                if (!currentById.ContainsKey(requestedId))
                {
                    return BadRequest(new { error = "El desglose contiene una linea que no pertenece a este extracto." });
                }
            }

            var keptIds = normalizedLines.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();
            foreach (var line in activeCurrent.Where(x => !keptIds.Contains(x.Id)))
            {
                line.DeletedAt = DateTime.UtcNow;
                line.DeletedById = actor.Id;
                line.UsuarioModificacionId = actor.Id;
                line.FechaModificacion = DateTime.UtcNow;
            }

            var keptExisting = normalizedLines
                .Where(x => x.Id.HasValue)
                .Select((line, index) => new { Line = line, Entity = currentById[line.Id!.Value], Index = index })
                .ToList();
            foreach (var item in keptExisting)
            {
                // Evita colisiones del indice unico (extracto_id, orden) al borrar,
                // insertar o intercambiar ordenes en un mismo reemplazo.
                item.Entity.Orden = -100_000 - item.Index;
            }

            if (activeCurrent.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            foreach (var line in normalizedLines)
            {
                if (line.Id.HasValue && currentById.TryGetValue(line.Id.Value, out var existing))
                {
                    existing.Orden = line.Orden;
                    existing.TerceroNombre = line.TerceroNombre;
                    existing.Importe = line.Importe;
                    existing.Notas = line.Notas;
                    existing.UsuarioModificacionId = actor.Id;
                    existing.FechaModificacion = DateTime.UtcNow;
                    continue;
                }

                _db.ExtractosDesgloses.Add(new ExtractoDesglose
                {
                    Id = Guid.NewGuid(),
                    ExtractoId = id,
                    Orden = line.Orden,
                    TerceroNombre = line.TerceroNombre,
                    Importe = line.Importe,
                    Notas = line.Notas,
                    UsuarioCreacionId = actor.Id
                });
            }

            await _db.SaveChangesAsync(ct);
            var updated = await _db.ExtractosDesgloses
                .Where(x => x.ExtractoId == id)
                .OrderBy(x => x.Orden)
                .ToListAsync(ct);
            var afterSummary = BuildDesgloseAuditSummary(ex.Monto, updated);
            if (!string.Equals(beforeSummary, afterSummary, StringComparison.Ordinal))
            {
                await SaveAudit(actor.Id, "extracto_desglose_actualizado", ex.Id, "desglose", $"DES{ex.FilaNumero}", beforeSummary, afterSummary, ct);
            }

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            var responseLines = updated
                .OrderBy(x => x.Orden)
                .Select(MapDesgloseLine)
                .ToList();
            return Ok(BuildDesgloseResumen(ex, responseLines));
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    [HttpPatch("{id:guid}/check")]
    public async Task<IActionResult> ToggleCheck(Guid id, [FromBody] ToggleCheckedRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        // S-8: el extracto EXISTE (lo acabamos de leer) pero su cuenta no es
        // accesible (borrada): el actor no tiene permiso sobre el. El 404 queda
        // para "no existe".
        if (cuenta is null) return Forbid();
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanEdit || !CanEditColumn(p, "checked")) return Forbid();

        var old = ex.Checked;
        ex.Checked = req.Checked;
        ex.CheckedAt = req.Checked ? DateTime.UtcNow : null;
        ex.CheckedById = req.Checked ? actor.Id : null;
        ex.UsuarioModificacionId = actor.Id;
        ex.FechaModificacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SaveAudit(actor.Id, "extracto_toggle_check", ex.Id, "checked", $"CHK{ex.FilaNumero}", old.ToString(), ex.Checked.ToString(), ct);
        return Ok(new { message = "Movimiento marcado como revisado." });
    }

    [HttpPatch("{id:guid}/flag")]
    public async Task<IActionResult> ToggleFlag(Guid id, [FromBody] ToggleFlagRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { error = "La solicitud para marcar el movimiento esta incompleta." });
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        // S-8: el extracto EXISTE (lo acabamos de leer) pero su cuenta no es
        // accesible (borrada): el actor no tiene permiso sobre el. El 404 queda
        // para "no existe".
        if (cuenta is null) return Forbid();
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanEdit) return Forbid();

        var oldFlag = ex.Flagged;
        var oldNote = ex.FlaggedNota;
        var newNote = req.Flagged ? req.Nota?.Trim() : null;
        if (oldFlag != req.Flagged && !CanEditColumn(p, "flagged")) return Forbid();
        if (!string.Equals(oldNote, newNote, StringComparison.Ordinal) && !CanEditColumn(p, "flagged_nota")) return Forbid();

        ex.Flagged = req.Flagged;
        ex.FlaggedNota = newNote;
        ex.FlaggedAt = req.Flagged ? DateTime.UtcNow : null;
        ex.FlaggedById = req.Flagged ? actor.Id : null;
        ex.UsuarioModificacionId = actor.Id;
        ex.FechaModificacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SaveAudit(actor.Id, "extracto_toggle_flag", ex.Id, "flagged", $"FLG{ex.FilaNumero}", oldFlag.ToString(), ex.Flagged.ToString(), ct);
        if (!string.Equals(oldNote, ex.FlaggedNota, StringComparison.Ordinal))
        {
            await SaveAudit(actor.Id, "extracto_toggle_flag", ex.Id, "flagged_nota", $"FLG{ex.FilaNumero}", oldNote, ex.FlaggedNota, ct);
        }
        return Ok(new { message = "Alerta del movimiento actualizada." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        // S-8: el extracto EXISTE (lo acabamos de leer) pero su cuenta no es
        // accesible (borrada): el actor no tiene permiso sobre el. El 404 queda
        // para "no existe".
        if (cuenta is null) return Forbid();
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanDelete) return Forbid();
        ex.DeletedAt = DateTime.UtcNow;
        ex.DeletedById = actor.Id;
        ex.UsuarioModificacionId = actor.Id;
        ex.FechaModificacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SaveAudit(actor.Id, "extracto_eliminado", ex.Id, null, null, null, null, ct);
        return Ok(new { message = "Extracto eliminado" });
    }

    [HttpPost("{id:guid}/restaurar")]
    [Authorize(Roles = "ADMIN,GERENTE")]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        // S-8: el extracto EXISTE (lo acabamos de leer) pero su cuenta no es
        // accesible (borrada): el actor no tiene permiso sobre el. El 404 queda
        // para "no existe".
        if (cuenta is null) return Forbid();
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanDelete) return Forbid();
        ex.DeletedAt = null;
        ex.DeletedById = null;
        ex.UsuarioModificacionId = actor.Id;
        ex.FechaModificacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SaveAudit(actor.Id, "extracto_restaurado", ex.Id, null, null, null, null, ct);
        return Ok(new { message = "Extracto restaurado" });
    }

    [HttpGet("{id:guid}/audit-celda")]
    public async Task<IActionResult> GetAuditCelda(Guid id, [FromQuery] string? columna = null, [FromQuery] int top = 50, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        // S-8: el extracto existe (esta en la papelera) y el actor no es admin:
        // no tiene permiso sobre el. 404 seria "no existe".
        if (ex.DeletedAt.HasValue && !actor.IsAdmin) return Forbid();
        if (!await CanView(actor, ex.CuentaId, ct)) return Forbid();

        var q = _db.Auditorias.Where(a => a.EntidadTipo == "EXTRACTOS" && a.EntidadId == id);
        if (!string.IsNullOrWhiteSpace(columna))
        {
            var col = columna.Trim().ToLowerInvariant();
            q = q.Where(a => (a.ColumnaNombre ?? "").ToLower() == col);
        }

        var data = await q.OrderByDescending(a => a.Timestamp).Take(Math.Clamp(top, 1, 200)).Select(a => new AuditCellEntryResponse
        {
            Id = a.Id,
            TipoAccion = a.TipoAccion,
            CeldaReferencia = a.CeldaReferencia,
            ColumnaNombre = a.ColumnaNombre,
            ValorAnterior = a.ValorAnterior,
            ValorNuevo = a.ValorNuevo,
            Timestamp = a.Timestamp,
            UsuarioId = a.UsuarioId
        }).ToListAsync(ct);

        return Ok(data);
    }

    [HttpGet("cuentas/{cuentaId:guid}/resumen")]
    public async Task<IActionResult> GetCuentaResumen(Guid cuentaId, [FromQuery] string periodo = "1m", [FromQuery] Guid? paisId = null, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (!await CanView(actor, cuentaId, ct)) return Forbid();
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == cuentaId, ct);
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
        if (paisId.HasValue && cuenta.PaisId != paisId.Value) return NotFound(new { error = "Cuenta no encontrada en el pais activo" });
        var titular = await _db.Titulares.Where(t => t.Id == cuenta.TitularId).Select(t => t.Nombre).FirstOrDefaultAsync(ct);
        var resumen = await BuildSummariesAsync(actor, [new ResumenCuenta(cuenta, cuenta.TitularId, titular ?? string.Empty)], periodo, ct);
        return Ok(resumen[0]);
    }

    [HttpGet("titulares/{titularId:guid}/cuentas")]
    public async Task<IActionResult> GetCuentasTitular(Guid titularId, [FromQuery] string periodo = "1m", [FromQuery] Guid? paisId = null, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (!await CanViewTitular(actor, titularId, ct)) return Forbid();
        var titular = await _db.Titulares.FirstOrDefaultAsync(t => t.Id == titularId, ct);
        if (titular is null) return NotFound(new { error = "Titular no encontrado" });
        var allowed = await GetAllowedAccountIds(await GetScopeAsync(actor, ct), ct, paisId);
        var cuentas = await _db.Cuentas.Where(c => c.TitularId == titularId && allowed.Contains(c.Id)).ToListAsync(ct);
        // R-7: un solo resumen agrupado para todas las cuentas del titular, en
        // vez de una consulta por cuenta dentro de un bucle.
        var summary = await BuildSummariesAsync(actor, cuentas.Select(c => new ResumenCuenta(c, titular.Id, titular.Nombre)).ToList(), periodo, ct);
        return Ok(new TitularConCuentasResponse { TitularId = titular.Id, TitularNombre = titular.Nombre, Cuentas = summary });
    }

    [HttpGet("titulares-resumen")]
    public async Task<IActionResult> GetTitularesResumen([FromQuery] string periodo = "1m", [FromQuery] Guid? paisId = null, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var allowed = await GetAllowedAccountIds(await GetScopeAsync(actor, ct), ct, paisId);
        var cuentas = await _db.Cuentas.Where(c => allowed.Contains(c.Id)).ToListAsync(ct);
        var titularesIds = cuentas.Select(c => c.TitularId).Distinct().ToList();
        var titulares = await _db.Titulares.Where(t => titularesIds.Contains(t.Id)).OrderBy(t => t.Nombre).ToListAsync(ct);
        var nombrePorTitular = titulares.ToDictionary(t => t.Id, t => t.Nombre);
        // R-7: un unico resumen agrupado para todas las cuentas de todos los
        // titulares, en vez de una consulta por cuenta dentro de un bucle.
        var entradas = cuentas
            .Where(c => nombrePorTitular.ContainsKey(c.TitularId))
            .Select(c => new ResumenCuenta(c, c.TitularId, nombrePorTitular[c.TitularId]))
            .ToList();
        var resumenes = await BuildSummariesAsync(actor, entradas, periodo, ct);
        var porCuenta = resumenes.ToDictionary(r => r.CuentaId);
        var outData = new List<TitularConCuentasResponse>();
        foreach (var t in titulares)
        {
            var s = new List<CuentaResumenKpiResponse>();
            foreach (var c in cuentas.Where(c => c.TitularId == t.Id))
            {
                s.Add(porCuenta[c.Id]);
            }
            outData.Add(new TitularConCuentasResponse { TitularId = t.Id, TitularNombre = t.Nombre, Cuentas = s });
        }
        return Ok(outData);
    }

    [HttpGet("columnas-visibles")]
    public async Task<IActionResult> GetColumnasVisibles(
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] Guid? titularId = null,
        [FromQuery] Guid? paisId = null,
        CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var scope = await ResolvePreferenciaScope(actor, cuentaId, titularId, paisId, ct);
        if (scope.Forbidden) return Forbid();
        if (scope.NotFound) return NotFound(new { error = "Cuenta no encontrada" });

        var conjunto = await GetConjuntoColumnasAsync(actor.Id, scope, ct);
        var pref = await QueryPreferenciaUsuarioCuenta(actor.Id, scope).FirstOrDefaultAsync(ct);

        // S-2: columnas_visibles es el conjunto EFECTIVO (regla del admin ∩
        // preferencia del usuario), no solo la preferencia del usuario.
        // columnas_restringidas es la whitelist del administrador, para que el
        // cliente no ofrezca en su interruptor columnas que el admin prohibe.
        return Ok(new
        {
            columnas_visibles = conjunto.Efectivo,
            orden_columnas = ParseArray(pref?.OrdenColumnas),
            columnas_restringidas = conjunto.Admin
        });
    }

    [HttpPut("columnas-visibles")]
    public async Task<IActionResult> SaveColumnasVisibles([FromBody] SaveColumnasVisiblesRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var scope = await ResolvePreferenciaScope(actor, req.CuentaId, req.TitularId, req.PaisId, ct);
        if (scope.Forbidden) return Forbid();
        if (scope.NotFound) return NotFound(new { error = "Cuenta no encontrada" });

        var conjunto = await GetConjuntoColumnasAsync(actor.Id, scope, ct);
        var pref = await QueryPreferenciaUsuarioCuenta(actor.Id, scope).FirstOrDefaultAsync(ct);

        if (pref is null)
        {
            pref = new PreferenciaUsuarioCuenta
            {
                Id = Guid.NewGuid(),
                UsuarioId = actor.Id,
                PaisId = scope.PaisId,
                TitularId = scope.TitularId,
                CuentaId = scope.CuentaId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.PreferenciasUsuarioCuenta.Add(pref);
        }

        // S-2: lo guardado nunca puede ser mas ancho que la regla del
        // administrador (si el admin no restringe, se guarda lo pedido tal cual).
        var columnas = req.ColumnasVisibles;
        if (columnas is not null && conjunto.Admin is not null)
        {
            var permitidas = new HashSet<string>(conjunto.Admin, StringComparer.OrdinalIgnoreCase);
            columnas = [.. columnas.Where(permitidas.Contains)];
        }

        pref.ColumnasVisibles = columnas is null ? null : JsonSerializer.Serialize(columnas);
        if (req.OrdenColumnas is not null)
            pref.OrdenColumnas = JsonSerializer.Serialize(req.OrdenColumnas);
        pref.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Preferencias guardadas" });
    }

    /// <summary>
    /// R-7: resumen KPI de un conjunto de cuentas con un numero de consultas
    /// que NO depende del numero de cuentas. Antes se llamaba a BuildSummary
    /// dentro de un bucle por cuenta y cada iteracion lanzaba ~5 consultas
    /// (N+1). Ahora se agregan todas las cuentas en un solo grupo de
    /// consultas y se reparte el resultado en memoria.
    ///
    /// El resultado JSON es el mismo que el de la version por cuenta: mismos
    /// importes, mismo orden y mismos KPIs.
    /// </summary>
    private async Task<List<CuentaResumenKpiResponse>> BuildSummariesAsync(Actor actor, IReadOnlyList<ResumenCuenta> entradas, string periodo, CancellationToken ct)
    {
        var resultados = new List<CuentaResumenKpiResponse>(entradas.Count);
        if (entradas.Count == 0)
        {
            return resultados;
        }

        var cuentaIds = entradas.Select(e => e.Cuenta.Id).ToList();
        var meses = GetPeriodoMeses(NormalizePeriodo(periodo));

        // 1) Ultimo movimiento de cada cuenta (mayor fila_numero).
        var maxFilaPorCuenta = await _db.Extractos
            .Where(e => cuentaIds.Contains(e.CuentaId))
            .GroupBy(e => e.CuentaId)
            .Select(g => new { CuentaId = g.Key, MaxFila = g.Max(e => e.FilaNumero) })
            .ToDictionaryAsync(x => x.CuentaId, x => x.MaxFila, ct);

        // 2) Fecha y saldo de ese ultimo movimiento. La consulta se resuelve
        // en memoria porque el maximo de fila_numero es distinto por cuenta.
        var filasUltimoMovimiento = new Dictionary<Guid, (DateOnly Fecha, decimal Saldo)>();
        if (maxFilaPorCuenta.Count > 0)
        {
            var maxFilas = maxFilaPorCuenta.Values.Distinct().ToList();
            var candidatas = await _db.Extractos
                .Where(e => cuentaIds.Contains(e.CuentaId) && maxFilas.Contains(e.FilaNumero))
                .Select(e => new { e.CuentaId, e.FilaNumero, e.Fecha, e.Saldo })
                .ToListAsync(ct);
            foreach (var fila in candidatas)
            {
                if (fila.FilaNumero != maxFilaPorCuenta[fila.CuentaId]) continue;
                if (!filasUltimoMovimiento.TryGetValue(fila.CuentaId, out var actual) || fila.Fecha > actual.Fecha)
                {
                    filasUltimoMovimiento[fila.CuentaId] = (fila.Fecha, fila.Saldo);
                }
            }
        }

        // 3) Ventana de cada cuenta: [ultimo movimiento - N meses, ultimo
        // movimiento]. Las cuentas sin extractos no tienen ventana: sus
        // importes son 0, como en la version por cuenta.
        var ventanas = new Dictionary<Guid, (DateOnly Desde, DateOnly Hasta)>();
        foreach (var entrada in entradas)
        {
            if (!filasUltimoMovimiento.TryGetValue(entrada.Cuenta.Id, out var ultimo)) continue;
            var hasta = ultimo.Fecha;
            ventanas[entrada.Cuenta.Id] = (hasta.AddMonths(-meses), hasta);
        }

        var ingresosPorCuenta = new Dictionary<Guid, decimal>();
        var egresosPorCuenta = new Dictionary<Guid, decimal>();
        if (ventanas.Count > 0)
        {
            var minDesde = ventanas.Values.Min(v => v.Desde);
            var maxHasta = ventanas.Values.Max(v => v.Hasta);

            // 4) Importes del periodo, agrupados por cuenta y dia (una
            // consulta por signo). El dia es la granularidad exacta: la
            // ventana se define por fecha, no por hora.
            var ingresosPorDia = await _db.Extractos
                .Where(e => cuentaIds.Contains(e.CuentaId) && e.Fecha >= minDesde && e.Fecha <= maxHasta && e.Monto > 0)
                .GroupBy(e => new { e.CuentaId, e.Fecha })
                .Select(g => new { g.Key.CuentaId, g.Key.Fecha, Total = g.Sum(x => x.Monto) })
                .ToListAsync(ct);
            var egresosPorDia = await _db.Extractos
                .Where(e => cuentaIds.Contains(e.CuentaId) && e.Fecha >= minDesde && e.Fecha <= maxHasta && e.Monto < 0)
                .GroupBy(e => new { e.CuentaId, e.Fecha })
                .Select(g => new { g.Key.CuentaId, g.Key.Fecha, Total = g.Sum(x => x.Monto) })
                .ToListAsync(ct);

            foreach (var cuentaId in ventanas.Keys)
            {
                var ventana = ventanas[cuentaId];
                ingresosPorCuenta[cuentaId] = ingresosPorDia
                    .Where(x => x.CuentaId == cuentaId && x.Fecha >= ventana.Desde && x.Fecha <= ventana.Hasta)
                    .Sum(x => x.Total);
                egresosPorCuenta[cuentaId] = egresosPorDia
                    .Where(x => x.CuentaId == cuentaId && x.Fecha >= ventana.Desde && x.Fecha <= ventana.Hasta)
                    .Sum(x => x.Total);
            }
        }

        // 5) Ultima modificacion de cada cuenta.
        var ultimaModificacion = await _db.Extractos
            .Where(e => cuentaIds.Contains(e.CuentaId))
            .GroupBy(e => e.CuentaId)
            .Select(g => new { CuentaId = g.Key, Ultima = g.Max(e => (DateTime?)(e.FechaModificacion ?? e.FechaCreacion)) })
            .ToDictionaryAsync(x => x.CuentaId, x => x.Ultima, ct);

        // 6) Nombre del pais de cada cuenta (una sola consulta).
        var paisIds = entradas
            .Select(e => e.Cuenta.PaisId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var paises = paisIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Paises.IgnoreQueryFilters().Where(p => paisIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        // 7) Plazo fijo de las cuentas que lo tienen (una sola consulta).
        var cuentasConPlazo = entradas
            .Where(e => e.Cuenta.TipoCuenta == TipoCuenta.PLAZO_FIJO)
            .Select(e => e.Cuenta.Id)
            .ToList();
        var plazos = new Dictionary<Guid, PlazoFijoResponse>();
        if (cuentasConPlazo.Count > 0)
        {
            var filasPlazo = await (
                from plazo in _db.PlazosFijos
                join refCuenta in (actor.IsAdmin ? _db.Cuentas.IgnoreQueryFilters() : _db.Cuentas) on plazo.CuentaReferenciaId equals refCuenta.Id into refJoin
                from cuentaReferencia in refJoin.DefaultIfEmpty()
                where cuentasConPlazo.Contains(plazo.CuentaId)
                select new PlazoFijoResponse
                {
                    Id = plazo.Id,
                    CuentaId = plazo.CuentaId,
                    CuentaReferenciaId = cuentaReferencia != null ? plazo.CuentaReferenciaId : null,
                    CuentaReferenciaNombre = cuentaReferencia != null ? cuentaReferencia.Nombre : null,
                    FechaInicio = plazo.FechaInicio,
                    FechaVencimiento = plazo.FechaVencimiento,
                    InteresPrevisto = plazo.InteresPrevisto,
                    Renovable = plazo.Renovable,
                    Estado = plazo.Estado.ToString(),
                    FechaUltimaNotificacion = plazo.FechaUltimaNotificacion,
                    FechaRenovacion = plazo.FechaRenovacion,
                    Notas = plazo.Notas
                })
                .ToListAsync(ct);
            foreach (var fila in filasPlazo)
            {
                plazos[fila.CuentaId] = fila;
            }
        }

        // 8) Las cuentas de referencia que el usuario no puede ver se ocultan.
        // El conjunto de cuentas visibles se calcula una sola vez para todo el
        // lote, no una vez por cuenta.
        HashSet<Guid>? cuentasVisibles = null;
        if (!actor.IsAdmin && plazos.Values.Any(p => p.CuentaReferenciaId.HasValue))
        {
            cuentasVisibles = await GetAllowedAccountIds(await GetScopeAsync(actor, ct), ct);
        }

        foreach (var entrada in entradas)
        {
            var cuenta = entrada.Cuenta;
            filasUltimoMovimiento.TryGetValue(cuenta.Id, out var ultimo);
            var plazoFijo = plazos.GetValueOrDefault(cuenta.Id);

            if (plazoFijo?.CuentaReferenciaId is Guid referenceId && cuentasVisibles is not null && !cuentasVisibles.Contains(referenceId))
            {
                plazoFijo.CuentaReferenciaId = null;
                plazoFijo.CuentaReferenciaNombre = null;
            }

            resultados.Add(new CuentaResumenKpiResponse
            {
                CuentaId = cuenta.Id,
                CuentaNombre = cuenta.Nombre,
                // V-02-07: enmascarado en KPI de cuenta/titular (respuesta agregada, no se usa para editar).
                Iban = PiiMasking.MaskIban(cuenta.Iban),
                BancoNombre = cuenta.BancoNombre,
                Divisa = cuenta.Divisa,
                PaisId = cuenta.PaisId,
                PaisNombre = cuenta.PaisId.HasValue && paises.TryGetValue(cuenta.PaisId.Value, out var paisNombre) ? paisNombre : null,
                TitularId = entrada.TitularId,
                TitularNombre = entrada.TitularNombre,
                EsEfectivo = cuenta.EsEfectivo,
                TipoCuenta = cuenta.TipoCuenta.ToString(),
                PlazoFijo = plazoFijo,
                Notas = cuenta.Notas,
                SaldoActual = ultimo.Saldo,
                IngresosMes = ingresosPorCuenta.GetValueOrDefault(cuenta.Id),
                EgresosMes = Math.Abs(egresosPorCuenta.GetValueOrDefault(cuenta.Id)),
                UltimaActualizacion = ultimaModificacion.GetValueOrDefault(cuenta.Id)
            });
        }

        return resultados;
    }

    private static string NormalizePeriodo(string? periodo)
    {
        var normalized = (periodo ?? "1m").Trim().ToLowerInvariant();
        return normalized switch
        {
            "1m" => "1m",
            "3m" => "3m",
            "6m" => "6m",
            "9m" => "9m",
            "12m" => "12m",
            "18m" => "18m",
            "24m" => "24m",
            _ => "1m"
        };
    }

    private static DateOnly GetPeriodStart(string periodo, DateOnly today)
    {
        return today.AddMonths(-GetPeriodoMeses(periodo));
    }

    // R-7: los meses del periodo se usan tanto para calcular el inicio de la
    // ventana como para calcularla por cuenta en el resumen agrupado.
    private static int GetPeriodoMeses(string periodo)
    {
        return periodo switch
        {
            "1m" => 1,
            "3m" => 3,
            "6m" => 6,
            "9m" => 9,
            "12m" => 12,
            "18m" => 18,
            "24m" => 24,
            _ => 1
        };
    }

    private static ExtractoDesgloseResumenResponse BuildDesgloseResumen(Extracto ex, IReadOnlyList<ExtractoDesgloseResponse> lineas)
    {
        var total = lineas.Sum(x => x.Importe);
        return new ExtractoDesgloseResumenResponse
        {
            ExtractoId = ex.Id,
            ExtractoMonto = ex.Monto,
            Count = lineas.Count,
            Total = total,
            Diferencia = ex.Monto - total,
            Estado = GetDesgloseEstado(lineas.Count, total, ex.Monto),
            Version = BuildDesgloseVersion(lineas),
            Lineas = lineas
        };
    }

    private static string BuildDesgloseVersion(IReadOnlyCollection<ExtractoDesglose> lineas)
    {
        var responseLines = lineas
            .Where(x => x.DeletedAt is null)
            .OrderBy(x => x.Orden)
            .Select(MapDesgloseLine)
            .ToList();
        return BuildDesgloseVersion(responseLines);
    }

    private static string BuildDesgloseVersion(IReadOnlyList<ExtractoDesgloseResponse> lineas)
    {
        var payload = string.Join('\n', lineas
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Id)
            .Select(x => string.Join('\u001f',
                x.Id,
                x.Orden,
                x.TerceroNombre,
                x.Importe.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                x.Notas ?? string.Empty)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ExtractoDesgloseResponse MapDesgloseLine(ExtractoDesglose line)
    {
        return new ExtractoDesgloseResponse
        {
            Id = line.Id,
            ExtractoId = line.ExtractoId,
            Orden = line.Orden,
            TerceroNombre = line.TerceroNombre,
            Importe = line.Importe,
            Notas = line.Notas,
            FechaCreacion = line.FechaCreacion,
            FechaModificacion = line.FechaModificacion
        };
    }

    private static string GetDesgloseEstado(int count, decimal total, decimal extractoMonto)
    {
        if (count == 0)
        {
            return "sin_desglose";
        }

        return Math.Round(total, 4) == Math.Round(extractoMonto, 4)
            ? "cuadrado"
            : "descuadrado";
    }

    private static string BuildDesgloseAuditSummary(decimal extractoMonto, IReadOnlyCollection<ExtractoDesglose> lineas)
    {
        var activeLines = lineas.Where(x => x.DeletedAt is null).ToList();
        var total = activeLines.Sum(x => x.Importe);
        var estado = GetDesgloseEstado(activeLines.Count, total, extractoMonto);
        return $"{activeLines.Count} lineas | total {total:0.####} | diferencia {extractoMonto - total:0.####} | {estado}";
    }

    private async Task SaveCellAudits(Extracto ex, Guid? userId, string action, IReadOnlyList<(string Col, string? A, string? N)> changes, CancellationToken ct)
    {
        if (changes.Count == 0) return;
        var extraCols = await _db.ExtractosColumnasExtra.Where(x => x.ExtractoId == ex.Id).Select(x => x.NombreColumna).ToListAsync(ct);
        extraCols.AddRange(changes.Where(x => !IsBase(x.Col)).Select(x => x.Col));
        var ordered = extraCols.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        // V-02-05 (HIGH-9): un solo SaveChanges para todas las auditorias de celda.
        // Antes: N SaveChanges (uno por cambio). Ahora: AddRange + 1 SaveChanges.
        var timestamp = DateTime.UtcNow;
        var ip = HttpContext.Connection.RemoteIpAddress;
        foreach (var ch in changes)
        {
            var idx = ch.Col.ToLowerInvariant() switch
            {
                "fecha" => 1,
                "concepto" => 2,
                "comentarios" => 3,
                "monto" => 4,
                "saldo" => 5,
                _ => 6 + Math.Max(0, ordered.FindIndex(x => x.Equals(ch.Col, StringComparison.OrdinalIgnoreCase)))
            };
            _db.Auditorias.Add(new Auditoria
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                TipoAccion = action,
                EntidadTipo = "EXTRACTOS",
                EntidadId = ex.Id,
                ColumnaNombre = ch.Col,
                CeldaReferencia = $"{ToExcel(idx)}{ex.FilaNumero}",
                ValorAnterior = ch.A,
                ValorNuevo = ch.N,
                Timestamp = timestamp,
                IpAddress = ip
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SaveAudit(Guid? userId, string action, Guid entityId, string? col, string? cell, string? before, string? after, CancellationToken ct)
    {
        _db.Auditorias.Add(new Auditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TipoAccion = action,
            EntidadTipo = "EXTRACTOS",
            EntidadId = entityId,
            ColumnaNombre = col,
            CeldaReferencia = cell,
            ValorAnterior = before,
            ValorNuevo = after,
            Timestamp = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress
        });
        await _db.SaveChangesAsync(ct);
    }

    // M-5: el alcance (que cuentas y titulares ve el actor) lo decide
    // UserAccessService. El controller ya no reimplementa la regla: solo
    // aplica el filtro por pais encima del alcance.
    private async Task<HashSet<Guid>> GetAllowedAccountIds(UserAccessScope scope, CancellationToken ct, Guid? paisId = null)
    {
        var ids = await _userAccessService
            .ApplyCuentaScope(_db.Cuentas.AsNoTracking(), scope)
            .ApplyPaisScope(paisId)
            .Select(c => c.Id)
            .ToListAsync(ct);
        return [.. ids];
    }

    private Task<UserAccessScope> GetScopeAsync(Actor actor, CancellationToken ct)
        => _userAccessService.GetScopeAsync(actor.Id, actor.IsAdmin ? nameof(RolUsuario.ADMIN) : string.Empty, ct);

    private async Task<bool> CanView(Actor actor, Guid cuentaId, CancellationToken ct)
        => await _userAccessService.CanAccessCuentaAsync(cuentaId, await GetScopeAsync(actor, ct), ct);

    private async Task<bool> CanViewTitular(Actor actor, Guid titularId, CancellationToken ct)
        => await _userAccessService.CanAccessTitularAsync(titularId, await GetScopeAsync(actor, ct), ct);

    private async Task<Perm> GetPermission(Actor actor, Cuenta cuenta, CancellationToken ct)
    {
        if (actor.IsAdmin) return new Perm { CanAdd = true, CanEdit = true, CanDelete = true, EditableCols = null };
        if (!await _db.Titulares.AnyAsync(t => t.Id == cuenta.TitularId && t.DeletedAt == null, ct)) return new Perm();
        var rows = await _db.PermisosUsuario
            .Where(p => p.UsuarioId == actor.Id)
            .Where(p => p.PaisId == null || p.PaisId == cuenta.PaisId)
            .Where(p => p.CuentaId == null || p.CuentaId == cuenta.Id)
            .Where(p => p.TitularId == null || p.TitularId == cuenta.TitularId)
            .ToListAsync(ct);
        if (!rows.Any()) return new Perm();

        var editableRows = rows.Where(r => r.PuedeEditarLineas).ToList();
        List<PreferenciaUsuarioCuenta> prefRows = [];
        if (editableRows.Count > 0)
        {
            prefRows = await _db.PreferenciasUsuarioCuenta
                .Where(p => p.UsuarioId == actor.Id)
                .ToListAsync(ct);
        }
        var cols = ResolveEditableColumns(editableRows, prefRows);

        return new Perm { CanAdd = rows.Any(r => r.PuedeAgregarLineas), CanEdit = rows.Any(r => r.PuedeEditarLineas), CanDelete = rows.Any(r => r.PuedeEliminarLineas), EditableCols = cols };
    }

    private async Task<PreferenciaScope> ResolvePreferenciaScope(Actor actor, Guid? cuentaId, Guid? titularId, Guid? paisId, CancellationToken ct)
    {
        if (!cuentaId.HasValue)
        {
            if (titularId.HasValue && !await CanViewTitular(actor, titularId.Value, ct))
            {
                return PreferenciaScope.Forbid();
            }

            return new PreferenciaScope(paisId, titularId, null, false, false);
        }

        var cuenta = await _db.Cuentas
            .AsNoTracking()
            .Where(c => c.Id == cuentaId.Value)
            .Select(c => new { c.Id, c.TitularId, c.PaisId })
            .FirstOrDefaultAsync(ct);
        if (cuenta is null)
        {
            return PreferenciaScope.Missing();
        }

        if (!await CanView(actor, cuenta.Id, ct))
        {
            return PreferenciaScope.Forbid();
        }

        if (paisId.HasValue && cuenta.PaisId != paisId.Value)
        {
            return PreferenciaScope.Missing();
        }

        if (titularId.HasValue && cuenta.TitularId != titularId.Value)
        {
            return PreferenciaScope.Missing();
        }

        return new PreferenciaScope(cuenta.PaisId, cuenta.TitularId, cuenta.Id, false, false);
    }

    private IQueryable<PreferenciaUsuarioCuenta> QueryPreferenciaUsuarioCuenta(Guid usuarioId, PreferenciaScope scope)
    {
        var query = _db.PreferenciasUsuarioCuenta.Where(p => p.UsuarioId == usuarioId);

        query = scope.PaisId.HasValue
            ? query.Where(p => p.PaisId == scope.PaisId.Value)
            : query.Where(p => p.PaisId == null);

        query = scope.TitularId.HasValue
            ? query.Where(p => p.TitularId == scope.TitularId.Value)
            : query.Where(p => p.TitularId == null);

        query = scope.CuentaId.HasValue
            ? query.Where(p => p.CuentaId == scope.CuentaId.Value)
            : query.Where(p => p.CuentaId == null);

        return query;
    }

    // ---------------------------------------------------------------------
    // S-2: conjunto efectivo de columnas visibles.
    //
    //   admin   = whitelist que fija el administrador (ColumnasVisiblesAdmin).
    //             null = "el administrador no restringe".
    //   usuario = preferencia de vista del usuario (ColumnasVisibles).
    //             null = "el usuario no restringe".
    //   efectivo = admin == null ? usuario
    //            : usuario == null ? admin
    //            : admin ∩ usuario.  Si ambos son null, efectivo = null
    //            (sin restriccion: se muestran todas).
    //
    // La regla del administrador puede vivir en una fila de preferencia de
    // alcance MAS AMPLIO (pais o titular) que la vista (cuenta), asi que se
    // recorren los alcances de mas especifico a mas amplio. Varias reglas del
    // administrador se combinan por INTERSECCION: son restricciones y una
    // union podria ampliar la vista por encima de lo permitido en el alcance
    // mas estrecho.
    // ---------------------------------------------------------------------

    private async Task<ConjuntoColumnas> GetConjuntoColumnasAsync(Guid usuarioId, PreferenciaScope scope, CancellationToken ct)
    {
        var prefRows = await _db.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == usuarioId)
            .ToListAsync(ct);
        return BuildConjuntoColumnas(prefRows, scope);
    }

    // S-2: el filtro de columnas se resuelve POR CUENTA, porque cada cuenta
    // puede tener su propia regla del administrador. El admin no restringe
    // (bypass): el mapa vuelve vacio y no se filtra nada.
    private async Task<Dictionary<Guid, ConjuntoColumnas>> GetConjuntosPorCuentaAsync(Actor actor, IEnumerable<Guid> cuentaIds, CancellationToken ct)
    {
        var resultado = new Dictionary<Guid, ConjuntoColumnas>();
        if (actor.IsAdmin) return resultado;

        var ids = cuentaIds.Distinct().ToList();
        if (ids.Count == 0) return resultado;

        var cuentas = await _db.Cuentas
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.PaisId, c.TitularId })
            .ToListAsync(ct);

        var prefRows = await _db.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == actor.Id)
            .ToListAsync(ct);

        foreach (var cuenta in cuentas)
        {
            var alcance = new PreferenciaScope(cuenta.PaisId, cuenta.TitularId, cuenta.Id, false, false);
            resultado[cuenta.Id] = BuildConjuntoColumnas(prefRows, alcance);
        }

        return resultado;
    }

    private static ConjuntoColumnas BuildConjuntoColumnas(IReadOnlyList<PreferenciaUsuarioCuenta> prefRows, PreferenciaScope scope)
    {
        var admin = ResolveColumnasAdmin(prefRows, scope);
        var usuario = ResolveColumnasUsuario(prefRows, scope);

        List<string>? efectiva = null;
        if (admin is not null && usuario is not null)
        {
            var permitidas = new HashSet<string>(admin, StringComparer.OrdinalIgnoreCase);
            efectiva = [.. usuario.Where(permitidas.Contains)];
        }
        else if (admin is not null)
        {
            efectiva = [.. admin];
        }
        else if (usuario is not null)
        {
            efectiva = [.. usuario];
        }

        return new ConjuntoColumnas
        {
            Admin = admin,
            Usuario = usuario,
            Efectivo = efectiva,
            EfectivoSet = efectiva is null ? null : new HashSet<string>(efectiva, StringComparer.OrdinalIgnoreCase)
        };
    }

    // S-2: whitelist del administrador. Se busca en las filas de preferencia
    // del usuario por alcance, de mas especifico a mas amplio, y se combinan
    // por interseccion.
    private static IReadOnlyList<string>? ResolveColumnasAdmin(IReadOnlyList<PreferenciaUsuarioCuenta> prefRows, PreferenciaScope scope)
    {
        List<string>? resultado = null;
        foreach (var nivel in ScopesChain(scope))
        {
            var fila = prefRows.FirstOrDefault(p => MismoAlcance(p, nivel));
            var columnas = ParseArray(fila?.ColumnasVisiblesAdmin);
            if (columnas is null) continue;
            if (resultado is null)
            {
                resultado = [.. columnas];
                continue;
            }
            resultado = [.. resultado.Where(c => columnas.Contains(c, StringComparer.OrdinalIgnoreCase))];
        }
        return resultado;
    }

    // S-2: la preferencia del usuario es de VISTA, no de restriccion: gana la
    // fila mas especifica del alcance que exista. Su columna null significa
    // "el usuario no restringe".
    private static IReadOnlyList<string>? ResolveColumnasUsuario(IReadOnlyList<PreferenciaUsuarioCuenta> prefRows, PreferenciaScope scope)
    {
        foreach (var nivel in ScopesChain(scope))
        {
            var fila = prefRows.FirstOrDefault(p => MismoAlcance(p, nivel));
            if (fila is not null) return ParseArray(fila.ColumnasVisibles);
        }
        return null;
    }

    // S-2: alcances de mas especifico a mas amplio para un alcance dado.
    private static List<(Guid? PaisId, Guid? TitularId, Guid? CuentaId)> ScopesChain(PreferenciaScope scope)
    {
        var chain = new List<(Guid? PaisId, Guid? TitularId, Guid? CuentaId)>();
        if (scope.CuentaId.HasValue)
        {
            chain.Add((scope.PaisId, scope.TitularId, scope.CuentaId));
            chain.Add((scope.PaisId, scope.TitularId, null));
            chain.Add((null, scope.TitularId, null));
        }
        else if (scope.TitularId.HasValue)
        {
            chain.Add((scope.PaisId, scope.TitularId, null));
            chain.Add((null, scope.TitularId, null));
        }
        if (scope.PaisId.HasValue)
        {
            chain.Add((scope.PaisId, null, null));
        }
        chain.Add((null, null, null));
        return chain;
    }

    private static bool MismoAlcance(PreferenciaUsuarioCuenta preferencia, (Guid? PaisId, Guid? TitularId, Guid? CuentaId) alcance)
    {
        return preferencia.PaisId == alcance.PaisId &&
               preferencia.TitularId == alcance.TitularId &&
               preferencia.CuentaId == alcance.CuentaId;
    }

    // S-2: interseccion de los conjuntos efectivos de varias cuentas. Un
    // conjunto null (cuenta sin restriccion) no acota: solo cuentan los que
    // restringen. null = no hay ninguna restriccion.
    private static HashSet<string>? IntersectConjuntos(IEnumerable<ConjuntoColumnas> conjuntos)
    {
        HashSet<string>? resultado = null;
        foreach (var conjunto in conjuntos)
        {
            if (conjunto.EfectivoSet is null) continue;
            if (resultado is null)
            {
                resultado = new HashSet<string>(conjunto.EfectivoSet, StringComparer.OrdinalIgnoreCase);
                continue;
            }
            resultado.IntersectWith(conjunto.EfectivoSet);
        }
        return resultado;
    }

    private static HashSet<string>? ResolveEditableColumns(
        IReadOnlyList<PermisoUsuario> editableRows,
        IReadOnlyList<PreferenciaUsuarioCuenta> prefRows)
    {
        if (editableRows.Count == 0)
        {
            return null;
        }

        var parsed = editableRows
            .Select(row => ParseArray(prefRows.FirstOrDefault(pref => SameScope(pref, row))?.ColumnasEditables))
            .ToList();
        if (parsed.Any(x => x is null))
        {
            return null;
        }

        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed)
        {
            foreach (var col in row!)
            {
                cols.Add(col);
            }
        }

        return cols;
    }

    private static bool SameScope(PreferenciaUsuarioCuenta preferencia, PermisoUsuario permiso)
    {
        return preferencia.PaisId == permiso.PaisId &&
               preferencia.TitularId == permiso.TitularId &&
               preferencia.CuentaId == permiso.CuentaId;
    }

    private static void EnsureEditable(Perm p, string col)
    {
        if (CanEditColumn(p, col)) return;
        throw new InvalidOperationException($"No tienes permiso para editar la columna '{col}'.");
    }

    private static bool CanEditColumn(Perm p, string col)
    {
        if (p.EditableCols is null) return true;
        return p.EditableCols.Contains(col.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? ParseArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(json); } catch { return null; }
    }

    private static bool IsBase(string col)
    {
        return col.Equals("fecha", StringComparison.OrdinalIgnoreCase)
            || col.Equals("concepto", StringComparison.OrdinalIgnoreCase)
            || col.Equals("comentarios", StringComparison.OrdinalIgnoreCase)
            || col.Equals("monto", StringComparison.OrdinalIgnoreCase)
            || col.Equals("saldo", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string ToExcel(int idx)
    {
        var s = "";
        while (idx > 0)
        {
            var m = (idx - 1) % 26;
            s = (char)('A' + m) + s;
            idx = (idx - m) / 26;
        }
        return s;
    }

    private bool TryGetUser(out Actor actor)
    {
        actor = default;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var id)) return false;
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        actor = new Actor { Id = id, IsAdmin = role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) };
        return true;
    }

    private readonly struct Actor
    {
        public Guid Id { get; init; }
        public bool IsAdmin { get; init; }
    }

    private sealed class Perm
    {
        public bool CanAdd { get; init; }
        public bool CanEdit { get; init; }
        public bool CanDelete { get; init; }
        public HashSet<string>? EditableCols { get; init; }
    }

    // S-2: conjunto de columnas visibles de un alcance (ver el bloque de
    // helpers "conjunto efectivo de columnas visibles").
    private sealed class ConjuntoColumnas
    {
        public IReadOnlyList<string>? Admin { get; init; }
        public IReadOnlyList<string>? Usuario { get; init; }
        public IReadOnlyList<string>? Efectivo { get; init; }
        public IReadOnlySet<string>? EfectivoSet { get; init; }
    }

    private sealed record NormalizedDesgloseLine(Guid? Id, int Orden, string TerceroNombre, decimal Importe, string? Notas);

    private readonly record struct PreferenciaScope(Guid? PaisId, Guid? TitularId, Guid? CuentaId, bool Forbidden, bool NotFound)
    {
        public static PreferenciaScope Forbid() => new(null, null, null, true, false);
        public static PreferenciaScope Missing() => new(null, null, null, false, true);
    }

    // R-7: cuenta de la que se pide el resumen KPI, con el titular al que
    // pertenece (el mismo titular puede tener varias cuentas).
    private sealed record ResumenCuenta(Cuenta Cuenta, Guid TitularId, string TitularNombre);
}
