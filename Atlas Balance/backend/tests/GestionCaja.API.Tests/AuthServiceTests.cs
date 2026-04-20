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
            ["JwtSettings:RefreshTokenExpDays"] = "7"
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
    public async Task Login_Should_Lock_User_After_Five_Failed_Attempts()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "lock@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!", workFactor: 12),
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
        locked.Which.StatusCode.Should().Be(StatusCodes.Status423Locked);

        var persisted = await db.Usuarios.FirstAsync(x => x.Id == user.Id);
        persisted.FailedLoginAttempts.Should().Be(5);
        persisted.LockedUntil.Should().NotBeNull();
        persisted.LockedUntil.Should().BeAfter(DateTime.UtcNow.AddMinutes(29));
    }

    [Fact]
    public async Task Login_Should_Return_Tokens_And_Reset_Lock_Counters_When_Password_Is_Valid()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "ok@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!", workFactor: 12),
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

        var result = await sut.LoginAsync(user.Email, "Valid1234!", "127.0.0.1", CancellationToken.None);

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
    public async Task ChangePassword_Should_Update_Hash_And_Clear_PrimerLogin()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "pwd@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!", workFactor: 12),
            NombreCompleto = "Pwd User",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = true,
            FechaCreacion = DateTime.UtcNow
        };

        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var result = await sut.ChangePasswordAsync(user.Id, "OldPass123!", "NewPass123!", "127.0.0.1", CancellationToken.None);

        var persisted = await db.Usuarios.FirstAsync(x => x.Id == user.Id);
        BCrypt.Net.BCrypt.Verify("NewPass123!", persisted.PasswordHash).Should().BeTrue();
        persisted.PrimerLogin.Should().BeFalse();
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!", workFactor: 12),
            NombreCompleto = "Refresh Locked",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "Valid1234!", "127.0.0.1", CancellationToken.None);

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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!", workFactor: 12),
            NombreCompleto = "Rotate User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "OldPass123!", "127.0.0.1", CancellationToken.None);
        var previousHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(login.RefreshToken!))).ToLowerInvariant();

        var changed = await sut.ChangePasswordAsync(user.Id, "OldPass123!", "NewPass123!", "127.0.0.1", CancellationToken.None);
        var newHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(changed.RefreshToken!))).ToLowerInvariant();

        var previousToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == previousHash);
        previousToken.RevocadoEn.Should().NotBeNull();

        var newToken = await db.RefreshTokens.SingleAsync(x => x.TokenHash == newHash);
        newToken.RevocadoEn.Should().BeNull();
    }

    [Fact]
    public async Task Logout_Should_Revoke_Refresh_Token_And_Return_UserId()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "logout@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!", workFactor: 12),
            NombreCompleto = "Logout User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var sut = new AuthService(db, BuildConfig(), new AuditService(db));
        var login = await sut.LoginAsync(user.Email, "Valid1234!", "127.0.0.1", CancellationToken.None);

        var revokedUserId = await sut.LogoutAsync(login.RefreshToken, CancellationToken.None);

        revokedUserId.Should().Be(user.Id);
        (await db.RefreshTokens.SingleAsync()).RevocadoEn.Should().NotBeNull();
    }
}
