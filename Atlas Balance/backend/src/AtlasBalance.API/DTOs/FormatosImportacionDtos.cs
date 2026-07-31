using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasBalance.API.DTOs;

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
    // V-02.07: sin [Required] a proposito, a diferencia de lo que pedia la tabla
    // de limites. FormatosImportacionController.ResolveFormatoNombre ignora Nombre
    // en la practica siempre que BancoNombre (que si es obligatorio, comprobado a
    // mano mas abajo en el controller) venga informado: usa BancoNombre.Trim() y
    // solo cae a Nombre si BancoNombre esta vacio. Exigir Nombre aqui rechazaria
    // peticiones validas que ya funcionan hoy sin mandarlo. MaxLength(200) si se
    // mantiene porque es inofensivo (el valor se ignora o se recorta igual).
    [MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? BancoNombre { get; set; }
    [MaxLength(8)]
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
    public string? Etiqueta { get; set; }
}

public sealed class ListarColumnasExtraSugeridasResponse
{
    public IReadOnlyList<string> Data { get; set; } = Array.Empty<string>();
}
