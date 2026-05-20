using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
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
    public async Task Logout_Should_Not_Delete_Trusted_Mfa_Cookie()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);

        var controller = new AuthController(
            new LogoutOnlyAuthService(),
            new CsrfService(),
            new TestWebHostEnvironment(),
            new AuditService(db));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "access_token=a; refresh_token=r; csrf_token=c; mfa_trusted=trusted";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Logout(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("access_token=");
        setCookie.Should().Contain("refresh_token=");
        setCookie.Should().Contain("csrf_token=");
        setCookie.Should().NotContain("mfa_trusted");
    }

    private sealed class LogoutOnlyAuthService : IAuthService
    {
        public Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null) =>
            throw new NotSupportedException();

        public Task<AuthResult> VerifyMfaAsync(string challengeId, string code, bool rememberDevice, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, CancellationToken cancellationToken) =>
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
