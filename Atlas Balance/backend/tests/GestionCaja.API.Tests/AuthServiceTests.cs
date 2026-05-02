using System.Text;
using FluentAssertions;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GestionCaja.API.Tests;

public class AuthServiceTests
{
    private static IConfiguration BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
            ["JwtSettings:AccessTokenExpMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpDays"] = "7",
            ["Security:RequireMfaForWebUsers"] = "false"
        })
        .Build();

    private static IConfiguration BuildMfaConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
            ["JwtSettings:AccessTokenExpMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpDays"] = "7",
            ["Security:RequireMfaForWebUsers"] = "true"
        })
        .Build();

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Login_Should_Lock_Account_On_Fifth_Bad_Password()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "lock@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Lock User",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = true,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));

        for (var i = 1; i <= 4; i++)
        {
            Func<Task> action = () => sut.LoginAsync(user.Email, "BadPass!", "127.0.0.1", CancellationToken.None);
            var exception = await action.Should().ThrowAsync<AuthException>();
            exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        Func<Task> fifthAttempt = () => sut.LoginAsync(user.Email, "BadPass!", "127.0.0.1", CancellationToken.None);
        var locked = await fifthAttempt.Should().ThrowAsync<AuthException>();
        locked.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        locked.Which.Message.Should().Be("Credenciales inválidas");

        var persisted = await db.Usuarios.FirstAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(5);
        persisted.LockedUntil.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_Should_Not_Reveal_When_User_Is_Already_Locked()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "already-locked@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Already Locked",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            LockedUntil = DateTime.UtcNow.AddMinutes(20),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));

        Func<Task> action = () => sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AuthException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        exception.Which.Message.Should().Be("Credenciales inválidas");
    }

    [Fact]
    public async Task Login_Should_Return_Tokens_And_Reset_Lock_Counters_When_Password_Is_Valid()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "ok@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Ok User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = true,
            FailedLoginAttempts = 3,
            LockedUntil = DateTime.UtcNow.AddMinutes(-1),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Usuario.Email.Should().Be(user.Email);

        var persisted = await db.Usuarios.FirstAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(0);
        persisted.LockedUntil.Should().BeNull();

        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(result.RefreshToken!))).ToLowerInvariant();
        (await db.RefreshTokens.AnyAsync(x => x.TokenHash == tokenHash)).Should().BeTrue();
    }

    [Fact]
    public async Task Login_Should_Require_Mfa_Setup_When_Mfa_Is_Enabled()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-setup@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Setup",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector());

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
        result.MfaRequired.Should().BeTrue();
        result.MfaSetupRequired.Should().BeTrue();
        result.MfaChallengeId.Should().NotBeNullOrWhiteSpace();
        result.MfaSecret.Should().NotBeNullOrWhiteSpace();
        result.MfaOtpAuthUri.Should().Contain("otpauth://totp/");
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == GestionCaja.API.Constants.AuditActions.LoginMfaRequired)).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyMfa_Should_Enable_Mfa_And_Issue_Tokens()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-verify@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Verify",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);

        var result = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, "127.0.0.1", CancellationToken.None);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Usuario.MfaEnabled.Should().BeTrue();

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.MfaEnabled.Should().BeTrue();
        persisted.MfaSecret.Should().NotBeNullOrWhiteSpace();
        persisted.MfaEnabledAt.Should().NotBeNull();
        persisted.MfaLastAcceptedStep.Should().NotBeNull();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == GestionCaja.API.Constants.AuditActions.MfaVerified)).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyMfa_Should_Lock_User_Across_New_Challenges_After_Repeated_Failures()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-lock@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Lock",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector());

        for (var i = 1; i <= 5; i++)
        {
            var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
            Func<Task> invalidMfa = () => sut.VerifyMfaAsync(login.MfaChallengeId!, "not-code", "127.0.0.1", CancellationToken.None);
            var exception = await invalidMfa.Should().ThrowAsync<AuthException>();
            exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.LockedUntil.Should().BeAfter(DateTime.UtcNow);
        persisted.FailedLoginAttempts.Should().Be(5);

        Func<Task> lockedLogin = () => sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var locked = await lockedLogin.Should().ThrowAsync<AuthException>();
        locked.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Login_Should_Not_Require_Mfa_Again_When_Trusted_Mfa_Cookie_Is_Valid()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-trusted@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Trusted",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, "127.0.0.1", CancellationToken.None);

        var trustedLogin = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, verified.TrustedMfaToken);

        verified.TrustedMfaToken.Should().NotBeNullOrWhiteSpace();
        verified.TrustedMfaTokenExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(89));
        trustedLogin.MfaRequired.Should().BeFalse();
        trustedLogin.AccessToken.Should().NotBeNullOrWhiteSpace();
        trustedLogin.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_Should_Require_Mfa_When_Trusted_Mfa_Cookie_Is_Expired()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-expired@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Expired",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var expiredTrustedToken = BuildTrustedMfaTokenForTest(user, DateTime.UtcNow.AddSeconds(-1));
        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector());

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, expiredTrustedToken);

        result.MfaRequired.Should().BeTrue();
        result.MfaSetupRequired.Should().BeFalse();
        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task ChangePassword_Should_Update_Hash_And_Clear_PrimerLogin()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "pwd@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!Ab", workFactor: 12),
            NombreCompleto = "Pwd User",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = true,
            FechaCreacion = DateTime.UtcNow
        };

        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var originalStamp = user.SecurityStamp;
        var result = await sut.ChangePasswordAsync(user.Id, "OldPass123!Ab", "NewPass12345!", "127.0.0.1", CancellationToken.None);

        var persisted = await db.Usuarios.FirstAsync(x => x.Id == user.Id);
        BCrypt.Net.BCrypt.Verify("NewPass12345!", persisted.PasswordHash).Should().BeTrue();
        persisted.PrimerLogin.Should().BeFalse();
        persisted.SecurityStamp.Should().NotBe(originalStamp);
        persisted.PasswordChangedAt.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RefreshToken_Should_Reject_Locked_User()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "refresh-locked@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Refresh Locked",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        await db.SaveChangesAsync();

        Func<Task> action = () => sut.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1", CancellationToken.None);
        var exception = await action.Should().ThrowAsync<AuthException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status423Locked);
    }

    [Fact]
    public async Task ChangePassword_Should_Revoke_Previous_Refresh_Tokens_And_Issue_New_One()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "rotate@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!Ab", workFactor: 12),
            NombreCompleto = "Rotate User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "OldPass123!Ab", "127.0.0.1", CancellationToken.None);
        var previousHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(login.RefreshToken!))).ToLowerInvariant();

        var changed = await sut.ChangePasswordAsync(user.Id, "OldPass123!Ab", "NewPass12345!", "127.0.0.1", CancellationToken.None);
        var newHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(changed.RefreshToken!))).ToLowerInvariant();

        var previousToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == previousHash);
        previousToken.RevocadoEn.Should().NotBeNull();

        var newToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == newHash);
        newToken.RevocadoEn.Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_Should_Revoke_Active_Sessions_When_Rotated_Token_Is_Reused()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "reuse@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Reuse User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var stampAfterLogin = (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp;

        var refreshed = await sut.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1", CancellationToken.None);
        var replacementHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(refreshed.RefreshToken!))).ToLowerInvariant();

        Func<Task> reuse = () => sut.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1", CancellationToken.None);
        var exception = await reuse.Should().ThrowAsync<AuthException>();

        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        var replacementToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == replacementHash);
        replacementToken.RevocadoEn.Should().NotBeNull();
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.SecurityStamp.Should().NotBe(stampAfterLogin);
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == GestionCaja.API.Constants.AuditActions.RefreshTokenReuseDetected)).Should().BeTrue();
    }

    [Fact]
    public async Task Logout_Should_Revoke_Refresh_Token_And_Return_UserId()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "logout@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Logout User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        var revokedUserId = await sut.LogoutAsync(login.RefreshToken, CancellationToken.None);

        revokedUserId.Should().Be(user.Id);
        (await db.RefreshTokens.SingleAsync()).RevocadoEn.Should().NotBeNull();
    }

    private static string BuildTrustedMfaTokenForTest(Usuario usuario, DateTime expiresAtUtc)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = "v1",
            UserId = usuario.Id,
            SecurityStamp = usuario.SecurityStamp,
            ExpiresAtUnix = new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()
        });
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes("test-secret-key-minimum-32-characters-long"));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64)));
        return $"{payloadBase64}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
