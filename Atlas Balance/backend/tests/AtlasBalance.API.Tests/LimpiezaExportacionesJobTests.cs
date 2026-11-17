using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class LimpiezaExportacionesJobTests
{
    // -----------------------------------------------------------------------
    // V-02.07 (retencion de PII): ExportacionService escribe .xlsx en disco
    // con el nombre del titular en claro y nunca los purgaba. Este job borra
    // el fichero fisico y marca soft delete pasado el corte configurable
    // (exportacion_retention_days, default 90 dias), sin tocar las recientes.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Should_Delete_File_And_SoftDelete_Old_Exportaciones_But_Not_Recent_Ones()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-retention-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);
        var oldFilePath = Path.Combine(exportDirectory, "old-export.xlsx");
        var recentFilePath = Path.Combine(exportDirectory, "recent-export.xlsx");
        await File.WriteAllTextAsync(oldFilePath, "old");
        await File.WriteAllTextAsync(recentFilePath, "recent");

        try
        {
            var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
            var oldExportacion = new Exportacion
            {
                Id = Guid.NewGuid(),
                CuentaId = Guid.NewGuid(),
                FechaExportacion = now.AddDays(-91),
                RutaArchivo = oldFilePath,
                Estado = EstadoProceso.SUCCESS
            };
            var recentExportacion = new Exportacion
            {
                Id = Guid.NewGuid(),
                CuentaId = Guid.NewGuid(),
                FechaExportacion = now.AddDays(-10),
                RutaArchivo = recentFilePath,
                Estado = EstadoProceso.SUCCESS
            };
            // V-03.02 (J-4): el job solo borra ficheros bajo la raiz de
            // exportaciones configurada. El temporal del test actua como esa
            // raiz; sin esta configuracion la ruta quedaria fuera de la raiz
            // por defecto (C:\atlas-balance\exports) y el fichero no se
            // borraria.
            db.Configuraciones.Add(new Configuracion { Clave = "export_path", Valor = exportDirectory });
            db.Exportaciones.AddRange(oldExportacion, recentExportacion);
            await db.SaveChangesAsync();

            var job = new LimpiezaExportacionesJob(db, new FakeClock(now), NullLogger<LimpiezaExportacionesJob>.Instance);

            await job.ExecuteAsync();

            var reloadedOld = await db.Exportaciones.IgnoreQueryFilters().SingleAsync(e => e.Id == oldExportacion.Id);
            var reloadedRecent = await db.Exportaciones.IgnoreQueryFilters().SingleAsync(e => e.Id == recentExportacion.Id);

            reloadedOld.DeletedAt.Should().NotBeNull();
            File.Exists(oldFilePath).Should().BeFalse();

            reloadedRecent.DeletedAt.Should().BeNull();
            File.Exists(recentFilePath).Should().BeTrue();
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
    public async Task ExecuteAsync_Should_Respect_Configured_Exportacion_Retention_Days()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion { Clave = "exportacion_retention_days", Valor = "30" });

        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
        var exportacion = new Exportacion
        {
            Id = Guid.NewGuid(),
            CuentaId = Guid.NewGuid(),
            FechaExportacion = now.AddDays(-45),
            RutaArchivo = null,
            Estado = EstadoProceso.SUCCESS
        };
        db.Exportaciones.Add(exportacion);
        await db.SaveChangesAsync();

        var job = new LimpiezaExportacionesJob(db, new FakeClock(now), NullLogger<LimpiezaExportacionesJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.Exportaciones.IgnoreQueryFilters().SingleAsync(e => e.Id == exportacion.Id);
        reloaded.DeletedAt.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Tarea 3: ImportacionLote.ContenidoOriginal guarda el pegado bruto del
    // extracto bancario y nunca se purgaba. El job vacia ese campo a los
    // 180 dias (importacion_contenido_retention_days) sin borrar la fila:
    // el resto de campos de trazabilidad (hash, resumen, contadores) se
    // conservan intactos.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Should_Clear_ContenidoOriginal_But_Keep_Other_Fields_Intact()
    {
        await using var db = BuildDbContext();
        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
        var cuentaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var lote = new ImportacionLote
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            UsuarioCreadorId = usuarioId,
            TipoOrigen = "PEGADO",
            TamanioBytes = 1234,
            Sha256 = "deadbeef",
            Separador = ";",
            MapeoJson = """{"col":"Fecha"}""",
            ResumenJson = """{"total":10}""",
            ContenidoOriginal = "01/01/2026;Cobro cliente;100.00",
            LoteHash = "lote-hash-abc",
            Estado = "confirmado",
            FilasTotal = 10,
            FilasValidas = 9,
            FilasError = 1,
            FilasAdvertencia = 0,
            FechaCreacion = now.AddDays(-181)
        };
        db.ImportacionLotes.Add(lote);
        await db.SaveChangesAsync();

        var job = new LimpiezaExportacionesJob(db, new FakeClock(now), NullLogger<LimpiezaExportacionesJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.ImportacionLotes.SingleAsync(l => l.Id == lote.Id);
        reloaded.ContenidoOriginal.Should().BeEmpty();
        reloaded.CuentaId.Should().Be(cuentaId);
        reloaded.UsuarioCreadorId.Should().Be(usuarioId);
        reloaded.Sha256.Should().Be("deadbeef");
        reloaded.LoteHash.Should().Be("lote-hash-abc");
        reloaded.ResumenJson.Should().Be("""{"total":10}""");
        reloaded.FilasTotal.Should().Be(10);
        reloaded.FilasValidas.Should().Be(9);
        reloaded.FilasError.Should().Be(1);
        reloaded.Estado.Should().Be("confirmado");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Touch_ContenidoOriginal_Of_Recent_Lotes()
    {
        await using var db = BuildDbContext();
        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
        var lote = new ImportacionLote
        {
            Id = Guid.NewGuid(),
            CuentaId = Guid.NewGuid(),
            UsuarioCreadorId = Guid.NewGuid(),
            Sha256 = "recent-hash",
            ContenidoOriginal = "contenido reciente sin purgar",
            LoteHash = "lote-hash-recent",
            FechaCreacion = now.AddDays(-5)
        };
        db.ImportacionLotes.Add(lote);
        await db.SaveChangesAsync();

        var job = new LimpiezaExportacionesJob(db, new FakeClock(now), NullLogger<LimpiezaExportacionesJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.ImportacionLotes.SingleAsync(l => l.Id == lote.Id);
        reloaded.ContenidoOriginal.Should().Be("contenido reciente sin purgar");
    }

    // -----------------------------------------------------------------------
    // V-03.02 (J-4): el job solo purga la fila si el fichero fisico esta
    // confirmado (borrado o ya no existia) y solo toca ficheros bajo la raiz
    // de exportaciones configurada. Antes marcaba la fila siempre: un
    // fichero bloqueado se quedaba en disco con sus datos personales sin que
    // nadie supiera que seguia ahi.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Should_Not_SoftDelete_Exportacion_When_The_File_Cannot_Be_Deleted()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-locked-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);
        var lockedFilePath = Path.Combine(exportDirectory, "locked-export.xlsx");
        await File.WriteAllTextAsync(lockedFilePath, "locked");

        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "export_path", Valor = exportDirectory });

            var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
            var exportacion = new Exportacion
            {
                Id = Guid.NewGuid(),
                CuentaId = Guid.NewGuid(),
                FechaExportacion = now.AddDays(-91),
                RutaArchivo = lockedFilePath,
                Estado = EstadoProceso.SUCCESS
            };
            db.Exportaciones.Add(exportacion);
            await db.SaveChangesAsync();

            // En Windows, con el fichero abierto en FileShare.None, File.Delete falla.
            await using var bloqueo = new FileStream(lockedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var job = new LimpiezaExportacionesJob(db, new FakeClock(now), NullLogger<LimpiezaExportacionesJob>.Instance);

            await job.ExecuteAsync();

            var reloaded = await db.Exportaciones.IgnoreQueryFilters().SingleAsync(e => e.Id == exportacion.Id);

            reloaded.DeletedAt.Should().BeNull();
            File.Exists(lockedFilePath).Should().BeTrue();
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
    public async Task ExecuteAsync_Should_Delete_File_And_SoftDelete_Exportacion_Inside_The_Export_Root()
    {
        await using var db = BuildDbContext();
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-inside-test-{Guid.NewGuid():N}");
        var subDirectory = Path.Combine(exportDirectory, "2026");
        Directory.CreateDirectory(subDirectory);
        var filePath = Path.Combine(subDirectory, "export.xlsx");
        await File.WriteAllTextAsync(filePath, "contenido");

        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "export_path", Valor = exportDirectory });

            var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
            var exportacion = new Exportacion
            {
                Id = Guid.NewGuid(),
                CuentaId = Guid.NewGuid(),
                FechaExportacion = now.AddDays(-91),
                RutaArchivo = filePath,
                Estado = EstadoProceso.SUCCESS
            };
            db.Exportaciones.Add(exportacion);
            await db.SaveChangesAsync();

            var job = new LimpiezaExportacionesJob(db, new FakeClock(now), NullLogger<LimpiezaExportacionesJob>.Instance);

            await job.ExecuteAsync();

            var reloaded = await db.Exportaciones.IgnoreQueryFilters().SingleAsync(e => e.Id == exportacion.Id);

            reloaded.DeletedAt.Should().NotBeNull();
            File.Exists(filePath).Should().BeFalse();
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
    public async Task ExecuteAsync_Should_Not_Delete_Or_SoftDelete_Exportacion_Outside_The_Export_Root()
    {
        await using var db = BuildDbContext();
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-export-outside-test-{Guid.NewGuid():N}");
        var exportDirectory = Path.Combine(baseDirectory, "exports");
        var siblingDirectory = Path.Combine(baseDirectory, "exports-mal");
        Directory.CreateDirectory(exportDirectory);
        Directory.CreateDirectory(siblingDirectory);
        var outsideFilePath = Path.Combine(siblingDirectory, "fuera.xlsx");
        await File.WriteAllTextAsync(outsideFilePath, "fuera");

        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "export_path", Valor = exportDirectory });

            var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
            var exportacion = new Exportacion
            {
                Id = Guid.NewGuid(),
                CuentaId = Guid.NewGuid(),
                FechaExportacion = now.AddDays(-91),
                RutaArchivo = outsideFilePath,
                Estado = EstadoProceso.SUCCESS
            };
            db.Exportaciones.Add(exportacion);
            await db.SaveChangesAsync();

            var logger = new CapturingLogger<LimpiezaExportacionesJob>();
            var job = new LimpiezaExportacionesJob(db, new FakeClock(now), logger);

            await job.ExecuteAsync();

            var reloaded = await db.Exportaciones.IgnoreQueryFilters().SingleAsync(e => e.Id == exportacion.Id);

            reloaded.DeletedAt.Should().BeNull();
            File.Exists(outsideFilePath).Should().BeTrue();
            logger.Warnings.Should().Contain(w => w.Contains("ruta fuera de la raiz permitida"));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(@"C:\atlas-balance\exports\2026\export.xlsx", @"C:\atlas-balance\exports", true)]
    [InlineData(@"C:\atlas-balance\exports-mal\export.xlsx", @"C:\atlas-balance\exports", false)]
    [InlineData(@"C:\atlas-balance\exports2\export.xlsx", @"C:\atlas-balance\exports", false)]
    [InlineData(@"C:\atlas-balance\otro\export.xlsx", @"C:\atlas-balance\exports", false)]
    public void IsAllowedExportFile_Should_Only_Accept_Paths_Under_The_Export_Root(string ruta, string raiz, bool permitido)
    {
        LimpiezaExportacionesJob.IsAllowedExportFile(ruta, raiz).Should().Be(permitido);
    }

    // -----------------------------------------------------------------------
    // V-03.02 (J-1): con 2 workers de Hangfire, el job no puede solaparse
    // consigo mismo, y un fallo puntual (fichero bloqueado, base de datos
    // reiniciandose) debe reintentarse solo.
    // -----------------------------------------------------------------------

    [Fact]
    public void Job_Should_Have_Hangfire_Concurrency_And_Retry_Attributes()
    {
        var concurrencia = typeof(LimpiezaExportacionesJob)
            .GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false)
            .Cast<DisableConcurrentExecutionAttribute>()
            .SingleOrDefault();

        concurrencia.Should().NotBeNull();
        concurrencia!.TimeoutSec.Should().Be(900);

        var reintento = typeof(LimpiezaExportacionesJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .SingleOrDefault();

        reintento.Should().NotBeNull();
        reintento!.Attempts.Should().Be(3);
        reintento.DelaysInSeconds.Should().Equal(60, 300, 900);
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
