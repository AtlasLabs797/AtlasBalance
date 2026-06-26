using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

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
    public async Task Crear_Should_Reject_Manager_Without_DataScope()
    {
        await using var db = BuildDbContext();
        var controller = new UsuariosController(db, new AuditService(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var request = new CreateUsuarioRequest
        {
            Email = "manager.no-scope@atlasbalance.local",
            NombreCompleto = "Manager Without Scope",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = true,
            Password = "Manager12345!",
            Emails = new[] { "manager.no-scope@atlasbalance.local" },
            Permisos = Array.Empty<SavePermisoUsuarioRequest>()
        };

        var result = await controller.Crear(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Usuarios.AnyAsync(x => x.Email == request.Email)).Should().BeFalse();
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

    [Fact]
    public async Task RevocarMfa_Should_Clear_Authenticator_And_Revoke_Target_User_Sessions()
    {
        await using var db = BuildDbContext();
        var audit = new AuditService(db);
        var controller = new UsuariosController(db, audit);
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa.target@atlasbalance.local",
            NombreCompleto = "Mfa Target",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!", workFactor: 12),
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow,
            SecurityStamp = "old-stamp",
            MfaEnabled = true,
            MfaSecret = TotpService.GenerateSecret(),
            MfaEnabledAt = DateTime.UtcNow.AddDays(-1),
            MfaLastAcceptedStep = 123
        };
        db.Usuarios.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = user.Id,
            TokenHash = "mfa-token-hash",
            ExpiraEn = DateTime.UtcNow.AddDays(1),
            CreadoEn = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.RevocarMfa(user.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var persisted = await db.Usuarios.SingleAsync(x => x.Id == user.Id);
        persisted.MfaEnabled.Should().BeFalse();
        persisted.MfaSecret.Should().BeNull();
        persisted.MfaEnabledAt.Should().BeNull();
        persisted.MfaLastAcceptedStep.Should().BeNull();
        persisted.SecurityStamp.Should().NotBe("old-stamp");
        (await db.RefreshTokens.SingleAsync(x => x.UsuarioId == user.Id)).RevocadoEn.Should().NotBeNull();
        (await db.Auditorias.AnyAsync(x => x.EntidadId == user.Id && x.TipoAccion == AuditActions.MfaRevoked)).Should().BeTrue();
    }

    [Fact]
    public async Task Actualizar_Should_Reject_Deactivating_Only_Active_Admin()
    {
        await using var db = BuildDbContext();
        var controller = new UsuariosController(db, new AuditService(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());
        var admin = CreateUser(RolUsuario.ADMIN, activo: true);
        db.Usuarios.Add(admin);
        await db.SaveChangesAsync();

        var request = new UpdateUsuarioRequest
        {
            Email = admin.Email,
            NombreCompleto = admin.NombreCompleto,
            Rol = RolUsuario.ADMIN,
            Activo = false,
            PrimerLogin = false,
            Emails = new[] { admin.Email },
            Permisos = Array.Empty<SavePermisoUsuarioRequest>()
        };

        var result = await controller.Actualizar(admin.Id, request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Usuarios.SingleAsync(x => x.Id == admin.Id)).Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Actualizar_Should_Reject_Self_Demotion_From_Admin()
    {
        await using var db = BuildDbContext();
        var actor = CreateUser(RolUsuario.ADMIN, activo: true);
        var otherAdmin = CreateUser(RolUsuario.ADMIN, activo: true);
        db.Usuarios.AddRange(actor, otherAdmin);
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, new AuditService(db));
        controller.ControllerContext = BuildControllerContext(actor.Id);
        var request = new UpdateUsuarioRequest
        {
            Email = actor.Email,
            NombreCompleto = actor.NombreCompleto,
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false,
            Emails = new[] { actor.Email },
            Permisos = Array.Empty<SavePermisoUsuarioRequest>()
        };

        var result = await controller.Actualizar(actor.Id, request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Usuarios.SingleAsync(x => x.Id == actor.Id)).Rol.Should().Be(RolUsuario.ADMIN);
    }

    [Fact]
    public async Task Eliminar_Should_Reject_Deleting_Only_Active_Admin()
    {
        await using var db = BuildDbContext();
        var controller = new UsuariosController(db, new AuditService(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());
        var admin = CreateUser(RolUsuario.ADMIN, activo: true);
        db.Usuarios.Add(admin);
        await db.SaveChangesAsync();

        var result = await controller.Eliminar(admin.Id, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Usuarios.SingleAsync(x => x.Id == admin.Id)).DeletedAt.Should().BeNull();
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

    private static Usuario CreateUser(RolUsuario rol, bool activo)
    {
        var id = Guid.NewGuid();
        return new Usuario
        {
            Id = id,
            Email = $"{id:N}@atlasbalance.local",
            NombreCompleto = $"User {id:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            Rol = rol,
            Activo = activo,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
    }
}
