using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class BackupServiceTests
{
    // -----------------------------------------------------------------------
    // V-02.07 (retencion de PII en la nube): ApplyRetentionAsync ahora borra
    // tambien las BackupCloudCopy remotas asociadas a cada backup retirado.
    // Este test verifica que si ese borrado remoto falla (Drive caido, token
    // invalido, lo que sea), la retencion LOCAL del backup se completa igual:
    // fichero borrado del disco y Backup.DeletedAt marcado.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyRetentionAsync_Should_Complete_Local_Retention_When_Remote_Delete_Fails()
    {
        await using var db = BuildDbContext();
        var backupDirectory = Path.Combine(Path.GetTempPath(), $"atlas-balance-backup-retention-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, "old-backup.dump");
        await File.WriteAllTextAsync(backupPath, "dummy-dump-content");

        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = backupDirectory });
            var backup = new Backup
            {
                Id = Guid.NewGuid(),
                FechaCreacion = DateTime.UtcNow.AddDays(-60),
                RutaArchivo = backupPath,
                Estado = EstadoProceso.SUCCESS
            };
            db.Backups.Add(backup);
            db.BackupCloudCopies.Add(new BackupCloudCopy
            {
                Id = Guid.NewGuid(),
                BackupId = backup.Id,
                Provider = GoogleDriveBackupService.ProviderName,
                Estado = "SUCCESS",
                RemoteFileId = "drive-file-id",
                FechaCreacion = DateTime.UtcNow.AddDays(-60)
            });
            await db.SaveChangesAsync();

            var service = new BackupService(
                db,
                new ConfigurationBuilder().Build(),
                TestAuditService.Create(db),
                new ThrowingGoogleDriveBackupService(),
                NullLogger<BackupService>.Instance);

            await service.ApplyRetentionAsync(CancellationToken.None);

            // IgnoreQueryFilters: la retencion acaba de marcar DeletedAt y el filtro
            // global de soft delete ocultaria la fila que queremos comprobar.
            var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync(b => b.Id == backup.Id);
            reloaded.DeletedAt.Should().NotBeNull();
            File.Exists(backupPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    // -----------------------------------------------------------------------
    // A-03: si el servicio se para a mitad del volcado, el `SaveChanges` del
    // `catch` usaba el token ya cancelado y la fila se quedaba en PENDING para
    // siempre (el planificador omite el backup automatico mientras exista
    // cualquier PENDING y nada los recuperaba).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_Should_Mark_The_Backup_As_Failed_When_The_Dump_Is_Cancelled()
    {
        var recorder = new SaveChangesTokenRecorder();
        await using var db = BuildDbContext(recorder);
        var backupDirectory = CreateBackupDirectory("atlas-balance-backup-cancel-test");
        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = backupDirectory });
            await db.SaveChangesAsync();

            using var cts = new CancellationTokenSource();
            var service = new BackupService(
                db,
                new ConfigurationBuilder().Build(),
                TestAuditService.Create(db),
                new RecordingGoogleDriveBackupService(),
                NullLogger<BackupService>.Instance,
                (path, token) =>
                {
                    // El proceso externo se cancela (apagado del servicio o timeout).
                    cts.Cancel();
                    return Task.FromResult<(bool Success, string? ErrorMessage)>((false, "Proceso externo cancelado."));
                });

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await service.CreateBackupAsync(TipoProceso.MANUAL, null, cts.Token));

            var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync();
            reloaded.Estado.Should().Be(EstadoProceso.FAILED);
            reloaded.Estado.Should().NotBe(EstadoProceso.PENDING);

            // Ningun guardado del estado puede usar el token cancelado de la peticion.
            recorder.CanceledTokens.Should().NotContain(true);
        }
        finally
        {
            DeleteBackupDirectory(backupDirectory);
        }
    }

    // -----------------------------------------------------------------------
    // A-04: el volcado se escribia directamente en la ruta final. Si el
    // volcado se quedaba a medias, quedaba un dump corrupto en el disco
    // (y dos backups automaticos del mismo dia escribian en el mismo fichero).
    // Ahora se vuelca en un `.tmp` y solo se renombra si termina bien.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_Should_Not_Publish_A_Partial_Dump_When_The_Dump_Fails()
    {
        await using var db = BuildDbContext();
        var backupDirectory = CreateBackupDirectory("atlas-balance-backup-partial-test");
        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = backupDirectory });
            await db.SaveChangesAsync();

            var service = new BackupService(
                db,
                new ConfigurationBuilder().Build(),
                TestAuditService.Create(db),
                new RecordingGoogleDriveBackupService(),
                NullLogger<BackupService>.Instance,
                async (path, token) =>
                {
                    // Volcado a medias: deja un fichero parcial donde le digan.
                    await File.WriteAllTextAsync(path, "volcado incompleto");
                    return (false, "pg_dump fallo a medias");
                });

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await service.CreateBackupAsync(TipoProceso.AUTO, null, CancellationToken.None));

            Directory.GetFiles(backupDirectory).Should().BeEmpty();
            Directory.GetFiles(backupDirectory, "*.tmp").Should().BeEmpty();

            var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync();
            reloaded.Estado.Should().Be(EstadoProceso.FAILED);
        }
        finally
        {
            DeleteBackupDirectory(backupDirectory);
        }
    }

    [Fact]
    public async Task CreateBackupAsync_Should_Publish_The_Dump_And_Mark_Success_When_The_Dump_Finishes()
    {
        await using var db = BuildDbContext();
        var backupDirectory = CreateBackupDirectory("atlas-balance-backup-ok-test");
        try
        {
            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = backupDirectory });
            await db.SaveChangesAsync();

            var drive = new RecordingGoogleDriveBackupService();
            var service = new BackupService(
                db,
                new ConfigurationBuilder().Build(),
                TestAuditService.Create(db),
                drive,
                NullLogger<BackupService>.Instance,
                async (path, token) =>
                {
                    await File.WriteAllTextAsync(path, "volcado completo");
                    return (true, (string?)null);
                });

            var backup = await service.CreateBackupAsync(TipoProceso.AUTO, null, CancellationToken.None);

            var files = Directory.GetFiles(backupDirectory);
            files.Should().ContainSingle();
            Path.GetFileName(files[0]).Should().MatchRegex(@"^backup_\d{4}-\d{2}-\d{2}_\d{6}\.dump$");
            (await File.ReadAllTextAsync(files[0])).Should().Be("volcado completo");
            Directory.GetFiles(backupDirectory, "*.tmp").Should().BeEmpty();

            var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync(b => b.Id == backup.Id);
            reloaded.Estado.Should().Be(EstadoProceso.SUCCESS);
            reloaded.TamanioBytes.Should().Be("volcado completo".Length);
            reloaded.RutaArchivo.Should().Be(files[0]);
            reloaded.RutaArchivo.Should().NotContain(".tmp");

            // Google Drive recibe siempre la ruta final (cifra `<ruta>.enc`).
            drive.UploadedPath.Should().Be(files[0]);
        }
        finally
        {
            DeleteBackupDirectory(backupDirectory);
        }
    }

    [Fact]
    public async Task ApplyRetentionAsync_Should_Delete_A_Backup_Whose_Name_Includes_The_Hour()
    {
        await using var db = BuildDbContext();
        var backupDirectory = CreateBackupDirectory("atlas-balance-backup-retention-hour-test");
        try
        {
            var backupPath = Path.Combine(backupDirectory, "backup_2026-05-01_020000.dump");
            await File.WriteAllTextAsync(backupPath, "dummy-dump-content");

            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = backupDirectory });
            var backup = new Backup
            {
                Id = Guid.NewGuid(),
                FechaCreacion = DateTime.UtcNow.AddDays(-60),
                RutaArchivo = backupPath,
                Estado = EstadoProceso.SUCCESS
            };
            db.Backups.Add(backup);
            await db.SaveChangesAsync();

            var service = new BackupService(
                db,
                new ConfigurationBuilder().Build(),
                TestAuditService.Create(db),
                new RecordingGoogleDriveBackupService(),
                NullLogger<BackupService>.Instance);

            await service.ApplyRetentionAsync(CancellationToken.None);

            var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync(b => b.Id == backup.Id);
            reloaded.DeletedAt.Should().NotBeNull();
            File.Exists(backupPath).Should().BeFalse();
        }
        finally
        {
            DeleteBackupDirectory(backupDirectory);
        }
    }

    // -----------------------------------------------------------------------
    // A-04: desde que el volcado se escribe en un temporal, un proceso que
    // muere a mitad deja un "<nombre>.dump.tmp" con datos personales dentro.
    // La retencion no lo toca (solo borra ficheros con extension ".dump"), asi
    // que sin limpieza esos temporales se quedarian en disco para siempre.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyRetentionAsync_Should_Delete_Orphan_Temporary_Dumps_But_Keep_Recent_Ones()
    {
        await using var db = BuildDbContext();
        var backupDirectory = CreateBackupDirectory("atlas-balance-backup-orphan-tmp-test");
        try
        {
            var orphanPath = Path.Combine(backupDirectory, "backup_2026-05-01_020000.dump.tmp");
            var recentPath = Path.Combine(backupDirectory, "backup_2026-05-02_020000.dump.tmp");
            await File.WriteAllTextAsync(orphanPath, "volcado a medias");
            await File.WriteAllTextAsync(recentPath, "volcado en curso");

            // El huerfano viene de un volcado que se quedo a medias hace mas de
            // 2 h; el otro puede ser de un volcado en curso (pg_dump tiene un
            // timeout de 30 minutos).
            File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow.AddHours(-3));

            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = backupDirectory });
            await db.SaveChangesAsync();

            var service = new BackupService(
                db,
                new ConfigurationBuilder().Build(),
                TestAuditService.Create(db),
                new RecordingGoogleDriveBackupService(),
                NullLogger<BackupService>.Instance);

            await service.ApplyRetentionAsync(CancellationToken.None);

            File.Exists(orphanPath).Should().BeFalse();
            File.Exists(recentPath).Should().BeTrue();
        }
        finally
        {
            DeleteBackupDirectory(backupDirectory);
        }
    }

    // El constructor de pruebas de BackupService no debe romper la resolucion
    // por DI que hace Program.cs (`AddScoped<IBackupService, BackupService>()`).
    [Fact]
    public void BackupService_Should_Still_Resolve_From_The_Dependency_Injection_Container()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IAuditService>(sp => TestAuditService.Create(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped<IGoogleDriveBackupService, RecordingGoogleDriveBackupService>();
        services.AddLogging();
        // Mismo registro que hace Program.cs.
        services.AddScoped<IBackupService, BackupService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();

        service.Should().BeOfType<BackupService>();
    }

    private static string CreateBackupDirectory(string prefix)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteBackupDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AppDbContext BuildDbContext(ISaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new AppDbContext(builder.Options);
    }

    private sealed class ThrowingGoogleDriveBackupService : IGoogleDriveBackupService
    {
        public Task<GoogleDriveLinkStartResponse> StartLinkAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GoogleDriveLinkStatusResponse> PollLinkAsync(Guid sessionId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GoogleDriveLinkStatusResponse> TestConnectionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UploadBackupAsync(Backup backup, string backupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UploadBackupByIdAsync(Guid backupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GoogleDriveBackupFileResponse>> ListFilesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteRemoteBackupCopyAsync(BackupCloudCopy copy, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fallo simulado de borrado remoto en Google Drive.");
    }

    // A-04: Google Drive cifra el fichero creando `<ruta>.enc`, asi que
    // hay que comprobar que recibe siempre la ruta final y no la temporal.
    private sealed class RecordingGoogleDriveBackupService : IGoogleDriveBackupService
    {
        public string? UploadedPath { get; private set; }

        public Task<GoogleDriveLinkStartResponse> StartLinkAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GoogleDriveLinkStatusResponse> PollLinkAsync(Guid sessionId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GoogleDriveLinkStatusResponse> TestConnectionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UploadBackupAsync(Backup backup, string backupPath, CancellationToken cancellationToken)
        {
            UploadedPath = backupPath;
            return Task.CompletedTask;
        }

        public Task UploadBackupByIdAsync(Guid backupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GoogleDriveBackupFileResponse>> ListFilesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteRemoteBackupCopyAsync(BackupCloudCopy copy, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // A-03: deja ver que token uso cada SaveChanges. El token de la peticion
    // puede venir ya cancelado (apagado del servicio o timeout) y con ese
    // token el estado del backup no se puede guardar.
    private sealed class SaveChangesTokenRecorder : ISaveChangesInterceptor
    {
        public List<bool> CanceledTokens { get; } = new();

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CanceledTokens.Add(cancellationToken.IsCancellationRequested);
            return ValueTask.FromResult(result);
        }
    }
}
