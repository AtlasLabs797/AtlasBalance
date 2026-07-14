namespace AtlasBalance.API.DTOs;

public sealed class VersionActualResponse
{
    public string VersionActual { get; set; } = "0.0.0";
}

public sealed class VersionDisponibleResponse
{
    public string VersionActual { get; set; } = "0.0.0";
    public string? VersionDisponible { get; set; }
    public bool ActualizacionDisponible { get; set; }
    public bool Instalable { get; set; }
    public IReadOnlyList<string> Bloqueos { get; set; } = [];
    public string? AssetZipNombre { get; set; }
    public bool AssetZipDetectado { get; set; }
    public bool FirmaDetectada { get; set; }
    public bool DigestPresente { get; set; }
    public bool ClavePublicaConfigurada { get; set; }
    public bool WatchdogDisponible { get; set; }
    public string? Mensaje { get; set; }
}

public sealed class ActualizacionRequest
{
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
}
