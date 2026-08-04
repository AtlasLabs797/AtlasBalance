using System.ComponentModel.DataAnnotations;

namespace AtlasBalance.Watchdog.Models;

public sealed class RestaurarBackupRequest
{
    [Required]
    [StringLength(1024)]
    [RegularExpression(@"^[^\u0000-\u001F]+$")]
    public string BackupPath { get; set; } = string.Empty;

    [Required]
    public Guid? OperationId { get; set; }
}

public sealed class ActualizarAppRequest
{
    [Required]
    [StringLength(1024)]
    [RegularExpression(@"^[^\u0000-\u001F]+$")]
    public string? SourcePath { get; set; }

    [Required]
    [StringLength(1024)]
    [RegularExpression(@"^[^\u0000-\u001F]+$")]
    public string? TargetPath { get; set; }

    [Required]
    [StringLength(1024)]
    [RegularExpression(@"(?i)^[^\u0000-\u001F]+\.zip$")]
    public string? PackageZipPath { get; set; }
}

public sealed class WatchdogState
{
    public string Estado { get; set; } = "IDLE";
    public string? Operacion { get; set; }
    public Guid? OperationId { get; set; }
    public string? Mensaje { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
