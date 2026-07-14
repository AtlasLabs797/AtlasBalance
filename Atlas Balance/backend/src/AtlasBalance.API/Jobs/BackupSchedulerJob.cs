using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

public sealed class BackupSchedulerJob
{
    private readonly AppDbContext _dbContext;
    private readonly IBackupService _backupService;
    private readonly IClock _clock;
    private readonly ILogger<BackupSchedulerJob> _logger;

    public BackupSchedulerJob(
        AppDbContext dbContext,
        IBackupService backupService,
        IClock clock,
        ILogger<BackupSchedulerJob> logger)
    {
        _dbContext = dbContext;
        _backupService = backupService;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var config = await _dbContext.Configuraciones
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, CancellationToken.None);
        var schedule = BackupSchedule.FromConfig(config);
        var lastStarted = ParseDate(BackupConfigurationService.GetValue(config, "backup_auto_last_started_utc"));
        var now = _clock.UtcNow;

        if (!schedule.IsDue(now, lastStarted))
        {
            return;
        }

        var hasPending = await _dbContext.Backups
            .AnyAsync(x => x.Estado == EstadoProceso.PENDING, CancellationToken.None);
        if (hasPending)
        {
            _logger.LogInformation("Backup automatico omitido porque ya hay una copia pendiente.");
            return;
        }

        await UpsertConfigAsync("backup_auto_last_started_utc", now.ToString("O", CultureInfo.InvariantCulture), CancellationToken.None);
        await UpsertConfigAsync("backup_auto_last_result", "STARTED", CancellationToken.None);

        try
        {
            await _backupService.CreateBackupAsync(TipoProceso.AUTO, null, CancellationToken.None);
            await UpsertConfigAsync("backup_auto_last_result", "SUCCESS", CancellationToken.None);
            _logger.LogInformation("BackupSchedulerJob completo");
        }
        catch (Exception ex)
        {
            await UpsertConfigAsync("backup_auto_last_result", "FAILED", CancellationToken.None);
            _logger.LogError(ex, "BackupSchedulerJob fallo");
            throw;
        }
    }

    private async Task UpsertConfigAsync(string key, string value, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Configuraciones
            .FirstOrDefaultAsync(x => x.Clave == key, cancellationToken);
        if (row is null)
        {
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = key,
                Valor = value,
                Tipo = "string",
                FechaModificacion = DateTime.UtcNow
            });
        }
        else
        {
            row.Valor = value;
            row.FechaModificacion = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTime? ParseDate(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        return null;
    }
}
