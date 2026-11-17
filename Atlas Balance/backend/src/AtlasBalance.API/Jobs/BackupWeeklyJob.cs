using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Hangfire;

namespace AtlasBalance.API.Jobs;

// J-1: el backup semanal no puede solaparse con otro backup (mismo fichero)
// ni con los dos workers de Hangfire. El timeout de 3600 s cubre el timeout
// interno de 30 minutos de pg_dump.
[DisableConcurrentExecution(timeoutInSeconds: 3600)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
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
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
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
