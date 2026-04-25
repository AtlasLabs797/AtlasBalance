using System.Security.Claims;
using FluentAssertions;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.Middleware;
using GestionCaja.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionCaja.API.Tests;

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

        var context = BuildContext(user.Id, user.SecurityStamp);

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
}
