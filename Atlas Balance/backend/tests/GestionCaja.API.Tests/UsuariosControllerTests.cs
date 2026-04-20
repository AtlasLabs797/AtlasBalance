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

        var credentialValue = string.Concat("QaUser", "1234!");
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
        permisos[0].PuedeEditarLineas.Should().BeTrue();

        var auditRows = await db.Auditorias.Where(x => x.EntidadId == created.Id && x.TipoAccion == AuditActions.CreateUsuario).ToListAsync();
        auditRows.Should().HaveCount(1);
    }
}
