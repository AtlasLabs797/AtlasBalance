using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AtlasBalance.API.Services;

public interface IGoogleDriveBackupService
{
    Task<GoogleDriveLinkStartResponse> StartLinkAsync(CancellationToken cancellationToken);
    Task<GoogleDriveLinkStatusResponse> PollLinkAsync(Guid sessionId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken);
    Task<GoogleDriveLinkStatusResponse> TestConnectionAsync(CancellationToken cancellationToken);
    Task UploadBackupAsync(Backup backup, string backupPath, CancellationToken cancellationToken);
    Task UploadBackupByIdAsync(Guid backupId, CancellationToken cancellationToken);
    Task DeleteRemoteBackupCopyAsync(BackupCloudCopy copy, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoogleDriveBackupFileResponse>> ListFilesAsync(CancellationToken cancellationToken);
    Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken);
}

public sealed class GoogleDriveBackupService : IGoogleDriveBackupService
{
    public const string ProviderName = "GOOGLE_DRIVE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ISecretProtector _secretProtector;
    private readonly IBackupEncryptionService _encryptionService;
    private readonly IAuditService _auditService;
    private readonly ILogger<GoogleDriveBackupService> _logger;

    public GoogleDriveBackupService(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ISecretProtector secretProtector,
        IBackupEncryptionService encryptionService,
        IAuditService auditService,
        ILogger<GoogleDriveBackupService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _secretProtector = secretProtector;
        _encryptionService = encryptionService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<GoogleDriveLinkStartResponse> StartLinkAsync(CancellationToken cancellationToken)
    {
        var oauth = await LoadOAuthConfigAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("google-oauth");
        var scope = "https://www.googleapis.com/auth/drive.file openid email";
        using var response = await client.PostAsync(
            "device/code",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = oauth.ClientId,
                ["scope"] = scope
            }),
            cancellationToken);

