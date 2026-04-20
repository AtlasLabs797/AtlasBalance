namespace GestionCaja.Watchdog.Models;

public sealed class RestaurarBackupRequest
{
    public string BackupPath { get; set; } = string.Empty;
    public Guid? SolicitadoPorId { get; set; }
}

public sealed class ActualizarAppRequest
{
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
}

public sealed class WatchdogState
{
    public string Estado { get; set; } = "IDLE";
    public string? Operacion { get; set; }
    public string? Mensaje { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
