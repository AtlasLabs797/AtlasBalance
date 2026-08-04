using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AtlasBalance.API.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// V-02.07: cubre <see cref="RateLimitingOptions"/> (defaults y enlace de
/// configuracion) y la logica de particionado de <c>RateLimitingSetup</c>.
///
/// <c>RateLimitingSetup</c> es <c>internal static</c>, pero
/// AtlasBalance.API ya declara
/// <c>[assembly: InternalsVisibleTo("AtlasBalance.API.Tests")]</c>
/// (ver Properties/AssemblyInfo.cs), asi que se puede llamar a
/// <c>AddAtlasRateLimiting</c> directamente y ejercitar el
/// <see cref="RateLimiterOptions.GlobalLimiter"/> real en vez de reimplementar
/// la seleccion de particion a mano. Los metodos que hacen esa seleccion
/// (ResolvePartition, IsAuthPath, Window, ResolveUserId, etc.) son
/// <c>private</c>, no <c>internal</c>: InternalsVisibleTo no da acceso a
/// miembros privados, asi que la unica via para cubrir esa logica es
/// black-box, a traves del limiter que la propia configuracion produce.
///
/// Son tests unitarios puros: no se monta WebApplicationFactory ni hay
/// servidor HTTP real. El PartitionedRateLimiter se resuelve desde un
/// ServiceProvider en memoria y se le hacen peticiones de prueba
/// directamente con HttpContexts sinteticos.
/// </summary>
public sealed class RateLimitingSetupTests
{
    [Fact]
    public void Defaults_Should_Match_Documented_Windows()
    {
        var options = new RateLimitingOptions();

        options.Enabled.Should().BeTrue();
        options.Window.Should().Be(TimeSpan.FromSeconds(60));
        options.LoginLockDuration.Should().Be(TimeSpan.FromMinutes(30));
        options.LoginFailureWindow.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Defaults_Should_Match_Documented_Thresholds()
    {
        var options = new RateLimitingOptions();

        options.AuthPerMinutePerIp.Should().Be(10);
        options.AnonymousPerMinutePerIp.Should().Be(60);
        options.ReadPerMinutePerUser.Should().Be(300);
        options.WritePerMinutePerUser.Should().Be(60);
        options.ExpensivePerMinutePerUser.Should().Be(5);
        options.LoginMaxFailedAttemptsPerAccount.Should().Be(5);
        options.LoginMaxFailuresPerIpAndEmail.Should().Be(3);
        options.LoginMaxFailuresPerIp.Should().Be(7);
    }

    [Fact]
    public void Configure_Should_Bind_RateLimitingOptions_Section_And_Apply_Overrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RateLimitingOptions.SectionName}:ReadPerMinutePerUser"] = "123"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAtlasRateLimiting(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        options.ReadPerMinutePerUser.Should().Be(123);
        // El bind de Configure<T> es incremental sobre section: las propiedades
        // no presentes en la configuracion deben seguir en su valor por defecto.
        options.AuthPerMinutePerIp.Should().Be(10);
        options.WritePerMinutePerUser.Should().Be(60);
    }

    [Fact]
    public void PolicyNames_Expensive_Should_Be_Stable_Identifier()
    {
        RateLimitingSetup.PolicyNames.Expensive.Should().Be("atlas-expensive");
    }

    [Fact]
    public void GlobalLimiter_Should_Not_Limit_NonApi_Paths()
    {
        using var provider = BuildProvider();
        var limiter = ResolveGlobalLimiter(provider);

        var context = BuildContext(provider, "/index.html", "GET", ip: "10.0.0.1");

        for (var i = 0; i < 50; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue("los estaticos de la SPA no consumen presupuesto de API");
        }
    }

    [Fact]
    public void GlobalLimiter_Should_Not_Limit_Health_Path()
    {
        using var provider = BuildProvider();
        var limiter = ResolveGlobalLimiter(provider);

        var context = BuildContext(provider, "/api/health", "GET", ip: "10.0.0.1");

        for (var i = 0; i < 50; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue();
        }
    }

    [Fact]
    public void GlobalLimiter_Should_Not_Limit_OpenClaw_Integration_Path()
    {
        using var provider = BuildProvider();
        var limiter = ResolveGlobalLimiter(provider);

        var context = BuildContext(provider, "/api/integration/openclaw/eventos", "POST", ip: "10.0.0.1");

        for (var i = 0; i < 50; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue("OpenClaw ya tiene su propio limite por token en IntegrationAuthMiddleware");
        }
    }

    [Fact]
    public void GlobalLimiter_Should_Partition_Auth_Path_By_Ip()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{RateLimitingOptions.SectionName}:AuthPerMinutePerIp"] = "2"
        });
        var limiter = ResolveGlobalLimiter(provider);

        var contextIp1 = BuildContext(provider, "/api/auth/login", "POST", ip: "10.0.0.1");

        using (var lease1 = limiter.AttemptAcquire(contextIp1))
        {
            lease1.IsAcquired.Should().BeTrue();
        }
        using (var lease2 = limiter.AttemptAcquire(contextIp1))
        {
            lease2.IsAcquired.Should().BeTrue();
        }
        using (var lease3 = limiter.AttemptAcquire(contextIp1))
        {
            lease3.IsAcquired.Should().BeFalse("el limite configurado por IP en /api/auth/login es 2");
        }

        var contextIp2 = BuildContext(provider, "/api/auth/login", "POST", ip: "10.0.0.2");
        using var leaseOtherIp = limiter.AttemptAcquire(contextIp2);
        leaseOtherIp.IsAcquired.Should().BeTrue("cada IP tiene su propio cubo, no comparten presupuesto");
    }

    [Fact]
    public void GlobalLimiter_Should_Partition_Anonymous_Path_By_Ip()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{RateLimitingOptions.SectionName}:AnonymousPerMinutePerIp"] = "2"
        });
        var limiter = ResolveGlobalLimiter(provider);

        var context = BuildContext(provider, "/api/telemetria", "POST", ip: "10.0.0.5");

        using (var lease1 = limiter.AttemptAcquire(context))
        {
            lease1.IsAcquired.Should().BeTrue();
        }
        using (var lease2 = limiter.AttemptAcquire(context))
        {
            lease2.IsAcquired.Should().BeTrue();
        }
        using var lease3 = limiter.AttemptAcquire(context);
        lease3.IsAcquired.Should().BeFalse("una ruta anonima bajo /api que no es de auth usa el cubo anonimo por IP");
    }

    [Fact]
    public void GlobalLimiter_Should_Partition_Read_Requests_By_User()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{RateLimitingOptions.SectionName}:ReadPerMinutePerUser"] = "2"
        });
        var limiter = ResolveGlobalLimiter(provider);

        var context = BuildContext(provider, "/api/cuentas", "GET", ip: "10.0.0.9", userId: "user-1");

        using (var lease1 = limiter.AttemptAcquire(context))
        {
            lease1.IsAcquired.Should().BeTrue();
        }
        using (var lease2 = limiter.AttemptAcquire(context))
        {
            lease2.IsAcquired.Should().BeTrue();
        }
        using var lease3 = limiter.AttemptAcquire(context);
        lease3.IsAcquired.Should().BeFalse("los GET autenticados particionan por usuario, no por IP");
    }

    [Fact]
    public void GlobalLimiter_Should_Partition_Write_Requests_By_User_Separately_From_Reads()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{RateLimitingOptions.SectionName}:WritePerMinutePerUser"] = "1",
            [$"{RateLimitingOptions.SectionName}:ReadPerMinutePerUser"] = "1"
        });
        var limiter = ResolveGlobalLimiter(provider);

        var writeContext = BuildContext(provider, "/api/cuentas", "POST", ip: "10.0.0.9", userId: "user-2");
        using (var writeLease = limiter.AttemptAcquire(writeContext))
        {
            writeLease.IsAcquired.Should().BeTrue();
        }
        using var writeLeaseSecond = limiter.AttemptAcquire(writeContext);
        writeLeaseSecond.IsAcquired.Should().BeFalse("el cubo de escritura del usuario ya esta agotado");

        // El cubo de lectura del mismo usuario es independiente del de escritura.
        var readContext = BuildContext(provider, "/api/cuentas", "GET", ip: "10.0.0.9", userId: "user-2");
        using var readLease = limiter.AttemptAcquire(readContext);
        readLease.IsAcquired.Should().BeTrue("read y write son cubos distintos aunque el usuario sea el mismo");
    }

    [Fact]
    public void GlobalLimiter_Should_Not_Limit_When_Disabled()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{RateLimitingOptions.SectionName}:Enabled"] = "false",
            [$"{RateLimitingOptions.SectionName}:AuthPerMinutePerIp"] = "1"
        });
        var limiter = ResolveGlobalLimiter(provider);

        var context = BuildContext(provider, "/api/auth/login", "POST", ip: "10.0.0.1");

        for (var i = 0; i < 10; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue("Enabled=false es el interruptor de emergencia: nada se limita");
        }
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? overrides = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddAtlasRateLimiting(configuration);
        return services.BuildServiceProvider();
    }

    private static PartitionedRateLimiter<HttpContext> ResolveGlobalLimiter(IServiceProvider provider)
    {
        var rateLimiterOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        return rateLimiterOptions.GlobalLimiter!;
    }

    private static DefaultHttpContext BuildContext(
        IServiceProvider provider,
        string path,
        string method,
        string ip,
        string? userId = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);

        if (userId is not null)
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth");
            context.User = new ClaimsPrincipal(identity);
        }

        return context;
    }
}
