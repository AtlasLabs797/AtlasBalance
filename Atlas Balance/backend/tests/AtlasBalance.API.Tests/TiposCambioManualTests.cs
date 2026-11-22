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

    private static async Task SembrarDivisasYClaveAsync(AppDbContext db, bool conClave = true, bool dosDivisas = true)
    {
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        if (dosDivisas)
        {
            db.DivisasActivas.Add(new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });
        }

        if (conClave)
        {
            db.Configuraciones.Add(new Configuracion
            {
                Clave = "exchange_rate_api_key",
                Valor = "test-key",
                Tipo = "string",
                Descripcion = "API key de tipos de cambio",
                FechaModificacion = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static StubHttpMessageHandler Http429() => new(_ =>
        new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"error\":true}") });

    [Fact]
    public async Task Antispam_Funciona_Con_DetallesJson_Normalizado_Como_Postgres()
    {
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db);

        // PostgreSQL devuelve el jsonb normalizado: espacio tras los dos puntos.
        db.NotificacionesAdmin.Add(new NotificacionAdmin
        {
            Id = Guid.NewGuid(),
            Tipo = TipoNotificacion,
            Mensaje = "previo",
            Fecha = DateTime.UtcNow.AddHours(-1),
            DetallesJson = "{\"clave\": \"http_429\", \"detalles\": {\"status\": 429}}"
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db, Http429());
        await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        (await db.NotificacionesAdmin.CountAsync(x => x.Tipo == TipoNotificacion)).Should().Be(1);
    }

    [Fact]
    public async Task Antispam_Vuelve_A_Avisar_Fuera_De_La_Ventana_De_24h()
    {
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db);

        db.NotificacionesAdmin.Add(new NotificacionAdmin
        {
            Id = Guid.NewGuid(),
            Tipo = TipoNotificacion,
            Mensaje = "antiguo",
            Fecha = DateTime.UtcNow.AddHours(-25),
            DetallesJson = "{\"clave\": \"http_429\", \"detalles\": {\"status\": 429}}"
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db, Http429());
        await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        (await db.NotificacionesAdmin.CountAsync(x => x.Tipo == TipoNotificacion)).Should().Be(2);
    }

    [Fact]
    public async Task Antispam_Distingue_Claves_Distintas_Dentro_De_La_Ventana()
    {
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db);

        db.NotificacionesAdmin.Add(new NotificacionAdmin
        {
            Id = Guid.NewGuid(),
            Tipo = TipoNotificacion,
            Mensaje = "otro fallo",
            Fecha = DateTime.UtcNow.AddMinutes(-5),
            DetallesJson = "{\"clave\": \"sin_tasas\", \"detalles\": {}}"
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db, Http429());
        await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        (await db.NotificacionesAdmin.CountAsync(x => x.Tipo == TipoNotificacion)).Should().Be(2);
    }

    [Fact]
    public async Task Menos_De_Dos_Divisas_No_Avisa_Al_Admin()
    {
        // Una instalacion con una sola divisa es una configuracion normal, no un
        // fallo: avisar cada 24 h solo seria ruido.
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db, dosDivisas: false);

        var result = await BuildService(db).SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        (await db.NotificacionesAdmin.AnyAsync(x => x.Tipo == TipoNotificacion)).Should().BeFalse();
    }

    [Fact]
    public async Task Sin_Clave_Api_Avisa_Al_Admin_Y_No_Se_Duplica()
    {
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db, conClave: false);

        var sut = BuildService(db);
        await sut.SincronizarTiposCambioAsync(CancellationToken.None);
        await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        var n = await db.NotificacionesAdmin.SingleAsync(x => x.Tipo == TipoNotificacion);
        n.DetallesJson.Should().Contain("sin_clave_api");
    }

    [Fact]
    public async Task Ninguna_Tasa_Aplicable_Avisa_Al_Admin()
    {
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db);

        // La API responde con tasas, pero ninguna es de una divisa activa.
        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { conversion_rates = new Dictionary<string, decimal> { ["JPY"] = 160m } })
            }));

        var result = await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        var n = await db.NotificacionesAdmin.SingleAsync(x => x.Tipo == TipoNotificacion);
        n.DetallesJson.Should().Contain("sin_tasa_aplicable");
    }

    [Fact]
    public async Task Excepcion_Inesperada_No_Arrastra_Cambios_Sin_Guardar()
    {
        await using var db = BuildDbContext();
        await SembrarDivisasYClaveAsync(db);
        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.10m,
            Fuente = FuenteTipoCambio.API,
            FechaActualizacion = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        // El cuerpo no es JSON valido: salta el catch general.
        var sut = BuildService(db, new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("no es json") }));
        // Se deja una entidad modificada y sin guardar en el mismo contexto.
        var tasa = await db.TiposCambio.SingleAsync();
        tasa.Tasa = 999m;

        await sut.SincronizarTiposCambioAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.TiposCambio.SingleAsync()).Tasa.Should().Be(1.10m);
        (await db.NotificacionesAdmin.CountAsync(x => x.Tipo == TipoNotificacion)).Should().Be(1);
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