        var payload = await ReadJsonAsync<GoogleDeviceCodeResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || payload is null)
        {
            throw new InvalidOperationException("Google rechazo el inicio de vinculacion. Revisa el OAuth Client ID.");
        }

        var sessionId = Guid.NewGuid();
        var session = new GoogleDriveDeviceSession(
            sessionId,
            payload.DeviceCode,
            payload.UserCode,
            payload.VerificationUrl,
            DateTime.UtcNow.AddSeconds(payload.ExpiresIn),
            Math.Clamp(payload.Interval, 5, 30),
            DateTime.MinValue);

        _cache.Set(CacheKey(sessionId), session, session.ExpiresAt);
        return new GoogleDriveLinkStartResponse
        {
            SessionId = sessionId,
            UserCode = session.UserCode,
            VerificationUrl = session.VerificationUrl,
            ExpiresAt = session.ExpiresAt,
            IntervalSeconds = session.IntervalSeconds
        };
    }

    public async Task<GoogleDriveLinkStatusResponse> PollLinkAsync(
        Guid sessionId,
        Guid? userId,
        HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(CacheKey(sessionId), out GoogleDriveDeviceSession? session) || session is null)
        {
            return new GoogleDriveLinkStatusResponse { Estado = "EXPIRED", Message = "La vinculacion ha caducado. Inicia una nueva." };
        }

        if (DateTime.UtcNow >= session.ExpiresAt)
        {
            _cache.Remove(CacheKey(sessionId));
            return new GoogleDriveLinkStatusResponse { Estado = "EXPIRED", Message = "La vinculacion ha caducado. Inicia una nueva." };
        }

        if (session.LastPollAt.AddSeconds(session.IntervalSeconds) > DateTime.UtcNow)
        {
            return new GoogleDriveLinkStatusResponse { Estado = "PENDING", PollAfterSeconds = session.IntervalSeconds };
        }

        session = session with { LastPollAt = DateTime.UtcNow };
        _cache.Set(CacheKey(sessionId), session, session.ExpiresAt);

        var oauth = await LoadOAuthConfigAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("google-oauth");
        using var response = await client.PostAsync(
            "token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = oauth.ClientId,
                ["client_secret"] = oauth.ClientSecret,
                ["device_code"] = session.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }),
            cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = ParseGoogleError(raw);
            if (error == "authorization_pending")
            {
                return new GoogleDriveLinkStatusResponse { Estado = "PENDING", PollAfterSeconds = session.IntervalSeconds };
            }

            if (error == "slow_down")
            {
                session = session with { IntervalSeconds = Math.Min(30, session.IntervalSeconds + 5) };
                _cache.Set(CacheKey(sessionId), session, session.ExpiresAt);
                return new GoogleDriveLinkStatusResponse { Estado = "PENDING", PollAfterSeconds = session.IntervalSeconds };
            }

            return new GoogleDriveLinkStatusResponse { Estado = "FAILED", Message = "Google rechazo la vinculacion." };
        }

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(raw, JsonOptions);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            return new GoogleDriveLinkStatusResponse { Estado = "FAILED", Message = "Google no devolvio un refresh token. Revoca el acceso en Google e intentalo de nuevo." };
        }

        var accountEmail = await ResolveAccountEmailAsync(token.AccessToken, cancellationToken);
        await StoreConnectionAsync(token.RefreshToken, token.Scope ?? string.Empty, accountEmail, userId, cancellationToken);
        _cache.Remove(CacheKey(sessionId));

        await _auditService.LogAsync(
            userId,
            AuditActions.BackupCloudLinked,
            "BACKUP_CLOUD_CONNECTIONS",
            null,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            JsonSerializer.Serialize(new { provider = ProviderName, account_email = accountEmail }),
            cancellationToken);

        return new GoogleDriveLinkStatusResponse
        {
            Estado = "CONNECTED",
            AccountEmail = accountEmail,
            Message = "Google Drive vinculado."
        };
    }

    public async Task DisconnectAsync(Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var connections = await _dbContext.BackupCloudConnections
            .Where(x => x.Provider == ProviderName)
            .ToListAsync(cancellationToken);
        foreach (var connection in connections)
        {
            connection.DeletedAt = DateTime.UtcNow;
            connection.DeletedById = userId;
            connection.Estado = "DISCONNECTED";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            userId,
            AuditActions.BackupCloudDisconnected,
            "BACKUP_CLOUD_CONNECTIONS",
            null,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            JsonSerializer.Serialize(new { provider = ProviderName }),
            cancellationToken);
    }

    public async Task<GoogleDriveLinkStatusResponse> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await LoadActiveConnectionAsync(cancellationToken);
        var accessToken = await RefreshAccessTokenAsync(connection, cancellationToken);
        var email = await ResolveAccountEmailAsync(accessToken, cancellationToken);
        connection.AccountEmail = email;
        connection.LastValidatedAt = DateTime.UtcNow;
        connection.LastError = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new GoogleDriveLinkStatusResponse
        {
            Estado = "CONNECTED",
            AccountEmail = email,
            Message = "Conexion con Google Drive correcta."
        };
    }

    public async Task UploadBackupAsync(Backup backup, string backupPath, CancellationToken cancellationToken)
    {
        var config = await LoadConfigMapAsync(cancellationToken);
        var destination = BackupConfigurationService.NormalizeDestination(BackupConfigurationService.GetValue(config, "backup_destination"));
        if (destination != BackupConfigurationService.DestinationLocalAndGoogleDrive)
        {
            return;
        }

        BackupCloudConnection connection;
        try
        {
            connection = await LoadActiveConnectionAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            _dbContext.BackupCloudCopies.Add(new BackupCloudCopy
            {
                Id = Guid.NewGuid(),
                BackupId = backup.Id,
                Provider = ProviderName,
                Estado = "FAILED",
                FechaCreacion = DateTime.UtcNow,
                ErrorCode = "google_drive_not_linked",
                ErrorMessage = "Google Drive no esta vinculado."
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        var copy = new BackupCloudCopy
        {
            Id = Guid.NewGuid(),
            BackupId = backup.Id,
            ConnectionId = connection.Id,
            Provider = ProviderName,
            Estado = "PENDING",
            FechaCreacion = DateTime.UtcNow
        };
        _dbContext.BackupCloudCopies.Add(copy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        string? encryptedPath = null;
        try
        {
            var encrypted = await _encryptionService.EncryptAsync(backupPath, cancellationToken);
            encryptedPath = encrypted.Path;
            var accessToken = await RefreshAccessTokenAsync(connection, cancellationToken);
            var folderId = await ResolveFolderIdAsync(accessToken, config, cancellationToken);
            var remoteName = $"{Path.GetFileName(backupPath)}.enc";
            var uploaded = await UploadFileAsync(accessToken, encrypted.Path, encrypted.SizeBytes, remoteName, folderId, backup.Id, cancellationToken);

            copy.Estado = "SUCCESS";
            copy.RemoteFileId = uploaded.Id;
            copy.RemoteFileName = uploaded.Name ?? remoteName;
            copy.RemoteSizeBytes = encrypted.SizeBytes;
            copy.ChecksumSha256 = encrypted.Sha256Hex;
            copy.UploadedAt = DateTime.UtcNow;
            copy.ErrorCode = null;
            copy.ErrorMessage = null;
            connection.LastValidatedAt = DateTime.UtcNow;
            connection.LastError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                backup.IniciadoPorId,
                AuditActions.BackupCloudUpload,
                "BACKUP_CLOUD_COPIES",
                copy.Id,
                ipAddress: null,
                detallesJson: JsonSerializer.Serialize(new { provider = ProviderName, backup_id = backup.Id, file_id = uploaded.Id }),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            copy.Estado = "FAILED";
            copy.ErrorCode = ClassifyError(ex);
            copy.ErrorMessage = BuildSafeErrorMessage(ex);
            connection.LastError = copy.ErrorMessage;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(ex, "Fallo al subir backup {BackupId} a Google Drive", backup.Id);
            throw new InvalidOperationException("La copia local se creo, pero no se pudo subir a Google Drive.", ex);
        }
        finally
        {
            TryDelete(encryptedPath);
        }
    }

    public async Task UploadBackupByIdAsync(Guid backupId, CancellationToken cancellationToken)
    {
        var backup = await _dbContext.Backups
            .FirstOrDefaultAsync(x => x.Id == backupId && x.Estado == EstadoProceso.SUCCESS, cancellationToken)
            ?? throw new InvalidOperationException("Copia local no encontrada o no esta lista.");
        if (!File.Exists(backup.RutaArchivo))
        {
            throw new InvalidOperationException("El archivo local de la copia no existe.");
        }

        await UploadBackupAsync(backup, backup.RutaArchivo, cancellationToken);
    }

    // V-02.07 (retencion de PII en la nube): la retencion local de BackupService
    // borraba el fichero del disco pero nunca el objeto subido a Drive, asi que
    // el dump completo (con toda la PII) se quedaba en la nube indefinidamente.
    // Este metodo borra el fichero remoto por su RemoteFileId y marca la copia
    // como retirada. Un 404 de Drive (el fichero ya no existe) se trata como
    // exito idempotente. Cualquier otro fallo se registra en
    // BackupCloudCopy.ErrorCode/ErrorMessage sin lanzar excepcion, para que la
    // retencion del resto de backups no se aborte.
    public async Task DeleteRemoteBackupCopyAsync(BackupCloudCopy copy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(copy.RemoteFileId))
        {
            copy.DeletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var connection = await LoadActiveConnectionAsync(cancellationToken);
            var accessToken = await RefreshAccessTokenAsync(connection, cancellationToken);
            var client = CreateGoogleApiClient(accessToken);
            using var response = await client.DeleteAsync(
                $"drive/v3/files/{Uri.EscapeDataString(copy.RemoteFileId)}",
                cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                copy.DeletedAt = DateTime.UtcNow;
                copy.ErrorCode = null;
                copy.ErrorMessage = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            copy.ErrorCode = $"google_drive_delete_http_{(int)response.StatusCode}";
            copy.ErrorMessage = "No se pudo borrar la copia remota en Google Drive.";
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no esta vinculado", StringComparison.OrdinalIgnoreCase))
        {
            copy.ErrorCode = "google_drive_not_linked";
            copy.ErrorMessage = "Google Drive no esta vinculado.";
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            copy.ErrorCode = ClassifyError(ex);
            copy.ErrorMessage = BuildSafeErrorMessage(ex);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(ex, "Fallo al borrar copia remota {CopyId} de Google Drive", copy.Id);
        }
    }

    public async Task<IReadOnlyList<GoogleDriveBackupFileResponse>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var connection = await LoadActiveConnectionAsync(cancellationToken);
        var accessToken = await RefreshAccessTokenAsync(connection, cancellationToken);
        var query = Uri.EscapeDataString("appProperties has { key='atlas_backup' and value='true' } and trashed=false");
        var client = CreateGoogleApiClient(accessToken);
        using var response = await client.GetAsync(
            $"drive/v3/files?pageSize=50&orderBy=createdTime desc&q={query}&fields=files(id,name,size,createdTime)",
            cancellationToken);
        var payload = await ReadJsonAsync<GoogleDriveFileListResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || payload is null)
        {
            throw new InvalidOperationException("No se pudieron listar las copias de Google Drive.");
        }

        return payload.Files.Select(file => new GoogleDriveBackupFileResponse
        {
            FileId = file.Id ?? string.Empty,
            Name = file.Name ?? string.Empty,
            SizeBytes = ParseLong(file.Size),
            CreatedTime = file.CreatedTime
        }).Where(x => !string.IsNullOrWhiteSpace(x.FileId)).ToList();
    }

    public async Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        if (!IsSafeGoogleIdentifier(fileId))
        {
            throw new InvalidOperationException("Identificador de archivo de Google Drive invalido.");
        }

        var connection = await LoadActiveConnectionAsync(cancellationToken);
        var accessToken = await RefreshAccessTokenAsync(connection, cancellationToken);
        var metadata = await GetFileMetadataAsync(accessToken, fileId, cancellationToken);
        var backupRoot = await ResolveBackupDirectoryAsync(cancellationToken);
        Directory.CreateDirectory(backupRoot);

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var encryptedPath = Path.Combine(backupRoot, $"drive_import_{stamp}.dump.enc");
        var dumpPath = Path.Combine(backupRoot, $"drive_import_{stamp}.dump");

        var keepDump = false;
        try
        {
            await DownloadFileAsync(accessToken, fileId, encryptedPath, cancellationToken);

        // V-02.06 (PR F3): la verificacion se hace sobre el `.enc` descargado
        // (mismo dominio que el que se almaceno en upload). Antes se comparaba
        // el dump descifrado contra el SHA-256 del cifrado, lo que rechaza
        // cualquier copia valida por ser dominios cruzados. Si no hay
        // `BackupCloudCopy` registrada (importacion manual sin upload previo),
        // aceptamos el archivo y lo dejamos sin ancla de integridad.
        var originalCopy = await _dbContext.BackupCloudCopies
            .IgnoreQueryFilters()
            .Where(c => c.RemoteFileId == fileId && c.Provider == ProviderName && !string.IsNullOrEmpty(c.ChecksumSha256))
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync(cancellationToken);

        if (originalCopy is not null)
        {
            var actualHash = await ComputeSha256Async(encryptedPath, cancellationToken);
            if (!string.Equals(actualHash, originalCopy.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "SHA-256 del archivo cifrado descargado no coincide con el registrado para " + LogScrubber.Scrub(fileId) +
                    " (BackupCloudCopy=" + originalCopy.Id + "). Posible corrupcion o alteracion del archivo en Drive.");
            }
        }
        else
        {
            _logger.LogWarning(
                "Import desde Google Drive sin BackupCloudCopy original para {FileIdSafe} (o sin ChecksumSha256 registrado). Se acepta el archivo sin verificacion de integridad.",
                LogScrubber.Scrub(fileId));
        }

        // Solo desciframos si la verificacion pasa o no hay registro contra
        // el que comparar; asi una copia manipulada se rechaza sin tocar el
        // dump plaintext.
        await _encryptionService.DecryptAsync(encryptedPath, dumpPath, cancellationToken);
        var backup = new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow,
            RutaArchivo = dumpPath,
            TamanioBytes = new FileInfo(dumpPath).Length,
            Estado = EstadoProceso.SUCCESS,
            Tipo = TipoProceso.MANUAL,
            IniciadoPorId = userId,
            Notas = "Importado desde Google Drive"
        };

        _dbContext.Backups.Add(backup);
        _dbContext.BackupCloudCopies.Add(new BackupCloudCopy
        {
            Id = Guid.NewGuid(),
            BackupId = backup.Id,
            ConnectionId = connection.Id,
            Provider = ProviderName,
            Estado = "IMPORTED",
            RemoteFileId = fileId,
            RemoteFileName = metadata.Name,
            RemoteSizeBytes = ParseLong(metadata.Size),
            FechaCreacion = DateTime.UtcNow,
            UploadedAt = metadata.CreatedTime
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            userId,
            AuditActions.BackupCloudImport,
            "BACKUPS",
            backup.Id,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            JsonSerializer.Serialize(new { provider = ProviderName, file_id = fileId, file_name = metadata.Name }),
            cancellationToken);

        keepDump = true;
        return backup;
        }
        finally
        {
            TryDelete(encryptedPath);
            if (!keepDump)
            {
                TryDelete(dumpPath);
            }
        }
    }

    private async Task<OAuthConfig> LoadOAuthConfigAsync(CancellationToken cancellationToken)
    {
        var config = await LoadConfigMapAsync(cancellationToken);
        var clientId = BackupConfigurationService.GetValue(config, "google_drive_oauth_client_id").Trim();
        var storedSecret = BackupConfigurationService.GetValue(config, "google_drive_oauth_client_secret");
        var clientSecret = _secretProtector.UnprotectFromStorage(storedSecret)?.Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Falta configurar el OAuth Client ID y Client Secret de Google Drive.");
        }

        return new OAuthConfig(clientId, clientSecret);
    }

    private async Task<Dictionary<string, string>> LoadConfigMapAsync(CancellationToken cancellationToken) =>
        await _dbContext.Configuraciones
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, cancellationToken);

    private async Task<BackupCloudConnection> LoadActiveConnectionAsync(CancellationToken cancellationToken) =>
        await _dbContext.BackupCloudConnections
            .Where(x => x.Provider == ProviderName)
            .OrderByDescending(x => x.ConnectedAt)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("Google Drive no esta vinculado.");

    private async Task StoreConnectionAsync(string refreshToken, string scope, string? accountEmail, Guid? userId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.BackupCloudConnections
            .Where(x => x.Provider == ProviderName)
            .ToListAsync(cancellationToken);
        foreach (var connection in existing)
        {
            connection.DeletedAt = DateTime.UtcNow;
            connection.DeletedById = userId;
            connection.Estado = "REPLACED";
        }

        _dbContext.BackupCloudConnections.Add(new BackupCloudConnection
        {
            Id = Guid.NewGuid(),
            Provider = ProviderName,
            Estado = "CONNECTED",
            AccountEmail = accountEmail,
            Scope = scope,
            RefreshToken = _secretProtector.ProtectForStorage(refreshToken),
            ConnectedAt = DateTime.UtcNow,
            LastValidatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> RefreshAccessTokenAsync(BackupCloudConnection connection, CancellationToken cancellationToken)
    {
        var oauth = await LoadOAuthConfigAsync(cancellationToken);
        var refreshToken = _secretProtector.UnprotectFromStorage(connection.RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("La vinculacion de Google Drive no tiene refresh token valido.");
        }

        var client = _httpClientFactory.CreateClient("google-oauth");
        using var response = await client.PostAsync(
            "token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = oauth.ClientId,
                ["client_secret"] = oauth.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken);

        var payload = await ReadJsonAsync<GoogleTokenResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new InvalidOperationException("Google rechazo el refresh token. Vuelve a vincular Google Drive.");
        }

        return payload.AccessToken;
    }

    private async Task<string?> ResolveAccountEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = CreateGoogleApiClient(accessToken);
        using var response = await client.GetAsync("oauth2/v2/userinfo", cancellationToken);
        var userInfo = await ReadJsonAsync<GoogleUserInfoResponse>(response, cancellationToken);
        return response.IsSuccessStatusCode ? userInfo?.Email : null;
    }

    private async Task<string?> ResolveFolderIdAsync(string accessToken, IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken)
    {
        var configured = BackupConfigurationService.GetValue(config, "google_drive_folder_id");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var client = CreateGoogleApiClient(accessToken);
        var request = new GoogleDriveCreateFileRequest
        {
            Name = "Atlas Balance Backups",
            MimeType = "application/vnd.google-apps.folder"
        };
        using var response = await client.PostAsync(
            "drive/v3/files?fields=id",
            JsonContent(request),
            cancellationToken);
        var folder = await ReadJsonAsync<GoogleDriveFileResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(folder?.Id))
        {
            throw new InvalidOperationException("No se pudo crear la carpeta de Google Drive.");
        }

        var row = await _dbContext.Configuraciones.FirstOrDefaultAsync(x => x.Clave == "google_drive_folder_id", cancellationToken);
        if (row is null)
        {
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = "google_drive_folder_id",
                Valor = folder.Id,
                Tipo = "string",
                Descripcion = "Carpeta de Google Drive para backups",
                FechaModificacion = DateTime.UtcNow
            });
        }
        else
        {
            row.Valor = folder.Id;
            row.FechaModificacion = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return folder.Id;
    }

    private async Task<GoogleDriveFileResponse> UploadFileAsync(
        string accessToken,
        string filePath,
        long length,
        string remoteName,
        string? folderId,
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var client = CreateGoogleApiClient(accessToken);
        var metadata = new GoogleDriveCreateFileRequest
        {
            Name = remoteName,
            Parents = string.IsNullOrWhiteSpace(folderId) ? null : [folderId],
            AppProperties = new Dictionary<string, string>
            {
                ["atlas_backup"] = "true",
                ["backup_id"] = backupId.ToString("N")
            }
        };

        using var start = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,name,size,createdTime");
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        start.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
        start.Headers.Add("X-Upload-Content-Length", length.ToString(CultureInfo.InvariantCulture));
        start.Content = JsonContent(metadata);

        using var startResponse = await client.SendAsync(start, cancellationToken);
        if (!startResponse.IsSuccessStatusCode || startResponse.Headers.Location is null)
        {
            throw new InvalidOperationException("Google no inicio la subida resumible.");
        }

        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, startResponse.Headers.Location)
        {
            Content = new StreamContent(file)
        };
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        uploadRequest.Content.Headers.ContentLength = length;

        using var uploadResponse = await client.SendAsync(uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var uploaded = await ReadJsonAsync<GoogleDriveFileResponse>(uploadResponse, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode || string.IsNullOrWhiteSpace(uploaded?.Id))
        {
            throw new InvalidOperationException("Google no completo la subida de la copia.");
        }

        return uploaded;
    }

    private async Task<GoogleDriveFileResponse> GetFileMetadataAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        var client = CreateGoogleApiClient(accessToken);
        using var response = await client.GetAsync($"drive/v3/files/{Uri.EscapeDataString(fileId)}?fields=id,name,size,createdTime", cancellationToken);
        var metadata = await ReadJsonAsync<GoogleDriveFileResponse>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || metadata is null)
        {
            throw new InvalidOperationException("No se pudo leer la metadata del archivo de Google Drive.");
        }

        return metadata;
    }

    private async Task DownloadFileAsync(string accessToken, string fileId, string destinationPath, CancellationToken cancellationToken)
    {
        var client = CreateGoogleApiClient(accessToken);
        using var response = await client.GetAsync($"drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("No se pudo descargar la copia desde Google Drive.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private async Task<string> ResolveBackupDirectoryAsync(CancellationToken cancellationToken)
    {
        var raw = await _dbContext.Configuraciones
            .Where(x => x.Clave == "backup_path")
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken) ?? @"C:\atlas-balance\backups";
        return ResolveSafeDirectory(raw);
    }

    private HttpClient CreateGoogleApiClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient("google-apis");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string ParseGoogleError(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<GoogleErrorResponse>(raw, JsonOptions)?.Error ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string CacheKey(Guid sessionId) => $"google-drive-device-link:{sessionId:N}";

    private static string ClassifyError(Exception ex) =>
        ex switch
        {
            HttpRequestException http when http.StatusCode == HttpStatusCode.Unauthorized => "google_unauthorized",
            HttpRequestException http when http.StatusCode == HttpStatusCode.Forbidden => "google_forbidden",
            OperationCanceledException => "timeout_or_cancelled",
            CryptographicException => "encryption_error",
            _ => "google_drive_upload_error"
        };

    private static string BuildSafeErrorMessage(Exception ex) =>
        ex switch
        {
            OperationCanceledException => "La subida a Google Drive fue cancelada o excedio el tiempo disponible.",
            CryptographicException => "No se pudo cifrar la copia antes de subirla.",
            _ => "No se pudo completar la operacion con Google Drive."
        };

    private static bool IsSafeGoogleIdentifier(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is >= 8 and <= 256 &&
               trimmed.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
    }

    private static string ResolveSafeDirectory(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("La ruta local de backups no es valida.");
        }

        var trimmed = rawPath.Trim();

        // V-02.07: este era el cuarto lector de `backup_path` y se quedo sin el
        // rechazo de rutas UNC que si tienen ConfiguracionController, BackupService
        // y ExportacionService. Path.IsPathRooted(@"\\servidor\recurso") devuelve
        // true en Windows, asi que una UNC pasaba y las copias descargadas de Drive
        // acababan en un recurso SMB remoto.
        if (IsUncPath(trimmed))
        {
            throw new InvalidOperationException("La ruta local de backups no puede ser una ruta de red (UNC).");
        }

        if (!Path.IsPathRooted(trimmed) && !LooksLikeWindowsRootedPath(trimmed))
        {
            throw new InvalidOperationException("La ruta local de backups debe ser absoluta.");
        }

        return Path.GetFullPath(trimmed);
    }

    private static bool IsUncPath(string value)
    {
        return value.StartsWith(@"\\", StringComparison.Ordinal) ||
               value.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool LooksLikeWindowsRootedPath(string value) =>
        value.Length >= 3 &&
        char.IsLetter(value[0]) &&
        value[1] == ':' &&
        (value[2] == '\\' || value[2] == '/');

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary encrypted artifacts are cleaned best-effort only.
        }
    }

    private sealed record OAuthConfig(string ClientId, string ClientSecret);

    private sealed record GoogleDriveDeviceSession(
        Guid SessionId,
        string DeviceCode,
        string UserCode,
        string VerificationUrl,
        DateTime ExpiresAt,
        int IntervalSeconds,
        DateTime LastPollAt);

    private sealed class GoogleDeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("verification_url")]
        public string VerificationUrl { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 5;
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class GoogleErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class GoogleUserInfoResponse
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    private sealed class GoogleDriveCreateFileRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("parents")]
        public IReadOnlyList<string>? Parents { get; set; }

        [JsonPropertyName("appProperties")]
        public Dictionary<string, string>? AppProperties { get; set; }
    }

    private sealed class GoogleDriveFileResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        [JsonPropertyName("createdTime")]
        public DateTime? CreatedTime { get; set; }
    }

    private sealed class GoogleDriveFileListResponse
    {
        [JsonPropertyName("files")]
        public List<GoogleDriveFileResponse> Files { get; set; } = [];
    }

    /// <summary>
    /// V-02-05 (HIGH-2): calcula SHA-256 de un archivo en disco. Usado para
    /// verificar la integridad del dump descifrado contra el ChecksumSha256
    /// del BackupCloudCopy original.
    /// </summary>
    // V-02.06 (HIGH-2): exponer como internal para que los tests puedan
    // verificar el helper de hashing que valida el SHA-256 del dump
    // descifrado. El flujo integral (descarga + descifrado + verificacion)
    // requiere mocks de HttpClient y IBackupEncryptionService y se cubre
    // por tests de integracion contra el API real en F4.
    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
