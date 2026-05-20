using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

public sealed class AutoUpdateJob
{
    private const string EnabledKey = "app_update_auto_enabled";
    private const string HourUtcKey = "app_update_auto_hour_utc";
    private const string LastCheckedUtcKey = "app_update_auto_last_checked_utc";
    private const string LastStartedUtcKey = "app_update_auto_last_started_utc";
    private const string LastResultKey = "app_update_auto_last_result";

    private readonly AppDbContext _dbContext;
    private readonly IActualizacionService _actualizacionService;
    private readonly IClock _clock;
    private readonly ILogger<AutoUpdateJob> _logger;

    public AutoUpdateJob(
        AppDbContext dbContext,
        IActualizacionService actualizacionService,
        IClock clock,
        ILogger<AutoUpdateJob> logger)
    {
        _dbContext = dbContext;
        _actualizacionService = actualizacionService;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var now = _clock.UtcNow;
        var config = await _dbContext.Configuraciones.ToListAsync(CancellationToken.None);

        if (!ParseBool(GetValue(config, EnabledKey), fallback: false))
        {
            return;
        }

        var hourUtc = Math.Clamp(ParseInt(GetValue(config, HourUtcKey), 3), 0, 23);
        if (now.Hour < hourUtc)
        {
            return;
        }

        if (TryParseUtc(GetValue(config, LastCheckedUtcKey), out var lastChecked) &&
            lastChecked.Date == now.Date)
        {
            return;
        }

        Upsert(
            config,
            LastCheckedUtcKey,
            now.ToString("O", CultureInfo.InvariantCulture),
            "datetime",
            "Ultima comprobacion automatica de actualizaciones en UTC",
            now);
        Upsert(
            config,
            LastResultKey,
            "Comprobacion automatica iniciada.",
            "string",
            "Ultimo resultado de actualizacion automatica",
            now);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        try
        {
            var available = await _actualizacionService.CheckVersionDisponibleAsync(CancellationToken.None);
            if (!available.ActualizacionDisponible)
            {
                Upsert(
                    config,
                    LastResultKey,
                    string.IsNullOrWhiteSpace(available.Mensaje)
                        ? "Sin actualizacion disponible."
                        : Trim(available.Mensaje, 300),
                    "string",
                    "Ultimo resultado de actualizacion automatica",
                    now);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                return;
            }

            var version = string.IsNullOrWhiteSpace(available.VersionDisponible)
                ? "version desconocida"
                : available.VersionDisponible;
            Upsert(
                config,
                LastResultKey,
                $"Actualizacion {version} detectada; solicitando aplicacion al Watchdog.",
                "string",
                "Ultimo resultado de actualizacion automatica",
                now);
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            var accepted = await _actualizacionService.IniciarActualizacionAsync(null, null, CancellationToken.None);
            var finishedAt = _clock.UtcNow;
            if (accepted)
            {
                Upsert(
                    config,
                    LastStartedUtcKey,
                    finishedAt.ToString("O", CultureInfo.InvariantCulture),
                    "datetime",
                    "Ultima actualizacion automatica iniciada en UTC",
                    finishedAt);
            }

            Upsert(
                config,
                LastResultKey,
                accepted
                    ? $"Actualizacion automatica {version} iniciada."
                    : $"Watchdog rechazo la actualizacion automatica {version}.",
                "string",
                "Ultimo resultado de actualizacion automatica",
                finishedAt);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoUpdateJob fallo");
            Upsert(
                config,
                LastResultKey,
                "Error al comprobar o iniciar la actualizacion automatica.",
                "string",
                "Ultimo resultado de actualizacion automatica",
                _clock.UtcNow);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static string GetValue(IEnumerable<Configuracion> config, string key)
    {
        return config
            .FirstOrDefault(x => x.Clave.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Valor ?? string.Empty;
    }

    private void Upsert(
        ICollection<Configuracion> config,
        string key,
        string value,
        string type,
        string description,
        DateTime now)
    {
        var item = config.FirstOrDefault(x => x.Clave.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = new Configuracion
            {
                Clave = key,
                Tipo = type,
                Descripcion = description
            };
            config.Add(item);
            _dbContext.Configuraciones.Add(item);
        }

        item.Valor = value;
        item.FechaModificacion = now;
    }

    private static bool ParseBool(string value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryParseUtc(string value, out DateTime utc)
    {
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utc = parsed;
            return true;
        }

        utc = default;
        return false;
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
