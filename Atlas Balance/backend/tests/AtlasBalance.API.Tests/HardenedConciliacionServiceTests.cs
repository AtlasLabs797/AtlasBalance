using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class HardenedConciliacionServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SugerirAsync_Should_Match_When_Amount_Is_Within_Configured_Tolerance()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Tolerancia", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Tolerancia", Divisa = "EUR", Activa = true };
        var extracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 6, 29),
            Concepto = "Transferencia proveedor REF-999 con comision",
            Monto = -998.50m,
            Saldo = 1.50m,
            FilaNumero = 1
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Extractos.Add(extracto);
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "conciliacion_tolerance_amount", Valor = "2", Tipo = "decimal" },
            new Configuracion { Clave = "conciliacion_tolerance_percent", Valor = "0.01", Tipo = "decimal" });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeConciliar = true
        });
        await db.SaveChangesAsync();

        var audit = new AuditService(db);
        var service = new HardenedConciliacionService(new ConciliacionService(db, audit), db, audit);
        await service.CrearMovimientoEsperadoAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new MovimientoEsperadoCrearRequest
            {
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -1000m,
                Referencia = "REF 999",
                Concepto = "Transferencia proveedor"
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        var sugerencias = await service.SugerirAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ConciliacionSugerirRequest { CuentaId = cuenta.Id, VentanaDias = 1 },
            new DefaultHttpContext(),
            CancellationToken.None);

        sugerencias.SugerenciasCreadas.Should().Be(1);
        sugerencias.Sugerencias[0].ExtractoId.Should().Be(extracto.Id);
        sugerencias.Sugerencias[0].Score.Should().BeGreaterThanOrEqualTo(70);
    }
}
