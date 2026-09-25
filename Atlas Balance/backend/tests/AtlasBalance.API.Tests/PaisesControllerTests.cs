using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var paisActivo = new Pais { Id = Guid.NewGuid(), Nombre = "Activo", CodigoIso2 = "AC", Activo = true };
        db.Paises.AddRange(
            paisActivo,
            new Pais { Id = Guid.NewGuid(), Nombre = "Inactivo", CodigoIso2 = "IN", Activo = false });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, PaisId = paisActivo.Id, Nombre = "Cuenta", Divisa = "EUR" });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisActivo.Id,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(activos: false, cancellationToken: CancellationToken.None);

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<PaginatedResponse<PaisResponse>>().Subject;
        payload.Total.Should().Be(1);
        payload.Data.Should().ContainSingle();
        payload.Data.Single().Nombre.Should().Be("Activo");
    }

    [Fact]
    public async Task Listar_Should_Derive_Countries_From_Effective_Account_Scope()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var paisEspana = new Pais { Id = Guid.NewGuid(), Nombre = "España", CodigoIso2 = "ES", Activo = true };
        var paisFrancia = new Pais { Id = Guid.NewGuid(), Nombre = "Francia", CodigoIso2 = "FR", Activo = true };
        var paisPortugal = new Pais { Id = Guid.NewGuid(), Nombre = "Portugal", CodigoIso2 = "PT", Activo = true };
        var titularA = new Titular { Id = Guid.NewGuid(), Nombre = "Titular A", Tipo = TipoTitular.EMPRESA };
        var titularB = new Titular { Id = Guid.NewGuid(), Nombre = "Titular B", Tipo = TipoTitular.EMPRESA };

        db.Paises.AddRange(paisEspana, paisFrancia, paisPortugal);
        db.Titulares.AddRange(titularA, titularB);
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularA.Id, PaisId = paisEspana.Id, Nombre = "ES-A1", Divisa = "EUR" },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularB.Id, PaisId = paisEspana.Id, Nombre = "ES-B1", Divisa = "EUR" },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularA.Id, PaisId = paisFrancia.Id, Nombre = "FR-A1", Divisa = "EUR" });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisEspana.Id,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cancellationToken: CancellationToken.None);

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<PaginatedResponse<PaisResponse>>().Subject;
        payload.Data.Select(p => p.Id).Should().Equal(paisEspana.Id);
    }

    [Fact]
    public async Task Listar_Should_Derive_Countries_From_Titular_Scope_Without_Country()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var paisEspana = new Pais { Id = Guid.NewGuid(), Nombre = "España", Activo = true };
        var paisFrancia = new Pais { Id = Guid.NewGuid(), Nombre = "Francia", Activo = true };
        var paisPortugal = new Pais { Id = Guid.NewGuid(), Nombre = "Portugal", Activo = true };
        var titularA = new Titular { Id = Guid.NewGuid(), Nombre = "Titular A", Tipo = TipoTitular.EMPRESA };
        var titularB = new Titular { Id = Guid.NewGuid(), Nombre = "Titular B", Tipo = TipoTitular.EMPRESA };

        db.Paises.AddRange(paisEspana, paisFrancia, paisPortugal);
        db.Titulares.AddRange(titularA, titularB);
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularA.Id, PaisId = paisEspana.Id, Nombre = "ES-A1", Divisa = "EUR" },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularA.Id, PaisId = paisFrancia.Id, Nombre = "FR-A1", Divisa = "EUR" },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularB.Id, PaisId = paisPortugal.Id, Nombre = "PT-B1", Divisa = "EUR" });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularA.Id,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cancellationToken: CancellationToken.None);

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<PaginatedResponse<PaisResponse>>().Subject;
        payload.Data.Select(p => p.Nombre).Should().BeEquivalentTo("España", "Francia");
    }

    [Fact]
    public async Task Listar_Should_Show_All_Countries_With_Global_Access()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var paisEspana = new Pais { Id = Guid.NewGuid(), Nombre = "España", Activo = true };
        var paisFrancia = new Pais { Id = Guid.NewGuid(), Nombre = "Francia", Activo = true };
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular", Tipo = TipoTitular.EMPRESA };

        db.Paises.AddRange(paisEspana, paisFrancia);
        db.Titulares.Add(titular);
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, PaisId = paisEspana.Id, Nombre = "ES-1", Divisa = "EUR" },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, PaisId = paisFrancia.Id, Nombre = "FR-1", Divisa = "EUR" });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cancellationToken: CancellationToken.None);

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<PaginatedResponse<PaisResponse>>().Subject;
        payload.Data.Select(p => p.Nombre).Should().BeEquivalentTo("España", "Francia");
    }

    [Fact]
    public async Task Listar_Should_Keep_All_Countries_For_Admin()
    {
        await using var db = BuildDbContext();
        db.Paises.AddRange(
            new Pais { Id = Guid.NewGuid(), Nombre = "España", Activo = true },
            new Pais { Id = Guid.NewGuid(), Nombre = "Sin cuentas", Activo = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, Guid.NewGuid(), RolUsuario.ADMIN);

        var result = await controller.Listar(cancellationToken: CancellationToken.None);

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<PaginatedResponse<PaisResponse>>().Subject;
        payload.Total.Should().Be(2);
    }

    private static PaisesController BuildController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var accessService = new UserAccessService(
            db,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()));
        var controller = new PaisesController(db, accessService, TestAuditService.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
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
