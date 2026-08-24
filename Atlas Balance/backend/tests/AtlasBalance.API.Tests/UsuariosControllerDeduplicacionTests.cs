using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Constants;
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

// V-02.08: deteccion de permisos redundantes. Cuando dos filas tienen el mismo
// flagset y una cubre la otra en todas las dimensiones, el backend debe
// responder 409 con la lista para que el admin decida (no dedup silenciosa).
public class UsuariosControllerDeduplicacionTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ControllerContext BuildContext(Guid adminId)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
                    new Claim(ClaimTypes.Role, "ADMIN")
                ], "TestAuth")),
            },
        };
    }

    private static async Task<(Usuario user, Pais paisA)> SeedAsync(AppDbContext db)
    {
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "subject@atlasbalance.local",
            NombreCompleto = "Subject",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow,
        };
        var paisA = new Pais { Id = Guid.NewGuid(), Nombre = "Pais A", Activo = true };
        db.Usuarios.Add(user);
        db.Paises.Add(paisA);
        await db.SaveChangesAsync();
        return (user, paisA);
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaDevolver409ConListaParaPermisoGlobalQueCubreOtroPorPais()
    {
        await using var db = BuildDbContext();
        var (_, paisA) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                // Permiso global: pais null, titular null, cuenta null, ver=true
                PaisId = null,
                TitularId = null,
                CuentaId = null,
                PuedeVerCuentas = true,
            },
            new SavePermisoUsuarioRequest
            {
                // Cubierto por el global: solo pais A
                PaisId = paisA.Id,
                TitularId = null,
                CuentaId = null,
                PuedeVerCuentas = true,
            },
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaDevolver409CuandoPaisTitularCubrePaisTitularCuenta()
    {
        await using var db = BuildDbContext();
        var (_, paisA) = await SeedAsync(db);

        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "T", Tipo = TipoTitular.EMPRESA };
        db.Titulares.Add(titular);
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            PaisId = paisA.Id,
            Nombre = "C",
            Divisa = "EUR",
        };
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                PaisId = paisA.Id,
                TitularId = titular.Id,
                CuentaId = null,
                PuedeVerCuentas = true,
            },
            new SavePermisoUsuarioRequest
            {
                PaisId = paisA.Id,
                TitularId = titular.Id,
                CuentaId = cuenta.Id,
                PuedeVerCuentas = true,
            },
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GuardarPermisos_NoDeberiaMarcarComoRedundantesPermisosConFlagsDistintos()
    {
        // Misma dimension, distinto flag: NO son redundantes, conviven.
        await using var db = BuildDbContext();
        var (_, paisA) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                PaisId = null,
                TitularId = null,
                CuentaId = null,
                PuedeVerCuentas = true,
            },
            new SavePermisoUsuarioRequest
            {
                PaisId = null,
                TitularId = null,
                CuentaId = null,
                PuedeEditarLineas = true,
            },
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var permisos = await db.PermisosUsuario.AsNoTracking()
            .Where(p => p.UsuarioId == userId)
            .ToListAsync();
        permisos.Should().HaveCount(2);
        permisos.Should().Contain(p => p.PuedeVerCuentas);
        permisos.Should().Contain(p => p.PuedeEditarLineas);
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaAceptarPermisosDeDimensionesDistintasNoRelacionadas()
    {
        // Pais A vs Pais B: no se cubren, ambos viven.
        await using var db = BuildDbContext();
        var (_, paisA) = await SeedAsync(db);
        var paisB = new Pais { Id = Guid.NewGuid(), Nombre = "Pais B", Activo = true };
        db.Paises.Add(paisB);
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                PaisId = paisA.Id,
                TitularId = null,
                CuentaId = null,
                PuedeVerCuentas = true,
            },
            new SavePermisoUsuarioRequest
            {
                PaisId = paisB.Id,
                TitularId = null,
                CuentaId = null,
                PuedeVerCuentas = true,
            },
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }
}
