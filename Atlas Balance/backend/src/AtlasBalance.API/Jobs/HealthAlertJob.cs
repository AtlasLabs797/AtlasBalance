using System.Globalization;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Jobs;

/// <summary>
/// V-02.07: vigila la salud de la aplicacion y avisa por los mismos canales que
/// las alertas de seguridad.
///
/// Tres senales, cada una con su razon de ser:
/// - Tasa de error por encima de lo normal -> algo se rompio en el ultimo deploy
///   o alguien esta forzando la aplicacion.
/// - Latencia p95 muy por encima de la ventana anterior -> agotamiento de
///   recursos, pool saturado o carga anomala.
/// - Comprobaciones de salud en rojo -> base de datos caida o disco lleno.
/// </summary>
public sealed class HealthAlertJob
{
    public static class Reglas
    {
        public const string TasaErrorElevada = "TASA_ERROR_ELEVADA";
        public const string LatenciaDegradada = "LATENCIA_DEGRADADA";
        public const string SaludDegradada = "SALUD_DEGRADADA";
    }

    /// <summary>Ventana que se compara, en minutos. Coincide con la cadencia del job.</summary>
    private const int VentanaMinutos = 5;

    /// <summary>
    /// Peticiones minimas en la ventana para que los porcentajes signifiquen
    /// algo. Con 3 peticiones, un solo 500 da un 33% de error y no dice nada.
    /// </summary>
    private const int MinPeticiones = 20;

    /// <summary>Porcentaje de 5xx a partir del cual se avisa siempre.</summary>
    private const double UmbralError5xxPorcentaje = 5;

    /// <summary>Cuanto tiene que empeorar el p95 respecto a la ventana anterior.</summary>
    private const double FactorLatencia = 3.0;

    /// <summary>Suelo absoluto de p95: por debajo, un x3 sigue siendo irrelevante.</summary>
    private const double MinLatenciaP95Ms = 250;

    private readonly IRequestMetrics _metrics;
    private readonly IAppHealthService _health;
    private readonly IAlertDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly SecurityAlertOptions _options;
    private readonly ILogger<HealthAlertJob> _logger;

    public HealthAlertJob(
        IRequestMetrics metrics,
        IAppHealthService health,
        IAlertDispatcher dispatcher,
        IClock clock,
        IOptions<SecurityAlertOptions> options,
        ILogger<HealthAlertJob> logger)
    {
        _metrics = metrics;
        _health = health;
        _dispatcher = dispatcher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        if (!_options.Habilitado)
        {
            return;
        }

        var candidatas = new List<SecurityAlert>();
        var actual = _metrics.Ventana(VentanaMinutos);
        var anterior = _metrics.VentanaAnterior(VentanaMinutos);

        if (actual.Peticiones >= MinPeticiones && actual.TasaError5xxPorcentaje > UmbralError5xxPorcentaje)
        {
            candidatas.Add(new SecurityAlert(
                Reglas.TasaErrorElevada,
                SecurityAlertService.SeveridadAlta,
                "global",
                $"{actual.Errores5xx} errores 5xx de {actual.Peticiones} peticiones ({actual.TasaError5xxPorcentaje.ToString("N1", CultureInfo.InvariantCulture)}%) en {VentanaMinutos} minutos.",
                new[]
                {
                    $"Umbral: {UmbralError5xxPorcentaje}% de 5xx con al menos {MinPeticiones} peticiones.",
                    $"Errores 4xx en la misma ventana: {actual.Errores4xx}.",
                    "Revisa el log de aplicacion: los 500 se registran con AtlasBalance.API.UnhandledException."
                },
                null));
        }

        if (actual.Peticiones >= MinPeticiones &&
            actual.LatenciaP95Ms >= MinLatenciaP95Ms &&
            anterior.Peticiones >= MinPeticiones &&
            anterior.LatenciaP95Ms > 0 &&
            actual.LatenciaP95Ms > anterior.LatenciaP95Ms * FactorLatencia)
        {
            candidatas.Add(new SecurityAlert(
                Reglas.LatenciaDegradada,
                SecurityAlertService.SeveridadMedia,
                "global",
                $"La latencia p95 subio de {anterior.LatenciaP95Ms.ToString("N0", CultureInfo.InvariantCulture)} ms a {actual.LatenciaP95Ms.ToString("N0", CultureInfo.InvariantCulture)} ms en {VentanaMinutos} minutos.",
                new[]
                {
                    $"Umbral: x{FactorLatencia} sobre la ventana anterior, con p95 minimo de {MinLatenciaP95Ms} ms.",
                    $"p50 actual: {actual.LatenciaP50Ms.ToString("N0", CultureInfo.InvariantCulture)} ms. Maximo: {actual.LatenciaMaxMs.ToString("N0", CultureInfo.InvariantCulture)} ms.",
                    "Puede ser agotamiento de recursos, pool de conexiones saturado o carga anomala."
                },
                null));
        }

        var salud = await _health.ComprobarAsync(CancellationToken.None);
        if (salud.Estado != EstadoSalud.Sano)
        {
            var problemas = salud.Comprobaciones
                .Where(c => c.Estado != EstadoSalud.Sano)
                .Select(c => $"{c.Nombre}: {c.Estado} - {c.Detalle}")
                .ToList();

            candidatas.Add(new SecurityAlert(
                Reglas.SaludDegradada,
                salud.Estado == EstadoSalud.NoSano
                    ? SecurityAlertService.SeveridadAlta
                    : SecurityAlertService.SeveridadMedia,
                // La clave incluye que comprobaciones fallan: si primero cae el
                // disco y luego ademas la base de datos, el enfriamiento de la
                // primera alerta no puede tapar la segunda.
                string.Join(",", salud.Comprobaciones.Where(c => c.Estado != EstadoSalud.Sano).Select(c => c.Nombre)),
                $"Estado de la aplicacion: {salud.Estado}.",
                problemas,
                null));
        }

        if (candidatas.Count == 0)
        {
            return;
        }

        var yaAvisadas = await _dispatcher.ClavesEnEnfriamientoAsync(_clock.UtcNow, CancellationToken.None);
        foreach (var alerta in candidatas)
        {
            var clave = $"{alerta.Regla}|{alerta.Clave}";
            if (yaAvisadas.Contains(clave))
            {
                continue;
            }

            await _dispatcher.DespacharAsync(alerta, CancellationToken.None);
            yaAvisadas.Add(clave);
            _logger.LogWarning("HealthAlertJob notifico {Regla}: {Resumen}", alerta.Regla, alerta.Resumen);
        }
    }
}
