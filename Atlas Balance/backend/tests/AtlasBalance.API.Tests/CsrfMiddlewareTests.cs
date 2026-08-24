using AtlasBalance.API.Middleware;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class CsrfMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_Should_Return403_When_CsrfTokenInvalid()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        var csrf = new RejectingCsrfService();

        await middleware.InvokeAsync(context, csrf);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_NotThrow_When_UserAgent_Contains_CrLf()
    {
        var middleware = new CsrfMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext(
            "/api/usuarios",
            "POST",
            userAgent: "Mozilla/5.0\r\n2026-01-01 FAKE LOG ENTRY\r\n");
        var csrf = new RejectingCsrfService();

        var act = async () => await middleware.InvokeAsync(context, csrf);

        await act.Should().NotThrowAsync();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // V-02.06 (CodeQL #10): el middleware envuelve Request.Path con
    // LogScrubber.Scrub antes de loguearlo para evitar log forging
    // (CWE-117) por CRLF en la URL. Este test fija esa garantia
    // explicitamente sobre el path, no solo sobre el UA.
    [Fact]
    public async Task InvokeAsync_Should_NotThrow_When_RequestPath_Contains_CrLf()
    {
        var middleware = new CsrfMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext(
            "/api/usuarios\r\n2026-01-01 FAKE LOG ENTRY\r\n",
            "POST",
            userAgent: "Mozilla/5.0");
        var csrf = new RejectingCsrfService();

        var act = async () => await middleware.InvokeAsync(context, csrf);

        await act.Should().NotThrowAsync();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // V-02.07 (CodeQL #16): HttpRequest.Method es string, no enum. CodeQL
    // lo considera tainted aunque Kestrel normalice verbos validos, asi que
    // el middleware lo envuelve con LogScrubber.Scrub antes de loguearlo.
    // Este test fija esa garantia sobre el verbo, en paralelo al de UA y Path.
    [Fact]
    public async Task InvokeAsync_Should_NotThrow_When_Method_Contains_CrLf()
    {
        var middleware = new CsrfMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext(
            "/api/usuarios",
            "POST\r\n2026-01-01 FAKE LOG ENTRY\r\n",
            userAgent: "Mozilla/5.0");
        var csrf = new RejectingCsrfService();

        var act = async () => await middleware.InvokeAsync(context, csrf);

        await act.Should().NotThrowAsync();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_Should_CallNext_When_Method_IsGet()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "GET", userAgent: "Mozilla/5.0");
        var csrf = new RejectingCsrfService();

        await middleware.InvokeAsync(context, csrf);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_CallNext_When_Path_IsNotApi()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/health", "POST", userAgent: "Mozilla/5.0");
        var csrf = new RejectingCsrfService();

        await middleware.InvokeAsync(context, csrf);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_CallNext_When_Tokens_Match()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        // IRequestCookieCollection no expone Append; el middleware lee la
        // cookie via Request.Headers["Cookie"], asi que esa es la via
        // correcta para sembrar el valor en tests.
        context.Request.Headers.Append("Cookie", "csrf_token=valid-token");
        context.Request.Headers.Append("X-CSRF-Token", "valid-token");
        var csrf = new AcceptingCsrfService();

        await middleware.InvokeAsync(context, csrf);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_Accept_Standard_Base64_Cookie_With_Plus_Slash_And_Padding()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());
        var token = Convert.ToBase64String(Enumerable.Repeat((byte)0xfb, 32).ToArray());
        token.Should().Contain("+").And.Contain("/").And.EndWith("=");
        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Headers.Append("Cookie", $"__Host-atlas-csrf-token={token}");
        context.Request.Headers.Append("X-CSRF-Token", token);

        await middleware.InvokeAsync(context, new CsrfService());

        nextCalled.Should().BeTrue();
    }

    // V-02.07: verificacion de Origin/Referer. Se aplica a todo verbo mutador bajo /api
    // sin las exclusiones de ExcludedPaths, para cubrir tambien /api/auth/refresh-token,
    // que antes dependia unicamente de SameSite=Strict.
    [Fact]
    public async Task InvokeAsync_Should_Return403_When_Origin_IsCrossSite()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Host = new HostString("atlas.local");
        context.Request.Headers.Origin = "https://evil.example.com";

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_Return403_When_Origin_IsCrossSite_OnExcludedPath()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/auth/refresh-token", "POST", userAgent: "Mozilla/5.0");
        context.Request.Host = new HostString("atlas.local");
        context.Request.Headers.Origin = "https://evil.example.com";

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_CallNext_When_Origin_IsSameOrigin()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("atlas.local");
        context.Request.Headers.Origin = "https://atlas.local";
        context.Request.Headers.Append("Cookie", "csrf_token=valid-token");
        context.Request.Headers.Append("X-CSRF-Token", "valid-token");

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_Return403_When_Referer_IsCrossSite_And_Origin_Absent()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("atlas.local");
        context.Request.Headers.Referer = "https://evil.example.com/pagina";

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    // Sin Origin ni Referer no hay peticion de navegador cross-site: los navegadores
    // mandan Origin en todo verbo mutador. Se deja pasar para no romper clientes
    // no-navegador; el token CSRF sigue siendo obligatorio.
    [Fact]
    public async Task InvokeAsync_Should_CallNext_When_Origin_And_Referer_Absent()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Headers.Append("Cookie", "csrf_token=valid-token");
        context.Request.Headers.Append("X-CSRF-Token", "valid-token");

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_CallNext_When_ViteOrigin_InDevelopment()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment("Development"));

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Host = new HostString("localhost:5000");
        context.Request.Headers.Origin = "http://localhost:5173";
        context.Request.Headers.Append("Cookie", "csrf_token=valid-token");
        context.Request.Headers.Append("X-CSRF-Token", "valid-token");

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_Return403_When_ViteOrigin_OutsideDevelopment()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/usuarios", "POST", userAgent: "Mozilla/5.0");
        context.Request.Host = new HostString("localhost:5000");
        context.Request.Headers.Origin = "http://localhost:5173";

        await middleware.InvokeAsync(context, new AcceptingCsrfService());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    // V-02.08: la integracion OpenClaw se autentica con Bearer token (sin
    // cookies de sesion), asi que la validacion CSRF no aplica a su ruta.
    // Antes exigia cookie csrf_token + header a clientes Bearer-only.
    [Fact]
    public async Task InvokeAsync_Should_SkipCsrf_For_Integration_Paths()
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CsrfMiddleware>.Instance,
            new FakeWebHostEnvironment());

        var context = BuildContext("/api/integration/openclaw/resolver-nombres", "POST", userAgent: "OpenClaw/1.0");

        await middleware.InvokeAsync(context, new RejectingCsrfService());

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext BuildContext(string path, string method, string userAgent)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Append("User-Agent", userAgent);
        return context;
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string environmentName = "Production")
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "AtlasBalance.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RejectingCsrfService : ICsrfService
    {
        public string GenerateToken() => "rejected";
        public bool IsValid(string? cookieToken, string? headerToken) => false;
    }

    private sealed class AcceptingCsrfService : ICsrfService
    {
        public string GenerateToken() => "accepted";
        public bool IsValid(string? cookieToken, string? headerToken) =>
            !string.IsNullOrEmpty(cookieToken) && !string.IsNullOrEmpty(headerToken);
    }
}
