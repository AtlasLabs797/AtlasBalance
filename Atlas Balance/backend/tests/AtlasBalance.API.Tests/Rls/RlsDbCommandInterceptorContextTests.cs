using System.Security.Claims;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AtlasBalance.API.Tests.Rls;

// V-02-06 (RLS-UNIT-01): tests sin PostgreSQL que verifican que el interceptor
// deriva correctamente el scope del path, identifica usuarios autenticados,
// detecta el contexto de integracion y emite los modos anonimo/auth segun
// corresponda. La verificacion de las policies mismas requiere PostgreSQL
// (RowLevelSecurityTests); aqui solo se valida la construccion del contexto.
public sealed class RlsDbCommandInterceptorContextTests
{
    private const string Secret = "test-rls-context-placeholder-value-32-chars";

    private static RlsDbCommandInterceptor CreateInterceptor(IHttpContextAccessor accessor) =>
        new(accessor, new RlsContextSecret(Secret));

    [Fact]
    public void BuildContext_WithoutHttpContext_ShouldReturnSystem()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var interceptor = CreateInterceptor(accessor);

        var context = interceptor.BuildContext();

        context.AuthMode.Should().Be("system");
        context.IsSystem.Should().BeTrue();
        context.IsAdmin.Should().BeTrue();
        context.RequestScope.Should().Be("system");
    }

    [Fact]
    public void BuildContext_AnonymousRequestToNonAuthEndpoint_ShouldReturnAnonymous()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/dashboard/evolucion";
        var accessor = new HttpContextAccessor { HttpContext = http };
        var interceptor = CreateInterceptor(accessor);

        var context = interceptor.BuildContext();

        context.AuthMode.Should().Be("anonymous");
        context.IsAdmin.Should().BeFalse();
        context.RequestScope.Should().Be("anonymous");
    }

    [Fact]
    public void BuildContext_AnonymousRequestToAuthEndpoint_ShouldReturnAuthFlow()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/auth/login";
        var accessor = new HttpContextAccessor { HttpContext = http };
        var interceptor = CreateInterceptor(accessor);

        var context = interceptor.BuildContext();

        context.AuthMode.Should().Be("auth");
        context.RequestScope.Should().Be("auth");
    }

    [Fact]
    public void BuildContext_AuthenticatedReadOnDashboard_ShouldReturnDataScope()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/dashboard/principal";
        http.Request.Method = "GET";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test");
        http.User = new ClaimsPrincipal(identity);
        var accessor = new HttpContextAccessor { HttpContext = http };
        var interceptor = CreateInterceptor(accessor);

        var context = interceptor.BuildContext();

        context.AuthMode.Should().Be("user");
        context.UserId.Should().Be(userId.ToString());
        context.IsAdmin.Should().BeFalse();
        context.RequestScope.Should().Be("dashboard");
    }

    [Fact]
    public void BuildContext_AuthenticatedWriteOnExtractos_ShouldReturnWriteScope()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/extractos";
        http.Request.Method = "POST";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test");
        http.User = new ClaimsPrincipal(identity);
        var accessor = new HttpContextAccessor { HttpContext = http };
        var interceptor = CreateInterceptor(accessor);

        var context = interceptor.BuildContext();

        context.AuthMode.Should().Be("user");
        context.RequestScope.Should().Be("write");
    }

    [Fact]
    public void BuildContext_AuthenticatedReadOnRevisionPath_ShouldReturnRevisionScope()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/revision/extracto/abc";
        http.Request.Method = "GET";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test");
        http.User = new ClaimsPrincipal(identity);
        var accessor = new HttpContextAccessor { HttpContext = http };
        var interceptor = CreateInterceptor(accessor);

        var context = interceptor.BuildContext();

        context.RequestScope.Should().Be("revision");
    }

    [Theory]
    [InlineData("/api/conciliacion/sugerir", "reconcile")]
    [InlineData("/api/conciliacion/0f4e6f32-a12e-47ad-b92d-4c1f4cd7f23d/resolver", "reconcile-close")]
    public void BuildContext_ConciliacionPaths_ShouldUseTheDedicatedOperationScope(string path, string expectedScope)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.Method = "POST";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            authenticationType: "Test");
        http.User = new ClaimsPrincipal(identity);
        var interceptor = CreateInterceptor(new HttpContextAccessor { HttpContext = http });

        var context = interceptor.BuildContext();

        context.AuthMode.Should().Be("user");
        context.RequestScope.Should().Be(expectedScope);
    }

    [Fact]
    public void Di_Factory_Should_Resolve_AppDbContext_With_Internal_Interceptor_Secret_Type()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(new RlsContextSecret(Secret));
        services.AddScoped<RlsDbCommandInterceptor>(serviceProvider =>
            new RlsDbCommandInterceptor(
                serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                serviceProvider.GetRequiredService<RlsContextSecret>()));
        // V-02.07: el interceptor firma cada fila de auditoria, asi que el
        // contenedor tiene que poder resolver IAuditSigner igual que en Program.cs.
        services.AddSingleton(new AtlasBalance.API.Services.AuditSigningKey(TestAuditService.SigningKey));
        services.AddSingleton<AtlasBalance.API.Services.IAuditSigner, AtlasBalance.API.Services.AuditSigner>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(
                    serviceProvider.GetRequiredService<RlsDbCommandInterceptor>(),
                    serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<AppDbContext>().Should().NotBeNull();
    }

    [Fact]
    public async Task ReentryGuard_Should_Isolate_Parallel_Async_Flows()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            RlsDbCommandInterceptor.ReentryGuard.Enter();
            try
            {
                RlsDbCommandInterceptor.ReentryGuard.IsActive.Should().BeTrue();
                firstEntered.SetResult();
                await releaseFirst.Task;
                RlsDbCommandInterceptor.ReentryGuard.IsActive.Should().BeTrue();
            }
            finally
            {
                RlsDbCommandInterceptor.ReentryGuard.Exit();
            }
        });

        await firstEntered.Task;
        RlsDbCommandInterceptor.ReentryGuard.IsActive.Should().BeFalse();
        await Task.Run(() => RlsDbCommandInterceptor.ReentryGuard.IsActive.Should().BeFalse());

        releaseFirst.SetResult();
        await first;
        RlsDbCommandInterceptor.ReentryGuard.IsActive.Should().BeFalse();
    }
}
