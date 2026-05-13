using AtlasBalance.API.Services;
using AtlasBalance.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/sistema")]
public sealed class SistemaController : ControllerBase
{
    private readonly IWatchdogClientService _watchdogClientService;
    private readonly IActualizacionService _actualizacionService;

    public SistemaController(IWatchdogClientService watchdogClientService, IActualizacionService actualizacionService)
    {
        _watchdogClientService = watchdogClientService;
        _actualizacionService = actualizacionService;
    }

    [HttpGet("version-actual")]
    public async Task<IActionResult> VersionActual(CancellationToken cancellationToken)
    {
        var data = await _actualizacionService.GetVersionActualAsync(cancellationToken);
        return Ok(data);
    }

    [HttpGet("version-disponible")]
    public async Task<IActionResult> VersionDisponible(CancellationToken cancellationToken)
    {
        var data = await _actualizacionService.CheckVersionDisponibleAsync(cancellationToken);
        return Ok(data);
    }

    [HttpPost("actualizar")]
    public async Task<IActionResult> Actualizar([FromBody] ActualizacionRequest? request, CancellationToken cancellationToken)
    {
        var available = await _actualizacionService.CheckVersionDisponibleAsync(cancellationToken);
        if (!available.ActualizacionDisponible)
        {
            return BadRequest(new { error = available.Mensaje ?? "No hay actualización disponible." });
        }

        var accepted = await _actualizacionService.IniciarActualizacionAsync(
            request?.SourcePath,
            request?.TargetPath,
            cancellationToken);

        if (!accepted)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Watchdog rechazó la actualización." });
        }

        return Accepted(new { message = "Actualización iniciada" });
    }

    [HttpGet("estado")]
    public async Task<IActionResult> Estado(CancellationToken cancellationToken)
    {
        var estado = await _watchdogClientService.GetEstadoAsync(cancellationToken);
        return Ok(estado);
    }
}
