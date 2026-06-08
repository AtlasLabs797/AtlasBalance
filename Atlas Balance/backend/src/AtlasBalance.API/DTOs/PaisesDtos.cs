namespace AtlasBalance.API.DTOs;

public sealed class PaisResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? CodigoIso2 { get; set; }
    public bool Activo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SavePaisRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? CodigoIso2 { get; set; }
    public bool Activo { get; set; } = true;
}
