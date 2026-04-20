using System.Text.Json.Serialization;
using GestionCaja.API.Models;

namespace GestionCaja.API.DTOs;

public sealed class TitularListItemResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string? Identificacion { get; set; }
    public string? ContactoEmail { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int CuentasCount { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class TitularDetalleResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string? Identificacion { get; set; }
    public string? ContactoEmail { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int CuentasCount { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SaveTitularRequest
{
    public string Nombre { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoTitular Tipo { get; set; }
    public string? Identificacion { get; set; }
    public string? ContactoEmail { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? Notas { get; set; }
}
