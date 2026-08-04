using System.Diagnostics;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasBalance.API.Services;

public interface IAppHealthService
{
    /// <summary>
    /// Comprobacion completa: base de datos, disco y pool de conexiones. Es la
    /// que responde /api/sistema/salud y la que consume el job de alertas.
    /// </summary>
    Task<SaludResponse> ComprobarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// V-02.07: salud real de la aplicacion.
///
/// Antes, /api/health devolvia {status:"healthy"} constante: respondia OK con la
/// base de datos caida y el disco lleno, que es peor que no tener health check,
/// porque da una falsa garantia al watchdog y a cualquier monitor externo.
/// Ese endpoint se mantiene como sonda de vida (el proceso responde), y la
/// comprobacion de verdad vive aqui.
/// </summary>
public sealed class AppHealthService : IAppHealthService
{
    /// <summary>Timeout de la consulta de sondeo. Un health check no puede colgarse.</summary>
    private static readonly TimeSpan TimeoutBd = TimeSpan.FromSeconds(5);

    /// <summary>Por debajo de esto el disco pasa a estado degradado.</summary>
    private const double UmbralDiscoAvisoPorcentaje = 15;

    /// <summary>Por debajo de esto, no sano: los backups y los logs van a fallar.</summary>
    private const double UmbralDiscoCriticoPorcentaje = 5;

    private readonly AppDbContext _dbContext;
    private readonly IRequestMetrics _metrics;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppHealthService> _logger;

    public AppHealthService(
        AppDbContext dbContext,
        IRequestMetrics metrics,
        IClock clock,
        IConfiguration configuration,
        ILogger<AppHealthService> logger)
    {
        _dbContext = dbContext;
        _metrics = metrics;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SaludResponse> ComprobarAsync(CancellationToken cancellationToken)
    {
        var comprobaciones = new List<ComprobacionSalud>
        {
            await ComprobarBaseDatosAsync(cancellationToken),
            ComprobarDisco(),
            ComprobarPoolConexiones()
        };

        var ventana = _metrics.Ventana(5);

        return new SaludResponse
        {
            Estado = comprobaciones.Any(c => c.Estado == EstadoSalud.NoSano)
                ? EstadoSalud.NoSano
                : comprobaciones.Any(c => c.Estado == EstadoSalud.Degradado)
                    ? EstadoSalud.Degradado
                    : EstadoSalud.Sano,
            FechaUtc = _clock.UtcNow,
            UptimeSegundos = (long)(_clock.UtcNow - _metrics.ArranqueUtc).TotalSeconds,
            Comprobaciones = comprobaciones,
            PeticionesUltimos5Min = ventana.Peticiones,
            TasaErrorPorcentaje = Math.Round(ventana.TasaErrorPorcentaje, 2),
            LatenciaP50Ms = ventana.LatenciaP50Ms,
            LatenciaP95Ms = ventana.LatenciaP95Ms
        };
    }

    private async Task<ComprobacionSalud> ComprobarBaseDatosAsync(CancellationToken cancellationToken)
    {
        var marca = Stopwatch.GetTimestamp();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeoutBd);

            // SELECT 1 y no CanConnectAsync: CanConnect solo abre la conexion,
            // que puede tener exito contra una base de datos en recuperacion o
            // sin permisos de lectura.
            _ = await _dbContext.Database
                .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
                .SingleAsync(cts.Token);

            return new ComprobacionSalud
            {
                Nombre = "base_datos",
                Estado = EstadoSalud.Sano,
                DuracionMs = Math.Round(Stopwatch.GetElapsedTime(marca).TotalMilliseconds, 1),
                Detalle = "Consulta de sondeo correcta."
            };
        }
        catch (Exception ex)
        {
            // Sin exponer el mensaje del motor: puede llevar host, base y usuario.
            _logger.LogError(ex, "Health check de base de datos fallido");
            return new ComprobacionSalud
            {
                Nombre = "base_datos",
                Estado = EstadoSalud.NoSano,
                DuracionMs = Math.Round(Stopwatch.GetElapsedTime(marca).TotalMilliseconds, 1),
                Detalle = "No se pudo consultar la base de datos. Revisa el log del servidor."
            };
        }
    }

    private ComprobacionSalud ComprobarDisco()
    {
        try
        {
            // El volumen que importa es donde viven logs y backups, no el del
            // ejecutable: es lo que se llena solo con el tiempo.
            var rutaVigilada = _configuration["Serilog:FilePath"] is { Length: > 0 } logPath
                ? Path.GetDirectoryName(Path.GetFullPath(logPath))
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AtlasBalance");

            var raiz = Path.GetPathRoot(rutaVigilada ?? AppContext.BaseDirectory);
            if (string.IsNullOrEmpty(raiz))
            {
                return Indeterminado("disco", "No se pudo determinar el volumen a vigilar.");
            }

            var unidad = new DriveInfo(raiz);
            var libresPorcentaje = unidad.TotalSize == 0
                ? 100
                : unidad.AvailableFreeSpace * 100.0 / unidad.TotalSize;

            var estado = libresPorcentaje < UmbralDiscoCriticoPorcentaje
                ? EstadoSalud.NoSano
                : libresPorcentaje < UmbralDiscoAvisoPorcentaje
                    ? EstadoSalud.Degradado
                    : EstadoSalud.Sano;

            return new ComprobacionSalud
            {
                Nombre = "disco",
                Estado = estado,
                Detalle = $"{libresPorcentaje:N1}% libre en {raiz} ({unidad.AvailableFreeSpace / (1024 * 1024)} MB).",
                Valor = Math.Round(libresPorcentaje, 1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo comprobar el espacio en disco");
            return Indeterminado("disco", "No se pudo leer el espacio libre.");
        }
    }

    private ComprobacionSalud ComprobarPoolConexiones()
    {
        try
        {
            // Npgsql no expone el estado del pool por API publica estable. Lo que
            // si se puede afirmar es el limite configurado, que es el dato que
            // hace falta para interpretar una saturacion: si el pool es de 20 y
            // hay timeouts de conexion, el problema es el pool.
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Indeterminado("pool_conexiones", "Sin cadena de conexion configurada.");
            }

            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return new ComprobacionSalud
            {
                Nombre = "pool_conexiones",
                Estado = EstadoSalud.Sano,
                Detalle = $"Pool configurado entre {builder.MinPoolSize} y {builder.MaxPoolSize} conexiones.",
                Valor = builder.MaxPoolSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer la configuracion del pool de conexiones");
            return Indeterminado("pool_conexiones", "No se pudo leer la configuracion del pool.");
        }
    }

    private static ComprobacionSalud Indeterminado(string nombre, string detalle) => new()
    {
        Nombre = nombre,
        Estado = EstadoSalud.Degradado,
        Detalle = detalle
    };
}
