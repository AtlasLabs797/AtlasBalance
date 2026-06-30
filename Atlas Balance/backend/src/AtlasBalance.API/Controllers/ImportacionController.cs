using System.Security.Claims;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

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
    public async Task<IActionResult> Contexto([FromQuery] Guid? paisId = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        var result = await _importacionService.GetContextoAsync(userId, rol, paisId, cancellationToken);
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

    [HttpGet("lotes")]
    public async Task<IActionResult> ListarLotes(
        [FromQuery] Guid? cuentaId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.ListarLotesAsync(userId, rol, cuentaId, page, pageSize, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("lotes")]
    public async Task<IActionResult> CrearLote([FromBody] ImportacionLoteCrearRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.CrearLoteAsync(userId, rol, request, HttpContext, cancellationToken);
            return CreatedAtAction(nameof(ObtenerLote), new { id = result.Lote.Id }, result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpGet("lotes/{id:guid}")]
    public async Task<IActionResult> ObtenerLote(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.ObtenerLoteAsync(userId, rol, id, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpGet("lotes/{id:guid}/filas")]
    public async Task<IActionResult> ListarLoteFilas(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.ListarLoteFilasAsync(userId, rol, id, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("lotes/{id:guid}/confirmar")]
    public async Task<IActionResult> ConfirmarLote(Guid id, [FromBody] ImportacionLoteConfirmarRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.ConfirmarLoteAsync(userId, rol, id, request, HttpContext, cancellationToken);
            return Ok(result);
        }
        catch (ImportacionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("lotes/{id:guid}/revertir")]
    public async Task<IActionResult> RevertirLote(Guid id, [FromBody] ImportacionLoteRevertirRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId, out var rol))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _importacionService.RevertirLoteAsync(userId, rol, id, request, HttpContext, cancellationToken);
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
            return BadRequest(new { error = "Selecciona una cuenta y adjunta el archivo que quieres importar." });
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
