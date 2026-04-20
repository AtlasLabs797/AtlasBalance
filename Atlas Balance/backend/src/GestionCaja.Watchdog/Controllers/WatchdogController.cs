using GestionCaja.Watchdog.Models;
using GestionCaja.Watchdog.Services;
using Microsoft.AspNetCore.Mvc;

namespace GestionCaja.Watchdog.Controllers;

[ApiController]
[Route("watchdog")]
public sealed class WatchdogController : ControllerBase
{
    private readonly IWatchdogOperationsService _operationsService;
    private readonly IWatchdogStateStore _stateStore;

    public WatchdogController(IWatchdogOperationsService operationsService, IWatchdogStateStore stateStore)
    {
        _operationsService = operationsService;
        _stateStore = stateStore;
    }

    [HttpPost("restaurar-backup")]
    public async Task<IActionResult> RestaurarBackup([FromBody] RestaurarBackupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BackupPath))
        {
            return BadRequest(new { error = "backup_path es obligatorio" });
        }

        var accepted = await _operationsService.StartRestoreAsync(request.BackupPath, cancellationToken);
        if (!accepted)
        {
            return Conflict(new { error = "Ya hay una operacion watchdog en ejecucion o backup invalido" });
        }

        return Accepted(new { message = "Restauracion iniciada" });
    }

    [HttpPost("actualizar-app")]
    public async Task<IActionResult> ActualizarApp([FromBody] ActualizarAppRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return BadRequest(new { error = "source_path y target_path son obligatorios" });
        }

        var sourcePath = Path.GetFullPath(request.SourcePath);
        var targetPath = Path.GetFullPath(request.TargetPath);
        if (!Directory.Exists(sourcePath))
        {
            return BadRequest(new { error = "source_path no existe" });
        }

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "source_path y target_path no pueden ser iguales" });
        }

        if (PathsOverlap(sourcePath, targetPath))
        {
            return BadRequest(new { error = "source_path y target_path no pueden estar anidados" });
        }

        var accepted = await _operationsService.StartUpdateAsync(sourcePath, targetPath, cancellationToken);
        if (!accepted)
        {
            return Conflict(new { error = "Ya hay una operacion watchdog en ejecucion" });
        }

        return Accepted(new { message = "Actualizacion iniciada" });
    }

    [HttpGet("estado")]
    public async Task<IActionResult> Estado(CancellationToken cancellationToken)
    {
        var state = await _stateStore.GetAsync(cancellationToken);
        return Ok(state);
    }

    private static bool PathsOverlap(string sourcePath, string targetPath)
    {
        var sourceWithSeparator = EnsureTrailingSeparator(sourcePath);
        var targetWithSeparator = EnsureTrailingSeparator(targetPath);

        return sourceWithSeparator.StartsWith(targetWithSeparator, StringComparison.OrdinalIgnoreCase) ||
               targetWithSeparator.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }
}
