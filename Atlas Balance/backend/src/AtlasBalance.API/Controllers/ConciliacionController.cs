using System.Security.Claims;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize]
[Route("api/conciliacion")]
public sealed class ConciliacionController : ControllerBase
{
    private readonly IConciliacionService _conciliacionService;

    public ConciliacionController(IConciliacionService conciliacionService)
    {
        _conciliacionService = conciliacionService;
    }

    [HttpGet("movimientos-esperados")]
    public async Task<IActionResult> ListarMovimientosEsperados(
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] string? estado = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            return Ok(await _conciliacionService.ListarMovimientosEsperadosAsync(userId, rol, cuentaId, estado, cancellationToken));
        }
        catch (ConciliacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("movimientos-esperados")]
    public async Task<IActionResult> CrearMovimientoEsperado([FromBody] MovimientoEsperadoCrearRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            return Ok(await _conciliacionService.CrearMovimientoEsperadoAsync(userId, rol, request, HttpContext, cancellationToken));
        }
        catch (ConciliacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListarConciliaciones(
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] string? estado = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            return Ok(await _conciliacionService.ListarConciliacionesAsync(userId, rol, cuentaId, estado, cancellationToken));
        }
        catch (ConciliacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("sugerir")]
    public async Task<IActionResult> Sugerir([FromBody] ConciliacionSugerirRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            return Ok(await _conciliacionService.SugerirAsync(userId, rol, request, HttpContext, cancellationToken));
        }
        catch (ConciliacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/confirmar")]
    public Task<IActionResult> Confirmar(Guid id, [FromBody] ConciliacionCambiarEstadoRequest request, CancellationToken cancellationToken)
    {
        return CambiarEstadoAsync(id, request, "confirmar", cancellationToken);
    }

    [HttpPost("{id:guid}/excepcion")]
    public Task<IActionResult> MarcarExcepcion(Guid id, [FromBody] ConciliacionCambiarEstadoRequest request, CancellationToken cancellationToken)
    {
        return CambiarEstadoAsync(id, request, "excepcion", cancellationToken);
    }

    [HttpPost("{id:guid}/resolver")]
    public Task<IActionResult> Resolver(Guid id, [FromBody] ConciliacionCambiarEstadoRequest request, CancellationToken cancellationToken)
    {
        return CambiarEstadoAsync(id, request, "resolver", cancellationToken);
    }

    private async Task<IActionResult> CambiarEstadoAsync(Guid id, ConciliacionCambiarEstadoRequest request, string action, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = action switch
            {
                "confirmar" => await _conciliacionService.ConfirmarAsync(userId, rol, id, request, HttpContext, cancellationToken),
                "excepcion" => await _conciliacionService.MarcarExcepcionAsync(userId, rol, id, request, HttpContext, cancellationToken),
                _ => await _conciliacionService.ResolverAsync(userId, rol, id, request, HttpContext, cancellationToken)
            };
            return Ok(result);
        }
        catch (ConciliacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    private bool TryGetActor(out Guid userId, out string rol)
    {
        rol = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }
}
