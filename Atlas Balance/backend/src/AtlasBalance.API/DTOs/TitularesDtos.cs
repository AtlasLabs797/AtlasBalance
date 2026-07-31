using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.DTOs;

public sealed class TitularListItemResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
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
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int CuentasCount { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SaveTitularRequest
{
    [Required]
    [MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;
    // V-02.07: ver el comentario de RolUsuario en UsuariosDtos. Un entero fuera
    // de rango pasa el converter y muere en Npgsql como 500; esto lo baja a 400.
    [EnumDataType(typeof(TipoTitular))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoTitular Tipo { get; set; }
    [MaxLength(4000)]
    public string? Notas { get; set; }
}
