using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/divisas")]
public sealed class DivisasController : ControllerBase
{
    private readonly ITiposCambioService _tiposCambioService;

    public DivisasController(ITiposCambioService tiposCambioService)
    {
        _tiposCambioService = tiposCambioService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var data = await _tiposCambioService.ListarDivisasAsync(cancellationToken);
        return Ok(data);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearDivisaRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Codigo))
        {
            return BadRequest(new { error = "El código de divisa es obligatorio." });
        }

        try
        {
            var divisa = await _tiposCambioService.CrearDivisaAsync(
                request.Codigo,
                request.Nombre,
                request.Simbolo,
                request.Activa,
                request.EsBase,
                cancellationToken);

            return CreatedAtAction(nameof(Listar), new { codigo = divisa.Codigo }, divisa);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{codigo}")]
    public async Task<IActionResult> Actualizar(string codigo, [FromBody] ActualizarDivisaRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var divisa = await _tiposCambioService.ActualizarDivisaAsync(
                codigo,
                request.Nombre,
                request.Simbolo,
                request.Activa,
                request.EsBase,
                cancellationToken);

            return Ok(divisa);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("establecer-por-defecto")]
    public async Task<IActionResult> EstablecerPorDefecto([FromBody] EstablecerDivisaPorDefectoRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var divisa = await _tiposCambioService.ActualizarDivisaAsync(
                request.Codigo,
                null,
                null,
                true,
                true,
                cancellationToken);

            return Ok(new { mensaje = $"Divisa {divisa.Codigo} establecida como base", divisa });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed class CrearDivisaRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string? Simbolo { get; set; }
    public bool Activa { get; set; } = true;
    public bool EsBase { get; set; }
}

public sealed class ActualizarDivisaRequest
{
    public string? Nombre { get; set; }
    public string? Simbolo { get; set; }
    public bool Activa { get; set; } = true;
    public bool EsBase { get; set; }
}

public sealed class EstablecerDivisaPorDefectoRequest
{
    public string Codigo { get; set; } = string.Empty;
}
