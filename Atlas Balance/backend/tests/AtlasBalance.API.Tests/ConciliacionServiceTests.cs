using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class ConciliacionServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SugerirAsync_And_ConfirmarAsync_Should_Create_Deterministic_Match_And_MakerChecker_Warning()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Conciliacion", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Conciliacion", Divisa = "EUR", Activa = true };
        var extracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 6, 28),
            Concepto = "Pago factura REF-123 proveedor",
            Monto = -125.50m,
            Saldo = 874.50m,
            FilaNumero = 1
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Extractos.Add(extracto);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeConciliar = true,
            PuedeCerrarConciliacion = true
        });
        await db.SaveChangesAsync();

        var service = new ConciliacionService(db, new AuditService(db));
        var esperado = await service.CrearMovimientoEsperadoAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new MovimientoEsperadoCrearRequest
            {
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Referencia = "REF 123",
                Concepto = "Pago proveedor"
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        var sugerencias = await service.SugerirAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ConciliacionSugerirRequest { CuentaId = cuenta.Id, VentanaDias = 3 },
            new DefaultHttpContext(),
            CancellationToken.None);

        sugerencias.MovimientosEvaluados.Should().Be(1);
        sugerencias.SugerenciasCreadas.Should().Be(1);
        sugerencias.Sugerencias[0].MovimientoEsperadoId.Should().Be(esperado.Id);
        sugerencias.Sugerencias[0].ExtractoId.Should().Be(extracto.Id);
        sugerencias.Sugerencias[0].Score.Should().BeGreaterThanOrEqualTo(90);
        sugerencias.Sugerencias[0].ReferenciaNormalizada.Should().Be("ref 123");

        var confirmada = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            sugerencias.Sugerencias[0].Id,
            new ConciliacionCambiarEstadoRequest { Observacion = "ok" },
            new DefaultHttpContext(),
            CancellationToken.None);

        confirmada.Estado.Should().Be("conciliada");
        confirmada.FechaConfirmacion.Should().NotBeNull();
        (await db.MovimientosEsperados.SingleAsync()).Estado.Should().Be("conciliada");
        (await db.NotificacionesAdmin.CountAsync(x => x.Tipo == "maker_checker_conciliacion")).Should().Be(1);
    }

    [Fact]
    public async Task SugerirAsync_Should_Reject_ImportOnly_User()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Sin Conciliacion", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Sin Conciliacion", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var service = new ConciliacionService(db, new AuditService(db));
        var act = () => service.SugerirAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ConciliacionSugerirRequest { CuentaId = cuenta.Id },
            new DefaultHttpContext(),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConciliacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
