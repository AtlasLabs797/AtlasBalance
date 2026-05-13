using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class IntegracionesControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IntegracionesController BuildController(AppDbContext dbContext)
    {
        var controller = new IntegracionesController(
            dbContext,
            new AuditService(dbContext),
            new IntegrationTokenService(dbContext));

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

    [Fact]
    public async Task Crear_Should_Reject_Token_Without_Scope_Permissions()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);

        var result = await controller.Crear(new CreateIntegrationTokenRequest
        {
            Nombre = "sin-scope",
            PermisoLectura = true,
            PermisoEscritura = false,
            Permisos = []
        }, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(badRequest.Value).Should().Contain("al menos un permiso de alcance");
    }

    [Fact]
    public async Task Crear_Should_Reject_Invalid_Access_Type_Or_Missing_Global_Permission()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);

        var invalidAccessType = await controller.Crear(new CreateIntegrationTokenRequest
        {
            Nombre = "invalid-access",
            PermisoLectura = true,
            PermisoEscritura = false,
            Permisos =
            [
                new SaveIntegrationPermissionRequest { AccesoTipo = "admin" }
            ]
        }, CancellationToken.None);

        var invalidAccessTypeBadRequest = invalidAccessType.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(invalidAccessTypeBadRequest.Value)
            .Should()
            .Contain("lectura")
            .And.Contain("escritura");

        var invalidWriteScope = await controller.Crear(new CreateIntegrationTokenRequest
        {
            Nombre = "invalid-write-scope",
            PermisoLectura = true,
            PermisoEscritura = false,
            Permisos =
            [
                new SaveIntegrationPermissionRequest { AccesoTipo = "escritura" }
            ]
        }, CancellationToken.None);

        var invalidWriteScopeBadRequest = invalidWriteScope.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(invalidWriteScopeBadRequest.Value)
            .Should()
            .Contain("no permite escritura");
    }

    [Fact]
    public async Task Crear_Should_Normalize_And_Persist_Access_Type()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);

        var result = await controller.Crear(new CreateIntegrationTokenRequest
        {
            Nombre = "global-read",
            PermisoLectura = true,
            PermisoEscritura = false,
            Permisos =
            [
                new SaveIntegrationPermissionRequest { AccesoTipo = " Lectura " }
            ]
        }, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var storedPermission = await dbContext.IntegrationPermissions.SingleAsync();
        storedPermission.AccesoTipo.Should().Be("lectura");
    }
}
