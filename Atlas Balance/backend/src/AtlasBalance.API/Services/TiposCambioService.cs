using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AtlasBalance.API.Services;

public interface ITiposCambioService
{
    Task<decimal> ConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken);
    Task<IReadOnlyList<TipoCambioDto>> ListarTiposCambioAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DivisaActivaDto>> ListarDivisasAsync(CancellationToken cancellationToken);
    Task<TipoCambioDto> GuardarTipoCambioManualAsync(string divisaOrigen, string divisaDestino, decimal tasa, CancellationToken cancellationToken);
    Task<DivisaActivaDto> CrearDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken);
    Task<DivisaActivaDto> ActualizarDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken);
    Task<SyncTiposCambioResult> SincronizarTiposCambioAsync(CancellationToken cancellationToken);
}

public sealed class TiposCambioService : ITiposCambioService
{
    private const string CacheKey = "tipos_cambio_rates";
    private const string ExchangeRateClient = "exchange-rate-api";
    private const string ExchangeRateApiKeyConfig = "exchange_rate_api_key";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TiposCambioService> _logger;
    private readonly ISecretProtector _secretProtector;

    public TiposCambioService(
        AppDbContext dbContext,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        ILogger<TiposCambioService> logger,
        ISecretProtector secretProtector)
    {
        _dbContext = dbContext;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _secretProtector = secretProtector;
    }

