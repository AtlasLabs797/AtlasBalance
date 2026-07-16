using System.Security.Claims;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
}
