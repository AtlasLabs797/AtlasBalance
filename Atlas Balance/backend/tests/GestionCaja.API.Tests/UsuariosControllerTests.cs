using System.Security.Claims;
using FluentAssertions;
using GestionCaja.API.Controllers;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Constants;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionCaja.API.Tests;

public class UsuariosControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Crear_Should_Create_User_With_Emails_And_Permissions_And_Audit()
    {
        await using var db = BuildDbContext();
        var audit = new AuditService(db);
        var controller = new UsuariosController(db, audit);

        var adminId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
            new Claim(ClaimTypes.Role, "ADMIN")
        }, "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        var credentialValue = string.Concat("QaUser", "123456!");
        var request = new CreateUsuarioRequest
        {
            Email = "controller.test@atlasbalance.local",
            NombreCompleto = "Controller Test",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = true,
            Password = credentialValue,
            Emails = new[] { "controller.test@atlasbalance.local", "notify.test@atlasbalance.local" },
            Permisos = new[]
            {
                new SavePermisoUsuarioRequest
                {
                    PuedeVerCuentas = true,
                    PuedeAgregarLineas = true,
                    PuedeEditarLineas = true,
                    PuedeEliminarLineas = false,
                    PuedeImportar = true,
                    PuedeVerDashboard = true,
                    ColumnasVisibles = new[] { "fecha", "monto" },
                    ColumnasEditables = new[] { "monto" }
                }
            }
        };

        var result = await controller.Crear(request, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var created = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == request.Email);
        created.Should().NotBeNull();
        created!.NombreCompleto.Should().Be(request.NombreCompleto);

        var userEmails = await db.UsuarioEmails.Where(x => x.UsuarioId == created.Id).ToListAsync();
        userEmails.Should().HaveCount(2);
        userEmails.Should().Contain(x => x.EsPrincipal && x.Email == request.Email);

        var permisos = await db.PermisosUsuario.Where(x => x.UsuarioId == created.Id).ToListAsync();
        permisos.Should().HaveCount(1);
        permisos[0].PuedeVerCuentas.Should().BeTrue();
        permisos[0].PuedeEditarLineas.Should().BeTrue();

        var auditRows = await db.Auditorias.Where(x => x.EntidadId == created.Id && x.TipoAccion == AuditActions.CreateUsuario).ToListAsync();
        auditRows.Should().HaveCount(1);
    }

    [Fact]
    public async Task Actualizar_Should_Revoke_Sessions_When_Admin_Resets_Password()
    {
        await using var db = BuildDbContext();
        var audit = new AuditService(db);
        var controller = new UsuariosController(db, audit);
        var adminId = Guid.NewGuid();
        controller.ControllerContext = BuildControllerContext(adminId);

        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "reset.target@atlasbalance.local",
            NombreCompleto = "Reset Target",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!", workFactor: 12),
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = user.Id,
            TokenHash = "token-hash",
            ExpiraEn = DateTime.UtcNow.AddDays(1),
            CreadoEn = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var originalStamp = user.SecurityStamp;

        var request = new UpdateUsuarioRequest
        {
            Email = user.Email,
            NombreCompleto = user.NombreCompleto,
            Rol = user.Rol,
            Activo = true,
            PrimerLogin = false,
            PasswordNueva = "ResetPass12345!",
            Emails = new[] { user.Email },
            Permisos = Array.Empty<SavePermisoUsuarioRequest>()
        };

        var result = await controller.Actualizar(user.Id, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.SecurityStamp.Should().NotBe(originalStamp);
        persisted.PasswordChangedAt.Should().NotBeNull();
        BCrypt.Net.BCrypt.Verify("ResetPass12345!", persisted.PasswordHash).Should().BeTrue();
        (await db.RefreshTokens.SingleAsync(x => x.UsuarioId == user.Id)).RevocadoEn.Should().NotBeNull();
        (await db.Auditorias.AnyAsync(x => x.EntidadId == user.Id && x.TipoAccion == AuditActions.PasswordReset)).Should().BeTrue();
    }

    [Fact]
    public async Task GuardarPermisos_Should_Revoke_Target_User_Sessions()
    {
        await using var db = BuildDbContext();
        var audit = new AuditService(db);
        var controller = new UsuariosController(db, audit);
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "perms.target@atlasbalance.local",
            NombreCompleto = "Perms Target",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!", workFactor: 12),
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow,
            SecurityStamp = "old-stamp"
        };
        db.Usuarios.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = user.Id,
            TokenHash = "permissions-token-hash",
            ExpiraEn = DateTime.UtcNow.AddDays(1),
            CreadoEn = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = new[]
        {
            new SavePermisoUsuarioRequest
            {
                PuedeVerCuentas = true,
                PuedeVerDashboard = true
            }
        };

        var result = await controller.GuardarPermisos(user.Id, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.SecurityStamp.Should().NotBe("old-stamp");
        (await db.RefreshTokens.SingleAsync(x => x.UsuarioId == user.Id)).RevocadoEn.Should().NotBeNull();
        (await db.Auditorias.AnyAsync(x => x.EntidadId == user.Id && x.TipoAccion == AuditActions.CambioPermisos)).Should().BeTrue();
    }

    private static ControllerContext BuildControllerContext(Guid adminId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
            new Claim(ClaimTypes.Role, "ADMIN")
        }, "TestAuth");

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
