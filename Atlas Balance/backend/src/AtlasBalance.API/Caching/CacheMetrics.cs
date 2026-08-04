using Microsoft.Extensions.Logging;

namespace AtlasBalance.API.Caching;

/// <summary>
/// Contadores internos de la capa de caché. Se exponen como
/// <see cref="Snapshot"/> y se loguean de forma periodica. No contienen
/// claves ni IDs: solo agregados por espacio de caché.
/// </summary>
public sealed class CacheMetrics
{
    public long Hits { get; internal set; }
    public long Misses { get; internal set; }
    public long Loads { get; internal set; }
    public long SingleFlightWaits { get; internal set; }
    public long Invalidations { get; internal set; }
    public long LoadFailures { get; internal set; }

    public CacheMetricsSnapshot Snapshot() => new()
    {
        Hits = Hits,
        Misses = Misses,
        Loads = Loads,
        SingleFlightWaits = SingleFlightWaits,
        Invalidations = Invalidations,
        LoadFailures = LoadFailures
    };
}

public readonly struct CacheMetricsSnapshot
{
    public long Hits { get; init; }
    public long Misses { get; init; }
    public long Loads { get; init; }
    public long SingleFlightWaits { get; init; }
    public long Invalidations { get; init; }
    public long LoadFailures { get; init; }

    public double HitRatio => Hits + Misses == 0 ? 0d : (double)Hits / (Hits + Misses);
}

/// <summary>
/// Snapshot inmutable que devuelve <see cref="ICacheMetricsSink.FlushAsync"/>
/// cuando el sink concreto quiere publicar periodicamente las metricas
/// de cache. La implementacion por defecto es nula (no hacer nada).
/// </summary>
public interface ICacheMetricsSink
{
    Task FlushAsync(CacheMetricsSnapshot snapshot, CancellationToken cancellationToken);
}
