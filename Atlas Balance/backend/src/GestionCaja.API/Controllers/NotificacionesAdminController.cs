using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/notificaciones-admin")]
public sealed class NotificacionesAdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public NotificacionesAdminController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen(CancellationToken cancellationToken)
    {
        var unreadQuery = _dbContext.NotificacionesAdmin
            .AsNoTracking()
            .Where(n => !n.Leida);

        var totalPendientes = await unreadQuery.CountAsync(cancellationToken);
        var exportacionesPendientes = await unreadQuery
            .Where(n => n.Tipo == "EXPORTACION")
            .CountAsync(cancellationToken);

        return Ok(new NotificacionesAdminResumenResponse
        {
            ExportacionesPendientes = exportacionesPendientes,
            TotalPendientes = totalPendientes
        });
    }

    [HttpPost("marcar-leidas")]
    public async Task<IActionResult> MarcarLeidas(
        [FromBody] MarcarNotificacionesLeidasRequest? request,
        CancellationToken cancellationToken)
    {
        var normalizedType = request?.Tipo?.Trim().ToUpperInvariant();
        var query = _dbContext.NotificacionesAdmin.Where(n => !n.Leida);

        if (!string.IsNullOrWhiteSpace(normalizedType))
        {
            query = query.Where(n => n.Tipo == normalizedType);
        }

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Leida = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { updated = rows.Count });
    }
}
