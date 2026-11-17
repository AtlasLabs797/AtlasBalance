using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// Lote 2 del informe de revision: hallazgos A-2, S-10, S-11, S-13 y S-15 del
/// nucleo de autenticacion.
/// </summary>
public class AuthLot2Tests
{
    private static IConfiguration BuildConfig(int maxSessionDays = 30) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
            ["JwtSettings:AccessTokenExpMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpDays"] = "7",
            ["JwtSettings:MaxSessionDays"] = maxSessionDays.ToString(),
            ["Security:RequireMfaForWebUsers"] = "false"
        })
        .Build();

    private static IConfiguration BuildMfaConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
            ["JwtSettings:AccessTokenExpMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpDays"] = "7",
            ["JwtSettings:MaxSessionDays"] = "30",
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

    private static AuthService BuildSut(
        AppDbContext db,
        IConfiguration? configuration = null,
        RateLimitingOptions? rateLimiting = null,
        IMemoryCache? cache = null) =>
        new(
            db,
            configuration ?? BuildConfig(),
            TestAuditService.Create(db),
            new PlainTextSecretProtector(),
            BuildCacheService(db),
            BuildCachingOptions(),
            Options.Create(rateLimiting ?? new RateLimitingOptions()),
            cache);

    private static Usuario BuildActiveUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
        NombreCompleto = "Lot2 User",
        Rol = RolUsuario.EMPLEADO,
        Activo = true,
        PrimerLogin = false,
        FechaCreacion = DateTime.UtcNow
    };

    private static string HashToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    // ---------------------------------------------------------------------
    // A-2: el bloqueo por intentos fallidos solo impide logins nuevos.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Login_Should_Reset_The_Failed_Attempts_When_The_Previous_Lock_Expired()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-login-reset@test.local");
        user.FailedLoginAttempts = 5;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var rateLimiting = new RateLimitingOptions
        {
            LoginMaxFailuresPerIpAndEmail = int.MaxValue,
            LoginMaxFailuresPerIp = int.MaxValue
        };
        var sut = BuildSut(db, rateLimiting: rateLimiting);

        Func<Task> action = () => sut.LoginAsync(user.Email, "BadPass!", "10.20.30.40", CancellationToken.None);
        await action.Should().ThrowAsync<AuthException>();

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(1, "el bloqueo caducado debe reiniciar el contador antes de contar el fallo nuevo");
        persisted.LockedUntil.Should().BeNull("un solo fallo no puede volver a bloquear la cuenta");
    }

    [Fact]
    public async Task VerifyMfa_Should_Reset_The_Failed_Attempts_When_The_Previous_Lock_Expired()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-mfa-reset@test.local");
        user.Rol = RolUsuario.ADMIN;
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db, BuildMfaConfig());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.41", CancellationToken.None);
        login.MfaChallengeId.Should().NotBeNullOrWhiteSpace();

        // El bloqueo anterior caduca entre el login y la verificacion MFA.
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts = 5;
        persisted.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Func<Task> verify = () => sut.VerifyMfaAsync(login.MfaChallengeId!, "111111", false, "10.20.30.41", CancellationToken.None);
        await verify.Should().ThrowAsync<AuthException>();

        var after = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        after.FailedLoginAttempts.Should().Be(0, "el bloqueo caducado debe reiniciar el contador");
        after.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ChangePassword_Should_Reset_The_Failed_Attempts_When_The_Previous_Lock_Expired()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-change-reset@test.local");
        user.FailedLoginAttempts = 5;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db);

        Func<Task> action = () => sut.ChangePasswordAsync(
            user.Id, "WrongCurrent!123", "BrandNew!Password9", "10.20.30.42", null, CancellationToken.None);
        await action.Should().ThrowAsync<AuthException>();

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(1, "el bloqueo caducado debe reiniciar el contador antes de contar el fallo nuevo");
        persisted.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_Should_Keep_The_Session_Alive_When_The_Account_Is_Locked()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-refresh-locked@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.43", CancellationToken.None);

        user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        await db.SaveChangesAsync();

        var refreshed = await sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.43", CancellationToken.None);

        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UserStateMiddleware_Should_Accept_A_Locked_User()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-middleware-locked@test.local");
        user.SecurityStamp = "fresh-stamp";
        user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new UserStateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, NullLogger<UserStateMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/usuarios";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.EMPLEADO)),
            new Claim(AuthClaimNames.SecurityStamp, user.SecurityStamp)
        ], "TestAuth"));

        await middleware.InvokeAsync(context, db);

        nextCalled.Should().BeTrue("el bloqueo solo impide logins nuevos, no corta las sesiones ya abiertas");
    }

    [Fact]
    public async Task Desbloquear_Should_Clear_The_Counter_And_The_Lock()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-desbloquear@test.local");
        user.FailedLoginAttempts = 5;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var controller = BuildUsuariosController(db);
        var result = await controller.Desbloquear(user.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        ReadPayloadProperty(((OkObjectResult)result).Value!, "message").Should().Be("Usuario desbloqueado");

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(0);
        persisted.LockedUntil.Should().BeNull();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.AccountUnlocked && x.EntidadId == user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Desbloquear_Should_Return_404_When_The_User_Does_Not_Exist()
    {
        await using var db = BuildDbContext();
        var controller = BuildUsuariosController(db);

        var result = await controller.Desbloquear(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
        ReadPayloadProperty(((NotFoundObjectResult)result).Value!, "error").Should().Be("Usuario no encontrado");
    }

    [Fact]
    public async Task Desbloquear_Should_Return_400_When_The_User_Is_Not_Locked()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-desbloquear-ok@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var controller = BuildUsuariosController(db);
        var result = await controller.Desbloquear(user.Id, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        ReadPayloadProperty(((BadRequestObjectResult)result).Value!, "error").Should().Be("El usuario no esta bloqueado");
    }

    [Fact]
    public async Task Listar_Should_Expose_The_Lockout_Fields()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("a2-listado@test.local");
        user.FailedLoginAttempts = 3;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var controller = BuildUsuariosController(db);
        var result = await controller.Listar(page: 1, pageSize: 20, sortBy: "fecha_creacion", sortDir: "desc", search: null, incluirEliminados: false, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var paginated = (PaginatedResponse<UsuarioListItemResponse>)ok.Value!;
        var item = paginated.Data.Should().ContainSingle().Subject;
        item.IntentosFallidos.Should().Be(3);
        item.BloqueadoHasta.Should().NotBeNull();

        // Contrato de JSON para el frontend: snake_case.
        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        json.Should().Contain("bloqueado_hasta");
        json.Should().Contain("intentos_fallidos");
    }

    // ---------------------------------------------------------------------
    // S-10: el alta de MFA no puede sobrescribir el secreto de otra via.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task VerifyMfa_Should_Not_Overwrite_An_Existing_Mfa_Secret()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("s10-mfa-sobrewrite@test.local");
        user.Rol = RolUsuario.ADMIN;
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db, BuildMfaConfig());
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.44", CancellationToken.None);
        var code = TotpService.GenerateCode(login.MfaSecret!, DateTime.UtcNow);

        // Mientras el desafio estaba abierto, la victima ya tiene MFA (challenge obsoleto).
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.MfaEnabled = true;
        persisted.MfaSecret = "secreto-de-la-victima";
        persisted.MfaEnabledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Func<Task> verify = () => sut.VerifyMfaAsync(login.MfaChallengeId!, code, false, "10.20.30.44", CancellationToken.None);
        var exception = await verify.Should().ThrowAsync<AuthException>();

        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        exception.Which.Message.Should().Be("Codigo MFA invalido o expirado");

        var after = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        after.MfaSecret.Should().Be("secreto-de-la-victima", "el secreto vigente no se puede sobrescribir desde un desafio obsoleto");
    }

    // ---------------------------------------------------------------------
    // S-11: limite por IP tambien para cuentas existentes.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Login_Should_Return_429_When_The_Ip_Exceeds_The_Existing_User_Limit()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("s11-ip-existing@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var rateLimiting = new RateLimitingOptions
        {
            LoginMaxFailuresPerIpAndEmail = int.MaxValue,
            LoginMaxFailuresPerIp = int.MaxValue,
            LoginMaxFailuresPerIpExistingUser = 3
        };
        var sut = BuildSut(db, rateLimiting: rateLimiting, cache: new MemoryCache(new MemoryCacheOptions()));

        for (var i = 0; i < 3; i++)
        {
            Func<Task> failed = () => sut.LoginAsync(user.Email, "BadPass!", "10.20.30.45", CancellationToken.None);
            var failure = await failed.Should().ThrowAsync<AuthException>();
            failure.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        Func<Task> throttled = () => sut.LoginAsync(user.Email, "BadPass!", "10.20.30.45", CancellationToken.None);
        var exception = await throttled.Should().ThrowAsync<AuthException>();

        exception.Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        exception.Which.RetryAfterSeconds.Should().NotBeNull();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.LoginFailed &&
                                            x.DetallesJson != null && x.DetallesJson.Contains("rate_limited"))).Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // S-13: vida maxima de sesion.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RefreshToken_Should_Fail_When_The_Session_Expired()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("s13-sesion-caducada@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db, BuildConfig(maxSessionDays: 30));
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.46", CancellationToken.None);

        var stored = await db.RefreshTokens.SingleAsync(x => x.TokenHash == HashToken(login.RefreshToken!));
        stored.SesionIniEn = DateTime.UtcNow.AddDays(-31);
        await db.SaveChangesAsync();

        Func<Task> refresh = () => sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.46", CancellationToken.None);
        var exception = await refresh.Should().ThrowAsync<AuthException>();

        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        exception.Which.Message.Should().Be("La sesion ha caducado. Vuelve a iniciar sesion.");

        var tokens = await db.RefreshTokens.Where(x => x.UsuarioId == user.Id).ToListAsync();
        tokens.Should().OnlyContain(x => x.RevocadoEn != null);
    }

    [Fact]
    public async Task RefreshToken_Should_Keep_The_Session_Anchor_And_Id_When_Rotating()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("s13-ancla@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.47", CancellationToken.None);
        var original = await db.RefreshTokens.SingleAsync(x => x.TokenHash == HashToken(login.RefreshToken!));

        var refreshed = await sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.47", CancellationToken.None);
        var replacement = await db.RefreshTokens.SingleAsync(x => x.TokenHash == HashToken(refreshed.RefreshToken!));

        original.SesionIniEn.Should().NotBeNull();
        replacement.SessionId.Should().Be(original.SessionId);
        replacement.SesionIniEn.Should().Be(original.SesionIniEn);
    }

    // ---------------------------------------------------------------------
    // S-15: ventana de gracia para el reuso benigno del refresh token.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RefreshToken_Should_Issue_A_New_Token_When_The_Rotated_Token_Is_Reused_Inside_The_Grace_Window()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("s15-benigno@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.48", CancellationToken.None);
        var stampAfterLogin = (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp;
        var otherSession = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.49", CancellationToken.None);

        var rotated = await sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.48", CancellationToken.None);

        // Segunda pestana que presenta el token viejo todavia dentro de la ventana.
        var benign = await sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.48", CancellationToken.None);

        benign.AccessToken.Should().NotBeNullOrWhiteSpace();
        benign.RefreshToken.Should().NotBeNullOrWhiteSpace();
        benign.RefreshToken.Should().NotBe(rotated.RefreshToken);

        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.SecurityStamp.Should().Be(stampAfterLogin, "un reintento benigno no es un robo: no rota el stamp");

        var otherRefreshed = await sut.RefreshTokenAsync(otherSession.RefreshToken!, "10.20.30.49", CancellationToken.None);
        otherRefreshed.RefreshToken.Should().NotBeNullOrWhiteSpace("las demas sesiones siguen vivas");

        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.RefreshTokenReuseBenign)).Should().BeTrue();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.RefreshTokenReuseDetected)).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_Should_Revoke_Every_Session_When_The_Rotated_Token_Is_Reused_Outside_The_Grace_Window()
    {
        await using var db = BuildDbContext();
        var user = BuildActiveUser("s15-robo@test.local");
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var login = await sut.LoginAsync(user.Email, "Valid1234!Ab", "10.20.30.50", CancellationToken.None);
        var stampAfterLogin = (await db.Usuarios.SingleAsync(x => x.Id == user.Id)).SecurityStamp;

        await sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.50", CancellationToken.None);

        // La rotacion ocurrio hace 5 minutos: fuera de la ventana de gracia.
        var stored = await db.RefreshTokens.SingleAsync(x => x.TokenHash == HashToken(login.RefreshToken!));
        stored.RevocadoEn = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        Func<Task> reuse = () => sut.RefreshTokenAsync(login.RefreshToken!, "10.20.30.50", CancellationToken.None);
        var exception = await reuse.Should().ThrowAsync<AuthException>();

        exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.SecurityStamp.Should().NotBe(stampAfterLogin);
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.RefreshTokenReuseDetected)).Should().BeTrue();
        (await db.Auditorias.AnyAsync(x => x.TipoAccion == AuditActions.RefreshTokenReuseBenign)).Should().BeFalse();
    }

    private static UsuariosController BuildUsuariosController(AppDbContext db)
    {
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "ADMIN")
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    /// <summary>
    /// Lee una propiedad del payload anonimo que devuelve el controller. El
    /// contrato (claves y textos) es lo que consume el frontend, asi que se
    /// comprueba literalmente y no solo el tipo de la respuesta.
    /// </summary>
    private static string? ReadPayloadProperty(object payload, string propertyName)
    {
        var property = payload.GetType().GetProperty(propertyName);
        return property?.GetValue(payload)?.ToString();
    }
}
