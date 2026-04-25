using System.Security.Claims;
using FluentAssertions;
using GestionCaja.API.Controllers;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionCaja.API.Tests;

public sealed class ExtractosControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SaveColumnasVisibles_Should_Reject_Null_CuentaId()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        await db.SaveChangesAsync();

        var controller = new ExtractosController(db, new NoOpAlertaService());
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        var beforeCount = await db.PreferenciasUsuarioCuenta.CountAsync();
        var result = await controller.SaveColumnasVisibles(
            new SaveColumnasVisiblesRequest
            {
                CuentaId = null,
                ColumnasVisibles = ["fecha", "monto"]
            },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.PreferenciasUsuarioCuenta.CountAsync()).Should().Be(beforeCount);
    }

    [Fact]
    public async Task GetCuentaResumen_Should_Anchor_Selected_Period_To_Latest_Movement()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var latestMovement = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(-2);
        var periodStart = latestMovement.AddMonths(-1);

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Resumen",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Resumen", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Resumen", Divisa = "EUR", Activa = true });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = periodStart.AddDays(-1),
                Monto = 999m,
                Saldo = 999m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = periodStart,
                Monto = 100m,
                Saldo = 1099m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = latestMovement,
                Monto = -30m,
                Saldo = 1069m,
                FilaNumero = 3
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.GetCuentaResumen(cuentaId, "1m", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<CuentaResumenKpiResponse>().Subject;
        summary.SaldoActual.Should().Be(1069m);
        summary.IngresosMes.Should().Be(100m);
        summary.EgresosMes.Should().Be(30m);
    }

    [Fact]
    public async Task Listar_Should_Not_Return_Deleted_Rows_To_NonAdmin_Even_When_Requested()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.extractos@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Extractos",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Extractos", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Extractos", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerDashboard = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Visible",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Eliminado",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2,
                DeletedAt = DateTime.UtcNow,
                DeletedById = userId
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(incluirEliminados: true, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(1);
        page.Data.Should().ContainSingle();
        page.Data.Single().Concepto.Should().Be("Visible");
        page.Data.Single().DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Listar_Should_Return_Empty_For_DashboardOnly_GlobalPermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularAId = Guid.NewGuid();
        var titularBId = Guid.NewGuid();
        var cuentaAId = Guid.NewGuid();
        var cuentaBId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.dashboard-only@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Dashboard",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularAId, Nombre = "Titular A", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBId, Nombre = "Titular B", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaAId, TitularId = titularAId, Nombre = "Cuenta A", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaBId, TitularId = titularBId, Nombre = "Cuenta B", Divisa = "USD", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = null,
            TitularId = null,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false,
            PuedeVerDashboard = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaAId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Cuenta A",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaBId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Cuenta B",
                Monto = 20m,
                Saldo = 20m,
                FilaNumero = 1
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(0);
        page.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCuentasTitular_Should_Forbid_Unauthorized_Titular()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.sinpermiso@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Sin Permiso",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Privado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta Privada", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.GetCuentasTitular(titularId, "1m", CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService());
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

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
