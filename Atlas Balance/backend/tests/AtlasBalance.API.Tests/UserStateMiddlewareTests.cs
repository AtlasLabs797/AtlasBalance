using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class UserStateMiddlewareTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task InvokeAsync_Should_Reject_Token_When_SecurityStamp_Is_Stale()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "stale@test.local",
            NombreCompleto = "Stale User",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            SecurityStamp = "current-stamp"
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new UserStateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = BuildContext(user.Id, "old-stamp");

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_Continue_When_SecurityStamp_Matches()
    {
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "fresh@test.local",
            NombreCompleto = "Fresh User",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            SecurityStamp = "fresh-stamp"
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new UserStateMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        // Un administrador con stamp vigente tambien debe acreditar el MFA que
        // exigimos a todas las sesiones administrativas desde V-02.06.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var context = BuildContextWithMfa(user.Id, user.SecurityStamp, user.SecurityStamp, nowUnix);

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_Reject_Admin_Without_Mfa_Assurance()
    {
        // V-02.06: los administradores no pueden mantener una sesion API sin
        // haber pasado MFA. Un token heredado (sin claim mfa_verified_at) debe
        // ser rechazado.
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "no-mfa-admin@test.local",
            NombreCompleto = "No Mfa Admin",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            SecurityStamp = "fresh-stamp"
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new UserStateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = BuildContext(user.Id, user.SecurityStamp);

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_Accept_Admin_With_Mfa_Assurance_And_Stamp_Anchored()
    {
        // V-02.06: la garantia MFA del JWT debe estar anclada al security
        // stamp del usuario. Si el stamp rota, la garantia queda obsoleta.
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "ok-mfa-admin@test.local",
            NombreCompleto = "Mfa Admin",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            SecurityStamp = "fresh-stamp"
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new UserStateMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var context = BuildContextWithMfa(user.Id, user.SecurityStamp, user.SecurityStamp, nowUnix);

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Should_Accept_NonAdmin_Without_Mfa_Assurance()
    {
        // V-02.06: solo los administradores necesitan la marca MFA en el JWT.
        // Los gerentes y empleados pueden seguir navegando aunque la politica
        // operativa este apagada.
        await using var db = BuildDbContext();
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "no-mfa-empleado@test.local",
            NombreCompleto = "Empleado",
            PasswordHash = "hash",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            SecurityStamp = "fresh-stamp"
        };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new UserStateMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        var context = BuildContextWithRole(user.Id, user.SecurityStamp, nameof(RolUsuario.EMPLEADO));

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext BuildContext(Guid userId, string securityStamp)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/usuarios";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.ADMIN)),
            new Claim(AuthClaimNames.SecurityStamp, securityStamp)
        ], "TestAuth"));

        return context;
    }

    private static DefaultHttpContext BuildContextWithRole(Guid userId, string securityStamp, string role)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/usuarios";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(AuthClaimNames.SecurityStamp, securityStamp)
        ], "TestAuth"));

        return context;
    }

    private static DefaultHttpContext BuildContextWithMfa(Guid userId, string securityStamp, string mfaStamp, string mfaVerifiedAtUnix)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/usuarios";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.ADMIN)),
            new Claim(AuthClaimNames.SecurityStamp, securityStamp),
            new Claim(AuthClaimNames.MfaVerifiedAt, mfaVerifiedAtUnix),
            new Claim(AuthClaimNames.MfaSecurityStamp, mfaStamp)
        ], "TestAuth"));

        return context;
    }
}
