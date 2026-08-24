using System.ComponentModel.DataAnnotations;

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
    // V-02.09: tope MAX_PATH (260) para espejar el limite de Windows y que
    // [ApiController] rechace con 400 un payload fuera de cota. Admin-only
    // (SistemaController) y el servicio ya validaba, pero la cota tiene que
    // vivir en el DTO para que ModelState la vea.
    [MaxLength(260)]
    public string? SourcePath { get; set; }
    [MaxLength(260)]
    public string? TargetPath { get; set; }
}
