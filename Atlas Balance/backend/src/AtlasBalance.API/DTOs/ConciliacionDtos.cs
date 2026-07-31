using System.ComponentModel.DataAnnotations;

namespace AtlasBalance.API.DTOs;

public sealed class MovimientoEsperadoCrearRequest
{
    public Guid CuentaId { get; set; }
    public DateOnly FechaEsperada { get; set; }
    // V-02.07: el limite espeja el rango de CreateExtractoRequest.Monto en
    // ImportacionDtos/ExtractosDtos. ParseLimitsInInvariantCulture es obligatorio:
    // el servidor corre en es-ES (coma decimal) y sin el, RangeAttribute revienta
    // con FormatException al parsear los limites y el endpoint responde 500.
    [Range(typeof(decimal), "-9999999999.9999", "9999999999.9999", ParseLimitsInInvariantCulture = true)]
    public decimal Monto { get; set; }
    // V-02.07: ConciliacionService asigna Divisa/Referencia/Concepto directo desde
    // el request (con Trim, sin truncar) a MovimientoEsperado, que en AppDbContext
    // tiene HasMaxLength(8/128/512). Sin estos limites, un valor mas largo revienta
    // SaveChangesAsync con un 500 en vez de un 400.
    [MaxLength(8)]
    public string? Divisa { get; set; }
    [MaxLength(128)]
    public string? Referencia { get; set; }
    [MaxLength(512)]
    public string? Concepto { get; set; }
    // Mismo caso que los tres de arriba: ConciliacionService lo asigna directo y
    // la columna es HasMaxLength(32).IsRequired().
    [MaxLength(32)]
    public string Origen { get; set; } = "manual";
}

public sealed class MovimientoEsperadoResponse
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public string? CuentaNombre { get; set; }
    public DateOnly FechaEsperada { get; set; }
    public decimal Monto { get; set; }
    public string Divisa { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public string? Concepto { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public Guid? UsuarioCreacionId { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public sealed class ConciliacionSugerirRequest
{
    public Guid? CuentaId { get; set; }
    public int VentanaDias { get; set; } = 3;
}

public sealed class ConciliacionCambiarEstadoRequest
{
    // V-02.07: espeja Conciliacion.Observacion, HasMaxLength(1000) en AppDbContext.
    // ConciliacionService lo asigna directo (solo Trim, sin truncar) a la entidad.
    [MaxLength(1000)]
    public string? Observacion { get; set; }
}

public sealed class ConciliacionResponse
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public string? CuentaNombre { get; set; }
    public Guid MovimientoEsperadoId { get; set; }
    public Guid? ExtractoId { get; set; }
    public string Estado { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Regla { get; set; } = string.Empty;
    public int DiferenciaDias { get; set; }
    public string? ReferenciaNormalizada { get; set; }
    public string? ConceptoNormalizado { get; set; }
    public string? Observacion { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public MovimientoEsperadoResponse? MovimientoEsperado { get; set; }
    public ExtractoConciliacionResponse? Extracto { get; set; }
}

public sealed class ExtractoConciliacionResponse
{
    public Guid Id { get; set; }
    public DateOnly Fecha { get; set; }
    public string? Concepto { get; set; }
    public decimal Monto { get; set; }
    public decimal Saldo { get; set; }
    public int FilaNumero { get; set; }
}

public sealed class ConciliacionSugerenciasResponse
{
    public int MovimientosEvaluados { get; set; }
    public int SugerenciasCreadas { get; set; }
    public IReadOnlyList<ConciliacionResponse> Sugerencias { get; set; } = [];
}
