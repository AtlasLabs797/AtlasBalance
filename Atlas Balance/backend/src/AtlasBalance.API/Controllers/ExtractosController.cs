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

    public ExtractosController(AppDbContext db, IAlertaService alertaService)
    {
        _db = db;
        _alertaService = alertaService;
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
        CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        if (!actor.IsAdmin)
        {
            incluirEliminados = false;
        }

        if (!DateRangeValidator.TryValidate(fechaDesde, fechaHasta, out var rangoError))
        {
            return BadRequest(new { error = rangoError });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var allowed = await GetAllowedAccountIds(actor, ct, paisId);
        if (!allowed.Any())
        {
            return Ok(new PaginatedResponse<ExtractoListItemResponse> { Data = [], Total = 0, Page = page, PageSize = pageSize, TotalPages = 0 });
        }

        var desc = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<Extracto> q = incluirEliminados ? _db.Extractos.IgnoreQueryFilters() : _db.Extractos;
        q = q.Where(x => allowed.Contains(x.CuentaId));
        if (cuentaId.HasValue) q = q.Where(x => x.CuentaId == cuentaId.Value);
        if (titularId.HasValue)
        {
            var cuentasTitular = await _db.Cuentas.Where(c => c.TitularId == titularId).Select(c => c.Id).ToListAsync(ct);
            q = q.Where(x => cuentasTitular.Contains(x.CuentaId));
        }
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

        var filteredQuery = q;

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

        var total = await q.CountAsync(ct);
        var columnasDisponibles = await (
                from extra in _db.ExtractosColumnasExtra
                join extracto in filteredQuery on extra.ExtractoId equals extracto.Id
                where extra.NombreColumna != ""
                select extra.NombreColumna)
            .Distinct()
            .OrderBy(nombre => nombre)
            .ToListAsync(ct);
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
            return new ExtractoListItemResponse
            {
                Id = x.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularId = t.Id,
                TitularNombre = t.Nombre,
                PaisId = c.PaisId,
                Divisa = c.Divisa,
                Fecha = x.Fecha,
                Concepto = x.Concepto,
                Comentarios = x.Comentarios,
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
                ColumnasExtra = extrasMap.TryGetValue(x.Id, out var ex) ? ex : [],
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

        await _alertaService.EvaluateSaldoPostAsync(ex.CuentaId, actor.Id, ct);
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
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
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
        await _alertaService.EvaluateSaldoPostAsync(ex.CuentaId, actor.Id, ct);
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
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
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
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
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
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
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
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
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
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
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
        if (ex.DeletedAt.HasValue && !actor.IsAdmin) return NotFound(new { error = "Extracto no encontrado" });
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
        var cuenta = await _db.Cuentas.Where(c => c.Id == cuentaId).Select(c => new { c.Id, c.Nombre, c.Iban, c.BancoNombre, c.Divisa, c.PaisId, c.EsEfectivo, c.TipoCuenta, c.TitularId, c.Notas }).FirstOrDefaultAsync(ct);
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
        if (paisId.HasValue && cuenta.PaisId != paisId.Value) return NotFound(new { error = "Cuenta no encontrada en el pais activo" });
        var titular = await _db.Titulares.Where(t => t.Id == cuenta.TitularId).Select(t => t.Nombre).FirstOrDefaultAsync(ct);
        return Ok(await BuildSummary(actor, cuenta.Id, cuenta.Nombre, cuenta.Iban, cuenta.BancoNombre, cuenta.Divisa, cuenta.EsEfectivo, cuenta.TipoCuenta, cuenta.TitularId, titular ?? string.Empty, cuenta.Notas, periodo, ct, cuenta.PaisId));
    }

    [HttpGet("titulares/{titularId:guid}/cuentas")]
    public async Task<IActionResult> GetCuentasTitular(Guid titularId, [FromQuery] string periodo = "1m", [FromQuery] Guid? paisId = null, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (!await CanViewTitular(actor, titularId, ct)) return Forbid();
        var titular = await _db.Titulares.FirstOrDefaultAsync(t => t.Id == titularId, ct);
        if (titular is null) return NotFound(new { error = "Titular no encontrado" });
        var allowed = await GetAllowedAccountIds(actor, ct, paisId);
        var cuentas = await _db.Cuentas.Where(c => c.TitularId == titularId && allowed.Contains(c.Id)).ToListAsync(ct);
        var summary = new List<CuentaResumenKpiResponse>();
        foreach (var c in cuentas)
        {
            summary.Add(await BuildSummary(actor, c.Id, c.Nombre, c.Iban, c.BancoNombre, c.Divisa, c.EsEfectivo, c.TipoCuenta, titular.Id, titular.Nombre, c.Notas, periodo, ct, c.PaisId));
        }
        return Ok(new TitularConCuentasResponse { TitularId = titular.Id, TitularNombre = titular.Nombre, Cuentas = summary });
    }

    [HttpGet("titulares-resumen")]
    public async Task<IActionResult> GetTitularesResumen([FromQuery] string periodo = "1m", [FromQuery] Guid? paisId = null, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var allowed = await GetAllowedAccountIds(actor, ct, paisId);
        var cuentas = await _db.Cuentas.Where(c => allowed.Contains(c.Id)).ToListAsync(ct);
        var titularesIds = cuentas.Select(c => c.TitularId).Distinct().ToList();
        var titulares = await _db.Titulares.Where(t => titularesIds.Contains(t.Id)).OrderBy(t => t.Nombre).ToListAsync(ct);
        var outData = new List<TitularConCuentasResponse>();
        foreach (var t in titulares)
        {
            var tc = cuentas.Where(c => c.TitularId == t.Id).ToList();
            var s = new List<CuentaResumenKpiResponse>();
            foreach (var c in tc) s.Add(await BuildSummary(actor, c.Id, c.Nombre, c.Iban, c.BancoNombre, c.Divisa, c.EsEfectivo, c.TipoCuenta, t.Id, t.Nombre, c.Notas, periodo, ct, c.PaisId));
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

        var pref = await QueryPreferenciaUsuarioCuenta(actor.Id, scope).FirstOrDefaultAsync(ct);

        return Ok(new { columnas_visibles = ParseArray(pref?.ColumnasVisibles) });
    }

    [HttpPut("columnas-visibles")]
    public async Task<IActionResult> SaveColumnasVisibles([FromBody] SaveColumnasVisiblesRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var scope = await ResolvePreferenciaScope(actor, req.CuentaId, req.TitularId, req.PaisId, ct);
        if (scope.Forbidden) return Forbid();
        if (scope.NotFound) return NotFound(new { error = "Cuenta no encontrada" });

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

        pref.ColumnasVisibles = req.ColumnasVisibles is null ? null : JsonSerializer.Serialize(req.ColumnasVisibles);
        pref.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Preferencias guardadas" });
    }

    private async Task<CuentaResumenKpiResponse> BuildSummary(Actor actor, Guid cuentaId, string cuentaNombre, string? iban, string? bancoNombre, string divisa, bool esEfectivo, TipoCuenta tipoCuenta, Guid titularId, string titularNombre, string? notas, string periodo, CancellationToken ct, Guid? paisId = null)
    {
        var paisNombre = paisId.HasValue
            ? await _db.Paises.IgnoreQueryFilters().Where(p => p.Id == paisId.Value).Select(p => p.Nombre).FirstOrDefaultAsync(ct)
            : null;
        var q = _db.Extractos.Where(e => e.CuentaId == cuentaId);
        var latest = await q
            .OrderByDescending(e => e.FilaNumero)
            .ThenByDescending(e => e.Fecha)
            .Select(e => new { e.Fecha, e.Saldo })
            .FirstOrDefaultAsync(ct);
        var periodEnd = latest?.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var periodStart = GetPeriodStart(NormalizePeriodo(periodo), periodEnd);
        var periodRows = q.Where(e => e.Fecha >= periodStart && e.Fecha <= periodEnd);
        var ingresos = await periodRows.Where(e => e.Monto > 0).SumAsync(e => (decimal?)e.Monto, ct) ?? 0m;
        var egresos = await periodRows.Where(e => e.Monto < 0).SumAsync(e => (decimal?)e.Monto, ct) ?? 0m;
        var last = await q.OrderByDescending(e => e.FechaModificacion ?? e.FechaCreacion).Select(e => (DateTime?)(e.FechaModificacion ?? e.FechaCreacion)).FirstOrDefaultAsync(ct);
        var plazoFijo = tipoCuenta == TipoCuenta.PLAZO_FIJO
            ? await (
                from plazo in _db.PlazosFijos
                join refCuenta in (actor.IsAdmin ? _db.Cuentas.IgnoreQueryFilters() : _db.Cuentas) on plazo.CuentaReferenciaId equals refCuenta.Id into refJoin
                from cuentaReferencia in refJoin.DefaultIfEmpty()
                where plazo.CuentaId == cuentaId
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
                .FirstOrDefaultAsync(ct)
            : null;

        if (!actor.IsAdmin && plazoFijo?.CuentaReferenciaId is Guid referenceId && !await CanView(actor, referenceId, ct))
        {
            plazoFijo.CuentaReferenciaId = null;
            plazoFijo.CuentaReferenciaNombre = null;
        }

        return new CuentaResumenKpiResponse
        {
            CuentaId = cuentaId,
            CuentaNombre = cuentaNombre,
            // V-02-07: enmascarado en KPI de cuenta/titular (respuesta agregada, no se usa para editar).
            Iban = PiiMasking.MaskIban(iban),
            BancoNombre = bancoNombre,
            Divisa = divisa,
            PaisId = paisId,
            PaisNombre = paisNombre,
            TitularId = titularId,
            TitularNombre = titularNombre,
            EsEfectivo = esEfectivo,
            TipoCuenta = tipoCuenta.ToString(),
            PlazoFijo = plazoFijo,
            Notas = notas,
            SaldoActual = latest?.Saldo ?? 0m,
            IngresosMes = ingresos,
            EgresosMes = Math.Abs(egresos),
            UltimaActualizacion = last
        };
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
        var months = periodo switch
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

        return today.AddMonths(-months);
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

    private async Task<HashSet<Guid>> GetAllowedAccountIds(Actor actor, CancellationToken ct, Guid? paisId = null)
    {
        var visibleAccounts = QueryVisibleAccounts(actor).ApplyPaisScope(paisId);
        if (actor.IsAdmin) return [.. await visibleAccounts.Select(c => c.Id).ToListAsync(ct)];
        var perms = await _db.PermisosUsuario.Where(p => p.UsuarioId == actor.Id).ToListAsync(ct);
        if (!perms.Any()) return [];
        if (perms.Any(p => p.PaisId is null && p.CuentaId is null && p.TitularId is null && GrantsAccountAccess(p)))
        {
            return [.. await visibleAccounts.Select(c => c.Id).ToListAsync(ct)];
        }

        return [.. await visibleAccounts
            .Where(c => _db.PermisosUsuario.Any(p =>
                p.UsuarioId == actor.Id &&
                p.PuedeVerCuentas &&
                (p.PaisId == null || p.PaisId == c.PaisId) &&
                (p.TitularId == null || p.TitularId == c.TitularId) &&
                (p.CuentaId == null || p.CuentaId == c.Id)))
            .Select(c => c.Id)
            .ToListAsync(ct)];
    }

    private async Task<bool> CanView(Actor actor, Guid cuentaId, CancellationToken ct) => (await GetAllowedAccountIds(actor, ct)).Contains(cuentaId);

    private async Task<bool> CanViewTitular(Actor actor, Guid titularId, CancellationToken ct)
    {
        if (actor.IsAdmin)
        {
            return true;
        }

        var perms = await _db.PermisosUsuario.Where(p => p.UsuarioId == actor.Id).ToListAsync(ct);
        if (!perms.Any())
        {
            return false;
        }

        var titularActivo = await _db.Titulares.AnyAsync(t => t.Id == titularId && t.DeletedAt == null, ct);
        if (!titularActivo)
        {
            return false;
        }

        if (perms.Any(p => p.PaisId is null && p.CuentaId is null && p.TitularId is null && GrantsAccountAccess(p)))
        {
            return true;
        }

        return await QueryVisibleAccounts(actor).AnyAsync(
            c => c.TitularId == titularId &&
                 _db.PermisosUsuario.Any(p =>
                     p.UsuarioId == actor.Id &&
                     p.PuedeVerCuentas &&
                     (p.PaisId == null || p.PaisId == c.PaisId) &&
                     (p.TitularId == null || p.TitularId == c.TitularId) &&
                     (p.CuentaId == null || p.CuentaId == c.Id)),
            ct);
    }

    private static bool GrantsAccountAccess(PermisoUsuario permiso) =>
        permiso.PuedeVerCuentas;

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

    private IQueryable<Cuenta> QueryVisibleAccounts(Actor actor)
    {
        var query = _db.Cuentas.AsQueryable();
        return actor.IsAdmin
            ? query
            : query.Where(c => c.DeletedAt == null && _db.Titulares.Any(t => t.Id == c.TitularId && t.DeletedAt == null));
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

    private sealed record NormalizedDesgloseLine(Guid? Id, int Orden, string TerceroNombre, decimal Importe, string? Notas);

    private readonly record struct PreferenciaScope(Guid? PaisId, Guid? TitularId, Guid? CuentaId, bool Forbidden, bool NotFound)
    {
        public static PreferenciaScope Forbid() => new(null, null, null, true, false);
        public static PreferenciaScope Missing() => new(null, null, null, false, true);
    }
}
