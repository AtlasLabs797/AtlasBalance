using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.Services;

// V-02.06 (PR F3): wrapper pasa a ser un pass-through puro hacia el servicio
// interno, que ahora concentra la verificacion del `.enc`. Antes, este
// wrapper descargaba y verificaba el `.enc` y luego llamaba al servicio
// interno, que volvia a descargar, descifrar y comparar el dump descifrado
// con el SHA-256 del cifrado (dominio cruzado) -> rechazaba copias validas.
// Ademas consultaba un estado "ACTIVE" inexistente en
// `BACKUP_CLOUD_CONNECTIONS` (los estados reales son
// CONNECTED/PENDING/DISCONNECTED/REPLACED).
//
// Se conserva el tipo para no romper el registro del DI existente.
public sealed class HardenedGoogleDriveBackupService : IGoogleDriveBackupService
{
    private readonly GoogleDriveBackupService _inner;

    public HardenedGoogleDriveBackupService(GoogleDriveBackupService inner)
    {
        _inner = inner;
    }

    public Task<GoogleDriveLinkStartResponse> StartLinkAsync(CancellationToken cancellationToken) =>
        _inner.StartLinkAsync(cancellationToken);

    public Task<GoogleDriveLinkStatusResponse> PollLinkAsync(Guid sessionId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
        _inner.PollLinkAsync(sessionId, userId, httpContext, cancellationToken);

    public Task DisconnectAsync(Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
        _inner.DisconnectAsync(userId, httpContext, cancellationToken);

    public Task<GoogleDriveLinkStatusResponse> TestConnectionAsync(CancellationToken cancellationToken) =>
        _inner.TestConnectionAsync(cancellationToken);

    public Task UploadBackupAsync(Backup backup, string backupPath, CancellationToken cancellationToken) =>
        _inner.UploadBackupAsync(backup, backupPath, cancellationToken);

    public Task UploadBackupByIdAsync(Guid backupId, CancellationToken cancellationToken) =>
        _inner.UploadBackupByIdAsync(backupId, cancellationToken);

    public Task<IReadOnlyList<GoogleDriveBackupFileResponse>> ListFilesAsync(CancellationToken cancellationToken) =>
        _inner.ListFilesAsync(cancellationToken);

    public Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken) =>
        _inner.ImportAsync(fileId, userId, httpContext, cancellationToken);
}