    public async Task<decimal> ConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken)
    {
        if (amount == 0m)
        {
            return 0m;
        }

        var from = Normalize(divisaOrigen);
        var to = Normalize(divisaDestino);

        if (from == to)
        {
            return amount;
        }

        var catalog = await GetRateCatalogAsync(cancellationToken);
        var rate = ResolveRate(from, to, catalog);

        // Fallback defensivo para no romper dashboards si falta una tasa.
        return rate.HasValue ? amount * rate.Value : amount;
    }

    public async Task<IReadOnlyList<TipoCambioDto>> ListarTiposCambioAsync(CancellationToken cancellationToken)
    {
        var items = await _dbContext.TiposCambio
            .AsNoTracking()
            .OrderBy(x => x.DivisaOrigen)
            .ThenBy(x => x.DivisaDestino)
            .Select(x => new TipoCambioDto(
                x.Id,
                Normalize(x.DivisaOrigen),
                Normalize(x.DivisaDestino),
                x.Tasa,
                x.FechaActualizacion,
                x.Fuente.ToString()))
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task<IReadOnlyList<DivisaActivaDto>> ListarDivisasAsync(CancellationToken cancellationToken)
    {
        var items = await _dbContext.DivisasActivas
            .AsNoTracking()
            .OrderByDescending(x => x.EsBase)
            .ThenBy(x => x.Codigo)
            .Select(x => new DivisaActivaDto(
                Normalize(x.Codigo),
                x.Nombre,
                x.Simbolo,
                x.Activa,
                x.EsBase))
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task<TipoCambioDto> GuardarTipoCambioManualAsync(string divisaOrigen, string divisaDestino, decimal tasa, CancellationToken cancellationToken)
    {
        if (tasa <= 0m)
        {
            throw new InvalidOperationException("La tasa debe ser mayor que cero.");
        }

        var origen = Normalize(divisaOrigen);
        var destino = Normalize(divisaDestino);

        if (origen == destino)
        {
            throw new InvalidOperationException("La divisa origen y destino no pueden ser iguales.");
        }

        var now = DateTime.UtcNow;
        var existente = await _dbContext.TiposCambio
            .FirstOrDefaultAsync(x => x.DivisaOrigen == origen && x.DivisaDestino == destino, cancellationToken);

        if (existente is null)
        {
            existente = new TipoCambio
            {
                Id = Guid.NewGuid(),
                DivisaOrigen = origen,
                DivisaDestino = destino
            };
            _dbContext.TiposCambio.Add(existente);
        }

        existente.Tasa = tasa;
        existente.Fuente = FuenteTipoCambio.MANUAL;
        existente.FechaActualizacion = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache();

        return new TipoCambioDto(existente.Id, origen, destino, existente.Tasa, existente.FechaActualizacion, existente.Fuente.ToString());
    }

    public async Task<DivisaActivaDto> CrearDivisaAsync(
        string codigo,
        string? nombre,
        string? simbolo,
        bool activa,
        bool esBase,
        CancellationToken cancellationToken)
    {
        var normalizedCodigo = Normalize(codigo);
        if (normalizedCodigo.Length is < 3 or > 10)
        {
            throw new InvalidOperationException("El código de divisa debe tener entre 3 y 10 caracteres.");
        }

        var exists = await _dbContext.DivisasActivas
            .AnyAsync(x => x.Codigo == normalizedCodigo, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("Ya existe una divisa con ese código.");
        }

        if (esBase && !activa)
        {
            throw new InvalidOperationException("La divisa base debe estar activa.");
        }

        if (esBase)
        {
            await _dbContext.DivisasActivas
                .Where(x => x.EsBase)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EsBase, false), cancellationToken);
        }

        var divisa = new DivisaActiva
        {
            Codigo = normalizedCodigo,
            Nombre = CleanNullable(nombre),
            Simbolo = CleanNullable(simbolo),
            Activa = activa,
            EsBase = esBase
        };

        _dbContext.DivisasActivas.Add(divisa);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncDefaultDashboardCurrencyAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DivisaActivaDto(divisa.Codigo, divisa.Nombre, divisa.Simbolo, divisa.Activa, divisa.EsBase);
    }

    public async Task<DivisaActivaDto> ActualizarDivisaAsync(
        string codigo,
        string? nombre,
        string? simbolo,
        bool activa,
        bool esBase,
        CancellationToken cancellationToken)
    {
        var normalizedCodigo = Normalize(codigo);
        var divisa = await _dbContext.DivisasActivas
            .FirstOrDefaultAsync(x => x.Codigo == normalizedCodigo, cancellationToken);
        if (divisa is null)
        {
            throw new InvalidOperationException("Divisa no encontrada.");
        }

        if (esBase && !activa)
        {
            throw new InvalidOperationException("La divisa base debe estar activa.");
        }

        if (esBase)
        {
            await _dbContext.DivisasActivas
                .Where(x => x.EsBase && x.Codigo != normalizedCodigo)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EsBase, false), cancellationToken);
        }

        divisa.Nombre = CleanNullable(nombre);
        divisa.Simbolo = CleanNullable(simbolo);
        divisa.Activa = activa;
        divisa.EsBase = esBase;

        if (!divisa.EsBase)
        {
            var hasOtherBase = await _dbContext.DivisasActivas
                .AnyAsync(x => x.Codigo != normalizedCodigo && x.EsBase, cancellationToken);
            if (!hasOtherBase)
            {
                var replacement = await _dbContext.DivisasActivas
                    .Where(x => x.Codigo != normalizedCodigo && x.Activa)
                    .OrderBy(x => x.Codigo)
                    .FirstOrDefaultAsync(cancellationToken);
                if (replacement is null)
                {
                    throw new InvalidOperationException("Debe existir al menos una divisa base activa.");
                }

                replacement.EsBase = true;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncDefaultDashboardCurrencyAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DivisaActivaDto(divisa.Codigo, divisa.Nombre, divisa.Simbolo, divisa.Activa, divisa.EsBase);
    }

    public async Task<SyncTiposCambioResult> SincronizarTiposCambioAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var divisasActivas = await _dbContext.DivisasActivas
            .AsNoTracking()
            .Where(x => x.Activa)
            .OrderByDescending(x => x.EsBase)
            .ThenBy(x => x.Codigo)
            .ToListAsync(cancellationToken);

        if (divisasActivas.Count < 2)
        {
            return new SyncTiposCambioResult(false, 0, "Se necesitan al menos dos divisas activas para sincronizar.");
        }

        var exchangeApiKey = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x => x.Clave == ExchangeRateApiKeyConfig)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(exchangeApiKey))
        {
            return new SyncTiposCambioResult(false, 0, "Configura la clave API de ExchangeRate-API en Ajustes antes de sincronizar.");
        }

        exchangeApiKey = _secretProtector.UnprotectFromStorage(exchangeApiKey)?.Trim();
        if (string.IsNullOrWhiteSpace(exchangeApiKey))
        {
            return new SyncTiposCambioResult(false, 0, "Configura la clave API de ExchangeRate-API en Ajustes antes de sincronizar.");
        }

        var baseCurrency = divisasActivas.FirstOrDefault(x => x.EsBase)?.Codigo
            ?? await _dbContext.Configuraciones
                .AsNoTracking()
                .Where(x => x.Clave == "divisa_principal_default")
                .Select(x => x.Valor)
                .FirstOrDefaultAsync(cancellationToken)
            ?? "EUR";

        baseCurrency = Normalize(baseCurrency);

        try
        {
            var client = _httpClientFactory.CreateClient(ExchangeRateClient);
            var response = await client.GetAsync($"{Uri.EscapeDataString(exchangeApiKey)}/latest/{baseCurrency}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "ExchangeRate API devolvió {StatusCode}. Body: {Body}",
                    (int)response.StatusCode,
                    errorBody);
                return new SyncTiposCambioResult(false, 0, "No se pudo sincronizar con la API de tipos de cambio.");
            }

            var payload = await response.Content.ReadFromJsonAsync<ExchangeRatesApiResponse>(cancellationToken);
            var sourceRates = payload?.ConversionRates ?? payload?.Rates;
            if (sourceRates is null || sourceRates.Count == 0)
            {
                return new SyncTiposCambioResult(false, 0, "La API no devolvió tasas válidas.");
            }

            var normalizedRates = sourceRates.ToDictionary(
                x => Normalize(x.Key),
                x => Convert.ToDecimal(x.Value, CultureInfo.InvariantCulture));

            var targets = divisasActivas
                .Select(x => Normalize(x.Codigo))
                .Where(x => x != baseCurrency)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existing = await _dbContext.TiposCambio
                .Where(x => x.DivisaOrigen == baseCurrency && targets.Contains(x.DivisaDestino))
                .ToDictionaryAsync(x => x.DivisaDestino, cancellationToken);

            var updated = 0;
            foreach (var target in targets)
            {
                if (!normalizedRates.TryGetValue(target, out var rate) || rate <= 0m)
                {
                    continue;
                }

                if (!existing.TryGetValue(target, out var entity))
                {
                    entity = new TipoCambio
                    {
                        Id = Guid.NewGuid(),
                        DivisaOrigen = baseCurrency,
                        DivisaDestino = target
                    };
                    _dbContext.TiposCambio.Add(entity);
                }

                entity.Tasa = rate;
                entity.Fuente = FuenteTipoCambio.API;
                entity.FechaActualizacion = now;
                updated++;
            }

            if (updated == 0)
            {
                return new SyncTiposCambioResult(false, 0, "No se encontró ninguna tasa aplicable para las divisas activas.");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            InvalidateCache();

            return new SyncTiposCambioResult(true, updated, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al sincronizar tipos de cambio desde API");
            return new SyncTiposCambioResult(false, 0, "No se pudo sincronizar con la API de tipos de cambio.");
        }
    }

    private async Task<RateCatalog> GetRateCatalogAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<RateCatalog>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var rawRates = await _dbContext.TiposCambio
            .AsNoTracking()
            .Select(x => new { x.DivisaOrigen, x.DivisaDestino, x.Tasa })
            .ToListAsync(cancellationToken);

        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var graph = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rawRates)
        {
            var from = Normalize(row.DivisaOrigen);
            var to = Normalize(row.DivisaDestino);
            if (from == to || row.Tasa <= 0m)
            {
                continue;
            }

            rates[$"{from}|{to}"] = row.Tasa;
            AddGraphEdge(graph, from, to, row.Tasa);
            AddGraphEdge(graph, to, from, 1m / row.Tasa);
        }

        var catalog = new RateCatalog(rates, graph);

        _cache.Set(CacheKey, catalog, CacheDuration);
        return catalog;
    }

    private void InvalidateCache() => _cache.Remove(CacheKey);

    private async Task SyncDefaultDashboardCurrencyAsync(CancellationToken cancellationToken)
    {
        var activeBase = await _dbContext.DivisasActivas
            .Where(x => x.Activa && x.EsBase)
            .OrderBy(x => x.Codigo)
            .Select(x => x.Codigo)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(activeBase))
        {
            return;
        }

        var config = await _dbContext.Configuraciones
            .FirstOrDefaultAsync(x => x.Clave == "divisa_principal_default", cancellationToken);

        if (config is null)
        {
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = "divisa_principal_default",
                Valor = activeBase,
                Tipo = "string",
                Descripcion = "Divisa principal para dashboards",
                FechaModificacion = DateTime.UtcNow
            });

            return;
        }

        if (config.Valor == activeBase)
        {
            return;
        }

        config.Valor = activeBase;
        config.FechaModificacion = DateTime.UtcNow;
    }

    private static decimal? ResolveRate(string from, string to, RateCatalog catalog)
    {
        if (catalog.DirectRates.TryGetValue($"{from}|{to}", out var direct))
        {
            return direct;
        }

        if (catalog.DirectRates.TryGetValue($"{to}|{from}", out var reverse) && reverse != 0m)
        {
            return 1m / reverse;
        }

        if (!catalog.Graph.TryGetValue(from, out var startingEdges))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
        var queue = new Queue<(string Currency, decimal AccumulatedRate)>();

        foreach (var edge in startingEdges)
        {
            if (!visited.Add(edge.Key))
            {
                continue;
            }

            if (edge.Key.Equals(to, StringComparison.OrdinalIgnoreCase))
            {
                return edge.Value;
            }

            queue.Enqueue((edge.Key, edge.Value));
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!catalog.Graph.TryGetValue(current.Currency, out var nextEdges))
            {
                continue;
            }

            foreach (var edge in nextEdges)
            {
                if (!visited.Add(edge.Key))
                {
                    continue;
                }

                var resolvedRate = current.AccumulatedRate * edge.Value;
                if (edge.Key.Equals(to, StringComparison.OrdinalIgnoreCase))
                {
                    return resolvedRate;
                }

                queue.Enqueue((edge.Key, resolvedRate));
            }
        }

        return null;
    }

    private static void AddGraphEdge(
        IDictionary<string, Dictionary<string, decimal>> graph,
        string from,
        string to,
        decimal rate)
    {
        if (!graph.TryGetValue(from, out var edges))
        {
            edges = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            graph[from] = edges;
        }

        edges[to] = rate;
    }

    private static string Normalize(string? divisa) =>
        string.IsNullOrWhiteSpace(divisa) ? "EUR" : divisa.Trim().ToUpperInvariant();

    private static string? CleanNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private sealed class ExchangeRatesApiResponse
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("conversion_rates")]
        public Dictionary<string, double> ConversionRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("rates")]
        public Dictionary<string, double> Rates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record RateCatalog(
        IReadOnlyDictionary<string, decimal> DirectRates,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> Graph);
}

public sealed record TipoCambioDto(
    Guid Id,
    string DivisaOrigen,
    string DivisaDestino,
    decimal Tasa,
    DateTime FechaActualizacion,
    string Fuente);

public sealed record DivisaActivaDto(
    string Codigo,
    string? Nombre,
    string? Simbolo,
    bool Activa,
    bool EsBase);

public sealed record SyncTiposCambioResult(
    bool Success,
    int UpdatedCount,
    string? ErrorMessage);
