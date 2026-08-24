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

// V-02.08: validacion de la coherencia Pais/Titular/Cuenta en endpoints de
// escritura de permisos. Antes de esta serie:
// - Cuenta + sin titular + sin pais pasaba y quedaba como alcance mal definido.
// - Cuenta + titular (sin pais) NO validaba que la cuenta perteneciera a ese titular.
// - Cuenta + pais (sin titular) validaba parcialmente.
// - Filas redundantes se guardaban en silencio.
//
// Comportamiento esperado tras los cambios:
// 1. Cuenta + titular + pais: validar todos los enlaces.
// 2. Cuenta + titular (sin pais): titular debe contener la cuenta.
// 3. Cuenta + pais (sin titular): pais debe contener la cuenta.
// 4. Cuenta sin titular ni pais: 400.
// 5. Cuenta inexistente: 400.
public class UsuariosControllerValidatePermisosTests
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

    private static async Task<(Usuario user, Pais paisA, Pais paisB, Titular titularA, Titular titularB, Cuenta cuenta)> SeedAsync(AppDbContext db)
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
        var paisB = new Pais { Id = Guid.NewGuid(), Nombre = "Pais B", Activo = true };
        var titularA = new Titular { Id = Guid.NewGuid(), Nombre = "Titular A", Tipo = TipoTitular.EMPRESA };
        var titularB = new Titular { Id = Guid.NewGuid(), Nombre = "Titular B", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titularA.Id,
            PaisId = paisA.Id,
            Nombre = "Cuenta A en Pais A",
            Divisa = "EUR",
        };
        db.Usuarios.Add(user);
        db.Paises.AddRange(paisA, paisB);
        db.Titulares.AddRange(titularA, titularB);
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync();
        return (user, paisA, paisB, titularA, titularB, cuenta);
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaRechazarCuentaSinTitularYPais()
    {
        await using var db = BuildDbContext();
        var (_, _, _, _, _, _) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var cuentaId = (await db.Cuentas.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                CuentaId = cuentaId,
                TitularId = null,
                PaisId = null,
                PuedeVerCuentas = true,
            }
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().NotBeNull();
        // Verificamos que el mensaje apunta a la falta de contexto.
        var errorText = badRequest.Value?.ToString() ?? string.Empty;
        var apuntaATitularOPais = errorText.Contains("titular", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("país", StringComparison.OrdinalIgnoreCase);
        apuntaATitularOPais.Should().BeTrue($"mensaje era: {errorText}");
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaRechazarCuentaConTitularQueNoLaPosee()
    {
        await using var db = BuildDbContext();
        var (_, _, _, _, titularB, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                CuentaId = cuenta.Id,
                TitularId = titularB.Id,
                PaisId = null,
                PuedeVerCuentas = true,
            }
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var errorText = badRequest.Value?.ToString() ?? string.Empty;
        errorText.Should().ContainEquivalentOf("titular");
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaRechazarCuentaConPaisQueNoLaPosee()
    {
        await using var db = BuildDbContext();
        var (_, _, paisB, _, _, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                CuentaId = cuenta.Id,
                TitularId = null,
                PaisId = paisB.Id,
                PuedeVerCuentas = true,
            }
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var errorText = badRequest.Value?.ToString() ?? string.Empty;
        errorText.Should().ContainEquivalentOf("país");
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaAceptarCuentaYTitularYPaisCorrectos()
    {
        await using var db = BuildDbContext();
        var (_, paisA, _, titularA, _, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                CuentaId = cuenta.Id,
                TitularId = titularA.Id,
                PaisId = paisA.Id,
                PuedeVerCuentas = true,
            }
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var persisted = await db.PermisosUsuario.AsNoTracking().FirstAsync();
        persisted.CuentaId.Should().Be(cuenta.Id);
        persisted.TitularId.Should().Be(titularA.Id);
        persisted.PaisId.Should().Be(paisA.Id);
        persisted.PuedeVerCuentas.Should().BeTrue();
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaAceptarCuentaYTitularSinPais()
    {
        await using var db = BuildDbContext();
        var (_, _, _, titularA, _, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                CuentaId = cuenta.Id,
                TitularId = titularA.Id,
                PaisId = null,
                PuedeVerCuentas = true,
            }
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GuardarPermisos_DeberiaAceptarCuentaYPaisSinTitular()
    {
        await using var db = BuildDbContext();
        var (_, paisA, _, _, _, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                CuentaId = cuenta.Id,
                TitularId = null,
                PaisId = paisA.Id,
                PuedeVerCuentas = true,
            }
        };

        var result = await controller.GuardarPermisos(userId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GuardarPermisoCuenta_DeberiaRechazarTitularYPaisIncoherentesConLaCuentaDelPath()
    {
        await using var db = BuildDbContext();
        var (_, _, paisB, _, titularB, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildContext(Guid.NewGuid());

        var userId = (await db.Usuarios.FirstAsync()).Id;
        var request = new SavePermisoUsuarioRequest
        {
            TitularId = titularB.Id,
            PaisId = paisB.Id,
            PuedeVerCuentas = true,
        };

        var result = await controller.GuardarPermisoCuenta(userId, cuenta.Id, request, CancellationToken.None);

        // Cualquier error 400 que indique incoherencia es valido.
        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
