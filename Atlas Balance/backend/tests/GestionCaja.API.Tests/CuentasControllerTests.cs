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

public sealed class CuentasControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Resumen_Should_Anchor_Selected_Period_To_Latest_Movement()
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
            Email = "admin.cuenta.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Cuenta Resumen",
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

        var controller = BuildController(db, userId);

        var result = await controller.Resumen(cuentaId, "1m", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<CuentaResumenResponse>().Subject;
        summary.CuentaId.Should().Be(cuentaId);
        summary.CuentaNombre.Should().Be("Cuenta Resumen");
        summary.TitularId.Should().Be(titularId);
        summary.TitularNombre.Should().Be("Titular Resumen");
        summary.TipoCuenta.Should().Be(nameof(TipoCuenta.NORMAL));
        summary.SaldoActual.Should().Be(1069m);
        summary.IngresosMes.Should().Be(100m);
        summary.EgresosMes.Should().Be(30m);
    }

    [Fact]
    public async Task Resumen_Should_Expose_PlazoFijo_Metadata()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var referenciaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.cuenta.plazo.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Cuenta Plazo Resumen",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Plazo", Tipo = TipoTitular.AUTONOMO });
        db.Cuentas.AddRange(
            new Cuenta { Id = referenciaId, TitularId = titularId, Nombre = "Cuenta Referencia", Divisa = "EUR", TipoCuenta = TipoCuenta.NORMAL, Activa = true },
            new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Deposito Resumen", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true, Notas = "Notas cuenta" });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            CuentaReferenciaId = referenciaId,
            FechaInicio = new DateOnly(2026, 4, 25),
            FechaVencimiento = new DateOnly(2026, 10, 25),
            InteresPrevisto = 150m,
            Renovable = true,
            Estado = EstadoPlazoFijo.PROXIMO_VENCER,
            Notas = "Notas plazo",
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Resumen(cuentaId, "1m", CancellationToken.None);

        var summary = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CuentaResumenResponse>().Subject;
        summary.TipoCuenta.Should().Be(nameof(TipoCuenta.PLAZO_FIJO));
        summary.PlazoFijo.Should().NotBeNull();
        summary.PlazoFijo!.CuentaReferenciaNombre.Should().Be("Cuenta Referencia");
        summary.PlazoFijo.FechaVencimiento.Should().Be(new DateOnly(2026, 10, 25));
        summary.PlazoFijo.Estado.Should().Be(nameof(EstadoPlazoFijo.PROXIMO_VENCER));
        summary.Notas.Should().Be("Notas cuenta");
    }

    private static CuentasController BuildController(AppDbContext db, Guid userId)
    {
        var controller = new CuentasController(db, new UserAccessService(db), new AuditService(db), new NoOpPlazoFijoService());
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.ADMIN))
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
    public async Task Crear_Should_Create_PlazoFijo_With_Metadata()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var referenciaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.plazo@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Plazo",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Autonomo Uno", Tipo = TipoTitular.AUTONOMO });
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        db.Cuentas.Add(new Cuenta { Id = referenciaId, TitularId = titularId, Nombre = "Cuenta Referencia", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Crear(new SaveCuentaRequest
        {
            TitularId = titularId,
            Nombre = "Deposito 6 meses",
            Divisa = "EUR",
            TipoCuenta = TipoCuenta.PLAZO_FIJO,
            PlazoFijo = new SavePlazoFijoRequest
            {
                FechaInicio = new DateOnly(2026, 4, 25),
                FechaVencimiento = new DateOnly(2026, 10, 25),
                InteresPrevisto = 120m,
                Renovable = true,
                CuentaReferenciaId = referenciaId,
                Notas = "Renovar si compensa"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Nombre == "Deposito 6 meses");
        cuenta.TipoCuenta.Should().Be(TipoCuenta.PLAZO_FIJO);
        cuenta.EsEfectivo.Should().BeFalse();
        cuenta.FormatoId.Should().BeNull();

        var plazo = await db.PlazosFijos.SingleAsync(p => p.CuentaId == cuenta.Id);
        plazo.FechaVencimiento.Should().Be(new DateOnly(2026, 10, 25));
        plazo.Estado.Should().Be(EstadoPlazoFijo.ACTIVO);
    }

    [Fact]
    public async Task Listar_Should_Filter_By_TipoTitular_And_TipoCuenta()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var autonomoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.filtros@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Filtros",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = empresaId, Nombre = "Empresa", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = autonomoId, Nombre = "Autonomo", Tipo = TipoTitular.AUTONOMO });
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = empresaId, Nombre = "Banco Empresa", Divisa = "EUR", TipoCuenta = TipoCuenta.NORMAL, Activa = true },
            new Cuenta { Id = Guid.NewGuid(), TitularId = autonomoId, Nombre = "Deposito Autonomo", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Listar(tipoTitular: TipoTitular.AUTONOMO, tipoCuenta: TipoCuenta.PLAZO_FIJO, cancellationToken: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;
        page.Data.Should().ContainSingle();
        page.Data.Single().Nombre.Should().Be("Deposito Autonomo");
        page.Data.Single().TitularTipo.Should().Be(nameof(TipoTitular.AUTONOMO));
        page.Data.Single().TipoCuenta.Should().Be(nameof(TipoCuenta.PLAZO_FIJO));
    }

    private sealed class NoOpPlazoFijoService : IPlazoFijoService
    {
        public Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
            => Task.FromResult(new PlazoFijoResponse { CuentaId = cuentaId });
    }
}
