using System.Text;
using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

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

    private static IConfiguration BuildRequireNonAdminMfaConfig(bool requireNonAdmin) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
            ["JwtSettings:AccessTokenExpMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpDays"] = "7",
            ["Security:RequireMfaForWebUsers"] = (requireNonAdmin ? "true" : "false")
        })
        .Build();

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static CacheService BuildCacheService(AppDbContext db) =>
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance);

    private static IOptions<CachingOptions> BuildCachingOptions() =>
        Options.Create(new CachingOptions());

    private static IOptions<RateLimitingOptions> BuildRateLimitingOptions(
        Action<RateLimitingOptions>? configure = null)
    {
        var options = new RateLimitingOptions();
        configure?.Invoke(options);
        return Options.Create(options);
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

        // El throttle por (IP,email) se aparta a proposito: este fact cubre el
        // bloqueo de cuenta persistido en BD, y con el valor real (3) el 429
        // cortaria antes de que el contador llegase al umbral de bloqueo.
        var rateLimiting = BuildRateLimitingOptions(o =>
        {
            o.LoginMaxFailuresPerIpAndEmail = int.MaxValue;
            o.LoginMaxFailuresPerIp = int.MaxValue;
        });
        var maxAttempts = rateLimiting.Value.LoginMaxFailedAttemptsPerAccount;
        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), rateLimiting);

        for (var i = 1; i < maxAttempts; i++)
        {
            Func<Task> action = () => sut.LoginAsync(user.Email, "BadPass!", "127.0.0.1", CancellationToken.None);
            var exception = await action.Should().ThrowAsync<AuthException>();
            exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        Func<Task> lockingAttempt = () => sut.LoginAsync(user.Email, "BadPass!", "127.0.0.1", CancellationToken.None);
        var locked = await lockingAttempt.Should().ThrowAsync<AuthException>();
        locked.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        locked.Which.Message.Should().Be("Credenciales inválidas");

        var persisted = await db.Usuarios.FirstAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(maxAttempts);
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

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());

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
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = true,
            FailedLoginAttempts = 3,
            LockedUntil = DateTime.UtcNow.AddMinutes(-1),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());

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
    public async Task Login_Should_Allow_Valid_Credentials_When_Client_Wide_Failure_Limit_Was_Reached()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("shared-ip-victim@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var rateLimiting = BuildRateLimitingOptions();
        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), rateLimiting, cache);
        const string sharedIp = "10.10.10.10";
        // Derivado de la configuracion, no hardcodeado: cada email es distinto,
        // asi que el unico contador que cuenta aqui es el de IP.
        var clientLimit = rateLimiting.Value.LoginMaxFailuresPerIp;

        for (var i = 0; i < clientLimit; i++)
        {
            Func<Task> invalidLogin = () => sut.LoginAsync(
                $"spray-{i}@test.local",
                "BadPass!",
                sharedIp,
                CancellationToken.None);

            var exception = await invalidLogin.Should().ThrowAsync<AuthException>();
            exception.Which.StatusCode.Should().Be(i == clientLimit - 1
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status401Unauthorized);
        }

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", sharedIp, CancellationToken.None);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Usuario.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task Login_Should_Clear_Client_Wide_Failures_After_Successful_Login()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("client-cleanup@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var rateLimiting = BuildRateLimitingOptions();
        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), rateLimiting, cache);
        const string sharedIp = "10.10.10.11";
        // Un fallo por debajo del umbral de IP: todos deben seguir siendo 401.
        var belowClientLimit = rateLimiting.Value.LoginMaxFailuresPerIp - 1;

        for (var i = 0; i < belowClientLimit; i++)
        {
            Func<Task> invalidLogin = () => sut.LoginAsync(
                $"before-success-{i}@test.local",
                "BadPass!",
                sharedIp,
                CancellationToken.None);

            var exception = await invalidLogin.Should().ThrowAsync<AuthException>();
            exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", sharedIp, CancellationToken.None);
        result.AccessToken.Should().NotBeNullOrWhiteSpace();

        Func<Task> nextInvalidLogin = () => sut.LoginAsync(
            "after-success@test.local",
            "BadPass!",
            sharedIp,
            CancellationToken.None);

        var afterSuccessException = await nextInvalidLogin.Should().ThrowAsync<AuthException>();
        afterSuccessException.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
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

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
        result.MfaRequired.Should().BeTrue();
        result.MfaSetupRequired.Should().BeTrue();
        result.MfaChallengeId.Should().NotBeNullOrWhiteSpace();
        result.MfaSecret.Should().NotBeNullOrWhiteSpace();
        result.MfaOtpAuthUri.Should().Contain("otpauth://totp/");
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AtlasBalance.API.Constants.AuditActions.LoginMfaRequired)).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshToken_Should_Reject_PreMfa_Token_When_Mfa_Becomes_Required()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "pre-mfa-refresh@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Pre Mfa Refresh",
            // EMPLEADO para que BuildConfig (MFA off) emita tokens. Los
            // administradores quedan excluidos porque V-02.06 los obliga
            // a MFA siempre, asi que no pueden emitir un refresh token
            // pre-MFA.
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var preMfaSut = new AuthService(db, BuildConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var preMfaLogin = await preMfaSut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        preMfaLogin.MfaRequired.Should().BeFalse();
        preMfaLogin.RefreshToken.Should().NotBeNullOrWhiteSpace();
        var preMfaRefreshHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(preMfaLogin.RefreshToken!))).ToLowerInvariant();

        var mfaSut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var mfaLogin = await mfaSut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        mfaLogin.MfaRequired.Should().BeTrue();
        mfaLogin.AccessToken.Should().BeNull();
        mfaLogin.RefreshToken.Should().BeNull();

        Func<Task> refresh = () => mfaSut.RefreshTokenAsync(preMfaLogin.RefreshToken!, "127.0.0.1", CancellationToken.None);

        var exception = await refresh.Should().ThrowAsync<AuthException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var storedToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == preMfaRefreshHash);
        storedToken.RevocadoEn.Should().NotBeNull();
        (await db.RefreshTokens.CountAsync()).Should().Be(1);
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

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);

        var result = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, false, "127.0.0.1", CancellationToken.None);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Usuario.MfaEnabled.Should().BeTrue();
        result.TrustedMfaToken.Should().BeNull();

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.MfaEnabled.Should().BeTrue();
        persisted.MfaSecret.Should().NotBeNullOrWhiteSpace();
        persisted.MfaEnabledAt.Should().NotBeNull();
        persisted.MfaLastAcceptedStep.Should().NotBeNull();
        var refreshHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(result.RefreshToken!))).ToLowerInvariant();
        var refreshToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == refreshHash);
        refreshToken.MfaVerifiedAt.Should().NotBeNull();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AtlasBalance.API.Constants.AuditActions.MfaVerified)).Should().BeTrue();

        var nextLogin = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        nextLogin.MfaRequired.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshToken_Should_Preserve_Mfa_Assurance_After_Verified_Mfa()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-refresh@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Refresh",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "true" });
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, false, "127.0.0.1", CancellationToken.None);
        var verifiedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(verified.RefreshToken!))).ToLowerInvariant();
        var verifiedToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == verifiedHash);

        var refreshed = await sut.RefreshTokenAsync(verified.RefreshToken!, "127.0.0.1", CancellationToken.None);
        var refreshedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(refreshed.RefreshToken!))).ToLowerInvariant();
        var replacementToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == refreshedHash);

        verified.TrustedMfaToken.Should().BeNull();
        verifiedToken.MfaVerifiedAt.Should().NotBeNull();
        verifiedToken.RevocadoEn.Should().NotBeNull();
        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBeNullOrWhiteSpace();
        replacementToken.MfaVerifiedAt.Should().Be(verifiedToken.MfaVerifiedAt);
        replacementToken.RevocadoEn.Should().BeNull();
    }

    [Fact]
    public async Task ChangePassword_Should_Reject_PreMfa_Session_When_Mfa_Is_Required()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-change-pre-session@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!Ab", workFactor: 12),
            NombreCompleto = "Mfa Change Pre Session",
            // EMPLEADO porque queremos emitir un refresh token sin garantia MFA
            // (con BuildConfig). V-02.06 obliga a administradores a usar
            // Authenticator, asi que no pueden tener sesiones pre-MFA.
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var preMfaSut = new AuthService(db, BuildConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var preMfaLogin = await preMfaSut.LoginAsync(user.Email, "OldPass123!Ab", "127.0.0.1", CancellationToken.None);
        preMfaLogin.MfaRequired.Should().BeFalse();

        var mfaSut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        Func<Task> changePassword = () => mfaSut.ChangePasswordAsync(
            user.Id,
            "OldPass123!Ab",
            "NewPass12345!",
            "127.0.0.1",
            preMfaLogin.RefreshToken,
            CancellationToken.None);

        var exception = await changePassword.Should().ThrowAsync<AuthException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        BCrypt.Net.BCrypt.Verify("OldPass123!Ab", persisted.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("NewPass12345!", persisted.PasswordHash).Should().BeFalse();
        (await db.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ChangePassword_Should_Reject_PreMfa_Setup_Session_When_Mfa_Is_Required()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-change-setup-session@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!Ab", workFactor: 12),
            NombreCompleto = "Mfa Change Setup Session",
            // EMPLEADO para que BuildConfig (MFA off) emita tokens. Los
            // administradores quedan excluidos porque V-02.06 los obliga
            // a MFA siempre.
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var preMfaSut = new AuthService(db, BuildConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var preMfaLogin = await preMfaSut.LoginAsync(user.Email, "OldPass123!Ab", "127.0.0.1", CancellationToken.None);
        preMfaLogin.MfaRequired.Should().BeFalse();
        preMfaLogin.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var mfaSut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        Func<Task> changePassword = () => mfaSut.ChangePasswordAsync(
            user.Id,
            "OldPass123!Ab",
            "NewPass12345!",
            "127.0.0.1",
            preMfaLogin.RefreshToken,
            CancellationToken.None);

        var exception = await changePassword.Should().ThrowAsync<AuthException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        BCrypt.Net.BCrypt.Verify("OldPass123!Ab", persisted.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("NewPass12345!", persisted.PasswordHash).Should().BeFalse();
        (await db.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ChangePassword_Should_Preserve_Mfa_Assurance_For_Verified_Session()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-change-verified@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!Ab", workFactor: 12),
            NombreCompleto = "Mfa Change Verified",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "OldPass123!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, false, "127.0.0.1", CancellationToken.None);
        var verifiedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(verified.RefreshToken!))).ToLowerInvariant();
        var verifiedToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == verifiedHash);
        var verifiedAt = verifiedToken.MfaVerifiedAt;

        var changed = await sut.ChangePasswordAsync(user.Id, "OldPass123!Ab", "NewPass12345!", "127.0.0.1", verified.RefreshToken, CancellationToken.None);
        var changedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(changed.RefreshToken!))).ToLowerInvariant();
        var replacementToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == changedHash);

        verifiedAt.Should().NotBeNull();
        verifiedToken.RevocadoEn.Should().NotBeNull();
        replacementToken.MfaVerifiedAt.Should().Be(verifiedAt);
        replacementToken.RevocadoEn.Should().BeNull();
        changed.AccessToken.Should().NotBeNullOrWhiteSpace();
        changed.RefreshToken.Should().NotBeNullOrWhiteSpace();
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

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());

        for (var i = 1; i <= 5; i++)
        {
            var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
            Func<Task> invalidMfa = () => sut.VerifyMfaAsync(login.MfaChallengeId!, "not-code", false, "127.0.0.1", CancellationToken.None);
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
    public async Task Login_Should_Require_Mfa_When_Trusted_Mfa_Cookie_Is_Present_But_Admin_Disables_Remember_Device()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-trusted-disabled@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Trusted Disabled",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "false" });
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, "opaque-token");

        result.MfaRequired.Should().BeTrue();
        result.MfaRememberDeviceAllowed.Should().BeFalse();
        result.ClearTrustedMfaToken.Should().BeTrue();
        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task VerifyMfa_Should_Not_Issue_Trusted_Mfa_Token_When_Admin_Disables_Remember_Device()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-remember-disabled@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Remember Disabled",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "false" });
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);

        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, true, "127.0.0.1", CancellationToken.None);

        login.MfaRememberDeviceAllowed.Should().BeFalse();
        verified.TrustedMfaToken.Should().BeNull();
        verified.TrustedMfaTokenExpiresAt.Should().BeNull();
        verified.ClearTrustedMfaToken.Should().BeTrue();
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
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "true" });
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, true, "127.0.0.1", CancellationToken.None, "UnitTest Browser");

        var trustedLogin = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, verified.TrustedMfaToken);

        login.MfaRememberDeviceAllowed.Should().BeTrue();
        verified.TrustedMfaToken.Should().NotBeNullOrWhiteSpace();
        verified.TrustedMfaTokenExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(89));
        verified.TrustedMfaTokenExpiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(91));
        var trustedDevice = await db.MfaTrustedDevices.SingleAsync(x => x.UsuarioId == user.Id);
        trustedDevice.TokenHash.Should().NotBe(verified.TrustedMfaToken);
        trustedDevice.UserAgentSummary.Should().Be("UnitTest Browser");
        trustedLogin.MfaRequired.Should().BeFalse();
        trustedLogin.AccessToken.Should().NotBeNullOrWhiteSpace();
        trustedLogin.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_Should_Require_Mfa_When_Trusted_Mfa_Device_Is_Revoked()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-trusted-revoked@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Trusted Revoked",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "true" });
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, true, "127.0.0.1", CancellationToken.None);
        var devices = await sut.GetTrustedMfaDevicesAsync(user.Id, verified.TrustedMfaToken, CancellationToken.None);

        var revoked = await sut.RevokeTrustedMfaDeviceAsync(user.Id, devices.Single().Id, CancellationToken.None);
        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, verified.TrustedMfaToken);

        revoked.Should().BeTrue();
        result.MfaRequired.Should().BeTrue();
        result.ClearTrustedMfaToken.Should().BeTrue();
    }

    [Fact]
    public async Task Login_Should_Require_Mfa_When_Trusted_Mfa_Token_Was_Revoked_By_SecurityStamp_Rotation()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-revoked@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Mfa Revoked",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "true" });
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, true, "127.0.0.1", CancellationToken.None);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        UserSessionState.RotateSecurityStamp(persisted);
        await db.SaveChangesAsync();

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, verified.TrustedMfaToken);

        result.MfaRequired.Should().BeTrue();
        result.MfaSetupRequired.Should().BeFalse();
        result.ClearTrustedMfaToken.Should().BeTrue();
        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
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
        db.Configuraciones.Add(new Configuracion { Clave = SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey, Valor = "true" });
        await db.SaveChangesAsync();

        var expiredTrustedToken = SeedTrustedMfaDevice(db, user, DateTime.UtcNow.AddSeconds(-1));
        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());

        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None, expiredTrustedToken);

        result.MfaRequired.Should().BeTrue();
        result.MfaSetupRequired.Should().BeFalse();
        result.ClearTrustedMfaToken.Should().BeTrue();
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

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var originalStamp = user.SecurityStamp;
        var result = await sut.ChangePasswordAsync(user.Id, "OldPass123!Ab", "NewPass12345!", "127.0.0.1", null, CancellationToken.None);

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
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
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
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "OldPass123!Ab", "127.0.0.1", CancellationToken.None);
        var previousHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(login.RefreshToken!))).ToLowerInvariant();

        var changed = await sut.ChangePasswordAsync(user.Id, "OldPass123!Ab", "NewPass12345!", "127.0.0.1", login.RefreshToken, CancellationToken.None);
        var newHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(changed.RefreshToken!))).ToLowerInvariant();

        var previousToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == previousHash);
        previousToken.RevocadoEn.Should().NotBeNull();

        var newToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == newHash);
        newToken.RevocadoEn.Should().BeNull();
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        newToken.SecurityStamp.Should().Be(persisted.SecurityStamp);
    }

    [Fact]
    public async Task RefreshToken_Should_Reject_Token_From_Previous_SecurityStamp()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "refresh-stamp@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Refresh Stamp User",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var refreshHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(login.RefreshToken!))).ToLowerInvariant();
        var storedToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == refreshHash);
        var tokenStamp = storedToken.SecurityStamp;

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        UserSessionState.RotateSecurityStamp(persisted);
        await db.SaveChangesAsync();

        Func<Task> refresh = () => sut.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1", CancellationToken.None);
        var exception = await refresh.Should().ThrowAsync<AuthException>();

        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        var revokedToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == refreshHash);
        revokedToken.SecurityStamp.Should().Be(tokenStamp);
        revokedToken.RevocadoEn.Should().NotBeNull();
        (await db.RefreshTokens.CountAsync()).Should().Be(1);
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
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
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
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AtlasBalance.API.Constants.AuditActions.RefreshTokenReuseDetected)).Should().BeTrue();
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
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        var revokedUserId = await sut.LogoutAsync(login.RefreshToken, CancellationToken.None);

        revokedUserId.Should().Be(user.Id);
        (await db.RefreshTokens.SingleAsync()).RevocadoEn.Should().NotBeNull();
    }

    // V-02.07: la invariante real no es "tarda algo", es que la rama de email
    // inexistente cueste lo MISMO que la de password incorrecta. Por eso el test
    // compara ambas en vez de fijar un umbral absoluto, que podria seguir en verde
    // por cualquier otra lentitud ajena. Se calienta primero para no medir la
    // inicializacion estatica de DummyPasswordHash ni el JIT. El margen del 50% es
    // amplio: ambas rutas estan dominadas por el mismo BCrypt (~250 ms), asi que el
    // cociente real ronda 1.
    [Fact]
    public async Task Login_Should_Cost_The_Same_Whether_Or_Not_The_Email_Exists()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("timing@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());

        // Calentamiento: fuerza la inicializacion estatica y el JIT de BCrypt.
        await Assert.ThrowsAsync<AuthException>(() =>
            sut.LoginAsync(user.Email, "WrongPassword!1", "10.99.0.7", CancellationToken.None));

        var knownEmail = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<AuthException>(() =>
            sut.LoginAsync(user.Email, "WrongPassword!2", "10.99.0.7", CancellationToken.None));
        knownEmail.Stop();

        var unknownEmail = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<AuthException>(() =>
            sut.LoginAsync("no-existe@test.local", "WrongPassword!2", "10.99.0.7", CancellationToken.None));
        unknownEmail.Stop();

        unknownEmail.ElapsedMilliseconds.Should().BeGreaterThan(
            (long)(knownEmail.ElapsedMilliseconds * 0.5),
            "un email inexistente debe pagar el mismo BCrypt que uno existente con password incorrecta");
    }

    // V-02.07: rehash oportunista. Una cuenta con un hash de work factor antiguo
    // debe migrar sola en su siguiente login correcto.
    [Fact]
    public async Task Login_Should_Rehash_A_Password_Stored_With_An_Older_Work_Factor()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("rehash@test.local");
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 10);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        BCrypt.Net.BCrypt.PasswordNeedsRehash(persisted.PasswordHash, 12).Should().BeFalse();
        BCrypt.Net.BCrypt.Verify("Valid1234!Ab", persisted.PasswordHash).Should().BeTrue();
    }

    // V-02.07: el cambio de IP entre emision y uso del refresh token se audita,
    // pero NO invalida la sesion: atarla a la IP expulsaria a usuarios legitimos
    // con VPN, DHCP o salto de red.
    [Fact]
    public async Task RefreshToken_Should_Audit_An_Ip_Change_Without_Closing_The_Session()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("ip-change@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.0.0.1", CancellationToken.None);

        var refreshed = await sut.RefreshTokenAsync(login.RefreshToken!, "10.0.0.99", CancellationToken.None);

        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.SessionIpChanged)).Should().BeTrue();
    }

    // V-02.07: la misma maquina puede llegar como 10.0.0.1 (X-Forwarded-For) o como
    // ::ffff:10.0.0.1 (socket dual-mode). No debe generar una alerta de cambio de IP.
    [Fact]
    public async Task RefreshToken_Should_Not_Audit_When_Only_The_Ipv4_Mapping_Differs()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("ip-mapped@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "::ffff:10.0.0.1", CancellationToken.None);

        await sut.RefreshTokenAsync(login.RefreshToken!, "10.0.0.1", CancellationToken.None);

        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.SessionIpChanged)).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_Should_Not_Audit_When_The_Ip_Is_Unchanged()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("ip-same@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.0.0.1", CancellationToken.None);

        await sut.RefreshTokenAsync(login.RefreshToken!, "10.0.0.1", CancellationToken.None);

        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.SessionIpChanged)).Should().BeFalse();
    }

    // V-02.07: logout rota el security stamp. Sin esta rotacion el access token
    // JWT ya emitido seguia siendo aceptado por UserStateMiddleware hasta 1h
    // despues de cerrar sesion, porque logout solo borraba cookies del navegador.
    [Fact]
    public async Task Logout_Should_Rotate_Security_Stamp_And_Revoke_Every_Active_Session()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("logout-stamp@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var sessionA = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var sessionB = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.2", CancellationToken.None);
        var stampBeforeLogout = (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp;

        var revokedUserId = await sut.LogoutAsync(sessionA.RefreshToken, CancellationToken.None);

        revokedUserId.Should().Be(user.Id);
        (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp.Should().NotBe(stampBeforeLogout);
        (await db.RefreshTokens.Where(rt => rt.UsuarioId == user.Id).ToListAsync())
            .Should().OnlyContain(rt => rt.RevocadoEn != null);

        // La otra sesion queda cerrada tambien: es el "cerrar sesion en todas partes".
        await Assert.ThrowsAsync<AuthException>(() =>
            sut.RefreshTokenAsync(sessionB.RefreshToken!, "127.0.0.2", CancellationToken.None));
    }

    // V-02.07: la rotacion del stamp no debe cancelar el recuerdo MFA del
    // navegador. Los dispositivos de confianza estan anclados al stamp, asi que
    // logout los re-ancla al nuevo. Protege el comportamiento fijado en V-01.09
    // ("logout conserva la cookie mfa_trusted").
    [Fact]
    public async Task Logout_Should_Keep_Trusted_Mfa_Devices_Anchored_To_The_New_Stamp()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("logout-trusted@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var session = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var loggedIn = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        SeedTrustedMfaDevice(db, loggedIn, DateTime.UtcNow.AddDays(30));

        await sut.LogoutAsync(session.RefreshToken, CancellationToken.None);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        var device = await db.MfaTrustedDevices.SingleAsync(x => x.UsuarioId == user.Id);
        device.RevokedAt.Should().BeNull();
        device.SecurityStamp.Should().Be(persisted.SecurityStamp);
    }

    // V-02.07: el re-anclaje de logout NO debe resucitar dispositivos que ya
    // quedaron huerfanos por una rotacion anterior (cambio de contrasena, reset
    // por admin, reuso de refresh). Esos dispositivos deben seguir exigiendo MFA:
    // si un logout rutinario posterior los readoptara, cambiar la contrasena por
    // sospecha de robo dejaria de expulsar al dispositivo del atacante.
    [Fact]
    public async Task Logout_Should_Not_Revive_Trusted_Devices_Orphaned_By_A_Password_Change()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("logout-orphan@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        // Dispositivo confiado anclado al stamp vigente en ese momento.
        var loggedIn = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        SeedTrustedMfaDevice(db, loggedIn, DateTime.UtcNow.AddDays(30));
        var orphanedStamp = loggedIn.SecurityStamp;

        // El cambio de contrasena rota el stamp y deja el dispositivo huerfano.
        var afterChange = await sut.ChangePasswordAsync(
            user.Id, "Valid1234!Ab", "BrandNew!Password9", "127.0.0.1", null, CancellationToken.None);

        // Un logout rutinario posterior no debe readoptarlo.
        await sut.LogoutAsync(afterChange.RefreshToken, CancellationToken.None);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        var device = await db.MfaTrustedDevices.SingleAsync(x => x.UsuarioId == user.Id);
        device.SecurityStamp.Should().Be(orphanedStamp);
        device.SecurityStamp.Should().NotBe(persisted.SecurityStamp);
    }

    // V-02.07: un refresh token ya revocado no autoriza otra rotacion. Si la
    // autorizase, cualquiera con una copia antigua podria expulsar al usuario
    // legitimo de forma repetida.
    [Fact]
    public async Task Logout_Should_Ignore_An_Already_Revoked_Refresh_Token()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("logout-replay@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var session = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        await sut.LogoutAsync(session.RefreshToken, CancellationToken.None);
        var stampAfterLogout = (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp;

        var secondAttempt = await sut.LogoutAsync(session.RefreshToken, CancellationToken.None);

        secondAttempt.Should().BeNull();
        (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp.Should().Be(stampAfterLogout);
    }

    // V-02.07: verificar passwordActual comparte el lockout del login. Antes no
    // contaba intentos ni auditaba el fallo, asi que una sesion robada permitia
    // fuerza bruta ilimitada y silenciosa sobre la contrasena actual.
    [Fact]
    public async Task ChangePassword_Should_Lock_Account_After_Repeated_Bad_Current_Password()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("change-lock@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await Assert.ThrowsAsync<AuthException>(() => sut.ChangePasswordAsync(
                user.Id, "WrongCurrent!123", "BrandNew!Password9", "127.0.0.1", null, CancellationToken.None));
        }

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(5);
        persisted.LockedUntil.Should().NotBeNull();
        (await db.Auditorias.CountAsync(x => x.TipoAccion == AuditActions.LoginFailed)).Should().Be(5);
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.AccountLocked)).Should().BeTrue();
    }

    // V-02.07: mientras la cuenta esta bloqueada no se acepta el cambio ni con la
    // contrasena actual correcta; si no, el bloqueo seria trivial de sortear.
    [Fact]
    public async Task ChangePassword_Should_Reject_While_Account_Is_Locked()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("change-locked@test.local");
        user.FailedLoginAttempts = 5;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());

        var ex = await Assert.ThrowsAsync<AuthException>(() => sut.ChangePasswordAsync(
            user.Id, "Valid1234!Ab", "BrandNew!Password9", "127.0.0.1", null, CancellationToken.None));

        ex.StatusCode.Should().Be(StatusCodes.Status423Locked);
        (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).PasswordHash.Should().Be(user.PasswordHash);
    }

    // V-02.07: misma mitigacion de latencia que en LoginAsync. Sin el señuelo, la
    // rama de cuenta bloqueada salia al instante y delataba el estado de la cuenta
    // frente a los ~250 ms de "password actual incorrecta".
    [Fact]
    public async Task ChangePassword_Should_Cost_The_Same_When_The_Account_Is_Locked()
    {
        await using var db = BuildDbContext();
        var locked = BuildActiveUser("change-timing-locked@test.local");
        locked.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        var unlocked = BuildActiveUser("change-timing-open@test.local");
        db.Usuarios.AddRange(locked, unlocked);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());

        // Calentamiento.
        await Assert.ThrowsAsync<AuthException>(() => sut.ChangePasswordAsync(
            unlocked.Id, "WrongCurrent!1", "BrandNew!Password9", "10.99.0.8", null, CancellationToken.None));

        var badPassword = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<AuthException>(() => sut.ChangePasswordAsync(
            unlocked.Id, "WrongCurrent!2", "BrandNew!Password9", "10.99.0.8", null, CancellationToken.None));
        badPassword.Stop();

        var lockedAccount = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<AuthException>(() => sut.ChangePasswordAsync(
            locked.Id, "WrongCurrent!2", "BrandNew!Password9", "10.99.0.8", null, CancellationToken.None));
        lockedAccount.Stop();

        lockedAccount.ElapsedMilliseconds.Should().BeGreaterThan(
            (long)(badPassword.ElapsedMilliseconds * 0.5),
            "una cuenta bloqueada no debe distinguirse por latencia de una password incorrecta");
    }

    private static string SeedTrustedMfaDevice(AppDbContext db, Usuario usuario, DateTime expiresAtUtc)
    {
        var token = $"trusted-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        db.MfaTrustedDevices.Add(new MfaTrustedDevice
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(),
            SecurityStamp = usuario.SecurityStamp,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = expiresAtUtc
        });
        db.SaveChanges();
        return token;
    }

    private static Usuario BuildActiveUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
        NombreCompleto = "Active User",
        // V-02.06: los tests que usan BuildConfig (MFA apagado) ya no pueden
        // usar ADMIN porque ahora los administradores siempre requieren
        // Authenticator. EMPLEADO respeta la politica configurable y permite
        // probar el flujo de login directo sin desafio MFA.
        Rol = RolUsuario.EMPLEADO,
        Activo = true,
        PrimerLogin = false,
        FechaCreacion = DateTime.UtcNow
    };

    // V-02.06: matriz admin/no-admin x politica. Cubre que la politica por rol
    // siempre obliga a administradores, independientemente de la configuracion
    // operativa, y que los no administradores siguen la clave almacenada.
    [Theory]
    [InlineData(RolUsuario.ADMIN, true)]
    [InlineData(RolUsuario.ADMIN, false)]
    [InlineData(RolUsuario.GERENTE, true)]
    [InlineData(RolUsuario.EMPLEADO, false)]
    public async Task Login_Should_Apply_Mfa_Policy_Per_Role(RolUsuario rol, bool requireNonAdmin)
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = $"policy-{rol}-{(requireNonAdmin ? "on" : "off")}@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Policy User",
            Rol = rol,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        if (rol != RolUsuario.ADMIN)
        {
            // Para no administradores, sembramos la clave operativa y
            // verificamos que el rol la respeta.
            db.Configuraciones.Add(new Configuracion
            {
                Clave = SecurityConfigurationDefaults.MfaRequireForNonAdminUsersKey,
                Valor = requireNonAdmin ? "true" : "false"
            });
        }
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildRequireNonAdminMfaConfig(requireNonAdmin), new AuditService(db), new PlainTextSecretProtector(), BuildCacheService(db), BuildCachingOptions(), BuildRateLimitingOptions());
        var result = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);

        if (rol == RolUsuario.ADMIN)
        {
            // Admin siempre MFA, nunca tokens directos.
            result.AccessToken.Should().BeNull();
            result.RefreshToken.Should().BeNull();
            result.MfaRequired.Should().BeTrue();
        }
        else if (requireNonAdmin)
        {
            result.MfaRequired.Should().BeTrue();
            result.AccessToken.Should().BeNull();
        }
        else
        {
            result.MfaRequired.Should().BeFalse();
            result.AccessToken.Should().NotBeNullOrWhiteSpace();
        }

        // Durante el desafio no se devuelve el perfil completo: MfaRequired
        // en la raiz es el contrato que dirige el siguiente paso del cliente.
    }

    [Fact]
    public async Task Login_Should_Reject_Admin_Session_When_Stale_Mfa_Challenge()
    {
        // V-02.06: si el security stamp rota durante el desafio MFA, el
        // verify debe rechazarlo aunque el codigo TOTP sea valido.
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-stale-admin@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Stale Admin",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var enrolledSecret = user.MfaSecret!;
        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(enrolledSecret, DateTime.UtcNow);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        UserSessionState.RotateSecurityStamp(persisted);
        await db.SaveChangesAsync();

        Func<Task> verify = () => sut.VerifyMfaAsync(login.MfaChallengeId!, code, false, "127.0.0.1", CancellationToken.None);
        var exception = await verify.Should().ThrowAsync<AuthException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        exception.Which.Message.Should().Be("Codigo MFA invalido o expirado");
    }

    [Fact]
    public async Task Login_Should_Keep_Admin_Assurance_After_Verified_Mfa()
    {
        // V-02.06: un admin que completa MFA obtiene un access token con la
        // marca de MFA verificado. Eso es lo que UserStateMiddleware usa
        // para permitir acceso administrativo sin re-login.
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa-assured-admin@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            NombreCompleto = "Assured Admin",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var enrolledSecret = user.MfaSecret!;
        var sut = new AuthService(db, BuildMfaConfig(), new AuditService(db), secretProtector: new PlainTextSecretProtector(), cacheService: BuildCacheService(db), cachingOptions: BuildCachingOptions(), rateLimitingOptions: BuildRateLimitingOptions());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "127.0.0.1", CancellationToken.None);
        var code = TotpService.GenerateCode(enrolledSecret, DateTime.UtcNow);
        var verified = await sut.VerifyMfaAsync(login.MfaChallengeId!, code, false, "127.0.0.1", CancellationToken.None);

        verified.AccessToken.Should().NotBeNullOrWhiteSpace();

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(verified.AccessToken!);
        jwt.Claims.Should().Contain(c => c.Type == AuthClaimNames.MfaVerifiedAt);
        jwt.Claims.Should().Contain(c => c.Type == AuthClaimNames.MfaSecurityStamp);
        jwt.Claims.First(c => c.Type == AuthClaimNames.MfaSecurityStamp).Value.Should().Be(user.SecurityStamp);
    }

}
