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

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Logout_Should_Keep_Trusted_Mfa_Cookie()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);

        var controller = new AuthController(
            new LogoutOnlyAuthService(),
            new CsrfService(),
            new TestWebHostEnvironment(),
            TestAuditService.Create(db));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "access_token=a; refresh_token=r; csrf_token=c; mfa_trusted=trusted";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Logout(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("access_token=");
        setCookie.Should().Contain("refresh_token=");
        setCookie.Should().Contain("csrf_token=");
        setCookie.Should().NotContain("mfa_trusted=");
    }

    [Fact]
    public async Task Logout_Should_Delete_HostPrefixed_Cookies_In_Production()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);

        var controller = new AuthController(
            new LogoutOnlyAuthService(),
            new CsrfService(),
            new TestWebHostEnvironment { EnvironmentName = "Production" },
            TestAuditService.Create(db));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            "__Host-atlas-access-token=a; __Host-atlas-refresh-token=r; __Host-atlas-csrf-token=c; __Host-atlas-mfa-trusted=trusted";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Logout(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("__Host-atlas-access-token=");
        setCookie.Should().Contain("__Host-atlas-refresh-token=");
        setCookie.Should().Contain("__Host-atlas-csrf-token=");
        setCookie.Should().NotContain("__Host-atlas-mfa-trusted=");
    }

    private sealed class LogoutOnlyAuthService : IAuthService
    {
        public Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null, string? userAgent = null) =>
            throw new NotSupportedException();

        public Task<AuthResult> VerifyMfaAsync(string challengeId, string code, bool rememberDevice, string? ipAddress, CancellationToken cancellationToken, string? userAgent = null) =>
            throw new NotSupportedException();

        public Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

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

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "AtlasBalance.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
