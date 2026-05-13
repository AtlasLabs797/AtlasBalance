using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
            new RevisionQueryRequest { Estado = estado, Page = page, PageSize = pageSize },
            cancellationToken));
    }

    [HttpGet("seguros")]
    public async Task<IActionResult> Seguros(
        [FromQuery] string? estado = null,
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
            new RevisionQueryRequest { Estado = estado, Page = page, PageSize = pageSize },
            cancellationToken));
    }

    [HttpPatch("{tipo}/{extractoId:guid}")]
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
