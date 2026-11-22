using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Tests;

/// <summary>
/// S-9/M-3 (LOTE 6): fabrica de un UserAccessService real para los tests que
/// construyen ConciliacionService o ImportacionService a mano. El cache es el
/// mismo que en produccion (namespace propio + TTL de CachingOptions), pero
/// en memoria y por instancia.
/// </summary>
internal static class TestUserAccess
{
    internal static IUserAccessService Create(AppDbContext dbContext)
    {
        return new UserAccessService(
            dbContext,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()));
    }
}
