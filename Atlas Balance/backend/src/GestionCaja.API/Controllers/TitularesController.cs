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
[Route("api/titulares")]
public sealed class TitularesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IUserAccessService _userAccessService;
    private readonly IAuditService _auditService;

    public TitularesController(AppDbContext dbContext, IUserAccessService userAccessService, IAuditService auditService)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "nombre",
        [FromQuery] string sortDir = "asc",
        [FromQuery] string? search = null,
        [FromQuery] TipoTitular? tipoTitular = null,
        [FromQuery] bool incluirEliminados = false,
        CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (!scope.IsAdmin)
        {
            incluirEliminados = false;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<Titular> query = incluirEliminados
            ? _dbContext.Titulares.IgnoreQueryFilters()
            : _dbContext.Titulares;

        query = _userAccessService.ApplyTitularScope(query, scope);

        if (tipoTitular.HasValue)
        {
            query = query.Where(t => t.Tipo == tipoTitular.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Nombre.ToLower().Contains(term) ||
                (t.Identificacion != null && t.Identificacion.ToLower().Contains(term)));
        }

        query = ApplySorting(query, sortBy, desc);

        var total = await query.CountAsync(cancellationToken);
        var pageItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Nombre,
                t.Tipo,
                t.Identificacion,
                t.ContactoEmail,
                t.ContactoTelefono,
                t.Notas,
                t.FechaCreacion,
                t.DeletedAt
            })
            .ToListAsync(cancellationToken);

        var titularIds = pageItems.Select(t => t.Id).ToList();
        var cuentasQuery = _dbContext.Cuentas.AsQueryable();
        cuentasQuery = _userAccessService.ApplyCuentaScope(cuentasQuery, scope);

        var cuentasCountByTitular = await cuentasQuery
            .Where(c => titularIds.Contains(c.TitularId))
            .GroupBy(c => c.TitularId)
            .Select(g => new { TitularId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TitularId, x => x.Count, cancellationToken);

        var data = pageItems.Select(t => new TitularListItemResponse
        {
            Id = t.Id,
            Nombre = t.Nombre,
            Tipo = t.Tipo.ToString(),
            Identificacion = t.Identificacion,
            ContactoEmail = t.ContactoEmail,
            ContactoTelefono = t.ContactoTelefono,
            Notas = t.Notas,
            FechaCreacion = t.FechaCreacion,
            CuentasCount = cuentasCountByTitular.GetValueOrDefault(t.Id, 0),
            DeletedAt = t.DeletedAt
        }).ToList();

        return Ok(new PaginatedResponse<TitularListItemResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id, [FromQuery] bool incluirEliminados = false, CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (!scope.IsAdmin)
        {
            incluirEliminados = false;
        }

        var allowed = await _userAccessService.CanAccessTitularAsync(id, scope, cancellationToken);
        if (!allowed)
        {
            return Forbid();
        }

        IQueryable<Titular> query = incluirEliminados
            ? _dbContext.Titulares.IgnoreQueryFilters()
            : _dbContext.Titulares;

        var titular = await query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (titular is null)
        {
            return NotFound(new { error = "Titular no encontrado" });
        }

        var cuentasQuery = _userAccessService.ApplyCuentaScope(_dbContext.Cuentas.AsQueryable(), scope);
        var cuentasCount = await cuentasQuery.CountAsync(c => c.TitularId == titular.Id, cancellationToken);

        return Ok(new TitularDetalleResponse
        {
            Id = titular.Id,
            Nombre = titular.Nombre,
            Tipo = titular.Tipo.ToString(),
            Identificacion = titular.Identificacion,
            ContactoEmail = titular.ContactoEmail,
            ContactoTelefono = titular.ContactoTelefono,
            Notas = titular.Notas,
            FechaCreacion = titular.FechaCreacion,
            CuentasCount = cuentasCount,
            DeletedAt = titular.DeletedAt
        });
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Crear([FromBody] SaveTitularRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return BadRequest(new { error = "Nombre es obligatorio" });
        }

        var titular = new Titular
        {
            Id = Guid.NewGuid(),
            Nombre = request.Nombre.Trim(),
            Tipo = request.Tipo,
            Identificacion = request.Identificacion?.Trim(),
            ContactoEmail = request.ContactoEmail?.Trim(),
            ContactoTelefono = request.ContactoTelefono?.Trim(),
            Notas = request.Notas?.Trim(),
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.Titulares.Add(titular);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "titular_creado", "TITULARES", titular.Id, HttpContext,
            JsonSerializer.Serialize(new { titular.Nombre, titular.Tipo }), cancellationToken);

        return CreatedAtAction(nameof(Obtener), new { id = titular.Id }, new { id = titular.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] SaveTitularRequest request, CancellationToken cancellationToken)
    {
        var titular = await _dbContext.Titulares.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (titular is null)
        {
            return NotFound(new { error = "Titular no encontrado" });
        }

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return BadRequest(new { error = "Nombre es obligatorio" });
        }

        titular.Nombre = request.Nombre.Trim();
        titular.Tipo = request.Tipo;
        titular.Identificacion = request.Identificacion?.Trim();
        titular.ContactoEmail = request.ContactoEmail?.Trim();
        titular.ContactoTelefono = request.ContactoTelefono?.Trim();
        titular.Notas = request.Notas?.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "titular_actualizado", "TITULARES", titular.Id, HttpContext,
            JsonSerializer.Serialize(new { titular.Nombre, titular.Tipo }), cancellationToken);

        return Ok(new { message = "Titular actualizado" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var titular = await _dbContext.Titulares.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (titular is null)
        {
            return NotFound(new { error = "Titular no encontrado" });
        }

        titular.DeletedAt = DateTime.UtcNow;
        titular.DeletedById = GetCurrentUserId();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "titular_eliminado", "TITULARES", titular.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Titular eliminado" });
    }

    [HttpPost("{id:guid}/restaurar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken cancellationToken)
    {
        var titular = await _dbContext.Titulares.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (titular is null)
        {
            return NotFound(new { error = "Titular no encontrado" });
        }

        titular.DeletedAt = null;
        titular.DeletedById = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "titular_restaurado", "TITULARES", titular.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Titular restaurado" });
    }

    private static IQueryable<Titular> ApplySorting(IQueryable<Titular> query, string sortBy, bool desc)
    {
        return (sortBy.ToLowerInvariant(), desc) switch
        {
            ("tipo", true) => query.OrderByDescending(t => t.Tipo),
            ("tipo", false) => query.OrderBy(t => t.Tipo),
            ("fecha_creacion", true) => query.OrderByDescending(t => t.FechaCreacion),
            ("fecha_creacion", false) => query.OrderBy(t => t.FechaCreacion),
            ("nombre", true) => query.OrderByDescending(t => t.Nombre),
            _ => query.OrderBy(t => t.Nombre)
        };
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
