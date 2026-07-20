using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

public sealed class BackupOperationJob
{
    private readonly AppDbContext _dbContext;
    private readonly IBackupService _backupService;
    private readonly IGoogleDriveBackupService _googleDriveBackupService;
    private readonly ILogger<BackupOperationJob> _logger;

    public BackupOperationJob(
        AppDbContext dbContext,
        IBackupService backupService,
        IGoogleDriveBackupService googleDriveBackupService,
        ILogger<BackupOperationJob> logger)
    {
        _dbContext = dbContext;
        _backupService = backupService;
        _googleDriveBackupService = googleDriveBackupService;
        _logger = logger;
    }

    public async Task ExecuteManualAsync(Guid operationId, Guid? userId, CancellationToken cancellationToken)
    {
        await ExecuteAsync(operationId, async () =>
        {
            var backup = await _backupService.CreateBackupAsync(TipoProceso.MANUAL, userId, cancellationToken);
            return (backup.Id, JsonSerializer.Serialize(new { backup_id = backup.Id }));
        }, cancellationToken);
    }

    public async Task ExecuteDriveImportAsync(Guid operationId, string fileId, Guid? userId, CancellationToken cancellationToken)
    {
        await ExecuteAsync(operationId, async () =>
        {
            var backup = await _googleDriveBackupService.ImportAsync(fileId, userId, null, cancellationToken);
            return (backup.Id, JsonSerializer.Serialize(new { backup_id = backup.Id }));
        }, cancellationToken);
    }

    private async Task ExecuteAsync(Guid operationId, Func<Task<(Guid BackupId, string Result)>> action, CancellationToken cancellationToken)
    {
        var operation = await _dbContext.BackupOperations.FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken)
            ?? throw new InvalidOperationException("Operacion de backup no encontrada.");
        if (operation.Estado is "SUCCESS" or "FAILED") return;

        operation.Estado = "RUNNING";
        operation.FechaInicio = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await action();
            operation.BackupId = result.BackupId;
            operation.ResultadoJson = result.Result;
            operation.Estado = "SUCCESS";
        }
        catch (Exception ex)
        {
            operation.Estado = "FAILED";
            operation.Error = "La operacion no pudo completarse.";
            _logger.LogError(ex, "Fallo la operacion de backup {OperationId}", operationId);
        }
        finally
        {
            operation.FechaFin = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }
}
