using GestionCaja.API.Models;
using GestionCaja.API.Services;

namespace GestionCaja.API.Jobs;

public sealed class BackupWeeklyJob
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupWeeklyJob> _logger;

    public BackupWeeklyJob(IBackupService backupService, ILogger<BackupWeeklyJob> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            await _backupService.CreateBackupAsync(TipoProceso.AUTO, null, CancellationToken.None);
            _logger.LogInformation("BackupWeeklyJob completado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackupWeeklyJob falló");
            throw;
        }
    }
}
