using System.Security.Claims;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Controllers;

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

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var allowed = await GetAllowedAccountIds(actor, ct);
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
        var pageRows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var accountIds = pageRows.Select(x => x.CuentaId).Distinct().ToList();
        var cuentas = await _db.Cuentas.IgnoreQueryFilters().Where(c => accountIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var titularIds = cuentas.Values.Select(c => c.TitularId).Distinct().ToList();
        var titulares = await _db.Titulares.IgnoreQueryFilters().Where(t => titularIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var extractoIds = pageRows.Select(x => x.Id).ToList();
        var extras = await _db.ExtractosColumnasExtra.Where(x => extractoIds.Contains(x.ExtractoId)).ToListAsync(ct);
        var extrasMap = extras.GroupBy(x => x.ExtractoId).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.NombreColumna, v => v.Valor, StringComparer.OrdinalIgnoreCase));

        var data = pageRows.Select(x =>
        {
            var c = cuentas[x.CuentaId];
            var t = titulares[c.TitularId];
            return new ExtractoListItemResponse
            {
                Id = x.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularId = t.Id,
                TitularNombre = t.Nombre,
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
                ColumnasExtra = extrasMap.TryGetValue(x.Id, out var ex) ? ex : []
            };
        }).ToList();

        return Ok(new PaginatedResponse<ExtractoListItemResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
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
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (isRelational)
        {
            tx = await _db.Database.BeginTransactionAsync(ct);
            await AcquireFilaNumeroLockAsync(req.CuentaId, ct);
        }

        var fila = (await _db.Extractos.IgnoreQueryFilters().Where(x => x.CuentaId == req.CuentaId).MaxAsync(x => (int?)x.FilaNumero, ct) ?? 0) + 1;
        var ex = new Extracto
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
            ("fecha", null, ex.Fecha.ToString("yyyy-MM-dd")),
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
            await tx.DisposeAsync();
        }

        await _alertaService.EvaluateSaldoPostAsync(ex.CuentaId, actor.Id, ct);
        return Ok(new { id = ex.Id, fila_numero = ex.FilaNumero });
    }

    private async Task AcquireFilaNumeroLockAsync(Guid cuentaId, CancellationToken ct)
    {
        var bytes = cuentaId.ToByteArray();
        var lockKey = BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], ct);
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
            if (req.Fecha.HasValue && req.Fecha.Value != ex.Fecha) { EnsureEditable(p, "fecha"); changes.Add(("fecha", ex.Fecha.ToString("yyyy-MM-dd"), req.Fecha.Value.ToString("yyyy-MM-dd"))); ex.Fecha = req.Fecha.Value; }
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
        return Ok(new { message = "Check actualizado" });
    }

    [HttpPatch("{id:guid}/flag")]
    public async Task<IActionResult> ToggleFlag(Guid id, [FromBody] ToggleFlagRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var ex = await _db.Extractos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ex is null) return NotFound(new { error = "Extracto no encontrado" });
        var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Id == ex.CuentaId, ct);
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
        var p = await GetPermission(actor, cuenta, ct);
        if (!p.CanEdit || (!CanEditColumn(p, "flagged") && !CanEditColumn(p, "flagged_nota"))) return Forbid();

        var oldFlag = ex.Flagged;
        var oldNote = ex.FlaggedNota;
        ex.Flagged = req.Flagged;
        ex.FlaggedNota = req.Flagged ? req.Nota?.Trim() : null;
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
        return Ok(new { message = "Flag actualizado" });
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
        if (!await CanView(actor, ex.CuentaId, ct)) return Forbid();
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
    public async Task<IActionResult> GetCuentaResumen(Guid cuentaId, [FromQuery] string periodo = "1m", CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (!await CanView(actor, cuentaId, ct)) return Forbid();
        var cuenta = await _db.Cuentas.Where(c => c.Id == cuentaId).Select(c => new { c.Id, c.Nombre, c.Divisa, c.EsEfectivo, c.TitularId, c.Notas }).FirstOrDefaultAsync(ct);
        if (cuenta is null) return NotFound(new { error = "Cuenta no encontrada" });
        var titular = await _db.Titulares.Where(t => t.Id == cuenta.TitularId).Select(t => t.Nombre).FirstOrDefaultAsync(ct);
        return Ok(await BuildSummary(cuenta.Id, cuenta.Nombre, cuenta.Divisa, cuenta.EsEfectivo, cuenta.TitularId, titular ?? string.Empty, cuenta.Notas, periodo, ct));
    }

    [HttpGet("titulares/{titularId:guid}/cuentas")]
    public async Task<IActionResult> GetCuentasTitular(Guid titularId, [FromQuery] string periodo = "1m", CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (!await CanViewTitular(actor, titularId, ct)) return Forbid();
        var titular = await _db.Titulares.FirstOrDefaultAsync(t => t.Id == titularId, ct);
        if (titular is null) return NotFound(new { error = "Titular no encontrado" });
        var allowed = await GetAllowedAccountIds(actor, ct);
        var cuentas = await _db.Cuentas.Where(c => c.TitularId == titularId && allowed.Contains(c.Id)).ToListAsync(ct);
        var summary = new List<CuentaResumenKpiResponse>();
        foreach (var c in cuentas)
        {
            summary.Add(await BuildSummary(c.Id, c.Nombre, c.Divisa, c.EsEfectivo, titular.Id, titular.Nombre, c.Notas, periodo, ct));
        }
        return Ok(new TitularConCuentasResponse { TitularId = titular.Id, TitularNombre = titular.Nombre, Cuentas = summary });
    }

    [HttpGet("titulares-resumen")]
    public async Task<IActionResult> GetTitularesResumen([FromQuery] string periodo = "1m", CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        var allowed = await GetAllowedAccountIds(actor, ct);
        var cuentas = await _db.Cuentas.Where(c => allowed.Contains(c.Id)).ToListAsync(ct);
        var titularesIds = cuentas.Select(c => c.TitularId).Distinct().ToList();
        var titulares = await _db.Titulares.Where(t => titularesIds.Contains(t.Id)).OrderBy(t => t.Nombre).ToListAsync(ct);
        var outData = new List<TitularConCuentasResponse>();
        foreach (var t in titulares)
        {
            var tc = cuentas.Where(c => c.TitularId == t.Id).ToList();
            var s = new List<CuentaResumenKpiResponse>();
            foreach (var c in tc) s.Add(await BuildSummary(c.Id, c.Nombre, c.Divisa, c.EsEfectivo, t.Id, t.Nombre, c.Notas, periodo, ct));
            outData.Add(new TitularConCuentasResponse { TitularId = t.Id, TitularNombre = t.Nombre, Cuentas = s });
        }
        return Ok(outData);
    }

    [HttpGet("columnas-visibles")]
    public async Task<IActionResult> GetColumnasVisibles([FromQuery] Guid? cuentaId = null, CancellationToken ct = default)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (cuentaId.HasValue && !await CanView(actor, cuentaId.Value, ct)) return Forbid();

        var pref = await _db.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == actor.Id && p.CuentaId == cuentaId)
            .FirstOrDefaultAsync(ct);

        return Ok(new { columnas_visibles = ParseArray(pref?.ColumnasVisibles) });
    }

    [HttpPut("columnas-visibles")]
    public async Task<IActionResult> SaveColumnasVisibles([FromBody] SaveColumnasVisiblesRequest req, CancellationToken ct)
    {
        if (!TryGetUser(out var actor)) return Unauthorized(new { error = "Usuario no autenticado" });
        if (!req.CuentaId.HasValue) return BadRequest(new { error = "cuenta_id es requerido" });
        if (!await CanView(actor, req.CuentaId.Value, ct)) return Forbid();

        var pref = await _db.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == actor.Id && p.CuentaId == req.CuentaId)
            .FirstOrDefaultAsync(ct);

        if (pref is null)
        {
            pref = new PreferenciaUsuarioCuenta
            {
                Id = Guid.NewGuid(),
                UsuarioId = actor.Id,
                CuentaId = req.CuentaId,
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

    private async Task<CuentaResumenKpiResponse> BuildSummary(Guid cuentaId, string cuentaNombre, string divisa, bool esEfectivo, Guid titularId, string titularNombre, string? notas, string periodo, CancellationToken ct)
    {
        var q = _db.Extractos.Where(e => e.CuentaId == cuentaId);
        var latest = await q
            .OrderByDescending(e => e.Fecha)
            .ThenByDescending(e => e.FilaNumero)
            .Select(e => new { e.Fecha, e.Saldo })
            .FirstOrDefaultAsync(ct);
        var periodEnd = latest?.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var periodStart = GetPeriodStart(NormalizePeriodo(periodo), periodEnd);
        var periodRows = q.Where(e => e.Fecha >= periodStart && e.Fecha <= periodEnd);
        var ingresos = await periodRows.Where(e => e.Monto > 0).SumAsync(e => (decimal?)e.Monto, ct) ?? 0m;
        var egresos = await periodRows.Where(e => e.Monto < 0).SumAsync(e => (decimal?)e.Monto, ct) ?? 0m;
        var last = await q.OrderByDescending(e => e.FechaModificacion ?? e.FechaCreacion).Select(e => (DateTime?)(e.FechaModificacion ?? e.FechaCreacion)).FirstOrDefaultAsync(ct);
        return new CuentaResumenKpiResponse
        {
            CuentaId = cuentaId,
            CuentaNombre = cuentaNombre,
            Divisa = divisa,
            TitularId = titularId,
            TitularNombre = titularNombre,
            EsEfectivo = esEfectivo,
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

    private async Task SaveCellAudits(Extracto ex, Guid? userId, string action, IReadOnlyList<(string Col, string? A, string? N)> changes, CancellationToken ct)
    {
        var extraCols = await _db.ExtractosColumnasExtra.Where(x => x.ExtractoId == ex.Id).Select(x => x.NombreColumna).ToListAsync(ct);
        extraCols.AddRange(changes.Where(x => !IsBase(x.Col)).Select(x => x.Col));
        var ordered = extraCols.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
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
            await SaveAudit(userId, action, ex.Id, ch.Col, $"{ToExcel(idx)}{ex.FilaNumero}", ch.A, ch.N, ct);
        }
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

    private async Task<HashSet<Guid>> GetAllowedAccountIds(Actor actor, CancellationToken ct)
    {
        if (actor.IsAdmin) return [.. await _db.Cuentas.Select(c => c.Id).ToListAsync(ct)];
        var perms = await _db.PermisosUsuario.Where(p => p.UsuarioId == actor.Id).ToListAsync(ct);
        if (!perms.Any()) return [];
        if (perms.Any(p => p.CuentaId is null && p.TitularId is null &&
                           (p.PuedeAgregarLineas || p.PuedeEditarLineas || p.PuedeEliminarLineas ||
                            p.PuedeImportar)))
        {
            return [.. await _db.Cuentas.Select(c => c.Id).ToListAsync(ct)];
        }

        var ids = perms.Where(p => p.CuentaId.HasValue).Select(p => p.CuentaId!.Value).ToHashSet();
        var titularIds = perms.Where(p => p.TitularId.HasValue).Select(p => p.TitularId!.Value).ToList();
        if (titularIds.Any()) ids.UnionWith(await _db.Cuentas.Where(c => titularIds.Contains(c.TitularId)).Select(c => c.Id).ToListAsync(ct));
        return ids;
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

        if (perms.Any(p => p.CuentaId is null && p.TitularId is null &&
                           (p.PuedeAgregarLineas || p.PuedeEditarLineas || p.PuedeEliminarLineas ||
                            p.PuedeImportar)))
        {
            return true;
        }

        if (perms.Any(p => p.TitularId == titularId))
        {
            return true;
        }

        var permittedCuentaIds = perms
            .Where(p => p.CuentaId.HasValue)
            .Select(p => p.CuentaId!.Value)
            .Distinct()
            .ToList();

        return permittedCuentaIds.Count > 0 &&
               await _db.Cuentas.AnyAsync(c => c.TitularId == titularId && permittedCuentaIds.Contains(c.Id), ct);
    }

    private async Task<Perm> GetPermission(Actor actor, Cuenta cuenta, CancellationToken ct)
    {
        if (actor.IsAdmin) return new Perm { CanAdd = true, CanEdit = true, CanDelete = true, EditableCols = null };
        var rows = await _db.PermisosUsuario.Where(p => p.UsuarioId == actor.Id).Where(p => p.CuentaId == null || p.CuentaId == cuenta.Id).Where(p => p.TitularId == null || p.TitularId == cuenta.TitularId).ToListAsync(ct);
        if (!rows.Any()) return new Perm();
        var prefRows = await _db.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == actor.Id && (p.CuentaId == null || p.CuentaId == cuenta.Id))
            .ToListAsync(ct);
        var parsed = prefRows.Select(r => ParseArray(r.ColumnasEditables)).ToList();
        HashSet<string>? cols;
        if (!parsed.Any() || parsed.Any(x => x is null)) cols = null;
        else
        {
            cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in parsed.Where(x => x is not null)) foreach (var c in x!) cols.Add(c);
        }
        return new Perm { CanAdd = rows.Any(r => r.PuedeAgregarLineas), CanEdit = rows.Any(r => r.PuedeEditarLineas), CanDelete = rows.Any(r => r.PuedeEliminarLineas), EditableCols = cols };
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
}
