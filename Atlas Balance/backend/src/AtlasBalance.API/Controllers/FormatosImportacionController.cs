using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/formatos-importacion")]
public sealed class FormatosImportacionController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public FormatosImportacionController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "fecha_creacion",
        [FromQuery] string sortDir = "desc",
        [FromQuery] string? search = null,
        [FromQuery] bool incluirEliminados = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<FormatoImportacion> query = incluirEliminados
            ? _dbContext.FormatosImportacion.IgnoreQueryFilters()
            : _dbContext.FormatosImportacion;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(f =>
                f.Nombre.ToLower().Contains(term) ||
                (f.BancoNombre != null && f.BancoNombre.ToLower().Contains(term)) ||
                (f.Divisa != null && f.Divisa.ToLower().Contains(term)));
        }

        query = ApplySorting(query, sortBy, desc);

        var total = await query.CountAsync(cancellationToken);
        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<FormatoImportacionResponse>
        {
            Data = data.Select(MapToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id, [FromQuery] bool incluirEliminados = false, CancellationToken cancellationToken = default)
    {
        IQueryable<FormatoImportacion> query = incluirEliminados
            ? _dbContext.FormatosImportacion.IgnoreQueryFilters()
            : _dbContext.FormatosImportacion;

        var formato = await query.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (formato is null)
        {
            return NotFound(new { error = "Formato no encontrado" });
        }

        return Ok(MapToResponse(formato));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] SaveFormatoImportacionRequest request, CancellationToken cancellationToken)
    {
        var validationError = await ValidateRequestAsync(request, null, cancellationToken);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var bancoNombre = request.BancoNombre?.Trim();
        var nombreFormato = ResolveFormatoNombre(request.Nombre, bancoNombre);

        var formato = new FormatoImportacion
        {
            Id = Guid.NewGuid(),
            Nombre = nombreFormato,
            BancoNombre = bancoNombre,
            Divisa = request.Divisa!.Trim().ToUpperInvariant(),
            MapeoJson = NormalizeMapeoJson(request.MapeoJson),
            Activo = request.Activo,
            UsuarioCreadorId = GetCurrentUserId(),
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.FormatosImportacion.Add(formato);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "formato_importacion_creado", "FORMATOS_IMPORTACION", formato.Id, HttpContext,
            JsonSerializer.Serialize(new { formato.Nombre, formato.BancoNombre, formato.Divisa }), cancellationToken);

        return CreatedAtAction(nameof(Obtener), new { id = formato.Id }, new { id = formato.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] SaveFormatoImportacionRequest request, CancellationToken cancellationToken)
    {
        var formato = await _dbContext.FormatosImportacion.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (formato is null)
        {
            return NotFound(new { error = "Formato no encontrado" });
        }

        var validationError = await ValidateRequestAsync(request, id, cancellationToken);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var bancoNombre = request.BancoNombre?.Trim();
        var nombreFormato = ResolveFormatoNombre(request.Nombre, bancoNombre);

        formato.Nombre = nombreFormato;
        formato.BancoNombre = bancoNombre;
        formato.Divisa = request.Divisa!.Trim().ToUpperInvariant();
        formato.MapeoJson = NormalizeMapeoJson(request.MapeoJson);
        formato.Activo = request.Activo;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "formato_importacion_actualizado", "FORMATOS_IMPORTACION", formato.Id, HttpContext,
            JsonSerializer.Serialize(new { formato.Nombre, formato.BancoNombre, formato.Divisa, formato.Activo }), cancellationToken);

        return Ok(new { message = "Formato actualizado" });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var formato = await _dbContext.FormatosImportacion.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (formato is null)
        {
            return NotFound(new { error = "Formato no encontrado" });
        }

        formato.Activo = false;
        formato.DeletedAt = DateTime.UtcNow;
        formato.DeletedById = GetCurrentUserId();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "formato_importacion_eliminado", "FORMATOS_IMPORTACION", formato.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Formato eliminado" });
    }

    [HttpPost("{id:guid}/restaurar")]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken cancellationToken)
    {
        var formato = await _dbContext.FormatosImportacion.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (formato is null)
        {
            return NotFound(new { error = "Formato no encontrado" });
        }

        formato.Activo = true;
        formato.DeletedAt = null;
        formato.DeletedById = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "formato_importacion_restaurado", "FORMATOS_IMPORTACION", formato.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Formato restaurado" });
    }

    private static IQueryable<FormatoImportacion> ApplySorting(IQueryable<FormatoImportacion> query, string sortBy, bool desc)
    {
        return (sortBy.ToLowerInvariant(), desc) switch
        {
            ("nombre", true) => query.OrderByDescending(f => f.Nombre),
            ("nombre", false) => query.OrderBy(f => f.Nombre),
            ("banco_nombre", true) => query.OrderByDescending(f => f.BancoNombre),
            ("banco_nombre", false) => query.OrderBy(f => f.BancoNombre),
            ("divisa", true) => query.OrderByDescending(f => f.Divisa),
            ("divisa", false) => query.OrderBy(f => f.Divisa),
            ("fecha_creacion", true) => query.OrderByDescending(f => f.FechaCreacion),
            _ => query.OrderBy(f => f.FechaCreacion)
        };
    }

    private static FormatoImportacionResponse MapToResponse(FormatoImportacion f)
    {
        return new FormatoImportacionResponse
        {
            Id = f.Id,
            Nombre = f.Nombre,
            BancoNombre = f.BancoNombre,
            Divisa = f.Divisa,
            MapeoJson = ParseJsonElement(f.MapeoJson),
            Activo = f.Activo,
            FechaCreacion = f.FechaCreacion,
            UsuarioCreadorId = f.UsuarioCreadorId,
            DeletedAt = f.DeletedAt
        };
    }

    private static JsonElement ParseJsonElement(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        return doc.RootElement.Clone();
    }

    private async Task<string?> ValidateRequestAsync(SaveFormatoImportacionRequest request, Guid? currentId, CancellationToken cancellationToken)
    {
        var normalizedBanco = request.BancoNombre?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedBanco))
        {
            return "Banco es obligatorio";
        }

        var normalizedNombre = ResolveFormatoNombre(request.Nombre, normalizedBanco);

        var normalizedDivisa = request.Divisa?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedDivisa))
        {
            return "Divisa es obligatoria";
        }

        var divisaExists = await _dbContext.DivisasActivas.AnyAsync(d => d.Activa && d.Codigo == normalizedDivisa, cancellationToken);
        if (!divisaExists)
        {
            return "La divisa indicada no esta activa";
        }

        var duplicateName = await _dbContext.FormatosImportacion
            .IgnoreQueryFilters()
            .AnyAsync(
                f => f.Id != currentId &&
                     f.Nombre.ToLower() == normalizedNombre.ToLower() &&
                     ((f.BancoNombre ?? string.Empty).ToLower() == normalizedBanco.ToLower()) &&
                     (f.Divisa ?? string.Empty).ToUpper() == normalizedDivisa,
                cancellationToken);

        if (duplicateName)
        {
            return "Ya existe un formato con el mismo banco y divisa";
        }

        if (request.MapeoJson.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "Mapeo JSON es obligatorio";
        }

        var mapeo = DeserializeMapeo(request.MapeoJson);
        if (mapeo is null)
        {
            return "Mapeo JSON invalido";
        }

        string tipoMonto;
        try
        {
            tipoMonto = NormalizeTipoMonto(mapeo.TipoMonto);
        }
        catch (InvalidOperationException)
        {
            return "Tipo de monto invalido";
        }
        (string Field, int? Index)[] requiredBaseIndices = tipoMonto switch
        {
            "dos_columnas" =>
            [
                ("fecha", mapeo.Fecha),
                ("concepto", mapeo.Concepto),
                ("ingreso", mapeo.Ingreso),
                ("egreso", mapeo.Egreso),
                ("saldo", mapeo.Saldo)
            ],
            "tres_columnas" =>
            [
                ("fecha", mapeo.Fecha),
                ("concepto", mapeo.Concepto),
                ("ingreso", mapeo.Ingreso),
                ("egreso", mapeo.Egreso),
                ("monto", mapeo.Monto),
                ("saldo", mapeo.Saldo)
            ],
            _ =>
            [
                ("fecha", mapeo.Fecha),
                ("concepto", mapeo.Concepto),
                ("monto", mapeo.Monto),
                ("saldo", mapeo.Saldo)
            ]
        };

        if (requiredBaseIndices.Any(item => !item.Index.HasValue))
        {
            return "Faltan indices obligatorios para el tipo de monto seleccionado";
        }

        var baseIndices = requiredBaseIndices.Select(item => item.Index.GetValueOrDefault()).ToArray();
        if (baseIndices.Any(index => index < 0))
        {
            return "Los indices base deben ser cero o positivos";
        }

        if (baseIndices.Distinct().Count() != baseIndices.Length)
        {
            return "Las columnas base no pueden reutilizar el mismo indice";
        }

        if (mapeo.ColumnasExtra is null || mapeo.ColumnasExtra.Count == 0)
        {
            return null;
        }

        var extraNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedIndices = new HashSet<int>(baseIndices);
        foreach (var columna in mapeo.ColumnasExtra)
        {
            if (string.IsNullOrWhiteSpace(columna.Nombre))
            {
                return "Las columnas extra deben tener nombre";
            }

            if (columna.Indice < 0)
            {
                return "Los indices de columnas extra deben ser cero o positivos";
            }

            if (!extraNames.Add(columna.Nombre.Trim()))
            {
                return "No puedes repetir nombres de columnas extra";
            }

            if (!usedIndices.Add(columna.Indice))
            {
                return "No puedes reutilizar indices entre columnas base y extra";
            }
        }

        return null;
    }

    private static string ResolveFormatoNombre(string? nombre, string? bancoNombre)
    {
        if (!string.IsNullOrWhiteSpace(bancoNombre))
        {
            return bancoNombre.Trim();
        }

        return nombre?.Trim() ?? string.Empty;
    }

    private static MapeoImportacionPayload? DeserializeMapeo(JsonElement rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<MapeoImportacionPayload>(
                rawJson.GetRawText(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeMapeoJson(JsonElement rawJson)
    {
        var mapeo = DeserializeMapeo(rawJson) ?? throw new InvalidOperationException("Mapeo JSON invalido");
        var tipoMonto = NormalizeTipoMonto(mapeo.TipoMonto);
        var normalized = new MapeoImportacionPayload
        {
            TipoMonto = tipoMonto,
            Fecha = mapeo.Fecha,
            Concepto = mapeo.Concepto,
            Monto = tipoMonto is "una_columna" or "tres_columnas" ? mapeo.Monto : null,
            Ingreso = tipoMonto is "dos_columnas" or "tres_columnas" ? mapeo.Ingreso : null,
            Egreso = tipoMonto is "dos_columnas" or "tres_columnas" ? mapeo.Egreso : null,
            Saldo = mapeo.Saldo,
            ColumnasExtra = mapeo.ColumnasExtra
        };

        return JsonSerializer.Serialize(
            normalized,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
    }

    private static string NormalizeTipoMonto(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "una_columna";
        }

        var normalized = raw.Trim().ToLowerInvariant();
        return normalized is "una_columna" or "dos_columnas" or "tres_columnas"
            ? normalized
            : throw new InvalidOperationException("Tipo de monto invalido");
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
