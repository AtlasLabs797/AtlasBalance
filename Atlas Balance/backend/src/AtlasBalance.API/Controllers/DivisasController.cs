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
        // SEC V-02.09: solo las reglas de negocio tipadas llegan al cliente con
        // su mensaje; cualquier otro fallo cae en el handler global (500 generico).
        catch (BusinessRuleException ex)
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
        // SEC V-02.09: solo las reglas de negocio tipadas llegan al cliente con
        // su mensaje; cualquier otro fallo cae en el handler global (500 generico).
        catch (BusinessRuleException ex)
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
        // SEC V-02.09: solo las reglas de negocio tipadas llegan al cliente con
        // su mensaje; cualquier otro fallo cae en el handler global (500 generico).
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed class CrearDivisaRequest
{
    // V-02.09: tope 8 (espejo DIVISAS.codigo). El servicio validaba por
    // codigo, pero la cota tiene que vivir en el DTO.
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string Codigo { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.MaxLength(128)]
    public string? Nombre { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Simbolo { get; set; }
    public bool Activa { get; set; } = true;
    public bool EsBase { get; set; }
}

public sealed class ActualizarDivisaRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(128)]
    public string? Nombre { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Simbolo { get; set; }
    public bool Activa { get; set; } = true;
    public bool EsBase { get; set; }
}

public sealed class EstablecerDivisaPorDefectoRequest
{
    // V-02.07: sin [Required] este campo en blanco no daba error: TiposCambioService
    // .Normalize convierte vacio o null en "EUR", asi que un POST con el codigo
    // vacio establecia el euro como divisa base sin que nadie lo hubiera pedido.
    // Para el endpoint que cambia la divisa base de toda la aplicacion, ese
    // silencio es lo peor que podia hacer.
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string Codigo { get; set; } = string.Empty;
}
