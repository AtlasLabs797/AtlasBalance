using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/tipos-cambio")]
public sealed class TiposCambioController : ControllerBase
{
    private readonly ITiposCambioService _tiposCambioService;

    public TiposCambioController(ITiposCambioService tiposCambioService)
    {
        _tiposCambioService = tiposCambioService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var data = await _tiposCambioService.ListarTiposCambioAsync(cancellationToken);
        return Ok(data);
    }

    [HttpPut("{origen}/{destino}")]
    public async Task<IActionResult> GuardarManual(
        string origen,
        string destino,
        [FromBody] GuardarTipoCambioRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _tiposCambioService.GuardarTipoCambioManualAsync(origen, destino, request.Tasa, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("sincronizar")]
    public async Task<IActionResult> Sincronizar(CancellationToken cancellationToken)
    {
        var result = await _tiposCambioService.SincronizarTiposCambioAsync(cancellationToken);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = result.ErrorMessage ?? "No se pudo sincronizar" });
        }

        return Ok(new
        {
            updated_count = result.UpdatedCount,
            timestamp = DateTime.UtcNow
        });
    }
}

public sealed class GuardarTipoCambioRequest
{
    public decimal Tasa { get; set; }
}
