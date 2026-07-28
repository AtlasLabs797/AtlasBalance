using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.Caching.Tests;

public class CacheIntegrationTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static CacheService BuildCacheService(out IMemoryCache memoryCache)
    {
        memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new CacheService(memoryCache, NullLogger<CacheService>.Instance);
    }

    [Fact]
    public async Task TiposCambio_Invalidate_Should_Refresh_After_Manual_Write()
    {
        var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var invalidator = new DashboardCacheInvalidator(cache);
        var sut = new TiposCambioService(
            db,
            cache,
            new StaticHttpClientFactory(),
            NullLogger<TiposCambioService>.Instance,
            new PlainTextSecretProtector(),
            Options.Create(new CachingOptions()));

        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.10m,
            FechaActualizacion = DateTime.UtcNow,
            Fuente = FuenteTipoCambio.MANUAL
        });
        await db.SaveChangesAsync();

        var first = await sut.ConvertAsync(100m, "EUR", "USD", CancellationToken.None);
        first.Should().Be(110m);

        invalidator.InvalidateDashboardMetrics();
        cache.Invalidate(new CacheNamespace("tipos_cambio_rates"));

        db.TiposCambio.Single().Tasa = 1.25m;
        await db.SaveChangesAsync();

        var second = await sut.ConvertAsync(100m, "EUR", "USD", CancellationToken.None);
        second.Should().Be(125m);
    }

    [Fact]
    public void DashboardCacheInvalidator_Should_Bump_Scope_And_Metrics_Generations()
    {
        var cache = BuildCacheService(out _);
        var invalidator = new DashboardCacheInvalidator(cache);

        var nsScope = new CacheNamespace("dashboard_scope");
        var nsMetrics = new CacheNamespace("dashboard_metrics");

        invalidator.InvalidateDashboardScope();
        invalidator.InvalidateDashboardMetrics();

        cache.GetMetricsSnapshot(nsScope.Name).Invalidations.Should().Be(1);
        cache.GetMetricsSnapshot(nsMetrics.Name).Invalidations.Should().Be(1);
    }

    [Fact]
    public async Task DashboardCacheInvalidator_Generations_Should_Invalidate_Scope_After_PermisosUsuario_Change()
    {
        var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var invalidator = new DashboardCacheInvalidator(cache);
        var nsScope = new CacheNamespace("dashboard_scope");

        var userId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "u@t.local",
            PasswordHash = "h",
            NombreCompleto = "U",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = Guid.NewGuid(),
            Nombre = "C",
            Divisa = "EUR",
            Activa = true
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true,
            PuedeAgregarLineas = true,
            PuedeEditarLineas = true,
            PuedeEliminarLineas = true,
            PuedeImportar = true,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        // Primera carga: el loader es solo observar que entra el invalidator.
        var loaded = 0;
        var first = await cache.GetOrLoadAsync(
            nsScope,
            "u",
            _ => { loaded++; return Task.FromResult("v1"); },
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        // Simulamos el cambio: cualquier consumidor (job, controller, interceptor)
        // deberia llamar a InvalidateDashboardScope tras tocar PermisosUsuario.
        invalidator.InvalidateDashboardScope();

        var second = await cache.GetOrLoadAsync(
            nsScope,
            "u",
            _ => { loaded++; return Task.FromResult("v2"); },
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        first.Should().Be("v1");
        second.Should().Be("v2");
        loaded.Should().Be(2);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client = new() { BaseAddress = new Uri("https://example.invalid/") };
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class PlainTextSecretProtector : AtlasBalance.API.Services.ISecretProtector
    {
        public string Protect(string plaintext) => plaintext ?? string.Empty;
        public string ProtectForStorage(string? plaintext) => plaintext ?? string.Empty;
        public string? UnprotectFromStorage(string? protectedValue) => protectedValue;
        public bool IsProtected(string? value) => false;
    }

    // ---------------------------------------------------------------------
    // ConfiguracionRepository: cache global de CONFIGURACIONES
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ConfiguracionRepository_GetAsync_Should_Return_Cached_Value_On_Second_Call()
    {
        await using var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var repo = new ConfiguracionRepository(
            db,
            new PlainTextSecretProtector(),
            new SystemClock(),
            cache,
            Options.Create(new CachingOptions()));

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "smtp_host",
            Valor = "smtp.example.com",
            EsSecreto = false,
            FechaModificacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var first = await repo.GetAsync("smtp_host", CancellationToken.None);
        var second = await repo.GetAsync("smtp_host", CancellationToken.None);

        first.Should().Be("smtp.example.com");
        second.Should().Be("smtp.example.com");
    }

    [Fact]
    public async Task ConfiguracionRepository_UpsertAsync_Should_Invalidate_Cache()
    {
        await using var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var repo = new ConfiguracionRepository(
            db,
            new PlainTextSecretProtector(),
            new SystemClock(),
            cache,
            Options.Create(new CachingOptions()));

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "alerta_saldo_cooldown_horas",
            Valor = "12",
            EsSecreto = false,
            FechaModificacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var first = await repo.GetAsync("alerta_saldo_cooldown_horas", CancellationToken.None);
        first.Should().Be("12");

        await repo.UpsertAsync("alerta_saldo_cooldown_horas", "24", false, "int", null, null, CancellationToken.None);

        var after = await repo.GetAsync("alerta_saldo_cooldown_horas", CancellationToken.None);
        after.Should().Be("24");
    }

    // ---------------------------------------------------------------------
    // IntegrationTokenService: cache por hash con invalidacion en revoke
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IntegrationTokenService_ValidateActiveTokenAsync_Should_Hit_Cache_After_First_Call()
    {
        await using var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var sut = new IntegrationTokenService(db, cache, Options.Create(new CachingOptions()));

        var plainToken = sut.GeneratePlainToken();
        var hash = sut.ComputeSha256(plainToken);

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = Guid.NewGuid(),
            TokenHash = hash,
            Estado = EstadoTokenIntegracion.Activo,
            FechaExpiracion = null,
            FechaCreacion = DateTime.UtcNow,
            DeletedAt = null
        });
        await db.SaveChangesAsync();

        var first = await sut.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        first.Should().NotBeNull();

        // Mutamos la BD sin pasar por el servicio: el cache debe seguir
        // devolviendo el token cacheado.
        db.IntegrationTokens.Single().Estado = EstadoTokenIntegracion.Revocado;
        await db.SaveChangesAsync();

        var cached = await sut.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        cached.Should().NotBeNull("el cache TTL es 20s por defecto y aun no se invalido");

        cache.Invalidate(new CacheNamespace(IntegrationTokenService.Namespace));

        var afterInvalidate = await sut.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        afterInvalidate.Should().BeNull();
    }

    [Fact]
    public async Task IntegrationTokenService_RevokeAsync_Should_Invalidate_Cache_Namespace()
    {
        await using var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var sut = new IntegrationTokenService(db, cache, Options.Create(new CachingOptions()));

        var plainToken = sut.GeneratePlainToken();
        var hash = sut.ComputeSha256(plainToken);
        var tokenId = Guid.NewGuid();

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = tokenId,
            TokenHash = hash,
            Estado = EstadoTokenIntegracion.Activo,
            FechaExpiracion = null,
            FechaCreacion = DateTime.UtcNow,
            DeletedAt = null
        });
        await db.SaveChangesAsync();

        var first = await sut.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        first.Should().NotBeNull();

        await sut.RevokeAsync(tokenId, CancellationToken.None);

        var after = await sut.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        after.Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // UserAccessService: cache del scope por usuario
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UserAccessService_GetScopeAsync_Should_Cache_For_NonAdmin_User()
    {
        await using var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var sut = new UserAccessService(db, cache, Options.Create(new CachingOptions()));

        var userId = Guid.NewGuid();
        var principal = BuildClaimsPrincipal(userId, "EMPLEADO");

        // Sin permisos: primer y segundo acceso deben producir el mismo
        // resultado (Guid.Empty, sin permisos) y no lanzar excepciones.
        var first = await sut.GetScopeAsync(principal, CancellationToken.None);
        var second = await sut.GetScopeAsync(principal, CancellationToken.None);

        first.UserId.Should().Be(userId);
        first.IsAdmin.Should().BeFalse();
        first.HasPermissions.Should().BeFalse();
        second.UserId.Should().Be(userId);

        var metrics = cache.GetMetricsSnapshot(UserAccessService.Namespace);
        metrics.Hits.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task UserAccessService_GetScopeAsync_Admin_Bypass_Should_Not_Touch_Cache()
    {
        // El bypass de admin no consulta el cache porque su resultado es
        // trivial. La metrica de hits/misses debe quedarse en cero.
        using var db = BuildDbContext();
        var cache = BuildCacheService(out _);
        var sut = new UserAccessService(db, cache, Options.Create(new CachingOptions()));

        var userId = Guid.NewGuid();
        var principal = BuildClaimsPrincipal(userId, "ADMIN");

        var scope = await sut.GetScopeAsync(principal, CancellationToken.None);

        scope.IsAdmin.Should().BeTrue();
        scope.HasGlobalAccess.Should().BeTrue();
        cache.GetMetricsSnapshot(UserAccessService.Namespace).Hits.Should().Be(0);
        cache.GetMetricsSnapshot(UserAccessService.Namespace).Misses.Should().Be(0);
    }

    private static System.Security.Claims.ClaimsPrincipal BuildClaimsPrincipal(Guid userId, string rol)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, rol)
            },
            authenticationType: "Test");
        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
}
