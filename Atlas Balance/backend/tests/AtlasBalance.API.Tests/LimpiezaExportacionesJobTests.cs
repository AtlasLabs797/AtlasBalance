using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
}
