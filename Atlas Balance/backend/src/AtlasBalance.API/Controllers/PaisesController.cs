using System.Security.Claims;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize]
[Route("api/paises")]
public sealed class PaisesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public PaisesController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] bool incluirInactivos = false,
        [FromQuery] bool incluirEliminados = false,
        [FromQuery] bool? activos = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        // SEC V-02.09: cota del filtro libre que viaja a LIKE/ILike; evita
        // busquedas kilometricas gastando CPU en cada listado.
        if (search is { Length: > 200 })
        {
            search = search[..200];
        }

        var isAdmin = User.IsInRole(nameof(RolUsuario.ADMIN));
        if (!isAdmin)
        {
            incluirInactivos = false;
            incluirEliminados = false;
            activos = true;
        }

        IQueryable<Pais> query = incluirEliminados
            ? _dbContext.Paises.IgnoreQueryFilters()
            : _dbContext.Paises;

        if (activos.HasValue)
        {
            query = query.Where(x => x.Activo == activos.Value);
        }
        else if (!incluirInactivos)
        {
            query = query.Where(x => x.Activo);
        }

        // V-02.08: el campo de busqueda de la UI (PaisesPage) mandaba "search"
        // sin que el backend lo aceptara, asi que el servidor lo ignoraba y
        // devolvia siempre la misma pagina sin filtrar.
        var searchTerm = search?.Trim();
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Nombre, $"%{searchTerm}%") ||
                (x.CodigoIso2 != null && EF.Functions.ILike(x.CodigoIso2, $"%{searchTerm}%")));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);

        var total = await query.CountAsync(cancellationToken);
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling((double)total / pageSize);

        var data = await query
            .OrderBy(x => x.Nombre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PaisResponse
            {
                Id = x.Id,
                Nombre = x.Nombre,
                CodigoIso2 = x.CodigoIso2,
                Activo = x.Activo,
                FechaCreacion = x.FechaCreacion,
                FechaModificacion = x.FechaModificacion,
                DeletedAt = x.DeletedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<PaisResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
        });
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Crear([FromBody] SavePaisRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidatePaisAsync(request, null, cancellationToken);
        if (validation.Error is not null)
        {
            return BadRequest(new { error = validation.Error });
        }

        var now = DateTime.UtcNow;
        var pais = new Pais
        {
            Id = Guid.NewGuid(),
            Nombre = validation.Nombre!,
            CodigoIso2 = validation.CodigoIso2,
            Activo = request.Activo,
            FechaCreacion = now,
            FechaModificacion = now
        };

        _dbContext.Paises.Add(pais);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(GetCurrentUserId(), "pais_creado", "PAISES", pais.Id, HttpContext, JsonSerializer.Serialize(new { pais.Nombre, pais.CodigoIso2, pais.Activo }), cancellationToken);

        return CreatedAtAction(nameof(Listar), new { id = pais.Id }, MapPais(pais));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] SavePaisRequest request, CancellationToken cancellationToken)
    {
        var pais = await _dbContext.Paises.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (pais is null)
        {
            return NotFound(new { error = "Pais no encontrado" });
        }

        var validation = await ValidatePaisAsync(request, id, cancellationToken);
        if (validation.Error is not null)
        {
            return BadRequest(new { error = validation.Error });
        }

        var before = new { pais.Nombre, pais.CodigoIso2, pais.Activo, pais.DeletedAt };
        pais.Nombre = validation.Nombre!;
        pais.CodigoIso2 = validation.CodigoIso2;
        pais.Activo = request.Activo;
        pais.FechaModificacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "pais_actualizado", "PAISES", pais.Id, HttpContext, JsonSerializer.Serialize(new { before, after = new { pais.Nombre, pais.CodigoIso2, pais.Activo, pais.DeletedAt } }), cancellationToken);
        return Ok(MapPais(pais));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var pais = await _dbContext.Paises.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (pais is null)
        {
            return NotFound(new { error = "Pais no encontrado" });
        }

        pais.Activo = false;
        pais.DeletedAt = DateTime.UtcNow;
        pais.DeletedById = GetCurrentUserId();
        pais.FechaModificacion = pais.DeletedAt;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(GetCurrentUserId(), "pais_eliminado", "PAISES", pais.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Pais eliminado" });
    }

    [HttpPost("{id:guid}/restaurar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken cancellationToken)
    {
        var pais = await _dbContext.Paises.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (pais is null)
        {
            return NotFound(new { error = "Pais no encontrado" });
        }

        pais.Activo = true;
        pais.DeletedAt = null;
        pais.DeletedById = null;
        pais.FechaModificacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(GetCurrentUserId(), "pais_restaurado", "PAISES", pais.Id, HttpContext, null, cancellationToken);

        return Ok(MapPais(pais));
    }

    private async Task<PaisValidationResult> ValidatePaisAsync(SavePaisRequest request, Guid? currentId, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Nombre))
        {
            return new PaisValidationResult("Nombre es obligatorio", null, null);
        }

        var nombre = request.Nombre.Trim();
        if (nombre.Length > 128)
        {
            return new PaisValidationResult("Nombre no puede superar 128 caracteres", null, null);
        }

        var codigoIso2 = request.CodigoIso2?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(codigoIso2))
        {
            codigoIso2 = null;
        }

        if (codigoIso2 is not null && (codigoIso2.Length != 2 || !codigoIso2.All(char.IsAsciiLetter)))
        {
            return new PaisValidationResult("Codigo ISO2 debe tener dos letras", null, null);
        }

        var duplicateName = await _dbContext.Paises
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id != currentId && x.Nombre.ToLower() == nombre.ToLower(), cancellationToken);
        if (duplicateName)
        {
            return new PaisValidationResult("Ya existe un pais con ese nombre", null, null);
        }

        if (codigoIso2 is not null)
        {
            var duplicateCode = await _dbContext.Paises
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id != currentId && x.CodigoIso2 != null && x.CodigoIso2.ToLower() == codigoIso2.ToLower(), cancellationToken);
            if (duplicateCode)
            {
                return new PaisValidationResult("Ya existe un pais con ese codigo ISO2", null, null);
            }
        }

        return new PaisValidationResult(null, nombre, codigoIso2);
    }

    private static PaisResponse MapPais(Pais pais) => new()
    {
        Id = pais.Id,
        Nombre = pais.Nombre,
        CodigoIso2 = pais.CodigoIso2,
        Activo = pais.Activo,
        FechaCreacion = pais.FechaCreacion,
        FechaModificacion = pais.FechaModificacion,
        DeletedAt = pais.DeletedAt
    };

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private sealed record PaisValidationResult(string? Error, string? Nombre, string? CodigoIso2);
}
