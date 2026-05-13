using FluentAssertions;
using AtlasBalance.API.Data;
using ClosedXML.Excel;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

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

    [Fact]
    public async Task ExportarCuentaAsync_Should_Reject_Relative_Export_Path()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "export_path",
            Valor = "exports",
            Tipo = "string",
            Descripcion = "Ruta relativa insegura"
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
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, new AuditService(db));

        var act = () => service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ruta absoluta*");
    }

    [Fact]
    public async Task ExportarCuentaAsync_Should_Preserve_Imported_Row_Order_And_European_Formats()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-format-test-{Guid.NewGuid():N}");
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
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 9, 5),
                Concepto = "Fila uno",
                Monto = 1000m,
                Saldo = 1000m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 1, 1),
                Concepto = "Fila dos",
                Monto = -20m,
                Saldo = 980m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 6, 30),
                Concepto = "Fila tres",
                Monto = 10m,
                Saldo = 990m,
                FilaNumero = 3
            });
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, new AuditService(db));

        try
        {
            var exportacion = await service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

            using var workbook = new XLWorkbook(exportacion.RutaArchivo!);
            var worksheet = workbook.Worksheet("Extractos");

            worksheet.Cell(2, 1).GetValue<int>().Should().Be(3);
            worksheet.Cell(3, 1).GetValue<int>().Should().Be(2);
            worksheet.Cell(4, 1).GetValue<int>().Should().Be(1);
            worksheet.Cell(2, 2).Style.DateFormat.Format.Should().Be("dd/mm/yyyy");
            worksheet.Cell(2, 4).Style.NumberFormat.Format.Should().Be("#,##0.00");
            worksheet.Cell(2, 5).Style.NumberFormat.Format.Should().Be("#,##0.00");
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExportarCuentaAsync_Should_Block_When_Row_Count_Exceeds_Configured_Limit()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-limit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Configuraciones.AddRange(
            new Configuracion
            {
                Clave = "export_path",
                Valor = exportDirectory,
                Tipo = "string",
                Descripcion = "Ruta de exportaciones"
            },
            new Configuracion
            {
                Clave = "export_max_rows",
                Valor = "2",
                Tipo = "int",
                Descripcion = "Limite de filas"
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
        for (var i = 1; i <= 3; i++)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, i),
                Concepto = $"Movimiento {i}",
                Monto = i,
                Saldo = i,
                FilaNumero = i
            });
        }

        await db.SaveChangesAsync();

        var service = new ExportacionService(db, new AuditService(db));

        try
        {
            var act = () => service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

            await act.Should().ThrowAsync<ExportacionTooLargeException>()
                .WithMessage("*supera el limite configurado*");

            var exportacion = await db.Exportaciones.SingleAsync();
            exportacion.Estado.Should().Be(EstadoProceso.FAILED);
            exportacion.TamanioBytes.Should().BeNull();
            Directory.GetFiles(exportDirectory, "*.xlsx").Should().BeEmpty();
            (await db.Auditorias.SingleAsync()).TipoAccion.Should().Be(AtlasBalance.API.Constants.AuditActions.ExportacionBloqueada);
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
