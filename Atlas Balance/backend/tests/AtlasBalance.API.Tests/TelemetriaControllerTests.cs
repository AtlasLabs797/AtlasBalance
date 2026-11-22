using System.Net;
using System.Net.Http.Json;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// S-25: un error de render con stack largo no se rechaza: el controller lo
/// recorta a sus topes y responde 204, y el error del frontend se registra a
/// nivel Warning, no Error.
/// </summary>
public sealed class TelemetriaControllerTests
{
    private const string CategoriaTelemetria = "AtlasBalance.API.Controllers.TelemetriaController";

    [Fact]
    public async Task Peticion_Con_Stack_Sobredimensionado_Debe_Devolver_204_Recortado_Sin_Log_Error()
    {
        var provider = new CapturingLoggerProvider();
        await using var fixture = await TestServerFixture.StartAsync(provider);

        var payload = new
        {
            mensaje = "error de render",
            stack = new string('x', 5000),
            componentStack = new string('y', 32),
            path = "/dashboard",
            timestamp = "2026-11-16T10:00:00Z"
        };

        var response = await fixture.Client.PostAsJsonAsync("/api/telemetria/errores", payload);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "un stack largo se recorta en el controller en vez de rechazar el informe con 400");

        var registros = provider.Records.Where(record => record.Category == CategoriaTelemetria).ToList();
        registros.Should().NotBeEmpty("la accion llega a ejecutarse y registra el error");
        registros.Should().NotContain(record => record.Level == LogLevel.Error);
        registros.Should().Contain(record => record.Level == LogLevel.Warning
            && record.Message.Contains(new string('x', 4000))
            && !record.Message.Contains(new string('x', 4001)),
            "el stack llega al log recortado a 4000 caracteres");
    }

    [Fact]
    public async Task Cuerpo_Por_Encima_Del_Limite_De_Peticion_Debe_Devolver_413()
    {
        var provider = new CapturingLoggerProvider();
        await using var fixture = await TestServerFixture.StartAsync(provider);

        var payload = new
        {
            mensaje = "error de render",
            stack = new string('x', 64 * 1024)
        };

        var response = await fixture.Client.PostAsJsonAsync("/api/telemetria/errores", payload);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "el endpoint anonimo tiene tope de tamano de cuerpo");
        provider.Records
            .Where(record => record.Category == CategoriaTelemetria)
            .Should().BeEmpty("el cuerpo se rechaza antes de llegar a la accion");
    }

    [Fact]
    public void Peticion_Normal_Debe_Registrar_Warning_Y_No_Error()
    {
        var provider = new CapturingLoggerProvider();
        var logger = provider.CreateTypedLogger<TelemetriaController>();
        var controller = new TelemetriaController(logger, new MemoryCache(new MemoryCacheOptions()));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = controller.RegistrarError(new ErrorClienteRequest
        {
            Mensaje = "error de render",
            Stack = "Error: boom",
            ComponentStack = "en Componente",
            Path = "/dashboard",
            Timestamp = "2026-11-16T10:00:00Z"
        });

        result.Should().BeOfType<NoContentResult>();

        var registros = provider.Records.Where(record => record.Category == CategoriaTelemetria).ToList();
        registros.Should().Contain(record => record.Level == LogLevel.Warning);
        registros.Should().NotContain(record => record.Level == LogLevel.Error);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogRecord> _records = new();

        public IReadOnlyList<LogRecord> Records => _records;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _records);

        public ILogger<T> CreateTypedLogger<T>() => new CapturingLogger<T>(typeof(T).FullName!, _records);

        public void Dispose()
        {
        }

        public sealed record LogRecord(string Category, LogLevel Level, string Message);

        private sealed class CapturingLogger(string category, List<LogRecord> records) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (records)
                {
                    records.Add(new LogRecord(category, logLevel, formatter(state, exception)));
                }
            }
        }

        // ILogger<T>: el controller recibe ILogger<TelemetriaController> y la
        // categoria real es el nombre completo del tipo.
        private sealed class CapturingLogger<T>(string category, List<LogRecord> records) : ILogger<T>
        {
            private readonly CapturingLogger _inner = new(category, records);

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    // Host minimo en proceso: solo se usa para comprobar el 400 automatico de
    // [ApiController] con el ModelState invalido. Se apaga al terminar el test.
    private sealed class TestServerFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestServerFixture(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<TestServerFixture> StartAsync(CapturingLoggerProvider provider)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            // appsettings de la API trae AllowedHosts=localhost, y el filtro de host
            // responde 400 a la llamada por 127.0.0.1. En el host de prueba no aplica.
            builder.Configuration["AllowedHosts"] = "*";
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddProvider(provider);
            builder.Services.AddMemoryCache();
            builder.Services.AddControllers()
                // El host de test es una app de consola: MVC solo registra como
                // "application part" el ensamblado de entrada, asi que los
                // controllers de la API no se descubririan (la ruta responderia
                // 404). Se anade el ensamblado de la API a mano.
                .ConfigureApplicationPartManager(manager =>
                    manager.ApplicationParts.Add(new AssemblyPart(typeof(TelemetriaController).Assembly)));

            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var url = addresses!.Addresses.First();

            return new TestServerFixture(app, new HttpClient { BaseAddress = new Uri(url) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
