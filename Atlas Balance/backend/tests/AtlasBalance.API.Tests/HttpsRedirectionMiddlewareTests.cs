using System.Net;
using AtlasBalance.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class HttpsRedirectionMiddlewareTests
{
    [Fact]
    public async Task Peticion_Https_Debe_Pasar_Sin_Redireccion()
    {
        var (statusCode, location, nextCalled) = await InvokeAsync(options =>
        {
            options.Scheme = "https";
            options.RemoteIp = IPAddress.Parse("10.0.0.8");
        });

        statusCode.Should().Be(StatusCodes.Status200OK);
        location.Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Peticion_Http_Remota_Debe_Redirigir_308_Conservando_Path_Query_Y_Metodo()
    {
        var (statusCode, location, nextCalled) = await InvokeAsync(options =>
        {
            options.Scheme = "http";
            options.RemoteIp = IPAddress.Parse("192.168.1.50");
            options.Path = "/api/cuentas";
            options.QueryString = "?page=2&search=iban";
            options.Method = "POST";
        });

        statusCode.Should().Be(StatusCodes.Status308PermanentRedirect);
        location.Should().Be("https://srv-tesoreria.local/api/cuentas?page=2&search=iban");
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Peticion_Http_De_Loopback_Sin_XForwardedProto_Debe_Pasar()
    {
        // Proxy inverso sin X-Forwarded-Proto, curl local y sondas internas:
        // redirigir aqui provocaria bucle 308 o romperia tooling local.
        var (statusCode, location, nextCalled) = await InvokeAsync(options =>
        {
            options.Scheme = "http";
            options.RemoteIp = IPAddress.Loopback;
        });

        statusCode.Should().Be(StatusCodes.Status200OK);
        location.Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Loopback_Ipv4_Mapeado_Debe_Tratarse_Como_Local()
    {
        var (statusCode, _, _) = await InvokeAsync(options =>
        {
            options.Scheme = "http";
            options.RemoteIp = IPAddress.Parse("::ffff:127.0.0.1");
        });

        statusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Loopback_Con_XForwardedProto_Http_Debe_Redirigir()
    {
        // El proxy conocido declaro que el cliente externo vino por HTTP:
        // redirigir es seguro porque el siguiente intento llegara con proto=https.
        var (statusCode, location, _) = await InvokeAsync(options =>
        {
            options.Scheme = "http";
            options.RemoteIp = IPAddress.Loopback;
            options.Headers["X-Forwarded-Proto"] = "http";
        });

        statusCode.Should().Be(StatusCodes.Status308PermanentRedirect);
        location.Should().Be("https://srv-tesoreria.local/");
    }

    [Fact]
    public async Task Health_Debe_Quedar_Exento_Aunque_Venga_Remota_Por_Http()
    {
        var (statusCode, location, nextCalled) = await InvokeAsync(options =>
        {
            options.Scheme = "http";
            options.RemoteIp = IPAddress.Parse("192.168.1.50");
            options.Path = "/api/health/ready";
        });

        statusCode.Should().Be(StatusCodes.Status200OK);
        location.Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task En_Development_Nunca_Debe_Redirigir()
    {
        var (statusCode, location, nextCalled) = await InvokeAsync(
            options =>
            {
                options.Scheme = "http";
                options.RemoteIp = IPAddress.Parse("192.168.1.50");
            },
            environmentName: "Development");

        statusCode.Should().Be(StatusCodes.Status200OK);
        location.Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Con_HttpsRedirect_Desactivado_No_Debe_Redirigir()
    {
        var (statusCode, location, nextCalled) = await InvokeAsync(
            options =>
            {
                options.Scheme = "http";
                options.RemoteIp = IPAddress.Parse("192.168.1.50");
            },
            configuration: new Dictionary<string, string?>
            {
                ["Security:HttpsRedirect"] = "false"
            });

        statusCode.Should().Be(StatusCodes.Status200OK);
        location.Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Con_HttpsPort_Configurado_Debe_Incluir_Puerto_No_Estandar()
    {
        var (_, location, _) = await InvokeAsync(
            options =>
            {
                options.Scheme = "http";
                options.RemoteIp = IPAddress.Parse("192.168.1.50");
            },
            configuration: new Dictionary<string, string?>
            {
                ["Security:HttpsPort"] = "8443"
            });

        location.Should().Be("https://srv-tesoreria.local:8443/");
    }

    [Fact]
    public async Task Con_HttpsPort_443_No_Debe_Anadir_Sufijo()
    {
        var (_, location, _) = await InvokeAsync(
            options =>
            {
                options.Scheme = "http";
                options.RemoteIp = IPAddress.Parse("192.168.1.50");
            },
            configuration: new Dictionary<string, string?>
            {
                ["Security:HttpsPort"] = "443"
            });

        location.Should().Be("https://srv-tesoreria.local/");
    }

    [Fact]
    public async Task PathBase_Debe_Respetarse_En_Location()
    {
        var (_, location, _) = await InvokeAsync(options =>
        {
            options.Scheme = "http";
            options.RemoteIp = IPAddress.Parse("192.168.1.50");
            options.PathBase = "/atlas";
            options.Path = "/api/cuentas";
        });

        location.Should().Be("https://srv-tesoreria.local/atlas/api/cuentas");
    }

    private static async Task<(int StatusCode, string? Location, bool NextCalled)> InvokeAsync(
        Action<RequestOptions> configure,
        string environmentName = "Production",
        IDictionary<string, string?>? configuration = null)
    {
        var options = new RequestOptions();
        configure(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
            .Build();

        var middleware = new HttpsRedirectionMiddleware(
            _ =>
            {
                options.NextCalled = true;
                return Task.CompletedTask;
            },
            config,
            new StubEnvironment(environmentName));

        var context = new DefaultHttpContext();
        context.Request.Scheme = options.Scheme;
        context.Request.Method = options.Method;
        context.Request.Host = new HostString("srv-tesoreria.local");
        context.Request.PathBase = options.PathBase;
        context.Request.Path = options.Path;
        context.Request.QueryString = new QueryString(options.QueryString);
        context.Connection.RemoteIpAddress = options.RemoteIp;
        foreach (var (key, value) in options.Headers)
        {
            context.Request.Headers[key] = value;
        }

        await middleware.InvokeAsync(context);

        var location = context.Response.Headers.Location.FirstOrDefault();
        return (context.Response.StatusCode, location, options.NextCalled);
    }

    private sealed class RequestOptions
    {
        public string Scheme { get; set; } = "http";
        public string Method { get; set; } = HttpMethods.Get;
        public string PathBase { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public string QueryString { get; set; } = string.Empty;
        public IPAddress? RemoteIp { get; set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool NextCalled { get; set; }
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public StubEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string ApplicationName { get; set; } = "AtlasBalance.API";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string? WebRootPath { get; set; }
    }
}
