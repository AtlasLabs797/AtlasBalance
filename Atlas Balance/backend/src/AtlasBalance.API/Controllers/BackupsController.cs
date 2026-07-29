using System.Security.Claims;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/backups")]
public sealed class BackupsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IBackupService _backupService;
    private readonly IWatchdogClientService _watchdogClientService;
    private readonly IBackupConfigurationService _backupConfigurationService;
    private readonly IGoogleDriveBackupService _googleDriveBackupService;
    private readonly ILogger<BackupsController> _logger;
    private readonly IBackgroundJobClient? _backgroundJobs;

    public BackupsController(
        AppDbContext dbContext,
        IBackupService backupService,
        IWatchdogClientService watchdogClientService,
        IBackupConfigurationService backupConfigurationService,
        IGoogleDriveBackupService googleDriveBackupService,
        ILogger<BackupsController>? logger = null,
        IBackgroundJobClient? backgroundJobs = null)
    {
        _dbContext = dbContext;
        _backupService = backupService;
        _watchdogClientService = watchdogClientService;
        _backupConfigurationService = backupConfigurationService;
        _googleDriveBackupService = googleDriveBackupService;
        _logger = logger ?? NullLogger<BackupsController>.Instance;
        _backgroundJobs = backgroundJobs;
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
        var backupIds = backups.Select(x => x.Id).ToList();
        var cloudCopies = await _dbContext.BackupCloudCopies
            .AsNoTracking()
            .Where(x => backupIds.Contains(x.BackupId))
            .OrderByDescending(x => x.FechaCreacion)
            .ToListAsync(cancellationToken);
        var latestCloudCopies = cloudCopies
            .GroupBy(x => x.BackupId)
            .ToDictionary(x => x.Key, x => x.First());

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
            Notas = x.Notas,
            Destino = latestCloudCopies.ContainsKey(x.Id) ? "LOCAL_Y_GOOGLE_DRIVE" : "LOCAL",
            CloudProvider = latestCloudCopies.GetValueOrDefault(x.Id)?.Provider,
            CloudEstado = latestCloudCopies.GetValueOrDefault(x.Id)?.Estado,
            CloudUploadedAt = latestCloudCopies.GetValueOrDefault(x.Id)?.UploadedAt,
            CloudFileId = latestCloudCopies.GetValueOrDefault(x.Id)?.RemoteFileId,
            CloudFileName = latestCloudCopies.GetValueOrDefault(x.Id)?.RemoteFileName,
            CloudErrorMessage = latestCloudCopies.GetValueOrDefault(x.Id)?.ErrorMessage
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

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var response = await _backupConfigurationService.GetAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateBackupConfigRequest request, CancellationToken cancellationToken)
    {
        var result = await _backupConfigurationService.UpdateAsync(request, GetCurrentUserId(), HttpContext, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { message = "Configuracion de copias actualizada." });
    }

    [HttpPost("manual")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> BackupManual(CancellationToken cancellationToken)
    {
        if (_backgroundJobs is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "El procesador de operaciones no esta disponible." });
        }

        var operation = await CreateOperationAsync("MANUAL", null, cancellationToken);
        var userId = operation.UsuarioId;
        try
        {
            _backgroundJobs.Enqueue<BackupOperationJob>(job => job.ExecuteManualAsync(operation.Id, userId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(operation, "No se pudo encolar la operacion.", CancellationToken.None);
            _logger.LogError(ex, "No se pudo encolar el backup manual {OperationId}", operation.Id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "No se pudo iniciar la copia de seguridad.", operation_id = operation.Id });
        }
        return Accepted(new { operation_id = operation.Id, status = operation.Estado });
    }

    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> GetOperation(Guid id, CancellationToken cancellationToken)
    {
        var operation = await _dbContext.BackupOperations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (operation is null)
        {
            return NotFound(new { error = "Operacion no encontrada." });
        }

        if (operation.Tipo == "RESTORE" && operation.Estado == "RUNNING")
        {
            var watchdog = await _watchdogClientService.GetEstadoAsync(cancellationToken);
            var watchdogState = watchdog.Estado?.ToUpperInvariant();
            if (watchdog.OperationId == operation.Id && watchdogState is "SUCCESS" or "FAILED")
            {
                var tracked = await _dbContext.BackupOperations.FirstAsync(x => x.Id == id, cancellationToken);
                tracked.Estado = watchdogState;
                tracked.Error = watchdogState == "FAILED" ? watchdog.Mensaje : null;
                tracked.FechaFin = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                operation = tracked;
            }
        }

        return Ok(new
        {
            operation_id = operation.Id,
            type = operation.Tipo,
            status = operation.Estado,
            backup_id = operation.BackupId,
            error = operation.Error,
            result = operation.ResultadoJson
        });
    }

    [HttpPost("google-drive/link/start")]
    public async Task<IActionResult> StartGoogleDriveLink(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _googleDriveBackupService.StartLinkAsync(cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo iniciar vinculacion con Google Drive");
            return BadRequest(new { error = "No se pudo iniciar la vinculacion con Google Drive. Revisa el OAuth Client ID y Client Secret." });
        }
    }

    [HttpGet("google-drive/link/{sessionId:guid}")]
    public async Task<IActionResult> PollGoogleDriveLink(Guid sessionId, CancellationToken cancellationToken)
    {
        var response = await _googleDriveBackupService.PollLinkAsync(sessionId, GetCurrentUserId(), HttpContext, cancellationToken);
        return Ok(response);
    }

    [HttpPost("google-drive/disconnect")]
    public async Task<IActionResult> DisconnectGoogleDrive(CancellationToken cancellationToken)
    {
        await _googleDriveBackupService.DisconnectAsync(GetCurrentUserId(), HttpContext, cancellationToken);
        return Ok(new { message = "Google Drive desvinculado." });
    }

    [HttpPost("google-drive/test")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> TestGoogleDrive(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _googleDriveBackupService.TestConnectionAsync(cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prueba de Google Drive fallida");
            return BadRequest(new { error = "No se pudo validar Google Drive. Vuelve a vincular la cuenta." });
        }
    }

    [HttpPost("{id:guid}/google-drive/retry")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> RetryGoogleDriveUpload(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _googleDriveBackupService.UploadBackupByIdAsync(id, cancellationToken);
            return Ok(new { message = "Subida a Google Drive completada." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reintento de subida a Google Drive fallido para backup {BackupId}", id);
            return BadRequest(new { error = "No se pudo subir esta copia a Google Drive." });
        }
    }

    [HttpGet("google-drive/files")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> ListGoogleDriveFiles(CancellationToken cancellationToken)
    {
        try
        {
            var files = await _googleDriveBackupService.ListFilesAsync(cancellationToken);
            return Ok(new { data = files });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron listar copias en Google Drive");
            return BadRequest(new { error = "No se pudieron listar las copias de Google Drive." });
        }
    }

    [HttpPost("google-drive/import")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> ImportGoogleDriveFile([FromBody] GoogleDriveImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FileId))
        {
            return BadRequest(new { error = "Debe indicar el archivo de Google Drive." });
        }

        if (_backgroundJobs is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "El procesador de operaciones no esta disponible." });
        }

        var operation = await CreateOperationAsync("DRIVE_IMPORT", request.FileId, cancellationToken);
        var userId = operation.UsuarioId;
        try
        {
            _backgroundJobs.Enqueue<BackupOperationJob>(job => job.ExecuteDriveImportAsync(operation.Id, request.FileId, userId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(operation, "No se pudo encolar la operacion.", CancellationToken.None);
            _logger.LogError(ex, "No se pudo encolar el import de Drive {OperationId}", operation.Id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "No se pudo iniciar la importacion de Google Drive.", operation_id = operation.Id });
        }
        return Accepted(new { operation_id = operation.Id, status = operation.Estado });
    }

    [HttpPost("{id:guid}/restaurar")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> Restaurar(Guid id, [FromBody] RestaurarBackupRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Confirmacion, "RESTAURAR", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "La confirmacion de restauracion no es valida." });
        }

        var backup = await _dbContext.Backups
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (backup is null)
        {
            return NotFound(new { error = "Copia de seguridad no encontrada." });
        }

        var backupRoot = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(c => c.Clave == "backup_path")
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken) ?? @"C:\atlas-balance\backups";

        if (string.IsNullOrWhiteSpace(backup.RutaArchivo) || !IsAllowedBackupFile(backup.RutaArchivo, backupRoot))
        {
            _logger.LogWarning("Restauracion de backup {BackupId} bloqueada por ruta no permitida", id);
            return BadRequest(new { error = "La ruta de la copia de seguridad no es valida." });
        }

        if (!System.IO.File.Exists(backup.RutaArchivo))
        {
            return BadRequest(new { error = "El archivo de la copia de seguridad no existe en disco." });
        }

        var operation = await CreateOperationAsync("RESTORE", backup.Id.ToString("N"), cancellationToken, backup.Id);
        bool accepted;
        try
        {
            accepted = await _watchdogClientService.SolicitarRestauracionAsync(
                backup.RutaArchivo,
                operation.UsuarioId,
                operation.Id,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(operation, "El servicio de mantenimiento no esta disponible.", CancellationToken.None);
            _logger.LogError(ex, "No se pudo solicitar restauracion para la operacion {OperationId}", operation.Id);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "El servicio de mantenimiento no esta disponible.", operation_id = operation.Id });
        }

        if (!accepted)
        {
            await MarkOperationFailedAsync(operation, "El servicio de mantenimiento rechazo la restauracion.", CancellationToken.None);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "El servicio de mantenimiento rechazo la restauracion.", operation_id = operation.Id });
        }

        operation.Estado = "RUNNING";
        operation.FechaInicio = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Accepted(new
        {
            message = "Restauración iniciada",
            backup_id = backup.Id,
            operation_id = operation.Id,
            status = operation.Estado
        });
    }

    private async Task<BackupOperation> CreateOperationAsync(
        string type,
        string? parameter,
        CancellationToken cancellationToken,
        Guid? backupId = null)
    {
        var operation = new BackupOperation
        {
            Id = Guid.NewGuid(),
            Tipo = type,
            Estado = "PENDING",
            UsuarioId = GetCurrentUserId(),
            BackupId = backupId,
            Parametro = parameter,
            FechaCreacion = DateTime.UtcNow
        };
        _dbContext.BackupOperations.Add(operation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    private async Task MarkOperationFailedAsync(BackupOperation operation, string error, CancellationToken cancellationToken)
    {
        operation.Estado = "FAILED";
        operation.Error = error;
        operation.FechaFin = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private static bool IsAllowedBackupFile(string filePath, string backupRoot)
    {
        if (!IsExplicitlyRooted(filePath))
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(filePath), ".dump", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsExplicitlyRooted(backupRoot))
        {
            return false;
        }

        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(backupRoot));
            return fullFilePath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }

    private static bool IsExplicitlyRooted(string path)
    {
        return Path.IsPathRooted(path) ||
               (path.Length >= 3 &&
                char.IsLetter(path[0]) &&
                path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'));
    }
}
