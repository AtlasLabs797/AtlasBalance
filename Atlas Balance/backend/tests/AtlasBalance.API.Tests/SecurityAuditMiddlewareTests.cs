using System.Security.Claims;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-02.07: antes de este middleware, los ~40 `Forbid()` repartidos por los
// controladores no dejaban ningun rastro. Un usuario legitimo probando ids de
// cuentas ajenas era completamente invisible en la auditoria. Estos tests
// fijan que se registra lo que hay que registrar y, tan importante como eso,
// que NO se registra el ruido que convertiria AUDITORIAS en un vector de
// denegacion de servicio.
// -----------------------------------------------------------------------
public sealed class SecurityAuditMiddlewareTests
{
    [Fact]
    public async Task Should_Audit_403_With_User_Path_And_Status()
    {
        var userId = Guid.NewGuid();
        await using var db = BuildDbContext();
        var context = BuildContext(db, "/api/extractos/123", userId);

        await BuildMiddleware(StatusCodes.Status403Forbidden).InvokeAsync(context);

        var audit = await db.Auditorias.SingleAsync();
        audit.TipoAccion.Should().Be(AuditActions.AuthzDenied);
        audit.UsuarioId.Should().Be(userId);
        audit.DetallesJson.Should().Contain("/api/extractos/123");
        audit.DetallesJson.Should().Contain("403");
    }

    [Fact]
    public async Task Should_Audit_401_On_Protected_Endpoints()
    {
        await using var db = BuildDbContext();
        var context = BuildContext(db, "/api/cuentas", usuarioId: null);

        await BuildMiddleware(StatusCodes.Status401Unauthorized).InvokeAsync(context);

        var audit = await db.Auditorias.SingleAsync();
        audit.TipoAccion.Should().Be(AuditActions.AuthnDenied);
        audit.UsuarioId.Should().BeNull();
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh-token")]
    [InlineData("/api/health")]
    public async Task Should_Not_Audit_401_Where_It_Is_Normal_Operation(string ruta)
    {
        // El login ya audita LOGIN_FAILED con mucho mas contexto, y el refresh
        // devuelve 401 cada vez que caduca un access token: auditarlos aqui
        // duplicaria filas y enterraria las senales de verdad.
        await using var db = BuildDbContext();
        var context = BuildContext(db, ruta, usuarioId: null);

        await BuildMiddleware(StatusCodes.Status401Unauthorized).InvokeAsync(context);

        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Should_Audit_Bulk_Read_Above_Threshold()
    {
        var userId = Guid.NewGuid();
        await using var db = BuildDbContext();
        var context = BuildContext(db, "/api/extractos", userId, query: "?pageSize=5000");

        await BuildMiddleware(StatusCodes.Status200OK).InvokeAsync(context);

        var audit = await db.Auditorias.SingleAsync();
        audit.TipoAccion.Should().Be(AuditActions.AccesoBulk);
        audit.DetallesJson.Should().Contain("5000");
    }

    [Fact]
    public async Task Should_Not_Audit_Normal_Page_Sizes()
    {
        await using var db = BuildDbContext();
        var context = BuildContext(db, "/api/extractos", Guid.NewGuid(), query: "?pageSize=50");

        await BuildMiddleware(StatusCodes.Status200OK).InvokeAsync(context);

        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Should_Deduplicate_Repeated_Denials_Within_The_Window()
    {
        // Un bucle del frontend o un escaneo pueden generar cientos de 403
        // identicos por segundo. Sin deduplicar, la tabla de auditoria se
        // convierte en el problema en vez de la defensa.
        var userId = Guid.NewGuid();
        await using var db = BuildDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var middleware = BuildMiddleware(StatusCodes.Status403Forbidden, cache);

        for (var i = 0; i < 5; i++)
        {
            await middleware.InvokeAsync(BuildContext(db, "/api/extractos/123", userId));
        }

        (await db.Auditorias.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Should_Not_Deduplicate_Across_Different_Resources()
    {
        // La deduplicacion no puede tapar un barrido: probar 3 recursos
        // distintos son 3 senales, no una repetida.
        var userId = Guid.NewGuid();
        await using var db = BuildDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var middleware = BuildMiddleware(StatusCodes.Status403Forbidden, cache);

        foreach (var id in new[] { "1", "2", "3" })
        {
            await middleware.InvokeAsync(BuildContext(db, $"/api/extractos/{id}", userId));
        }

        (await db.Auditorias.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Should_Ignore_Non_Api_Requests()
    {
        await using var db = BuildDbContext();
        var context = BuildContext(db, "/assets/index.js", usuarioId: null);

        await BuildMiddleware(StatusCodes.Status403Forbidden).InvokeAsync(context);

        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Should_Not_Break_The_Response_When_Auditing_Fails()
    {
        // La respuesta ya salio cuando se audita. Un fallo escribiendo la
        // auditoria no puede propagarse al cliente.
        var services = new ServiceCollection();
        services.AddScoped<IAuditService>(_ => new AuditServiceQueFalla());
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = "/api/extractos/1";
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        var accion = async () => await BuildMiddleware(StatusCodes.Status403Forbidden).InvokeAsync(context);

        await accion.Should().NotThrowAsync();
    }

    // --- helpers -----------------------------------------------------------

    private static SecurityAuditMiddleware BuildMiddleware(int statusCode, IMemoryCache? cache = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:Auditoria:UmbralAccesoBulk"] = "100",
            ["Security:Auditoria:VentanaDeduplicacionSegundos"] = "60"
        }).Build();

        return new SecurityAuditMiddleware(
            context =>
            {
                context.Response.StatusCode = statusCode;
                return Task.CompletedTask;
            },
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            configuration,
            NullLogger<SecurityAuditMiddleware>.Instance);
    }

    private static DefaultHttpContext BuildContext(AppDbContext db, string path, Guid? usuarioId, string query = "")
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IAuditService>(_ => new AuditService(
            db,
            new HttpContextAccessor(),
            new AuditSigner(new AuditSigningKey("clave-de-firma-de-pruebas-de-32-caracteres")),
            new SecurityEventLogNoOp()));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Method = "GET";

        if (usuarioId.HasValue)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, usuarioId.Value.ToString())],
                authenticationType: "Test"));
        }

        return context;
    }

    private static AppDbContext BuildDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class SecurityEventLogNoOp : ISecurityEventLog
    {
        public void RegistrarSiEsRelevante(Auditoria auditoria) { }
    }

    private sealed class AuditServiceQueFalla : IAuditService
    {
        public Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, HttpContext httpContext, string? detallesJson, CancellationToken cancellationToken)
            => throw new InvalidOperationException("BD caida");

        public Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, string? ipAddress, string? detallesJson, CancellationToken cancellationToken)
            => throw new InvalidOperationException("BD caida");
    }
}
