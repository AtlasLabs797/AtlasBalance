using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// S-22: el rate-limit se aplica ANTES del chequeo de scope (el sondeo de rutas
/// fuera de scope tambien consume cuota) y el rechazo por rate-limit notifica al
/// admin UNA VEZ por token y ventana, no en cada peticion.
/// </summary>
public sealed class IntegrationRateLimitScopeTests
{
    private const string RateLimitConfigKey = "integration_rate_limit_per_minute";

    private static AppDbContext BuildDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IntegrationTokenService BuildTokenService(AppDbContext dbContext, IClock clock)
    {
        return new IntegrationTokenService(
            dbContext,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()),
            clock);
    }

    private static async Task<(IntegrationToken Token, string PlainToken)> SeedTokenAsync(
        AppDbContext dbContext,
        IntegrationTokenService tokenService,
        string scopesJson)
    {
        const string plainToken = "sk_test_rate_limit_scope";
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "rate-limit-scope",
            TokenHash = tokenService.ComputeSha256(plainToken),
            Estado = EstadoTokenIntegracion.Activo,
            PermisoLectura = true,
            EndpointScopesJson = scopesJson,
            UsuarioCreadorId = Guid.NewGuid()
        };

        dbContext.IntegrationTokens.Add(token);
        await dbContext.SaveChangesAsync();

        return (token, plainToken);
    }

    private static void SeedRateLimit(AppDbContext dbContext, int limit)
    {
        dbContext.Configuraciones.Add(new Configuracion
        {
            Clave = RateLimitConfigKey,
            Valor = limit.ToString()
        });
    }

    private static async Task<int> InvokeAsync(
        IntegrationAuthMiddleware middleware,
        AppDbContext dbContext,
        IIntegrationTokenService tokenService,
        string plainToken,
        string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Authorization = $"Bearer {plainToken}";

        await middleware.InvokeAsync(context, dbContext, tokenService);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task Peticion_Fuera_De_Scope_Debe_Consumir_Cuota_Y_Responder_429_Cuando_Esta_Agotada()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var dbContext = BuildDbContext(databaseName);
        var clock = new FakeClock(new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tokenService = BuildTokenService(dbContext, clock);

        SeedRateLimit(dbContext, 2);
        var (token, plainToken) = await SeedTokenAsync(dbContext, tokenService, "[\"saldos\"]");
        await dbContext.SaveChangesAsync();

        var middleware = new IntegrationAuthMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            cache,
            clock);

        // Dos sondeos a una ruta FUERA de scope: con el rate-limit despues del
        // scope, ninguno consumia cuota.
        (await InvokeAsync(middleware, dbContext, tokenService, plainToken, "/api/integration/openclaw/titulares"))
            .Should().Be(StatusCodes.Status403Forbidden);
        (await InvokeAsync(middleware, dbContext, tokenService, plainToken, "/api/integration/openclaw/titulares"))
            .Should().Be(StatusCodes.Status403Forbidden);

        // La cuota de 2/min la han consumido los dos sondeos fuera de scope: la
        // peticion valida tiene que recibir 429, no 200.
        (await InvokeAsync(middleware, dbContext, tokenService, plainToken, "/api/integration/openclaw/saldos"))
            .Should().Be(StatusCodes.Status429TooManyRequests);

        // El rechazo por scope sigue dejando rastro en la auditoria.
        var rechazos = await dbContext.AuditoriaIntegraciones
            .Where(row => row.TokenId == token.Id && row.CodigoRespuesta == StatusCodes.Status403Forbidden)
            .CountAsync();
        rechazos.Should().Be(2);
    }

    [Fact]
    public async Task Dos_Rechazos_Por_Rate_Limit_En_La_Misma_Ventana_Deben_Generar_Una_Sola_Notificacion_Y_Dos_Filas_De_Auditoria()
    {
        await using var dbContext = BuildDbContext();
        var clock = new FakeClock(new DateTime(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tokenService = BuildTokenService(dbContext, clock);

        SeedRateLimit(dbContext, 1);
        var (token, plainToken) = await SeedTokenAsync(dbContext, tokenService, "[\"saldos\"]");
        await dbContext.SaveChangesAsync();

        var middleware = new IntegrationAuthMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            cache,
            clock);

        (await InvokeAsync(middleware, dbContext, tokenService, plainToken, "/api/integration/openclaw/saldos"))
            .Should().Be(StatusCodes.Status200OK);
        (await InvokeAsync(middleware, dbContext, tokenService, plainToken, "/api/integration/openclaw/saldos"))
            .Should().Be(StatusCodes.Status429TooManyRequests);
        (await InvokeAsync(middleware, dbContext, tokenService, plainToken, "/api/integration/openclaw/saldos"))
            .Should().Be(StatusCodes.Status429TooManyRequests);

        var notificaciones = await dbContext.NotificacionesAdmin.CountAsync();
        notificaciones.Should().Be(1, "el rechazo por rate-limit notifica una vez por token y ventana");

        var rechazos = await dbContext.AuditoriaIntegraciones
            .Where(row => row.TokenId == token.Id && row.CodigoRespuesta == StatusCodes.Status429TooManyRequests)
            .CountAsync();
        rechazos.Should().Be(2, "la auditoria es append-only: un rastro por peticion rechazada");
    }

    [Fact]
    public async Task Si_Falla_La_Escritura_De_Auditoria_La_Respuesta_Sigue_Siendo_403_Y_429()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var dbContext = BuildDbContext(databaseName);
        var clock = new FakeClock(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tokenService = BuildTokenService(dbContext, clock);

        SeedRateLimit(dbContext, 1);
        var (_, plainToken) = await SeedTokenAsync(dbContext, tokenService, "[\"saldos\"]");
        await dbContext.SaveChangesAsync();

        await using var failingDb = new FailingSaveDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options);

        var middleware = new IntegrationAuthMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            cache,
            clock);

        // 403: rechazo por scope con la BD caida al escribir la auditoria.
        (await InvokeAsync(middleware, failingDb, tokenService, plainToken, "/api/integration/openclaw/titulares"))
            .Should().Be(StatusCodes.Status403Forbidden);

        // 429: rechazo por rate-limit con la BD caida al escribir la auditoria.
        (await InvokeAsync(middleware, failingDb, tokenService, plainToken, "/api/integration/openclaw/saldos"))
            .Should().Be(StatusCodes.Status429TooManyRequests);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    // BD que falla al guardar: el middleware no debe convertir un 403/429 en un 500.
    private sealed class FailingSaveDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("BD no disponible");
    }
}
