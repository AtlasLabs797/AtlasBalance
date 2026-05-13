using System.Security.Claims;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN,GERENTE")]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("principal")]
    public async Task<IActionResult> Principal([FromQuery] string? divisaPrincipal, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _dashboardService.GetPrincipalAsync(userId, divisaPrincipal, cancellationToken);
            return Ok(result);
        }
        catch (DashboardAccessException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpGet("titular/{titularId:guid}")]
    public async Task<IActionResult> Titular(Guid titularId, [FromQuery] string? divisaPrincipal, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _dashboardService.GetTitularAsync(userId, titularId, divisaPrincipal, cancellationToken);
            return Ok(result);
        }
        catch (DashboardAccessException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpGet("saldos-divisa")]
    public async Task<IActionResult> SaldosDivisa(
        [FromQuery] string? divisaPrincipal,
        [FromQuery] Guid? titularId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _dashboardService.GetSaldosDivisaAsync(userId, divisaPrincipal, titularId, cancellationToken);
            return Ok(result);
        }
        catch (DashboardAccessException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpGet("evolucion")]
    public async Task<IActionResult> Evolucion(
        [FromQuery] string periodo = "1m",
        [FromQuery] string? divisaPrincipal = null,
        [FromQuery] Guid? titularId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _dashboardService.GetEvolucionAsync(userId, periodo, divisaPrincipal, titularId, cancellationToken);
            return Ok(result);
        }
        catch (DashboardAccessException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }
}
