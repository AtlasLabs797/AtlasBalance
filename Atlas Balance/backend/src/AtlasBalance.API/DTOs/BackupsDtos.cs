namespace AtlasBalance.API.DTOs;

public sealed class BackupListItemResponse
{
    public Guid Id { get; set; }
    public DateTime FechaCreacion { get; set; }
    public string RutaArchivo { get; set; } = string.Empty;
    public long? TamanioBytes { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public Guid? IniciadoPorId { get; set; }
    public string? IniciadoPorNombre { get; set; }
    public string? Notas { get; set; }
    public string Destino { get; set; } = "LOCAL";
    public string? CloudProvider { get; set; }
    public string? CloudEstado { get; set; }
    public DateTime? CloudUploadedAt { get; set; }
    public string? CloudFileId { get; set; }
    public string? CloudFileName { get; set; }
    public string? CloudErrorMessage { get; set; }
}

public sealed class RestaurarBackupRequest
{
    public string? Confirmacion { get; set; }
}

public sealed class BackupConfigResponse
{
    public bool AutoEnabled { get; set; } = true;
    public string Frequency { get; set; } = "WEEKLY";
    public string TimeUtc { get; set; } = "02:00";
    public int DayOfWeek { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public int IntervalHours { get; set; } = 24;
    public string Destination { get; set; } = "LOCAL";
    public string LastStartedUtc { get; set; } = string.Empty;
    public string LastResult { get; set; } = string.Empty;
    public GoogleDriveBackupConfigResponse GoogleDrive { get; set; } = new();
}

public sealed class GoogleDriveBackupConfigResponse
{
    public string ClientId { get; set; } = string.Empty;
    public bool ClientSecretConfigured { get; set; }
    public bool Connected { get; set; }
    public string? AccountEmail { get; set; }
    public string? FolderId { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string? LastError { get; set; }
    public bool EncryptionKeyConfigured { get; set; }
}

public sealed class UpdateBackupConfigRequest
{
    public bool AutoEnabled { get; set; } = true;
    public string Frequency { get; set; } = "WEEKLY";
    public string TimeUtc { get; set; } = "02:00";
    public int DayOfWeek { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public int IntervalHours { get; set; } = 24;
    public string Destination { get; set; } = "LOCAL";
    public string GoogleDriveClientId { get; set; } = string.Empty;
    public string GoogleDriveClientSecret { get; set; } = string.Empty;
    public string GoogleDriveFolderId { get; set; } = string.Empty;
}

public sealed class GoogleDriveLinkStartResponse
{
    public Guid SessionId { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int IntervalSeconds { get; set; }
}

public sealed class GoogleDriveLinkStatusResponse
{
    public string Estado { get; set; } = "PENDING";
    public string? Message { get; set; }
    public string? AccountEmail { get; set; }
    public int PollAfterSeconds { get; set; } = 5;
}

public sealed class GoogleDriveBackupFileResponse
{
    public string FileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public DateTime? CreatedTime { get; set; }
}

public sealed class GoogleDriveImportRequest
{
    public string? FileId { get; set; }
}

public sealed class WatchdogStateResponse
{
    public string Estado { get; set; } = "IDLE";
    public string? Operacion { get; set; }
    public string? Mensaje { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
