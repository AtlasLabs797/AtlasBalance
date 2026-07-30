using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/auditoria")]
public sealed class AuditoriaController : ControllerBase
{
    // Limite duro para exportar CSV: evita materializar en memoria un resultado sin
    // acotar si el filtro es demasiado amplio. Si se supera, se pide estrechar el filtro
    // en vez de truncar el export en silencio.
    private const int MaxExportRows = 50_000;

    private readonly AppDbContext _db;
    private readonly IAuditIntegrityService _integridad;
    private readonly IAuditService _auditService;
    private readonly IAuditSigner _auditSigner;

    public AuditoriaController(
        AppDbContext db,
        IAuditIntegrityService integridad,
        IAuditService auditService,
        IAuditSigner auditSigner)
    {
        _db = db;
        _integridad = integridad;
        _auditService = auditService;
        _auditSigner = auditSigner;
    }

    /// <summary>
    /// Verifica firmas y continuidad de secuencia de AUDITORIAS.
    ///
    /// Sin rango cubre la tabla entera. Con rango, los huecos internos siguen
    /// siendo senal fiable, pero no se puede concluir nada sobre los bordes: la
    /// primera y la ultima fila del rango son puntos de corte arbitrarios, no
    /// extremos de la secuencia global.
    /// </summary>
    [HttpGet("integridad")]
    public async Task<IActionResult> Integridad(
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        CancellationToken ct = default)
    {
        var desde = fechaDesde?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hasta = fechaHasta?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var resultado = await _integridad.VerificarAsync(desde, hasta, ct);

        // La verificacion se audita siempre: es una accion de admin sobre datos
        // sensibles. Si sale mal, con el tipo de accion que dispara alerta y
        // espejo al Event Log.
        await _auditService.LogAsync(
            GetCurrentUserId(),
            resultado.Integra ? "AUDITORIA_INTEGRIDAD_OK" : AuditActions.AuditoriaIntegridadFallida,
            "AUDITORIAS",
            null,
            HttpContext,
            JsonSerializer.Serialize(new
            {
                filas_examinadas = resultado.FilasExaminadas,
                firmas_invalidas = resultado.FirmasInvalidas,
                filas_faltantes = resultado.FilasFaltantes,
                sin_firma = resultado.SinFirma,
                rango_desde = resultado.RangoDesdeUtc,
                rango_hasta = resultado.RangoHastaUtc
            }),
            ct);

        return Ok(resultado);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? usuarioId = null,
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] Guid? paisId = null,
        [FromQuery] string? tipoAccion = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        CancellationToken ct = default)
    {
        var query = BuildFilteredAuditoriaQuery(usuarioId, cuentaId, paisId, tipoAccion, fechaDesde, fechaHasta);
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
                Secuencia = x.Secuencia,
                Timestamp = x.Timestamp,
                UsuarioId = x.UsuarioId,
                TipoAccion = x.TipoAccion,
                EntidadTipo = x.EntidadTipo,
                EntidadId = x.EntidadId,
                CeldaReferencia = x.CeldaReferencia,
                ColumnaNombre = x.ColumnaNombre,
                ValorAnterior = x.ValorAnterior,
                ValorNuevo = x.ValorNuevo,
                IpAddressRaw = x.IpAddress,
                IpAddress = x.IpAddress != null ? x.IpAddress.ToString() : null,
                UserAgent = x.UserAgent,
                SessionId = x.SessionId,
                Origen = x.Origen,
                Firma = x.Firma,
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
    public async Task<IActionResult> GetFiltros([FromQuery] Guid? paisId = null, CancellationToken ct = default)
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
            .ApplyPaisScope(paisId)
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

        var tiposAccion = await BuildFilteredAuditoriaQuery(null, null, paisId, null, null, null)
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
        [FromQuery] Guid? paisId = null,
        [FromQuery] string? tipoAccion = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        CancellationToken ct = default)
    {
        var query = BuildFilteredAuditoriaQuery(usuarioId, cuentaId, paisId, tipoAccion, fechaDesde, fechaHasta);
        var totalMatching = await query.CountAsync(ct);
        if (totalMatching > MaxExportRows)
        {
            return BadRequest(new
            {
                error = $"El filtro seleccionado incluye {totalMatching} registros, por encima del limite de {MaxExportRows} para exportar a CSV. Estrecha el rango de fechas u otros filtros.",
            });
        }

        var rawRows = await query
            .OrderByDescending(x => x.Timestamp)
            .Select(x => new RawAuditoriaRow
            {
                Id = x.Id,
                Secuencia = x.Secuencia,
                Timestamp = x.Timestamp,
                UsuarioId = x.UsuarioId,
                TipoAccion = x.TipoAccion,
                EntidadTipo = x.EntidadTipo,
                EntidadId = x.EntidadId,
                CeldaReferencia = x.CeldaReferencia,
                ColumnaNombre = x.ColumnaNombre,
                ValorAnterior = x.ValorAnterior,
                ValorNuevo = x.ValorNuevo,
                IpAddressRaw = x.IpAddress,
                IpAddress = x.IpAddress != null ? x.IpAddress.ToString() : null,
                UserAgent = x.UserAgent,
                SessionId = x.SessionId,
                Origen = x.Origen,
                Firma = x.Firma,
                DetallesJson = x.DetallesJson
            })
            .ToListAsync(ct);

        var rows = await MapRows(rawRows, ct);

        var sb = new StringBuilder();
        sb.AppendLine("secuencia,timestamp,usuario,tipo_accion,entidad_tipo,entidad_id,cuenta,titular,celda_referencia,columna_nombre,valor_anterior,valor_nuevo,ip_address,origen,session_id,user_agent,firma_valida");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.Secuencia.ToString(CultureInfo.InvariantCulture)),
                Csv(row.Timestamp.ToString("dd/MM/yyyy HH:mm:ss")),
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
                Csv(row.IpAddress),
                Csv(row.Origen),
                Csv(row.SessionId),
                Csv(row.UserAgent),
                // "sin_firma" y no vacio: en un export forense hay que poder
                // distinguir "no verificable" de "no verificado".
                Csv(row.FirmaValida is null ? "sin_firma" : row.FirmaValida.Value ? "si" : "NO")));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"auditoria_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private IQueryable<Models.Auditoria> BuildFilteredAuditoriaQuery(Guid? usuarioId, Guid? cuentaId, Guid? paisId, string? tipoAccion, DateOnly? fechaDesde, DateOnly? fechaHasta)
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

        if (paisId.HasValue)
        {
            var cuentasPais = _db.Cuentas
                .IgnoreQueryFilters()
                .Where(c => c.PaisId == paisId.Value)
                .Select(c => c.Id);

            var extractosPais = _db.Extractos
                .IgnoreQueryFilters()
                .Where(e => cuentasPais.Contains(e.CuentaId))
                .Select(e => e.Id);

            var titularesPais = _db.Cuentas
                .IgnoreQueryFilters()
                .Where(c => c.PaisId == paisId.Value)
                .Select(c => c.TitularId);

            query = query.Where(a =>
                (a.EntidadTipo == "EXTRACTOS" &&
                 a.EntidadId.HasValue &&
                 (extractosPais.Contains(a.EntidadId.Value) || cuentasPais.Contains(a.EntidadId.Value))) ||
                (a.EntidadTipo == "CUENTAS" &&
                 a.EntidadId.HasValue &&
                 cuentasPais.Contains(a.EntidadId.Value)) ||
                (a.EntidadTipo == "TITULARES" &&
                 a.EntidadId.HasValue &&
                 titularesPais.Contains(a.EntidadId.Value)));
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
                Secuencia = row.Secuencia,
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
                UserAgent = row.UserAgent,
                SessionId = row.SessionId,
                Origen = row.Origen,
                // null en filas pre-V-02.07: no llevan firma, asi que no son
                // verificables. Distinto de false, que si seria una alarma.
                FirmaValida = string.IsNullOrEmpty(row.Firma)
                    ? null
                    : _auditSigner.Verificar(row.ToEntidad()),
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
        public long Secuencia { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid? UsuarioId { get; set; }
        public string TipoAccion { get; set; } = string.Empty;
        public string? EntidadTipo { get; set; }
        public Guid? EntidadId { get; set; }
        public string? CeldaReferencia { get; set; }
        public string? ColumnaNombre { get; set; }
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
        public System.Net.IPAddress? IpAddressRaw { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? SessionId { get; set; }
        public string Origen { get; set; } = string.Empty;
        public string? Firma { get; set; }
        public string? DetallesJson { get; set; }

        /// <summary>
        /// Reconstruye la entidad para poder validar la firma sin volver a la BD.
        /// Debe incluir TODOS los campos que cubre AuditSigner.Canonicalizar.
        /// </summary>
        public Models.Auditoria ToEntidad() => new()
        {
            Id = Id,
            Secuencia = Secuencia,
            UsuarioId = UsuarioId,
            TipoAccion = TipoAccion,
            EntidadTipo = EntidadTipo,
            EntidadId = EntidadId,
            CeldaReferencia = CeldaReferencia,
            ColumnaNombre = ColumnaNombre,
            ValorAnterior = ValorAnterior,
            ValorNuevo = ValorNuevo,
            Timestamp = Timestamp,
            IpAddress = IpAddressRaw,
            UserAgent = UserAgent,
            SessionId = SessionId,
            Origen = Origen,
            Firma = Firma,
            DetallesJson = DetallesJson
        };
    }
}
