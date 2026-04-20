namespace GestionCaja.API.DTOs;

public sealed class VersionActualResponse
{
    public string VersionActual { get; set; } = "0.0.0";
}

public sealed class VersionDisponibleResponse
{
    public string VersionActual { get; set; } = "0.0.0";
    public string? VersionDisponible { get; set; }
    public bool ActualizacionDisponible { get; set; }
    public string? Mensaje { get; set; }
}

public sealed class ActualizacionRequest
{
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
}
