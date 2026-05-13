using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public class TiposCambioServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ConvertAsync_Should_Resolve_Cross_Rates_When_Stored_Base_Is_Not_EUR()
    {
        await using var db = BuildDbContext();

        db.TiposCambio.AddRange(
            new TipoCambio
            {
                Id = Guid.NewGuid(),
                DivisaOrigen = "USD",
                DivisaDestino = "EUR",
                Tasa = 0.80m,
                FechaActualizacion = DateTime.UtcNow,
                Fuente = FuenteTipoCambio.API
            },
            new TipoCambio
            {
                Id = Guid.NewGuid(),
                DivisaOrigen = "USD",
                DivisaDestino = "MXN",
                Tasa = 20.00m,
                FechaActualizacion = DateTime.UtcNow,
                Fuente = FuenteTipoCambio.API
            });

        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.ConvertAsync(100m, "EUR", "MXN", CancellationToken.None);

        result.Should().Be(2500m);
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_Should_Use_Active_Base_Currency_From_Db()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "MXN", Activa = true, EsBase = false });

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "divisa_principal_default",
            Valor = "EUR",
            Tipo = "string",
            Descripcion = "Divisa principal para dashboards",
            FechaModificacion = DateTime.UtcNow
        });
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "exchange_rate_api_key",
            Valor = "test-key",
            Tipo = "string",
            Descripcion = "API key de tipos de cambio",
            FechaModificacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var payload = new
        {
            conversion_rates = new Dictionary<string, decimal>
            {
                ["EUR"] = 0.80m,
                ["MXN"] = 20.00m,
                ["USD"] = 1.00m
            }
        };

        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            }));

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeTrue();
        result.UpdatedCount.Should().Be(2);

        var rates = await db.TiposCambio
            .OrderBy(x => x.DivisaDestino)
            .ToListAsync();

        rates.Should().HaveCount(2);
        rates.Should().ContainSingle(x => x.DivisaOrigen == "USD" && x.DivisaDestino == "EUR" && x.Tasa == 0.80m);
        rates.Should().ContainSingle(x => x.DivisaOrigen == "USD" && x.DivisaDestino == "MXN" && x.Tasa == 20.00m);
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_Should_Fail_When_ApiKey_Is_Missing()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        await db.SaveChangesAsync();

        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    conversion_rates = new Dictionary<string, decimal>
                    {
                        ["EUR"] = 1.00m,
                        ["USD"] = 1.10m
                    }
                })
            }));

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API key");
    }

    [Fact]
    public async Task ConvertAsync_Should_Fall_Back_To_Last_Stored_Rate_When_Sync_Fails()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.10m,
            FechaActualizacion = DateTime.UtcNow.AddHours(-12),
            Fuente = FuenteTipoCambio.MANUAL
        });
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "exchange_rate_api_key",
            Valor = "test-key",
            Tipo = "string",
            Descripcion = "API key de tipos de cambio",
            FechaModificacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var sut = BuildService(db, new StubHttpMessageHandler(_ => throw new HttpRequestException("offline")));

        var syncResult = await sut.SincronizarTiposCambioAsync(CancellationToken.None);
        var converted = await sut.ConvertAsync(100m, "EUR", "USD", CancellationToken.None);

        syncResult.Success.Should().BeFalse();
        converted.Should().Be(110m);
    }

    private static TiposCambioService BuildService(AppDbContext db, HttpMessageHandler? handler = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = new Uri("https://example.invalid/");

        return new TiposCambioService(
            db,
            cache,
            new StaticHttpClientFactory(client),
            NullLogger<TiposCambioService>.Instance,
            new PlainTextSecretProtector());
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
