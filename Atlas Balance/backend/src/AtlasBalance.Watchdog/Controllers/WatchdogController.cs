using AtlasBalance.Watchdog.Models;
using AtlasBalance.Watchdog.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.Watchdog.Controllers;

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
    public async Task<IActionResult> RestaurarBackup([FromBody] RestaurarBackupRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.BackupPath) || request.OperationId is null || request.OperationId == Guid.Empty)
        {
            return BadRequest(new { error = "backup_path y operation_id son obligatorios" });
        }

        var accepted = await _operationsService.StartRestoreAsync(request.BackupPath, request.OperationId.Value, cancellationToken);
        if (!accepted)
        {
            return Conflict(new { error = "Ya hay una operacion watchdog en ejecucion o backup invalido" });
        }

        return Accepted(new { message = "Restauracion iniciada" });
    }

    [HttpPost("actualizar-app")]
    public async Task<IActionResult> ActualizarApp([FromBody] ActualizarAppRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return BadRequest(new { error = "source_path y target_path son obligatorios" });
        }

        // V-02.06 (PR F3): la verificacion de firma requiere el ZIP firmado
        // y su clave publica. Antes este endpoint aceptaba el campo opcional
        // y el Watchdog pasaba a "modo legacy" sin firma; en produccion eso
        // es exactamente el camino que puede instalar un ZIP manipulado.
        if (string.IsNullOrWhiteSpace(request.PackageZipPath))
        {
            return BadRequest(new { error = "package_zip_path es obligatorio. La actualizacion debe llegar firmada y su hash verificado contra BackupCloudCopy o el release de GitHub." });
        }

        if (!TryGetFullPath(request.SourcePath, out var sourcePath) ||
            !TryGetFullPath(request.TargetPath, out var targetPath) ||
            !TryGetFullPath(request.PackageZipPath, out var packageZipPath))
        {
            return BadRequest(new { error = "source_path, target_path o package_zip_path no son validos" });
        }

        if (!Directory.Exists(sourcePath))
        {
            return BadRequest(new { error = "source_path no existe" });
        }

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "source_path y target_path no pueden ser iguales" });
        }

        if (!System.IO.File.Exists(packageZipPath))
        {
            return BadRequest(new { error = $"package_zip_path no existe: '{packageZipPath}'" });
        }

        var accepted = await _operationsService.StartUpdateAsync(sourcePath, targetPath, packageZipPath, cancellationToken);
        if (!accepted)
        {
            return Conflict(new { error = "Ya hay una operacion watchdog en ejecucion o el paquete no paso la verificacion de integridad" });
        }

        return Accepted(new { message = "Actualizacion iniciada" });
    }

    [HttpGet("estado")]
    public async Task<IActionResult> Estado(CancellationToken cancellationToken)
    {
        var state = await _stateStore.GetAsync(cancellationToken);
        return Ok(state);
    }

    private static bool TryGetFullPath(string rawPath, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(rawPath);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }
}
