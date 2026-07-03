using System.Text.Json.Serialization;

namespace AtlasBalance.API.DTOs;

public sealed class ExtractoListItemResponse
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public string CuentaNombre { get; set; } = string.Empty;
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public Guid? PaisId { get; set; }
    public string Divisa { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public string? Concepto { get; set; }
    public string? Comentarios { get; set; }
    public decimal Monto { get; set; }
    public decimal Saldo { get; set; }
    public int FilaNumero { get; set; }
    public bool Checked { get; set; }
    public DateTime? CheckedAt { get; set; }
    public Guid? CheckedById { get; set; }
    public bool Flagged { get; set; }
    public string? FlaggedNota { get; set; }
    public DateTime? FlaggedAt { get; set; }
    public Guid? FlaggedById { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Dictionary<string, string?> ColumnasExtra { get; set; } = [];
}

public sealed class CreateExtractoRequest
{
    public Guid CuentaId { get; set; }
    public int? InsertBeforeFilaNumero { get; set; }
    public DateOnly Fecha { get; set; }
    public string? Concepto { get; set; }
    public string? Comentarios { get; set; }
    public decimal Monto { get; set; }
    public decimal Saldo { get; set; }
    public Dictionary<string, string?>? ColumnasExtra { get; set; }
}

public sealed class UpdateExtractoRequest
{
    public DateOnly? Fecha { get; set; }
    public string? Concepto { get; set; }
    public string? Comentarios { get; set; }
    public decimal? Monto { get; set; }
    public decimal? Saldo { get; set; }
    public Dictionary<string, string?>? ColumnasExtra { get; set; }
}

public sealed class ToggleCheckedRequest
{
    public bool Checked { get; set; }
}

public sealed class ToggleFlagRequest
{
    public bool Flagged { get; set; }
    public string? Nota { get; set; }
}

public sealed class AuditCellEntryResponse
{
    public Guid Id { get; set; }
    public string TipoAccion { get; set; } = string.Empty;
    public string? CeldaReferencia { get; set; }
    public string? ColumnaNombre { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? UsuarioId { get; set; }
}

public sealed class CuentaResumenKpiResponse
{
    public Guid CuentaId { get; set; }
    public string CuentaNombre { get; set; } = string.Empty;
    public string? Iban { get; set; }
    public string? BancoNombre { get; set; }
    public string Divisa { get; set; } = string.Empty;
    public Guid? PaisId { get; set; }
    public string? PaisNombre { get; set; }
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public bool EsEfectivo { get; set; }
    public string TipoCuenta { get; set; } = "NORMAL";
    public PlazoFijoResponse? PlazoFijo { get; set; }
    public string? Notas { get; set; }
    public decimal SaldoActual { get; set; }
    public decimal IngresosMes { get; set; }
    public decimal EgresosMes { get; set; }
    public DateTime? UltimaActualizacion { get; set; }
}

public sealed class TitularConCuentasResponse
{
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public IReadOnlyList<CuentaResumenKpiResponse> Cuentas { get; set; } = [];
}

public sealed class SaveColumnasVisiblesRequest
{
    // BUG-COLUMNAS (V-02-04): los ids de scope llegan de estado de cliente
    // (URL, localStorage, bundles antiguos). Un valor vacio o no-GUID
    // producia un 400 de model binding y el toggle de columnas se revertia
    // en silencio. Para una preferencia de UI, un scope irreconocible debe
    // degradar a scope global (null), no rechazar el guardado.
    [JsonPropertyName("cuenta_id")]
    [JsonConverter(typeof(LenientNullableGuidJsonConverter))]
    public Guid? CuentaId { get; set; }

    [JsonPropertyName("titular_id")]
    [JsonConverter(typeof(LenientNullableGuidJsonConverter))]
    public Guid? TitularId { get; set; }

    [JsonPropertyName("pais_id")]
    [JsonConverter(typeof(LenientNullableGuidJsonConverter))]
    public Guid? PaisId { get; set; }

    [JsonPropertyName("columnas_visibles")]
    public IReadOnlyList<string>? ColumnasVisibles { get; set; }
}

public sealed class LenientNullableGuidJsonConverter : System.Text.Json.Serialization.JsonConverter<Guid?>
{
    public override Guid? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String &&
            Guid.TryParse(reader.GetString(), out var parsed))
        {
            return parsed;
        }

        // Consumir contenedores completos si llegara un objeto/array inesperado.
        reader.Skip();
        return null;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, Guid? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
