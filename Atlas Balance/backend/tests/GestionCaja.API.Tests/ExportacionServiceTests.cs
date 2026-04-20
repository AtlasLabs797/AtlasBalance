using FluentAssertions;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionCaja.API.Tests;

public class ExportacionServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ExportarCuentaAsync_Should_Create_A_Different_File_For_Each_Run()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "export_path",
            Valor = exportDirectory,
            Tipo = "string",
            Descripcion = "Ruta de exportaciones"
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular QA",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta QA",
            Divisa = "EUR",
            Activa = true
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 4, 15),
            Concepto = "Cobro QA",
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, new AuditService(db));

        try
        {
            var first = await service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);
            var second = await service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

            first.RutaArchivo.Should().NotBeNullOrWhiteSpace();
            second.RutaArchivo.Should().NotBeNullOrWhiteSpace();
            first.RutaArchivo.Should().NotBe(second.RutaArchivo);
            File.Exists(first.RutaArchivo!).Should().BeTrue();
            File.Exists(second.RutaArchivo!).Should().BeTrue();

            var notificaciones = await db.NotificacionesAdmin
                .Where(n => n.Tipo == "EXPORTACION" && !n.Leida)
                .ToListAsync();

            notificaciones.Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }
}
