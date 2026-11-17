using System.Globalization;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// J-1: sin DisableConcurrentExecution, dos workers de Hangfire pueden lanzar
// dos backups a la vez y pisarse el fichero. El timeout de 3600 s cubre el
// timeout interno de 30 minutos de pg_dump (BackupService.ProcessTimeout).
[DisableConcurrentExecution(timeoutInSeconds: 3600)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class BackupSchedulerJob
{
    // A-03: un backup abandonado (el proceso se quedo a mitad) no puede dejar
    // el backup automatico bloqueado para siempre.
    private static readonly TimeSpan PendingAbandonedThreshold = TimeSpan.FromHours(2);

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
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var config = await _dbContext.Configuraciones
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, CancellationToken.None);
        var schedule = BackupSchedule.FromConfig(config);
        var lastStarted = ParseDate(BackupConfigurationService.GetValue(config, "backup_auto_last_started_utc"));
        var now = _clock.UtcNow;

        // A-03: un PENDING de mas de 2 horas es un backup abandonado (el proceso
        // se quedo a mitad). Se marca FAILED para que deje de bloquear el
        // backup automatico.
        await AbandonStalePendingBackupsAsync(now);

        // A-03: aviso por antiguedad del ultimo backup correcto. Se comprueba
        // en cada pasada del job (cada 15 minutos), se dispare o no el backup.
        await CheckLastSuccessfulBackupAsync(schedule, now);

        // J-1: si la ejecucion anterior se quedo a mitad (resultado STARTED), el
        // reintento automatico de Hangfire tiene que volver a lanzar el backup
        // de verdad. Sin esto, IsDue(now, lastStarted) daria false y el
        // reintento no haria nada.
        var interruptedRun = string.Equals(
            BackupConfigurationService.GetValue(config, "backup_auto_last_result"),
            "STARTED",
            StringComparison.OrdinalIgnoreCase);
        if (interruptedRun && lastStarted.HasValue)
        {
            // Las filas PENDING nacidas en esa ejecucion son suyas: se abandonan.
            await AbandonPendingFromRunAsync(lastStarted.Value);
        }

        if (!interruptedRun && !schedule.IsDue(now, lastStarted))
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

    // A-03: un PENDING mas viejo de 2 horas es un backup abandonado.
    private async Task AbandonStalePendingBackupsAsync(DateTime now)
    {
        var cutoff = now.AddHours(-PendingAbandonedThreshold.TotalHours);
        var pending = await _dbContext.Backups
            .Where(x => x.Estado == EstadoProceso.PENDING && x.FechaCreacion < cutoff)
            .ToListAsync(CancellationToken.None);
        await MarkAbandonedAsync(pending);
    }

    // J-1: las filas PENDING nacidas en una ejecucion que se quedo a mitad
    // pertenecen a esa ejecucion: se abandonan para poder reintentar.
    private async Task AbandonPendingFromRunAsync(DateTime lastStarted)
    {
        // Solo los PENDING automaticos: una copia manual en curso no pertenece
        // a esta ejecucion y no debe abandonarse por ella.
        var pending = await _dbContext.Backups
            .Where(x => x.Estado == EstadoProceso.PENDING
                        && x.Tipo == TipoProceso.AUTO
                        && x.FechaCreacion >= lastStarted)
            .ToListAsync(CancellationToken.None);
        await MarkAbandonedAsync(pending);
    }

    private async Task MarkAbandonedAsync(List<Backup> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var backup in pending)
        {
            backup.Estado = EstadoProceso.FAILED;
            backup.Notas = "Backup abandonado: el proceso se detuvo a mitad";
        }

        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _logger.LogWarning("{Count} backups abandonados marcados como FAILED", pending.Count);
    }

    // A-03: avisa al administrador cuando el ultimo backup correcto es mas
    // viejo de lo que permite la frecuencia configurada. El umbral se deriva
    // de la frecuencia (HOURLY 1 dia, DAILY 2, WEEKLY 8, MONTHLY 32).
    private async Task CheckLastSuccessfulBackupAsync(BackupSchedule schedule, DateTime now)
    {
        try
        {
            // Si el administrador ha desactivado el backup automatico a proposito,
            // no se le avisa de nada.
            if (!schedule.AutoEnabled)
            {
                return;
            }

            var thresholdDays = schedule.Frequency switch
            {
                "HOURLY" => 1,
                "DAILY" => 2,
                "WEEKLY" => 8,
                "MONTHLY" => 32,
                _ => 8
            };

            var lastSuccess = await _dbContext.Backups
                .Where(x => x.Estado == EstadoProceso.SUCCESS && x.DeletedAt == null)
                .OrderByDescending(x => x.FechaCreacion)
                .FirstOrDefaultAsync(CancellationToken.None);

            var days = lastSuccess is null
                ? (int?)null
                : (int)(now - lastSuccess.FechaCreacion).TotalDays;
            if (days.HasValue && days.Value < thresholdDays)
            {
                return;
            }

            // Anti-spam: el job corre cada 15 minutos y no debe repetir el aviso.
            var alreadyNotified = await _dbContext.NotificacionesAdmin
                .AnyAsync(x => x.Tipo == "BACKUP" && x.Fecha >= now.AddDays(-thresholdDays), CancellationToken.None);
            if (alreadyNotified)
            {
                return;
            }

            var mensaje = days.HasValue
                ? $"No hay una copia de seguridad correcta desde hace {days.Value} dias (umbral de {thresholdDays} dias segun la frecuencia configurada)."
                : $"No hay ninguna copia de seguridad correcta registrada (umbral de {thresholdDays} dias segun la frecuencia configurada).";

            _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
            {
                Id = Guid.NewGuid(),
                Tipo = "BACKUP",
                Mensaje = mensaje,
                Leida = false,
                Fecha = now,
                DetallesJson = JsonSerializer.Serialize(new
                {
                    ultimo_backup_id = lastSuccess?.Id,
                    ultimo_backup_fecha = lastSuccess?.FechaCreacion,
                    dias = days,
                    umbral_dias = thresholdDays
                })
            });

            await _dbContext.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Sin copia de seguridad correcta: {Mensaje}", mensaje);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo comprobar la antiguedad del ultimo backup correcto");
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
