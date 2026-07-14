using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public sealed class HardenedGoogleDriveBackupService : IGoogleDriveBackupService
{
    private readonly GoogleDriveBackupService _inner;
    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretProtector _secretProtector;

    public HardenedGoogleDriveBackupService(
        GoogleDriveBackupService inner,
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ISecretProtector secretProtector)
    {
        _inner = inner;
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _secretProtector = secretProtector;
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

    public async Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        await VerifyRemoteChecksumIfKnownAsync(fileId, cancellationToken);
        return await _inner.ImportAsync(fileId, userId, httpContext, cancellationToken);
    }

    private async Task VerifyRemoteChecksumIfKnownAsync(string fileId, CancellationToken cancellationToken)
    {
        var expectedHash = await _dbContext.BackupCloudCopies
            .AsNoTracking()
            .Where(x => x.Provider == GoogleDriveBackupService.ProviderName && x.RemoteFileId == fileId && x.Estado == "SUCCESS" && x.ChecksumSha256 != null)
            .OrderByDescending(x => x.UploadedAt ?? x.FechaCreacion)
            .Select(x => x.ChecksumSha256)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return;
        }

        var connection = await _dbContext.BackupCloudConnections
            .AsNoTracking()
            .Where(x => x.Provider == GoogleDriveBackupService.ProviderName && x.Estado == "ACTIVE")
            .OrderByDescending(x => x.ConnectedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Google Drive no esta vinculado.");
        var accessToken = await RefreshAccessTokenAsync(connection, cancellationToken);
        var tempPath = Path.Combine(Path.GetTempPath(), $"atlas-drive-verify-{Guid.NewGuid():N}.enc");
        try
        {
            await DownloadFileAsync(accessToken, fileId, tempPath, cancellationToken);
            var actualHash = await ComputeSha256HexAsync(tempPath, cancellationToken);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La copia descargada de Google Drive no coincide con el checksum registrado.");
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task<string> RefreshAccessTokenAsync(BackupCloudConnection connection, CancellationToken cancellationToken)
    {
        var config = await _dbContext.Configuraciones
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var clientId = BackupConfigurationService.GetValue(config, "google_drive_oauth_client_id");
        var clientSecret = _secretProtector.UnprotectFromStorage(BackupConfigurationService.GetValue(config, "google_drive_oauth_client_secret"));
        var refreshToken = _secretProtector.UnprotectFromStorage(connection.RefreshToken);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("La configuracion de Google Drive no esta completa.");
        }

        var client = _httpClientFactory.CreateClient("google-oauth");
        using var response = await client.PostAsync(
            "token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken);
        if (!response.IsSuccessStatusCode || payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new InvalidOperationException("Google rechazo el refresh token. Vuelve a vincular Google Drive.");
        }

        return payload.AccessToken;
    }

    private async Task DownloadFileAsync(string accessToken, string fileId, string destinationPath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("google-apis");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.GetAsync($"drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("No se pudo descargar la copia desde Google Drive.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for verification artifact.
        }
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }
}
