using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize]
[Route("api/revision")]
public sealed class RevisionController : ControllerBase
{
    private readonly IRevisionService _revisionService;
    private readonly IUserAccessService _userAccessService;

    public RevisionController(IRevisionService revisionService, IUserAccessService userAccessService)
    {
        _revisionService = revisionService;
        _userAccessService = userAccessService;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        return Ok(await _revisionService.GetSettingsAsync(cancellationToken));
    }

    [HttpGet("comisiones")]
    public async Task<IActionResult> Comisiones(
        [FromQuery] string? estado = null,
        [FromQuery] Guid? paisId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        return Ok(await _revisionService.GetComisionesAsync(
            scope,
            new RevisionQueryRequest { Estado = estado, PaisId = paisId, Page = page, PageSize = pageSize },
            cancellationToken));
    }

    [HttpGet("seguros")]
    public async Task<IActionResult> Seguros(
        [FromQuery] string? estado = null,
        [FromQuery] Guid? paisId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        return Ok(await _revisionService.GetSegurosAsync(
            scope,
            new RevisionQueryRequest { Estado = estado, PaisId = paisId, Page = page, PageSize = pageSize },
            cancellationToken));
    }

    // V-02.08: verifica la devolucion de una comision emparejandola
    // automaticamente con su bonificacion (misma cuenta, importe exacto
    // opuesto, fecha posterior). Sin candidata -> 409.
    [HttpPost("comision/{extractoId:guid}/verificar-devolucion")]
    public async Task<IActionResult> VerificarDevolucion(Guid extractoId, CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _revisionService.VerificarDevolucionAsync(scope, extractoId, cancellationToken);
            return result.Encontrada ? Ok(result) : Conflict(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        // SEC V-02.09: solo las reglas de negocio tipadas llegan al cliente con
        // su mensaje; cualquier otro fallo cae en el handler global (500 generico).
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (DbUpdateException)
        {
            // Indice unico parcial sobre extracto_devolucion_id: el abono ya se
            // asigno a otra comision en carrera.
            return Conflict(new { error = "La bonificacion ya ha sido asignada a otra comision." });
        }
    }

    // V-02.09: regex explicito en el path param {tipo} para que un valor no
    // esperado (typo, probing) se rechace con 404 en el routing en vez de
    // llegar al servicio y rebotar como InvalidOperationException. Solo se
    // esperan dos valores: "comision" y "seguro".
    [HttpPatch("{tipo:regex(^(comision|seguro)$)}/{extractoId:guid}")]
    public async Task<IActionResult> ActualizarEstado(string tipo, Guid extractoId, [FromBody] UpdateRevisionEstadoRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "La solicitud de revision esta incompleta." });
        }

        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            await _revisionService.SetEstadoAsync(scope, extractoId, tipo, request.Estado, cancellationToken);
            return Ok(new { message = "Estado actualizado" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        // SEC V-02.09: solo las reglas de negocio tipadas llegan al cliente con
        // su mensaje; cualquier otro fallo cae en el handler global (500 generico).
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
