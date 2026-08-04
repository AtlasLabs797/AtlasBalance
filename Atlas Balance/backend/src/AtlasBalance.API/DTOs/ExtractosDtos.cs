using System.ComponentModel.DataAnnotations;
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
    public int DesgloseCount { get; set; }
    public decimal DesgloseTotal { get; set; }
    public string DesgloseEstado { get; set; } = "sin_desglose";
}

public sealed class CreateExtractoRequest
{
    public Guid CuentaId { get; set; }
    public int? InsertBeforeFilaNumero { get; set; }
    public DateOnly Fecha { get; set; }
    [MaxLength(ExtractoLimits.ConceptoMaxLength)]
    public string? Concepto { get; set; }
    [MaxLength(ExtractoLimits.ComentariosMaxLength)]
    public string? Comentarios { get; set; }
    [Range(typeof(decimal), ExtractoLimits.ImporteMin, ExtractoLimits.ImporteMax, ParseLimitsInInvariantCulture = true)]
    public decimal Monto { get; set; }
    [Range(typeof(decimal), ExtractoLimits.ImporteMin, ExtractoLimits.ImporteMax, ParseLimitsInInvariantCulture = true)]
    public decimal Saldo { get; set; }
    [MaxLength(ExtractoLimits.ColumnasExtraMaxEntries)]
    public Dictionary<string, string?>? ColumnasExtra { get; set; }
}

public sealed class UpdateExtractoRequest
{
    public DateOnly? Fecha { get; set; }
    [MaxLength(ExtractoLimits.ConceptoMaxLength)]
    public string? Concepto { get; set; }
    [MaxLength(ExtractoLimits.ComentariosMaxLength)]
    public string? Comentarios { get; set; }
    [Range(typeof(decimal), ExtractoLimits.ImporteMin, ExtractoLimits.ImporteMax, ParseLimitsInInvariantCulture = true)]
    public decimal? Monto { get; set; }
    [Range(typeof(decimal), ExtractoLimits.ImporteMin, ExtractoLimits.ImporteMax, ParseLimitsInInvariantCulture = true)]
    public decimal? Saldo { get; set; }
    [MaxLength(ExtractoLimits.ColumnasExtraMaxEntries)]
    public Dictionary<string, string?>? ColumnasExtra { get; set; }
}

/// <summary>
/// Limites de los campos de extracto. Viven en constantes porque el mismo rango
/// se repite en create y update y <c>[Range]</c> exige literales constantes.
///
/// V-02.07: <c>Monto</c> y <c>Saldo</c> aceptaban cualquier decimal y el unico
/// tope era la precision (18,4) de la columna, que no da un 400 sino una
/// <c>DbUpdateException</c> convertida en 500. El rango es simetrico porque un
/// egreso es negativo, y se queda en 10 digitos enteros -igual que el
/// <c>[Range]</c> que ya usaba ImportacionDtos- para no acercarse al limite de
/// la columna.
///
/// Los <c>[Range]</c> llevan <c>ParseLimitsInInvariantCulture = true</c> a
/// proposito: sin eso los limites se parsean con la cultura del proceso y en
/// es-ES (la del servidor) el separador decimal es la coma, asi que
/// DecimalConverter lanza FormatException y la peticion acaba en 500 en vez de
/// validarse. Ver DtoValidationTests.
/// </summary>
internal static class ExtractoLimits
{
    public const string ImporteMin = "-9999999999.9999";
    public const string ImporteMax = "9999999999.9999";
    public const int ConceptoMaxLength = 512;
    public const int ComentariosMaxLength = 1000;

    // Topes de coleccion. No son reglas de negocio: solo cierran la via de
    // reservar memoria sin limite desde una peticion. Van holgados a proposito
    // para no chocar nunca con un uso real.
    public const int ColumnasExtraMaxEntries = 100;
    public const int DesgloseMaxLineas = 500;
    public const int ColumnasVisiblesMaxEntries = 200;
}

public sealed class ExtractoDesgloseResponse
{
    public Guid Id { get; set; }
    public Guid ExtractoId { get; set; }
    public int Orden { get; set; }
    public string TerceroNombre { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
}

public sealed class ExtractoDesgloseResumenResponse
{
    public Guid ExtractoId { get; set; }
    public decimal ExtractoMonto { get; set; }
    public int Count { get; set; }
    public decimal Total { get; set; }
    public decimal Diferencia { get; set; }
    public string Estado { get; set; } = "sin_desglose";
    public string Version { get; set; } = string.Empty;
    public IReadOnlyList<ExtractoDesgloseResponse> Lineas { get; set; } = [];
}

public sealed class ExtractoDesgloseUpsertRequest
{
    [MaxLength(64)]
    public string? Version { get; set; }
    [MaxLength(ExtractoLimits.DesgloseMaxLineas)]
    public IReadOnlyList<ExtractoDesgloseLineaRequest>? Lineas { get; set; }
}

public sealed class ExtractoDesgloseLineaRequest
{
    public Guid? Id { get; set; }
    // Sin [Required] a proposito: ExtractosController ya rechaza la linea sin
    // tercero con un mensaje que dice cual falla ("La linea 3 necesita nombre de
    // persona o tercero"). Una anotacion aqui se adelantaria con un error
    // generico y peor. El 256 si falta: es el tope de la columna y hoy lo unico
    // que lo aplica es la BD, con un 500.
    [MaxLength(256)]
    public string? TerceroNombre { get; set; }
    // El controller ya rechaza el importe cero; esto acota la magnitud.
    [Range(typeof(decimal), ExtractoLimits.ImporteMin, ExtractoLimits.ImporteMax, ParseLimitsInInvariantCulture = true)]
    public decimal Importe { get; set; }
    [MaxLength(ExtractoLimits.ComentariosMaxLength)]
    public string? Notas { get; set; }
}

public sealed class ToggleCheckedRequest
{
    public bool Checked { get; set; }
}

public sealed class ToggleFlagRequest
{
    public bool Flagged { get; set; }
    [MaxLength(ExtractoLimits.ComentariosMaxLength)]
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
    [MaxLength(ExtractoLimits.ColumnasVisiblesMaxEntries)]
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
