using FluentAssertions;
using GestionCaja.API.Data;
using GestionCaja.API.Middleware;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace GestionCaja.API.Tests;

public sealed class IntegrationAuthMiddlewareTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RateLimit_Should_Not_Allow_Burst_Across_Minute_Boundary()
    {
        await using var db = BuildDbContext();
        var clock = new FakeClock(new DateTime(2026, 4, 18, 10, 58, 0, DateTimeKind.Utc));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tokenService = new IntegrationTokenService(db);
        var plainToken = "sk_test_rate_limit";

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "rate-limit",
            TokenHash = tokenService.ComputeSha256(plainToken),
            Estado = EstadoTokenIntegracion.Activo,
            PermisoLectura = true,
            UsuarioCreadorId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var middleware = new IntegrationAuthMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            cache,
            clock);

        for (var i = 0; i < 100; i++)
        {
            var statusCode = await InvokeWithTokenAsync(middleware, db, tokenService, plainToken, CancellationToken.None);
            statusCode.Should().Be(StatusCodes.Status200OK);
        }

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var burstStatus = await InvokeWithTokenAsync(middleware, db, tokenService, plainToken, CancellationToken.None);
        burstStatus.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task IntegrationAudit_Should_Persist_Even_If_Client_Cancels()
    {
        await using var db = BuildDbContext();
        var clock = new FakeClock(new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tokenService = new IntegrationTokenService(db);
        var plainToken = "sk_test_cancel";

        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "cancel",
            TokenHash = tokenService.ComputeSha256(plainToken),
            Estado = EstadoTokenIntegracion.Activo,
            PermisoLectura = true,
            UsuarioCreadorId = Guid.NewGuid()
        };
        db.IntegrationTokens.Add(token);
        await db.SaveChangesAsync();

        var middleware = new IntegrationAuthMiddleware(
            context => Task.FromCanceled(context.RequestAborted),
            cache,
            clock);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new DefaultHttpContext
        {
            RequestAborted = cts.Token
        };
        context.Request.Path = "/api/integration/openclaw/saldos";
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Authorization = $"Bearer {plainToken}";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(context, db, tokenService));

        var logs = await db.AuditoriaIntegraciones.Where(row => row.TokenId == token.Id).ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].CodigoRespuesta.Should().Be(StatusCodes.Status500InternalServerError);
    }

    private static async Task<int> InvokeWithTokenAsync(
        IntegrationAuthMiddleware middleware,
        AppDbContext db,
        IIntegrationTokenService tokenService,
        string plainToken,
        CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellationToken
        };
        context.Request.Path = "/api/integration/openclaw/saldos";
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Authorization = $"Bearer {plainToken}";

        await middleware.InvokeAsync(context, db, tokenService);
        return context.Response.StatusCode;
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }
    }
}
