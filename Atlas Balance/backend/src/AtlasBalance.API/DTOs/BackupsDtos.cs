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
}

public sealed class RestaurarBackupRequest
{
    public string? Confirmacion { get; set; }
}

public sealed class WatchdogStateResponse
{
    public string Estado { get; set; } = "IDLE";
    public string? Operacion { get; set; }
    public string? Mensaje { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
