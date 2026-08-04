using System.Globalization;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IBackupConfigurationService
{
    Task<BackupConfigResponse> GetAsync(CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> UpdateAsync(UpdateBackupConfigRequest request, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken);
}

public sealed class BackupConfigurationService : IBackupConfigurationService
{
    public const string DestinationLocal = "LOCAL";
    public const string DestinationLocalAndGoogleDrive = "LOCAL_Y_GOOGLE_DRIVE";

    private readonly AppDbContext _dbContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditService _auditService;

    public BackupConfigurationService(AppDbContext dbContext, ISecretProtector secretProtector, IAuditService auditService)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _auditService = auditService;
    }

    public async Task<BackupConfigResponse> GetAsync(CancellationToken cancellationToken)
    {
        var config = await LoadConfigMapAsync(cancellationToken);
        var connection = await _dbContext.BackupCloudConnections
            .AsNoTracking()
            .Where(x => x.Provider == GoogleDriveBackupService.ProviderName)
            .OrderByDescending(x => x.ConnectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var schedule = BackupSchedule.FromConfig(config);
        return new BackupConfigResponse
        {
            AutoEnabled = schedule.AutoEnabled,
            Frequency = schedule.Frequency,
            TimeUtc = schedule.TimeUtc,
            DayOfWeek = schedule.DayOfWeek,
            DayOfMonth = schedule.DayOfMonth,
            IntervalHours = schedule.IntervalHours,
            Destination = NormalizeDestination(GetValue(config, "backup_destination", DestinationLocal)),
            LastStartedUtc = GetValue(config, "backup_auto_last_started_utc"),
            LastResult = GetValue(config, "backup_auto_last_result"),
            GoogleDrive = new GoogleDriveBackupConfigResponse
            {
                ClientId = GetValue(config, "google_drive_oauth_client_id"),
                ClientSecretConfigured = !string.IsNullOrWhiteSpace(GetValue(config, "google_drive_oauth_client_secret")),
                Connected = connection is not null,
                AccountEmail = connection?.AccountEmail,
                FolderId = GetValue(config, "google_drive_folder_id"),
                LastValidatedAt = connection?.LastValidatedAt,
                LastError = connection?.LastError,
                EncryptionKeyConfigured = !string.IsNullOrWhiteSpace(GetValue(config, "backup_cloud_encryption_key"))
            }
        };
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        UpdateBackupConfigRequest request,
        Guid? userId,
        HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return (false, "La solicitud esta incompleta.");
        }

        if (!BackupSchedule.TryNormalize(
                request.AutoEnabled,
                request.Frequency,
                request.TimeUtc,
                request.DayOfWeek,
                request.DayOfMonth,
                request.IntervalHours,
                out var normalized,
                out var error))
        {
            return (false, error);
        }

        var requestedDestination = (request.Destination ?? string.Empty).Trim().ToUpperInvariant();
        if (requestedDestination is not (DestinationLocal or DestinationLocalAndGoogleDrive or "GOOGLE_DRIVE"))
        {
            return (false, "Destino de copia de seguridad invalido.");
        }

        var destination = NormalizeDestination(requestedDestination);
        if (!string.IsNullOrWhiteSpace(request.GoogleDriveFolderId) &&
            !GoogleDriveBackupService.IsSafeGoogleIdentifier(request.GoogleDriveFolderId))
        {
            return (false, "El identificador de carpeta de Google Drive no es valido.");
        }

        if (destination == DestinationLocalAndGoogleDrive &&
            string.IsNullOrWhiteSpace(request.GoogleDriveClientId))
        {
            return (false, "Para subir a Google Drive falta el OAuth Client ID.");
        }

        var rows = await _dbContext.Configuraciones.ToListAsync(cancellationToken);
        var before = rows.ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        Upsert(rows, "backup_auto_enabled", normalized.AutoEnabled ? "true" : "false", userId, now);
        Upsert(rows, "backup_auto_frequency", normalized.Frequency, userId, now);
        Upsert(rows, "backup_auto_time_utc", normalized.TimeUtc, userId, now);
        Upsert(rows, "backup_auto_day_of_week", normalized.DayOfWeek.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(rows, "backup_auto_day_of_month", normalized.DayOfMonth.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(rows, "backup_auto_interval_hours", normalized.IntervalHours.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(rows, "backup_destination", destination, userId, now);
        // V-02.07: estos dos eran los unicos `.Trim()` del fichero sin guarda de
        // null (compara con la linea de `requestedDestination`). El tipo dice
        // `string` no anulable, pero System.Text.Json no respeta esa anotacion en
        // .NET 8: un `"google_drive_client_id": null` explicito en el JSON pisaba
        // el `= string.Empty` del DTO y el Trim() reventaba con un 500 opaco.
        // Se deja vacio en vez de exigir [Required] porque no configurar Drive es
        // un caso valido.
        Upsert(rows, "google_drive_oauth_client_id", (request.GoogleDriveClientId ?? string.Empty).Trim(), userId, now);
        Upsert(rows, "google_drive_folder_id", (request.GoogleDriveFolderId ?? string.Empty).Trim(), userId, now);

        if (!string.IsNullOrWhiteSpace(request.GoogleDriveClientSecret))
        {
            // V-02-05 (MED-8): marcar EsSecreto=true explicitamente. Antes el
            // flag quedaba en false para secrets recien actualizados.
            Upsert(rows, "google_drive_oauth_client_secret", _secretProtector.ProtectForStorage(request.GoogleDriveClientSecret), userId, now, isSecret: true);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = rows.ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        await _auditService.LogAsync(
            userId,
            AuditActions.BackupConfigUpdated,
            "CONFIGURACION",
            null,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            JsonSerializer.Serialize(new
            {
                before = RedactSensitiveConfig(before),
                after = RedactSensitiveConfig(after)
            }),
            cancellationToken);

        return (true, null);
    }

    private async Task<Dictionary<string, string>> LoadConfigMapAsync(CancellationToken cancellationToken) =>
        await _dbContext.Configuraciones
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public static string NormalizeDestination(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "LOCAL_Y_GOOGLE_DRIVE" or "GOOGLE_DRIVE" => DestinationLocalAndGoogleDrive,
            _ => DestinationLocal
        };
    }

    public static string GetValue(IReadOnlyDictionary<string, string> map, string key, string defaultValue = "")
    {
        return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private void Upsert(
        ICollection<Configuracion> existing,
        string key,
        string value,
        Guid? userId,
        DateTime now,
        bool isSecret = false)
    {
        var item = existing.FirstOrDefault(x => x.Clave.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            var created = new Configuracion
            {
                Clave = key,
                Valor = value,
                // V-02-05 (MED-8): marcar EsSecreto explicitamente. Para claves
                // sensibles (password/api_key/token/secret) siempre se marca
                // true; para el resto, segun el parametro isSecret.
                EsSecreto = isSecret || IsSensitiveConfigKey(key),
                FechaModificacion = now,
                UsuarioModificacionId = userId
            };
            _dbContext.Configuraciones.Add(created);
            existing.Add(created);
            return;
        }

        item.Valor = value;
        item.EsSecreto = isSecret || IsSensitiveConfigKey(key);
        item.FechaModificacion = now;
        item.UsuarioModificacionId = userId;
    }

    private static Dictionary<string, string> RedactSensitiveConfig(IReadOnlyDictionary<string, string> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => IsSensitiveConfigKey(pair.Key)
                ? (string.IsNullOrEmpty(pair.Value) ? string.Empty : "[REDACTED]")
                : pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveConfigKey(string key)
    {
        var normalized = key.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("api_key", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("credential", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized.Contains("bearer", StringComparison.Ordinal) ||
               normalized.Contains("encryption_key", StringComparison.Ordinal);
    }
}

public sealed record BackupSchedule(
    bool AutoEnabled,
    string Frequency,
    string TimeUtc,
    int DayOfWeek,
    int DayOfMonth,
    int IntervalHours)
{
    public static BackupSchedule FromConfig(IReadOnlyDictionary<string, string> config)
    {
        var enabled = ParseBool(BackupConfigurationService.GetValue(config, "backup_auto_enabled", "true"), true);
        var frequency = BackupConfigurationService.GetValue(config, "backup_auto_frequency", "WEEKLY");
        var time = BackupConfigurationService.GetValue(config, "backup_auto_time_utc", "02:00");
        var dayOfWeek = ParseInt(BackupConfigurationService.GetValue(config, "backup_auto_day_of_week", "0"), 0);
        var dayOfMonth = ParseInt(BackupConfigurationService.GetValue(config, "backup_auto_day_of_month", "1"), 1);
        var intervalHours = ParseInt(BackupConfigurationService.GetValue(config, "backup_auto_interval_hours", "24"), 24);

        return TryNormalize(enabled, frequency, time, dayOfWeek, dayOfMonth, intervalHours, out var normalized, out _)
            ? normalized
            : new BackupSchedule(true, "WEEKLY", "02:00", 0, 1, 24);
    }

    public static bool TryNormalize(
        bool enabled,
        string? frequency,
        string? timeUtc,
        int dayOfWeek,
        int dayOfMonth,
        int intervalHours,
        out BackupSchedule normalized,
        out string? error)
    {
        normalized = new BackupSchedule(true, "WEEKLY", "02:00", 0, 1, 24);
        error = null;

        var frequencyNormalized = (frequency ?? string.Empty).Trim().ToUpperInvariant();
        if (frequencyNormalized is not ("HOURLY" or "DAILY" or "WEEKLY" or "MONTHLY"))
        {
            error = "Frecuencia de copia invalida.";
            return false;
        }

        if (!TimeOnly.TryParseExact(timeUtc?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
        {
            error = "La hora UTC debe usar formato HH:mm.";
            return false;
        }

        if (dayOfWeek is < 0 or > 6)
        {
            error = "El dia de la semana debe estar entre 0 y 6.";
            return false;
        }

        if (dayOfMonth is < 1 or > 31)
        {
            error = "El dia del mes debe estar entre 1 y 31.";
            return false;
        }

        if (intervalHours is < 1 or > 168)
        {
            error = "El intervalo horario debe estar entre 1 y 168 horas.";
            return false;
        }

        normalized = new BackupSchedule(
            enabled,
            frequencyNormalized,
            parsedTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            dayOfWeek,
            dayOfMonth,
            intervalHours);
        return true;
    }

    public bool IsDue(DateTime utcNow, DateTime? lastStartedUtc)
    {
        if (!AutoEnabled)
        {
            return false;
        }

        var now = utcNow.Kind == DateTimeKind.Utc ? utcNow : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (Frequency == "HOURLY")
        {
            return !lastStartedUtc.HasValue ||
                   lastStartedUtc.Value <= now.AddHours(-IntervalHours);
        }

        var occurrence = GetCurrentOccurrence(now);
        return now >= occurrence && (!lastStartedUtc.HasValue || lastStartedUtc.Value < occurrence);
    }

    private DateTime GetCurrentOccurrence(DateTime utcNow)
    {
        var scheduledTime = TimeOnly.ParseExact(TimeUtc, "HH:mm", CultureInfo.InvariantCulture);
        return Frequency switch
        {
            "DAILY" => utcNow.Date.Add(scheduledTime.ToTimeSpan()),
            "WEEKLY" => utcNow.Date.AddDays(-DaysSinceScheduledDay(utcNow)).Add(scheduledTime.ToTimeSpan()),
            "MONTHLY" => new DateTime(
                utcNow.Year,
                utcNow.Month,
                Math.Min(DayOfMonth, DateTime.DaysInMonth(utcNow.Year, utcNow.Month)),
                scheduledTime.Hour,
                scheduledTime.Minute,
                0,
                DateTimeKind.Utc),
            _ => utcNow
        };
    }

    private int DaysSinceScheduledDay(DateTime utcNow)
    {
        var current = (int)utcNow.DayOfWeek;
        return current >= DayOfWeek
            ? current - DayOfWeek
            : current + 7 - DayOfWeek;
    }

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static bool ParseBool(string value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;
}
