using FluentAssertions;
using AtlasBalance.API.Data;
using ClosedXML.Excel;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

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

    /// <summary>
    /// V-02.07: guardarrail contra la deriva que provoco el hallazgo de esta
    /// version. La regla "nada de rutas de red" esta implementada por separado en
    /// ConfiguracionController, BackupService, ExportacionService y
    /// GoogleDriveBackupService, y este ultimo se quedo sin ella durante un
    /// tiempo porque nadie lo sincronizo con los otros tres.
    ///
    /// El riesgo es que Path.IsPathRooted(@"\\host\recurso") devuelve true en
    /// Windows: sin el chequeo explicito, una UNC se cuela como "ruta absoluta
    /// valida" y el volcado entero sale de la red hacia un recurso SMB.
    ///
    /// Se probo unificar las cuatro copias en un helper comun y se descarto: las
    /// cuatro politicas difieren de verdad (mensajes, try/catch alrededor de
    /// Path.GetFullPath, una recomprobacion posterior a canonicalizar y un
    /// criterio de raiz mas laxo), asi que el helper necesitaba cinco flags
    /// booleanos y quedaba menos legible que la duplicacion. Lo que evita la
    /// recaida no es el refactor: es este test.
    /// </summary>
    [Theory]
    [InlineData(@"\\servidor\backups")]
    [InlineData("//servidor/backups")]
    public async Task ExportarCuentaAsync_Should_Reject_Unc_Export_Path(string uncPath)
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "export_path",
            Valor = uncPath,
            Tipo = "string",
            Descripcion = "Ruta de red insegura"
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

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        var act = () => service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
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

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        var act = () => service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ruta absoluta*");
    }

    [Fact]
    public async Task ExportarCuentaAsync_Should_Reject_Cuenta_When_Titular_Is_SoftDeleted()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-deleted-test-{Guid.NewGuid():N}");
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
            Nombre = "Titular eliminado",
            Tipo = TipoTitular.EMPRESA,
            DeletedAt = DateTime.UtcNow
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta eliminada",
            Divisa = "EUR",
            Activa = true
        });
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        try
        {
            var act = () => service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Cuenta no encontrada o inactiva*");
            Directory.GetFiles(exportDirectory, "*.xlsx").Should().BeEmpty();
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

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

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
    public async Task ExportarCuentaAsync_Should_Escape_Formula_Like_Text_After_Leading_Spaces()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-formula-test-{Guid.NewGuid():N}");
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
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular QA", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta QA", Divisa = "EUR", Activa = true });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "  =WEBSERVICE(\"https://example.test\")",
                Monto = 100m,
                Saldo = 100m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 16),
                Concepto = "\ttexto importado",
                Monto = 25m,
                Saldo = 125m,
                FilaNumero = 2
            });
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        try
        {
            var exportacion = await service.ExportarCuentaAsync(cuentaId, TipoProceso.MANUAL, null, CancellationToken.None);

            using var workbook = new XLWorkbook(exportacion.RutaArchivo!);
            var worksheet = workbook.Worksheet("Extractos");
            var cells = new[] { worksheet.Cell(2, 3), worksheet.Cell(3, 3) };
            var formulaLikeCell = cells.Single(c => c.GetString().Contains("WEBSERVICE", StringComparison.Ordinal));
            var tabCell = cells.Single(c => c.GetString().Contains("texto importado", StringComparison.Ordinal));

            formulaLikeCell.HasFormula.Should().BeFalse();
            (formulaLikeCell.GetString().StartsWith("'", StringComparison.Ordinal) || formulaLikeCell.Style.IncludeQuotePrefix)
                .Should().BeTrue();
            tabCell.HasFormula.Should().BeFalse();
            (tabCell.GetString().StartsWith("'", StringComparison.Ordinal) || tabCell.Style.IncludeQuotePrefix)
                .Should().BeTrue();
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

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

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

    [Fact]
    public async Task ExportarMensualAsync_Should_Skip_Cuentas_With_SoftDeleted_Titular()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-monthly-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var activeTitularId = Guid.NewGuid();
        var deletedTitularId = Guid.NewGuid();
        var activeCuentaId = Guid.NewGuid();
        var deletedCuentaId = Guid.NewGuid();

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "export_path",
            Valor = exportDirectory,
            Tipo = "string",
            Descripcion = "Ruta de exportaciones"
        });
        db.Titulares.AddRange(
            new Titular { Id = activeTitularId, Nombre = "Titular activo", Tipo = TipoTitular.EMPRESA },
            new Titular
            {
                Id = deletedTitularId,
                Nombre = "Titular eliminado",
                Tipo = TipoTitular.EMPRESA,
                DeletedAt = DateTime.UtcNow
            });
        db.Cuentas.AddRange(
            new Cuenta
            {
                Id = activeCuentaId,
                TitularId = activeTitularId,
                Nombre = "Cuenta activa",
                Divisa = "EUR",
                Activa = true
            },
            new Cuenta
            {
                Id = deletedCuentaId,
                TitularId = deletedTitularId,
                Nombre = "Cuenta eliminada",
                Divisa = "EUR",
                Activa = true
            });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = activeCuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "Cobro activo",
                Monto = 100m,
                Saldo = 100m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = deletedCuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "Cobro eliminado",
                Monto = 200m,
                Saldo = 200m,
                FilaNumero = 1
            });
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        try
        {
            var success = await service.ExportarMensualAsync(CancellationToken.None);

            success.Should().Be(1);
            db.Exportaciones.Should().ContainSingle(e => e.CuentaId == activeCuentaId && e.Estado == EstadoProceso.SUCCESS);
            db.Exportaciones.Should().NotContain(e => e.CuentaId == deletedCuentaId);
            Directory.GetFiles(exportDirectory, "*.xlsx").Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// J-2: el fallo de una cuenta no puede abortar el lote mensual, pero
    /// tampoco puede desaparecer: si la exportacion mensual falla para todas
    /// las cuentas, el job terminaba "bien" y nadie se enteraba.
    /// </summary>
    [Fact]
    public async Task ExportarMensualAsync_Should_Log_Warning_When_An_Account_Fails()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-monthly-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var titularId = Guid.NewGuid();
        var failingCuentaId = Guid.NewGuid();
        var okCuentaId = Guid.NewGuid();

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
                Valor = "1",
                Tipo = "int",
                Descripcion = "Limite de filas"
            });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular QA",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.AddRange(
            new Cuenta
            {
                Id = failingCuentaId,
                TitularId = titularId,
                Nombre = "Cuenta que falla",
                Divisa = "EUR",
                Activa = true
            },
            new Cuenta
            {
                Id = okCuentaId,
                TitularId = titularId,
                Nombre = "Cuenta correcta",
                Divisa = "EUR",
                Activa = true
            });
        // La cuenta que falla supera el limite de una fila; la otra no tiene
        // extractos, asi que el limite no la bloquea.
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = failingCuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "Movimiento 1",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = failingCuentaId,
                Fecha = new DateOnly(2026, 4, 16),
                Concepto = "Movimiento 2",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2
            });
        await db.SaveChangesAsync();

        var logger = new CapturingLogger<ExportacionService>();
        var service = new ExportacionService(db, TestAuditService.Create(db), logger);

        try
        {
            var success = await service.ExportarMensualAsync(CancellationToken.None);

            success.Should().Be(1);
            logger.Records.Should().Contain(r =>
                r.Level == LogLevel.Warning &&
                r.FormattedMessage.Contains(failingCuentaId.ToString()) &&
                r.FormattedMessage.Contains("ExportacionTooLargeException"));
            logger.Records.Should().NotContain(r =>
                r.Level == LogLevel.Warning &&
                r.FormattedMessage.Contains(okCuentaId.ToString()));
            logger.Records.Should().Contain(r =>
                r.Level == LogLevel.Information &&
                r.FormattedMessage.Contains("1 exportaciones correctas") &&
                r.FormattedMessage.Contains("1 fallos"));
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// R-1: la exportacion mensual reutiliza el mismo DbContext para todas las
    /// cuentas. Sin AsNoTracking y sin limpiar entre cuentas, el ChangeTracker
    /// acumula los extractos de todas ellas.
    /// </summary>
    [Fact]
    public async Task ExportarMensualAsync_Should_Not_Leave_Extractos_Tracked_Between_Accounts()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-monthly-tracking-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var titularId = Guid.NewGuid();
        var firstCuentaId = Guid.NewGuid();
        var secondCuentaId = Guid.NewGuid();

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
        db.Cuentas.AddRange(
            new Cuenta
            {
                Id = firstCuentaId,
                TitularId = titularId,
                Nombre = "Cuenta uno",
                Divisa = "EUR",
                Activa = true
            },
            new Cuenta
            {
                Id = secondCuentaId,
                TitularId = titularId,
                Nombre = "Cuenta dos",
                Divisa = "EUR",
                Activa = true
            });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = firstCuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "Movimiento cuenta uno",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = secondCuentaId,
                Fecha = new DateOnly(2026, 4, 16),
                Concepto = "Movimiento cuenta dos",
                Monto = 20m,
                Saldo = 20m,
                FilaNumero = 1
            });
        await db.SaveChangesAsync();

        // Las entidades sembradas las rastrea el propio test: se limpian para
        // que la comprobacion mida solo lo que deja la exportacion mensual.
        db.ChangeTracker.Clear();

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        try
        {
            var success = await service.ExportarMensualAsync(CancellationToken.None);

            success.Should().Be(2);
            db.ChangeTracker.Entries<Extracto>().Should().BeEmpty();
            db.ChangeTracker.Entries<ExtractoColumnaExtra>().Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// R-1: acortar el rango de filas que se usa para calcular el ancho de
    /// columna no puede alterar el contenido del XLSX.
    /// </summary>
    [Fact]
    public async Task ExportarMensualAsync_Should_Keep_Content_After_Limiting_Column_Width()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-monthly-width-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var titularId = Guid.NewGuid();
        var firstCuentaId = Guid.NewGuid();
        var secondCuentaId = Guid.NewGuid();

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
        db.Cuentas.AddRange(
            new Cuenta
            {
                Id = firstCuentaId,
                TitularId = titularId,
                Nombre = "Cuenta uno",
                Divisa = "EUR",
                Activa = true
            },
            new Cuenta
            {
                Id = secondCuentaId,
                TitularId = titularId,
                Nombre = "Cuenta dos",
                Divisa = "EUR",
                Activa = true
            });

        // Las dos cuentas tienen la misma forma para que el contenido
        // esperado sea identico en los dos XLSX.
        var firstExtractoId = Guid.NewGuid();
        var firstExtractoId2 = Guid.NewGuid();
        var secondExtractoId = Guid.NewGuid();
        var secondExtractoId2 = Guid.NewGuid();
        db.Extractos.AddRange(
            new Extracto
            {
                Id = firstExtractoId,
                CuentaId = firstCuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "Movimiento 1",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = firstExtractoId2,
                CuentaId = firstCuentaId,
                Fecha = new DateOnly(2026, 4, 16),
                Concepto = "Movimiento 2",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = secondExtractoId,
                CuentaId = secondCuentaId,
                Fecha = new DateOnly(2026, 4, 15),
                Concepto = "Movimiento 1",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = secondExtractoId2,
                CuentaId = secondCuentaId,
                Fecha = new DateOnly(2026, 4, 16),
                Concepto = "Movimiento 2",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2
            });
        db.ExtractosColumnasExtra.AddRange(
            new ExtractoColumnaExtra
            {
                Id = Guid.NewGuid(),
                ExtractoId = firstExtractoId2,
                NombreColumna = "Extra QA",
                Valor = "valor extra"
            },
            new ExtractoColumnaExtra
            {
                Id = Guid.NewGuid(),
                ExtractoId = secondExtractoId2,
                NombreColumna = "Extra QA",
                Valor = "valor extra"
            });
        await db.SaveChangesAsync();

        var service = new ExportacionService(db, TestAuditService.Create(db), NullLogger<ExportacionService>.Instance);

        try
        {
            var success = await service.ExportarMensualAsync(CancellationToken.None);

            success.Should().Be(2);
            var files = Directory.GetFiles(exportDirectory, "*.xlsx");
            files.Should().HaveCount(2);

            foreach (var file in files)
            {
                using var workbook = new XLWorkbook(file);
                var worksheet = workbook.Worksheet("Extractos");

                // Cabecera estandar (8 columnas) mas la columna extra.
                worksheet.LastRowUsed()!.RowNumber().Should().Be(3);
                worksheet.Cell(1, 9).GetString().Should().Be("Extra QA");
                worksheet.Cell(2, 2).Style.DateFormat.Format.Should().Be("dd/mm/yyyy");
                worksheet.Cell(2, 4).Style.NumberFormat.Format.Should().Be("#,##0.00");
                worksheet.Cell(2, 5).Style.NumberFormat.Format.Should().Be("#,##0.00");

                // Orden descendente por numero de fila (igual que antes del
                // cambio) y anchos calculados.
                worksheet.Cell(2, 1).GetValue<int>().Should().Be(2);
                worksheet.Cell(3, 1).GetValue<int>().Should().Be(1);
                worksheet.Cell(2, 3).GetString().Should().Be("Movimiento 2");
                worksheet.Cell(2, 4).GetValue<decimal>().Should().Be(20m);
                worksheet.Cell(2, 9).GetString().Should().Be("valor extra");
                worksheet.Column(4).Width.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// J-2: anadir ILogger&lt;ExportacionService&gt; al constructor no puede
    /// romper el registro de DI. El contenedor resuelve el servicio tal cual
    /// lo registra Program.cs (AddScoped&lt;IExportacionService,
    /// ExportacionService&gt;) y ILogger&lt;T&gt; lo resuelve el registro de
    /// logging por defecto.
    /// </summary>
    [Fact]
    public void ExportacionService_Should_Be_Resolvable_By_The_Dependency_Injection_Container()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var db = BuildDbContext();
        services.AddSingleton(db);
        services.AddSingleton<IAuditService>(TestAuditService.Create(db));
        services.AddScoped<IExportacionService, ExportacionService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IExportacionService>();

        service.Should().NotBeNull();
        service.Should().BeOfType<ExportacionService>();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogRecord> _records = new();

        public IReadOnlyList<LogRecord> Records => _records;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var formatted = formatter(state, exception);
            _records.Add(new LogRecord(logLevel, eventId, formatted, exception));
        }

        public sealed record LogRecord(LogLevel Level, EventId EventId, string FormattedMessage, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
