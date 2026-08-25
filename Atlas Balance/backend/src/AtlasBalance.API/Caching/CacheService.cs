using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using AtlasBalance.API.Logging;
using Microsoft.Extensions.Logging;

namespace AtlasBalance.API.Caching;

/// <summary>
/// Capa de caché en proceso con tres propiedades:
///   1. Single-flight: N llamadas concurrentes con la misma clave disparan
///      UNA sola carga. El resto espera el resultado.
///   2. Generaciones: cada escritura relevante bumpea un contador de grupo
///      que invalida toda la familia de claves asociada sin enumerar entries.
///   3. Aislado por espacio: cada consumidor declara su propio
///      <see cref="CacheNamespace"/> para no colisionar con otros.
/// No comparte estado entre instancias (IMemoryCache es por proceso). Esto
/// es aceptable para el despliegue single-node de Atlas Balance documentado
/// en SPEC.md; ver AUDITORIA_CONCURRENCIA_2026-07-10.md CONC-027.
/// </summary>
public interface ICacheService
{
    Task<T> GetOrLoadAsync<T>(
        CacheNamespace ns,
        string key,
        Func<CancellationToken, Task<T>> loader,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    void Invalidate(CacheNamespace ns);
    void InvalidateKey(CacheNamespace ns, string key);
    CacheMetricsSnapshot GetMetricsSnapshot(string namespaceName);
}

public readonly record struct CacheNamespace(string Name);

public sealed class CacheServiceOptions
{
    /// <summary>
    /// Tamaño lógico máximo del caché en entries. Por defecto 4096, suficiente
    /// para los namespaces previstos (tipos de cambio, scope, referencias,
    /// métricas). LRU eviction evita crecimiento descontrolado.
    /// </summary>
    public int SizeLimit { get; init; } = 4096;
}

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CacheService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, CacheMetrics> _metricsByNamespace = new();
    private readonly CacheServiceOptions _options;

    public CacheService(IMemoryCache memoryCache, ILogger<CacheService> logger, CacheServiceOptions? options = null)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _options = options ?? new CacheServiceOptions();
    }

    public int ConfiguredSizeLimit => _options.SizeLimit;

    public async Task<T> GetOrLoadAsync<T>(
        CacheNamespace ns,
        string key,
        Func<CancellationToken, Task<T>> loader,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var metrics = GetOrCreateMetrics(ns.Name);
        var generation = GetGeneration(ns);
        var compoundKey = BuildKey(ns, generation, key);

        if (_memoryCache.TryGetValue<T>(compoundKey, out var cached) && cached is not null)
        {
            metrics.Hits++;
            return cached;
        }

        metrics.Misses++;

        var lockKey = $"{ns.Name}:{key}";
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        var entered = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;

            if (_memoryCache.TryGetValue<T>(compoundKey, out cached) && cached is not null)
            {
                metrics.Hits++;
                return cached;
            }

            metrics.SingleFlightWaits++;
            metrics.Loads++;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var value = await loader(cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                if (value is not null)
                {
                    using var entry = _memoryCache.CreateEntry(compoundKey);
                    entry.AbsoluteExpirationRelativeToNow = ttl;
                    entry.Value = value;
                }
                else
                {
                    // codeql[cs/log-forging] OK: clave saneada con LogScrubber (sin CR/LF/TAB, max 256).
                    _logger.LogDebug("Cache miss load returned null for {Namespace} {Key}", ns.Name, LogScrubber.Scrub(key));
                }

                _logger.LogDebug(
                    "Cache load {Namespace} {Key} elapsed_ms={ElapsedMs}",
                    ns.Name,
                    // codeql[cs/log-forging] OK: clave saneada con LogScrubber (sin CR/LF/TAB, max 256).
                    LogScrubber.Scrub(key),
                    stopwatch.ElapsedMilliseconds);

                return value;
            }
            catch (OperationCanceledException)
            {
                metrics.LoadFailures++;
                throw;
            }
            catch (Exception)
            {
                metrics.LoadFailures++;
                throw;
            }
        }
        finally
        {
            if (entered)
            {
                semaphore.Release();
            }
        }
    }

    public void Invalidate(CacheNamespace ns)
    {
        BumpGeneration(ns);
        var metrics = GetOrCreateMetrics(ns.Name);
        metrics.Invalidations++;
    }

    public void InvalidateKey(CacheNamespace ns, string key)
    {
        _ = ns;
        _ = key;
    }

    public CacheMetricsSnapshot GetMetricsSnapshot(string namespaceName)
    {
        return GetOrCreateMetrics(namespaceName).Snapshot();
    }

    private CacheMetrics GetOrCreateMetrics(string namespaceName) =>
        _metricsByNamespace.GetOrAdd(namespaceName, _ => new CacheMetrics());

    private string BuildKey(CacheNamespace ns, long generation, string key) =>
        $"{ns.Name}|g{generation}|{key}";

    private static long GetGeneration(CacheNamespace ns) =>
        GenerationState.GetCurrent(ns);

    private static void BumpGeneration(CacheNamespace ns) =>
        GenerationState.Bump(ns);

    private static class GenerationState
    {
        private static readonly ConcurrentDictionary<string, long> _generations = new();

        public static long GetCurrent(CacheNamespace ns) =>
            _generations.TryGetValue(ns.Name, out var current) ? current : 0L;

        public static void Bump(CacheNamespace ns) =>
            _generations.AddOrUpdate(ns.Name, 1L, (_, current) => current + 1);
    }
}
