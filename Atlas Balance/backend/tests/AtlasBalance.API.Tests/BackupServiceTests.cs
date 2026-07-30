using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
                new AuditService(db),
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

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
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
}
