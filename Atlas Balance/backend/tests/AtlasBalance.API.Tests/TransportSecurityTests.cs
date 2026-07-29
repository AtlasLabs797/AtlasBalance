using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.07: las cookies de sesion llevan prefijo __Host- en produccion. El
// navegador RECHAZA una cookie __Host- que no lleve exactamente Path=/, sin
// Domain y con Secure. BuildCookieOptions no asigna Path explicitamente (se
// apoya en el default "/" de CookieOptions), asi que el invariante queda aqui
// fijado: si alguien toca esas opciones y el Path deja de ser "/", el login
// entero se rompe en produccion de forma silenciosa.
public sealed class TransportSecurityTests
{
    [Fact]
    public async Task RefreshToken_In_Production_Should_Emit_HostPrefixed_Cookies_With_Secure_Flags()
    {
        var setCookie = await RefreshAndCaptureSetCookieAsync("Production");

        var accessCookie = SingleCookie(setCookie, "__Host-atlas-access-token");
        accessCookie.Should().Contain("path=/", "una cookie __Host- sin Path=/ es rechazada por el navegador");
        accessCookie.Should().Contain("secure");
        accessCookie.Should().Contain("httponly");
        accessCookie.Should().Contain("samesite=strict");
        accessCookie.Should().NotContain("domain=", "una cookie __Host- no puede llevar atributo Domain");

        var refreshCookie = SingleCookie(setCookie, "__Host-atlas-refresh-token");
        refreshCookie.Should().Contain("path=/");
        refreshCookie.Should().Contain("secure");
        refreshCookie.Should().Contain("httponly");
        refreshCookie.Should().Contain("samesite=strict");
        refreshCookie.Should().NotContain("domain=");
    }

    [Fact]
    public async Task RefreshToken_In_Production_Should_Emit_Csrf_Cookie_Readable_By_Script_But_Secure()
    {
        var setCookie = await RefreshAndCaptureSetCookieAsync("Production");

        // El patron es double-submit: el frontend tiene que poder leer el token
        // para reenviarlo en X-CSRF-Token, asi que HttpOnly no aplica aqui. Lo
        // que si es obligatorio es Secure + SameSite=Strict + Path=/.
        var csrfCookie = SingleCookie(setCookie, "__Host-atlas-csrf-token");
        csrfCookie.Should().Contain("path=/");
        csrfCookie.Should().Contain("secure");
        csrfCookie.Should().Contain("samesite=strict");
        csrfCookie.Should().NotContain("httponly");
        csrfCookie.Should().NotContain("domain=");
    }

    private static string SingleCookie(string setCookieHeader, string cookieName)
    {
        var match = setCookieHeader
            .Split('\n')
            .SelectMany(chunk => SplitCookies(chunk))
            .FirstOrDefault(cookie => cookie.TrimStart().StartsWith(cookieName + "=", StringComparison.Ordinal));

        match.Should().NotBeNull($"la respuesta debe traer la cookie {cookieName}. Header completo: {setCookieHeader}");
        return match!.ToLowerInvariant();
    }

    // Set-Cookie llega como varios valores concatenados por ", " en el
    // ToString() de la coleccion de headers. Expires usa comas dentro del
    // valor ("Tue, 05 Aug 2026..."), asi que solo cortamos en las comas que
    // arrancan un nombre=valor nuevo.
    private static IEnumerable<string> SplitCookies(string raw)
    {
        var current = new List<char>();
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index] == ',' && StartsNewCookie(raw, index + 1))
            {
                yield return new string(current.ToArray());
                current.Clear();
                continue;
            }

            current.Add(raw[index]);
        }

        yield return new string(current.ToArray());
    }

    private static bool StartsNewCookie(string raw, int index)
    {
        while (index < raw.Length && raw[index] == ' ')
        {
            index++;
        }

        var equalsIndex = raw.IndexOf('=', index);
        if (equalsIndex < 0)
        {
            return false;
        }

        var name = raw[index..equalsIndex];
        return name.Length > 0 && !name.Contains(' ', StringComparison.Ordinal);
    }

    private static async Task<string> RefreshAndCaptureSetCookieAsync(string environmentName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);

        var controller = new AuthController(
            new RefreshOnlyAuthService(),
            new CsrfService(),
            new TransportTestWebHostEnvironment { EnvironmentName = environmentName },
            new AuditService(db));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "__Host-atlas-refresh-token=old-refresh";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.RefreshToken(CancellationToken.None);
        result.Should().BeOfType<OkObjectResult>();

        return httpContext.Response.Headers.SetCookie.ToString();
    }

    private sealed class RefreshOnlyAuthService : IAuthService
    {
        public Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthResult
            {
                AccessToken = "new-access-token",
                RefreshToken = "new-refresh-token"
            });

        public Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null, string? userAgent = null) =>
            throw new NotSupportedException();

        public Task<AuthResult> VerifyMfaAsync(string challengeId, string code, bool rememberDevice, string? ipAddress, CancellationToken cancellationToken, string? userAgent = null) =>
            throw new NotSupportedException();

        public Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, string? currentRefreshToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TrustedMfaDeviceResponse>> GetTrustedMfaDevicesAsync(Guid userId, string? currentTrustedMfaToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RevokeTrustedMfaDeviceAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RevokeCurrentTrustedMfaDeviceAsync(Guid userId, string? currentTrustedMfaToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TransportTestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "AtlasBalance.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
