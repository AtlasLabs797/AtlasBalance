using System.Net;
using System.Net.Http.Json;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

// B-2 (LOTE 5): la sincronizacion de tipos de cambio no debe pisar las tasas
// manuales (Fuente = MANUAL) y los fallos de sincronizacion deben dejar aviso
// en NOTIFICACIONES_ADMIN, no solo un LogWarning.
public class TiposCambioManualTests
{
    private const string TipoNotificacion = "TIPOS_CAMBIO";

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_No_Debe_Pisar_La_Tasa_Manual()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        var fechaManual = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.10m,
            FechaActualizacion = fechaManual,
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

        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    conversion_rates = new Dictionary<string, decimal>
                    {
                        ["EUR"] = 1.00m,
                        ["USD"] = 1.25m
                    }
                })
            }));

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeTrue();

        var tasa = await db.TiposCambio.SingleAsync(x => x.DivisaOrigen == "EUR" && x.DivisaDestino == "USD");
        tasa.Tasa.Should().Be(1.10m);
        tasa.Fuente.Should().Be(FuenteTipoCambio.MANUAL);
        tasa.FechaActualizacion.Should().Be(fechaManual);
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_Fallo_Http_Deja_Notificacion_Admin()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "exchange_rate_api_key",
            Valor = "test-key",
            Tipo = "string",
            Descripcion = "API key de tipos de cambio",
            FechaModificacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":true}")
            }));

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeFalse();

        var notificaciones = await db.NotificacionesAdmin.ToListAsync();
        notificaciones.Should().ContainSingle(x => x.Tipo == TipoNotificacion);
        notificaciones[0].Mensaje.Should().Contain("429");
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_Fallo_Http_Repetido_No_Duplica_La_Notificacion()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "exchange_rate_api_key",
            Valor = "test-key",
            Tipo = "string",
            Descripcion = "API key de tipos de cambio",
            FechaModificacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":true}")
            }));

        await sut.SincronizarTiposCambioAsync(CancellationToken.None);
        await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        var notificaciones = await db.NotificacionesAdmin.ToListAsync();
        notificaciones.Should().ContainSingle(x => x.Tipo == TipoNotificacion);
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_Sin_Tasas_En_La_Respuesta_Deja_Notificacion_Admin()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "exchange_rate_api_key",
            Valor = "test-key",
            Tipo = "string",
            Descripcion = "API key de tipos de cambio",
            FechaModificacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { conversion_rates = new Dictionary<string, decimal>() })
            }));

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeFalse();

        var notificaciones = await db.NotificacionesAdmin.ToListAsync();
        notificaciones.Should().ContainSingle(x => x.Tipo == TipoNotificacion);
    }

    [Fact]
    public async Task SincronizarTiposCambioAsync_Excepcion_Deja_Notificacion_Admin()
    {
        await using var db = BuildDbContext();

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

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

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeFalse();

        var notificaciones = await db.NotificacionesAdmin.ToListAsync();
        notificaciones.Should().ContainSingle(x => x.Tipo == TipoNotificacion);
    }

    private static TiposCambioService BuildService(AppDbContext db, HttpMessageHandler? handler = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheService = new CacheService(cache, NullLogger<CacheService>.Instance);
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = new Uri("https://example.invalid/");

        return new TiposCambioService(
            db,
            cacheService,
            new StaticHttpClientFactory(client),
            NullLogger<TiposCambioService>.Instance,
            new PlainTextSecretProtector(),
            Options.Create(new CachingOptions()));
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
