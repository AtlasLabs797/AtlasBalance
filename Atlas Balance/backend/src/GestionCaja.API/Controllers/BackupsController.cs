using System.Security.Claims;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/backups")]
public sealed class BackupsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IBackupService _backupService;
    private readonly IWatchdogClientService _watchdogClientService;
    private readonly ILogger<BackupsController> _logger;

    public BackupsController(
        AppDbContext dbContext,
        IBackupService backupService,
        IWatchdogClientService watchdogClientService,
        ILogger<BackupsController>? logger = null)
    {
        _dbContext = dbContext;
        _backupService = backupService;
        _watchdogClientService = watchdogClientService;
        _logger = logger ?? NullLogger<BackupsController>.Instance;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "fecha_creacion",
        [FromQuery] string sortDir = "desc",
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        var query = _dbContext.Backups.AsNoTracking();
        var sorted = (sortBy.ToLowerInvariant(), desc) switch
        {
            ("estado", true) => query.OrderByDescending(x => x.Estado).ThenByDescending(x => x.FechaCreacion),
            ("estado", false) => query.OrderBy(x => x.Estado).ThenByDescending(x => x.FechaCreacion),
            ("tipo", true) => query.OrderByDescending(x => x.Tipo).ThenByDescending(x => x.FechaCreacion),
            ("tipo", false) => query.OrderBy(x => x.Tipo).ThenByDescending(x => x.FechaCreacion),
            ("fecha_creacion", false) => query.OrderBy(x => x.FechaCreacion),
            _ => query.OrderByDescending(x => x.FechaCreacion)
        };

        var total = await sorted.CountAsync(cancellationToken);
        var backups = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var userIds = backups.Where(b => b.IniciadoPorId.HasValue).Select(b => b.IniciadoPorId!.Value).Distinct().ToList();
        var usersMap = await _dbContext.Usuarios.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, cancellationToken);

        var items = backups.Select(x => new BackupListItemResponse
        {
            Id = x.Id,
            FechaCreacion = x.FechaCreacion,
            RutaArchivo = Path.GetFileName(x.RutaArchivo),
            TamanioBytes = x.TamanioBytes,
            Estado = x.Estado.ToString(),
            Tipo = x.Tipo.ToString(),
            IniciadoPorId = x.IniciadoPorId,
            IniciadoPorNombre = x.IniciadoPorId.HasValue ? usersMap.GetValueOrDefault(x.IniciadoPorId.Value) : null,
            Notas = x.Notas
        }).ToList();

        return Ok(new PaginatedResponse<BackupListItemResponse>
        {
            Data = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpPost("manual")]
    public async Task<IActionResult> BackupManual(CancellationToken cancellationToken)
    {
        try
        {
            var backup = await _backupService.CreateBackupAsync(TipoProceso.MANUAL, GetCurrentUserId(), cancellationToken);
            return Ok(new
            {
                backup.Id,
                Estado = backup.Estado.ToString(),
                RutaArchivo = Path.GetFileName(backup.RutaArchivo),
                backup.TamanioBytes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al crear backup manual");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "No se pudo crear el backup. Revise los logs del servidor." });
        }
    }

    [HttpPost("{id:guid}/restaurar")]
    public async Task<IActionResult> Restaurar(Guid id, [FromBody] RestaurarBackupRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Confirmacion, "RESTAURAR", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Confirmación inválida. Debe enviar 'RESTAURAR'." });
        }

        var backup = await _dbContext.Backups
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (backup is null)
        {
            return NotFound(new { error = "Backup no encontrado" });
        }

        if (!System.IO.File.Exists(backup.RutaArchivo))
        {
            return BadRequest(new { error = "El archivo de backup no existe en disco" });
        }

        var accepted = await _watchdogClientService.SolicitarRestauracionAsync(backup.RutaArchivo, GetCurrentUserId(), cancellationToken);
        if (!accepted)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Watchdog rechazó la restauración" });
        }

        return Accepted(new
        {
            message = "Restauración iniciada",
            backup_id = backup.Id
        });
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
