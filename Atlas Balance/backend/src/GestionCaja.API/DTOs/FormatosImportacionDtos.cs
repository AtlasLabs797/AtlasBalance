using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestionCaja.API.DTOs;

public sealed class FormatoImportacionResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? BancoNombre { get; set; }
    public string? Divisa { get; set; }
    public JsonElement MapeoJson { get; set; }
    public bool Activo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public Guid? UsuarioCreadorId { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SaveFormatoImportacionRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? BancoNombre { get; set; }
    public string? Divisa { get; set; }
    public JsonElement MapeoJson { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class MapeoImportacionPayload
{
    [JsonPropertyName("tipo_monto")]
    public string? TipoMonto { get; set; }

    public int? Fecha { get; set; }
    public int? Concepto { get; set; }
    public int? Monto { get; set; }
    public int? Ingreso { get; set; }
    public int? Egreso { get; set; }
    public int? Saldo { get; set; }

    [JsonPropertyName("columnas_extra")]
    public IReadOnlyList<MapeoImportacionColumnaExtraPayload>? ColumnasExtra { get; set; }
}

public sealed class MapeoImportacionColumnaExtraPayload
{
    public string Nombre { get; set; } = string.Empty;
    public int Indice { get; set; }
}
