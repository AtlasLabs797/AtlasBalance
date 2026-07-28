using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Services;

public interface IConfiguracionRepository
{
    Task<string?> GetAsync(string clave, CancellationToken cancellationToken);
    Task UpsertAsync(string clave, string? valor, bool esSecreto, string? tipo, string? descripcion, Guid? usuarioModificacionId, CancellationToken cancellationToken);
    IReadOnlyList<string> SecretKeys { get; }
}

public static class ConfiguracionSecretKeys
{
    public static readonly IReadOnlyList<string> List = new[]
    {
        "smtp_password",
        "exchange_rate_api_key",
        "openrouter_api_key",
        "openai_api_key",
        "minimax_api_key",
        "google_drive_oauth_client_secret",
        "backup_cloud_encryption_key",
        "github_update_token"
    };
}

public sealed class ConfiguracionRepository : IConfiguracionRepository
{
    internal const string Namespace = "configuracion";

    private readonly AppDbContext _dbContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IClock _clock;
    private readonly ICacheService _cacheService;
    private readonly CachingOptions _cachingOptions;

    public ConfiguracionRepository(
        AppDbContext dbContext,
        ISecretProtector secretProtector,
        IClock clock,
        ICacheService cacheService,
        IOptions<CachingOptions> cachingOptions)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _clock = clock;
        _cacheService = cacheService;
        _cachingOptions = cachingOptions.Value;
    }

    public IReadOnlyList<string> SecretKeys => ConfiguracionSecretKeys.List;

    public async Task<string?> GetAsync(string clave, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(clave))
        {
            return null;
        }

        var map = await GetCachedConfiguracionesAsync(cancellationToken);
        if (!map.TryGetValue(clave, out var entry))
        {
            return null;
        }

        if (entry.EsSecreto)
        {
            return string.IsNullOrEmpty(entry.Valor) ? null : _secretProtector.UnprotectFromStorage(entry.Valor);
        }

        return entry.Valor;
    }

    public async Task UpsertAsync(string clave, string? valor, bool esSecreto, string? tipo, string? descripcion, Guid? usuarioModificacionId, CancellationToken cancellationToken)
    {
        var storedValue = valor;
        if (esSecreto && !string.IsNullOrEmpty(valor))
        {
            storedValue = _secretProtector.ProtectForStorage(valor);
        }

        var existing = await _dbContext.Configuraciones.FirstOrDefaultAsync(c => c.Clave == clave, cancellationToken);
        if (existing is null)
        {
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = clave,
                Valor = storedValue ?? string.Empty,
                EsSecreto = esSecreto,
                Tipo = tipo,
                Descripcion = descripcion,
                FechaModificacion = _clock.UtcNow,
                UsuarioModificacionId = usuarioModificacionId
            });
        }
        else
        {
            existing.Valor = storedValue ?? string.Empty;
            existing.EsSecreto = esSecreto;
            existing.Tipo = tipo;
            existing.Descripcion = descripcion;
            existing.FechaModificacion = _clock.UtcNow;
            existing.UsuarioModificacionId = usuarioModificacionId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalida el namespace completo: cualquier cambio puede afectar a
        // claves que ya estan cacheadas. El interceptor cubre el caso de
        // escrituras masivas, pero UpsertAsync tambien debe invalidar por
        // si el caller no pasa por EF (jobs, seeds, migraciones).
        _cacheService.Invalidate(new CacheNamespace(Namespace));
    }

    private Task<IReadOnlyDictionary<string, ConfiguracionEntry>> GetCachedConfiguracionesAsync(CancellationToken cancellationToken)
    {
        return _cacheService.GetOrLoadAsync(
            new CacheNamespace(Namespace),
            "all",
            LoadConfiguracionesAsync,
            _cachingOptions.ConfigurationTtl,
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, ConfiguracionEntry>> LoadConfiguracionesAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Configuraciones
            .AsNoTracking()
            .Select(c => new ConfiguracionEntry
            {
                Clave = c.Clave,
                Valor = c.Valor,
                EsSecreto = c.EsSecreto
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Clave, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ConfiguracionEntry
    {
        public string Clave { get; init; } = string.Empty;
        public string Valor { get; init; } = string.Empty;
        public bool EsSecreto { get; init; }
    }
}
