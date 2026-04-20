using System.Text;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/auditoria")]
public sealed class AuditoriaController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditoriaController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? usuarioId = null,
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] string? tipoAccion = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        CancellationToken ct = default)
    {
        var query = BuildFilteredAuditoriaQuery(usuarioId, cuentaId, tipoAccion, fechaDesde, fechaHasta);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = await query.CountAsync(ct);
        var rawRows = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RawAuditoriaRow
            {
                Id = x.Id,
                Timestamp = x.Timestamp,
                UsuarioId = x.UsuarioId,
                TipoAccion = x.TipoAccion,
                EntidadTipo = x.EntidadTipo,
                EntidadId = x.EntidadId,
                CeldaReferencia = x.CeldaReferencia,
                ColumnaNombre = x.ColumnaNombre,
                ValorAnterior = x.ValorAnterior,
                ValorNuevo = x.ValorNuevo,
                IpAddress = x.IpAddress != null ? x.IpAddress.ToString() : null,
                DetallesJson = x.DetallesJson
            })
            .ToListAsync(ct);

        var data = await MapRows(rawRows, ct);
        return Ok(new PaginatedResponse<AuditoriaListItemResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("filtros")]
    public async Task<IActionResult> GetFiltros(CancellationToken ct)
    {
        var usuarios = await _db.Usuarios
            .IgnoreQueryFilters()
            .OrderBy(u => u.NombreCompleto)
            .Select(u => new AuditoriaUsuarioFiltroResponse
            {
                Id = u.Id,
                Nombre = u.NombreCompleto
            })
            .ToListAsync(ct);

        var cuentas = await _db.Cuentas
            .IgnoreQueryFilters()
            .Join(
                _db.Titulares.IgnoreQueryFilters(),
                c => c.TitularId,
                t => t.Id,
                (c, t) => new AuditoriaCuentaFiltroResponse
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    TitularId = t.Id,
                    TitularNombre = t.Nombre
                })
            .OrderBy(c => c.TitularNombre)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);

        var tiposAccion = await _db.Auditorias
            .Select(a => a.TipoAccion)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        return Ok(new AuditoriaFiltrosResponse
        {
            Usuarios = usuarios,
            Cuentas = cuentas,
            TiposAccion = tiposAccion
        });
    }

    [HttpGet("exportar-csv")]
    public async Task<IActionResult> ExportarCsv(
        [FromQuery] Guid? usuarioId = null,
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] string? tipoAccion = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        CancellationToken ct = default)
    {
        var query = BuildFilteredAuditoriaQuery(usuarioId, cuentaId, tipoAccion, fechaDesde, fechaHasta);
        var rawRows = await query
            .OrderByDescending(x => x.Timestamp)
            .Select(x => new RawAuditoriaRow
            {
                Id = x.Id,
                Timestamp = x.Timestamp,
                UsuarioId = x.UsuarioId,
                TipoAccion = x.TipoAccion,
                EntidadTipo = x.EntidadTipo,
                EntidadId = x.EntidadId,
                CeldaReferencia = x.CeldaReferencia,
                ColumnaNombre = x.ColumnaNombre,
                ValorAnterior = x.ValorAnterior,
                ValorNuevo = x.ValorNuevo,
                IpAddress = x.IpAddress != null ? x.IpAddress.ToString() : null,
                DetallesJson = x.DetallesJson
            })
            .ToListAsync(ct);

        var rows = await MapRows(rawRows, ct);

        var sb = new StringBuilder();
        sb.AppendLine("timestamp,usuario,tipo_accion,entidad_tipo,entidad_id,cuenta,titular,celda_referencia,columna_nombre,valor_anterior,valor_nuevo,ip_address");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(row.UsuarioNombre),
                Csv(row.TipoAccion),
                Csv(row.EntidadTipo),
                Csv(row.EntidadId?.ToString()),
                Csv(row.CuentaNombre),
                Csv(row.TitularNombre),
                Csv(row.CeldaReferencia),
                Csv(row.ColumnaNombre),
                Csv(row.ValorAnterior),
                Csv(row.ValorNuevo),
                Csv(row.IpAddress)));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"auditoria_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private IQueryable<Models.Auditoria> BuildFilteredAuditoriaQuery(Guid? usuarioId, Guid? cuentaId, string? tipoAccion, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        var query = _db.Auditorias.AsNoTracking();

        if (usuarioId.HasValue)
        {
            query = query.Where(a => a.UsuarioId == usuarioId.Value);
        }

        if (!string.IsNullOrWhiteSpace(tipoAccion))
        {
            var action = tipoAccion.Trim().ToLowerInvariant();
            query = query.Where(a => a.TipoAccion.ToLower() == action);
        }

        if (fechaDesde.HasValue)
        {
            var from = fechaDesde.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp >= from);
        }

        if (fechaHasta.HasValue)
        {
            var untilExclusive = fechaHasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp < untilExclusive);
        }

        if (cuentaId.HasValue)
        {
            var extractosCuenta = _db.Extractos
                .IgnoreQueryFilters()
                .Where(e => e.CuentaId == cuentaId.Value)
                .Select(e => e.Id);

            query = query.Where(a =>
                (a.EntidadTipo == "EXTRACTOS" &&
                 a.EntidadId.HasValue &&
                 extractosCuenta.Contains(a.EntidadId.Value)) ||
                (a.EntidadTipo == "CUENTAS" &&
                 a.EntidadId == cuentaId.Value));
        }

        return query;
    }

    private async Task<List<AuditoriaListItemResponse>> MapRows(IReadOnlyList<RawAuditoriaRow> rawRows, CancellationToken ct)
    {
        var usuarioIds = rawRows.Where(r => r.UsuarioId.HasValue).Select(r => r.UsuarioId!.Value).Distinct().ToList();
        var usuariosMap = await _db.Usuarios
            .IgnoreQueryFilters()
            .Where(u => usuarioIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, ct);

        var extractoIds = rawRows
            .Where(r => string.Equals(r.EntidadTipo, "EXTRACTOS", StringComparison.OrdinalIgnoreCase) && r.EntidadId.HasValue)
            .Select(r => r.EntidadId!.Value)
            .Distinct()
            .ToList();
        var cuentaEntityIds = rawRows
            .Where(r => string.Equals(r.EntidadTipo, "CUENTAS", StringComparison.OrdinalIgnoreCase) && r.EntidadId.HasValue)
            .Select(r => r.EntidadId!.Value)
            .Distinct()
            .ToList();
        var titularEntityIds = rawRows
            .Where(r => string.Equals(r.EntidadTipo, "TITULARES", StringComparison.OrdinalIgnoreCase) && r.EntidadId.HasValue)
            .Select(r => r.EntidadId!.Value)
            .Distinct()
            .ToList();

        var extractos = await _db.Extractos
            .IgnoreQueryFilters()
            .Where(e => extractoIds.Contains(e.Id))
            .Select(e => new { e.Id, e.CuentaId })
            .ToListAsync(ct);
        var extractosMap = extractos.ToDictionary(x => x.Id, x => x.CuentaId);

        var cuentaIds = extractos
            .Select(x => x.CuentaId)
            .Concat(cuentaEntityIds)
            .Distinct()
            .ToList();
        var cuentas = await _db.Cuentas
            .IgnoreQueryFilters()
            .Where(c => cuentaIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Nombre, c.TitularId })
            .ToListAsync(ct);
        var cuentasMap = cuentas.ToDictionary(x => x.Id, x => x);

        var titularIds = cuentas
            .Select(c => c.TitularId)
            .Concat(titularEntityIds)
            .Distinct()
            .ToList();
        var titularesMap = await _db.Titulares
            .IgnoreQueryFilters()
            .Where(t => titularIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        var list = new List<AuditoriaListItemResponse>(rawRows.Count);
        foreach (var row in rawRows)
        {
            Guid? cuentaId = null;
            string? cuentaNombre = null;
            Guid? titularId = null;
            string? titularNombre = null;

            if (row.EntidadId.HasValue && extractosMap.TryGetValue(row.EntidadId.Value, out var extractoCuentaId))
            {
                cuentaId = extractoCuentaId;
                if (cuentasMap.TryGetValue(extractoCuentaId, out var cuenta))
                {
                    cuentaNombre = cuenta.Nombre;
                    titularId = cuenta.TitularId;
                    titularesMap.TryGetValue(cuenta.TitularId, out titularNombre);
                }
            }
            else if (row.EntidadId.HasValue && string.Equals(row.EntidadTipo, "CUENTAS", StringComparison.OrdinalIgnoreCase) && cuentasMap.TryGetValue(row.EntidadId.Value, out var cuenta))
            {
                cuentaId = cuenta.Id;
                cuentaNombre = cuenta.Nombre;
                titularId = cuenta.TitularId;
                titularesMap.TryGetValue(cuenta.TitularId, out titularNombre);
            }
            else if (row.EntidadId.HasValue && string.Equals(row.EntidadTipo, "TITULARES", StringComparison.OrdinalIgnoreCase))
            {
                titularId = row.EntidadId.Value;
                titularesMap.TryGetValue(row.EntidadId.Value, out titularNombre);
            }

            list.Add(new AuditoriaListItemResponse
            {
                Id = row.Id,
                Timestamp = row.Timestamp,
                UsuarioId = row.UsuarioId,
                UsuarioNombre = row.UsuarioId.HasValue && usuariosMap.TryGetValue(row.UsuarioId.Value, out var usuarioNombre)
                    ? usuarioNombre
                    : null,
                TipoAccion = row.TipoAccion,
                EntidadTipo = row.EntidadTipo,
                EntidadId = row.EntidadId,
                CuentaId = cuentaId,
                CuentaNombre = cuentaNombre,
                TitularId = titularId,
                TitularNombre = titularNombre,
                CeldaReferencia = row.CeldaReferencia,
                ColumnaNombre = row.ColumnaNombre,
                ValorAnterior = row.ValorAnterior,
                ValorNuevo = row.ValorNuevo,
                IpAddress = row.IpAddress,
                DetallesJson = row.DetallesJson
            });
        }

        return list;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var safeValue = EscapeSpreadsheetFormula(value);
        var escaped = safeValue.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string EscapeSpreadsheetFormula(string value)
    {
        var trimmed = value.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length == 0)
        {
            return value;
        }

        return trimmed[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
    }

    private sealed class RawAuditoriaRow
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid? UsuarioId { get; set; }
        public string TipoAccion { get; set; } = string.Empty;
        public string? EntidadTipo { get; set; }
        public Guid? EntidadId { get; set; }
        public string? CeldaReferencia { get; set; }
        public string? ColumnaNombre { get; set; }
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
        public string? IpAddress { get; set; }
        public string? DetallesJson { get; set; }
    }
}
