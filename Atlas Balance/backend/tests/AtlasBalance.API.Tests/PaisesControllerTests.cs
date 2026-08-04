using System.Security.Claims;
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

public sealed class PaisesControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Listar_Should_Ignore_ActivosFalse_For_NonAdmin()
    {
        await using var db = BuildDbContext();
        db.Paises.AddRange(
            new Pais { Id = Guid.NewGuid(), Nombre = "Activo", CodigoIso2 = "AC", Activo = true },
            new Pais { Id = Guid.NewGuid(), Nombre = "Inactivo", CodigoIso2 = "IN", Activo = false });
        await db.SaveChangesAsync();

        var controller = BuildController(db, RolUsuario.GERENTE);

        var result = await controller.Listar(activos: false, cancellationToken: CancellationToken.None);

        var paises = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<PaisResponse>>().Subject;
        paises.Should().ContainSingle();
        paises.Single().Nombre.Should().Be("Activo");
    }

    private static PaisesController BuildController(AppDbContext db, RolUsuario role)
    {
        var controller = new PaisesController(db, TestAuditService.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
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
}
