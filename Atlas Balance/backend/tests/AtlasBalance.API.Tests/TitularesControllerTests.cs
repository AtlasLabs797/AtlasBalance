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

using AtlasBalance.API.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace AtlasBalance.API.Tests;

public sealed class TitularesControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Obtener_Should_Return_Forbid_When_Titular_Is_Outside_User_Scope()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();
        var cuentaPermitidaId = Guid.NewGuid();
        var cuentaBloqueadaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.idor.titulares@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente IDOR Titulares",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularPermitidoId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBloqueadoId, Nombre = "Titular Bloqueado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaPermitidaId, TitularId = titularPermitidoId, Nombre = "Cuenta Permitida", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaBloqueadaId, TitularId = titularBloqueadoId, Nombre = "Cuenta Bloqueada", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularPermitidoId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var permitida = await controller.Obtener(titularPermitidoId, false, null, CancellationToken.None);
        var bloqueada = await controller.Obtener(titularBloqueadoId, false, null, CancellationToken.None);

        permitida.Should().BeOfType<OkObjectResult>();
        bloqueada.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Obtener_Should_Return_Forbid_When_Titular_Is_SoftDeleted_For_NonAdmin()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.idor.titular.softdeleted@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente IDOR Titular SoftDeleted",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Eliminado",
            Tipo = TipoTitular.EMPRESA,
            DeletedAt = DateTime.UtcNow
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Obtener(titularId, false, null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Obtener_Should_Return_Ok_For_Admin_Even_When_Outside_Permissions()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.titular.bypass@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Titular Bypass",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Sin Permiso Explicito",
            Tipo = TipoTitular.EMPRESA
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Obtener(titularId, false, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Listar_Should_Hide_Titulares_Outside_User_Scope()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.listar.titulares@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Listar Titulares",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularPermitidoId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBloqueadoId, Nombre = "Titular Bloqueado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularPermitidoId, Nombre = "Cuenta Permitida", Divisa = "EUR", Activa = true },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularBloqueadoId, Nombre = "Cuenta Bloqueada", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularPermitidoId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(page: 1, pageSize: 50, sortBy: "nombre", sortDir: "asc", search: null, tipoTitular: null, paisId: null, incluirEliminados: false, CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<TitularListItemResponse>>().Subject;
        page.Total.Should().Be(1);
        page.Data.Should().ContainSingle();
        page.Data.Single().Id.Should().Be(titularPermitidoId);
    }

    [Fact]
    public async Task Listar_Should_Not_Expose_Identificacion()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.titular.identificacion@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Titular Identificacion",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            // El nombre no debe contener la subcadena "dentificacion": la asercion de
            // abajo busca esa subcadena en el JSON y el propio nombre la haria fallar.
            Nombre = "Titular Legacy SL",
            Tipo = TipoTitular.EMPRESA,
            Identificacion = "12345678Z"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(page: 1, pageSize: 50, sortBy: "nombre", sortDir: "asc", search: null, tipoTitular: null, paisId: null, incluirEliminados: false, CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<TitularListItemResponse>>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(page.Data.Single());
        json.Should().NotContain("dentificacion");
        json.Should().NotContain("12345678Z");
    }

    private static TitularesController BuildController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new TitularesController(db, new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())), new AuditService(db));
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
