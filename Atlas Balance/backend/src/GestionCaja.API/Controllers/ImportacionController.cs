using System.Security.Claims;
using GestionCaja.API.DTOs;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize]
[Route("api/importacion")]
public sealed class ImportacionController : ControllerBase
{
    private readonly IImportacionService _importacionService;

    public ImportacionController(IImportacionService importacionService)
    {
        _importacionService = importacionService;
    }

    [HttpGet("contexto")]
    public async Task<IActionResult> Contexto(CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        var result = await _importacionService.GetContextoAsync(userId, rol, cancellationToken);
        return Ok(result);
    }

    [HttpPost("validar")]
    public async Task<IActionResult> Validar([FromBody] ImportacionValidarRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.ValidarAsync(userId, rol, request, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ImportacionConfirmarRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.ConfirmarAsync(userId, rol, request, HttpContext, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("plazo-fijo/movimiento")]
    public async Task<IActionResult> RegistrarMovimientoPlazoFijo([FromBody] ImportacionPlazoFijoMovimientoRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request invalido" });
        }

        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.RegistrarMovimientoPlazoFijoAsync(userId, rol, request, HttpContext, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
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
