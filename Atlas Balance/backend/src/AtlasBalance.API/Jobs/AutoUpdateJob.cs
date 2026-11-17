using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

public sealed class AutoUpdateJob
{
    private const string EnabledKey = "app_update_auto_enabled";
    private const string WindowStartLocalKey = "app_update_auto_window_start_local";
    private const string WindowEndLocalKey = "app_update_auto_window_end_local";
    private const string LastCheckedUtcKey = "app_update_auto_last_checked_utc";
    private const string LastStartedUtcKey = "app_update_auto_last_started_utc";
    private const string LastResultKey = "app_update_auto_last_result";

    // J-6: ventana de madrugada por defecto, en HORA LOCAL DEL SERVIDOR.
    private static readonly TimeOnly DefaultWindowStart = new(1, 0);
    private static readonly TimeOnly DefaultWindowEnd = new(5, 0);

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
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        // Sin este scope, RlsDbCommandInterceptor.BuildContext() deniega por defecto.
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var now = _clock.UtcNow;
        var config = await _dbContext.Configuraciones.ToListAsync(CancellationToken.None);

        if (!ParseBool(GetValue(config, EnabledKey), fallback: false))
        {
            return;
        }

        // J-6: la actualizacion automatica solo actua dentro de una ventana de madrugada en
        // HORA LOCAL DEL SERVIDOR (por defecto 01:00-05:00). Fuera de la ventana no se hace
        // nada ni se escribe nada: la comprobacion se aplaza al dia siguiente, de modo que
        // un servidor que se levanta a media mañana no reinicia la app en horario laboral.
        // La clave app_update_auto_hour_utc sigue existiendo en CONFIGURACIONES (y en la
        // pantalla de Configuracion), pero J-6 la sustituye por esta ventana local.
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.Local);
        var localTime = TimeOnly.FromTimeSpan(localNow.TimeOfDay);
        var (windowStart, windowEnd) = ReadWindow(config);
        if (localTime < windowStart || localTime >= windowEnd)
        {
            return;
        }

        // J-6: "ya comprobado hoy" se mide en el dia LOCAL (la marca guardada es un instante UTC).
        if (TryParseUtc(GetValue(config, LastCheckedUtcKey), out var lastChecked) &&
            TimeZoneInfo.ConvertTimeFromUtc(lastChecked, TimeZoneInfo.Local).Date == localNow.Date)
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

            if (!available.Instalable)
            {
                var bloqueos = available.Bloqueos.Count == 0
                    ? "preflight no instalable."
                    : string.Join(" | ", available.Bloqueos);
                Upsert(
                    config,
                    LastResultKey,
                    Trim($"Actualizacion disponible, pero no instalable: {bloqueos}", 300),
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

    // J-6: la ventana se lee de CONFIGURACIONES; si falta o es invalida se usa la de por defecto.
    private static (TimeOnly Start, TimeOnly End) ReadWindow(IEnumerable<Configuracion> config)
    {
        var start = ParseTime(GetValue(config, WindowStartLocalKey), DefaultWindowStart);
        var end = ParseTime(GetValue(config, WindowEndLocalKey), DefaultWindowEnd);
        if (start >= end)
        {
            // La ventana no cruza medianoche: un rango invertido no es valido.
            start = DefaultWindowStart;
            end = DefaultWindowEnd;
        }

        return (start, end);
    }

    private static TimeOnly ParseTime(string value, TimeOnly fallback)
    {
        return TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
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
