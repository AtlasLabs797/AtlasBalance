using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
        // SEC V-02.09: solo las reglas de negocio tipadas llegan al cliente con
        // su mensaje; cualquier otro fallo cae en el handler global (500 generico).
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("sincronizar")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
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
    // V-02.09: cota explicita para que ModelState rechace tasas fuera de
    // rango antes de llegar al servicio. ParseLimitsInInvariantCulture=true
    // evita que en servidores con cultura es-ES la coma como separador
    // decimal rompa la validacion (mismo bug que ya documento
    // ImportacionPlazoFijoMovimientoRequest.Monto).
    [System.ComponentModel.DataAnnotations.Range(
        typeof(decimal),
        "0.00000001",
        "9999999999.9999",
        ParseLimitsInInvariantCulture = true)]
    public decimal Tasa { get; set; }
}
