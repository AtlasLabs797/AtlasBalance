namespace AtlasBalance.API.DTOs;

public sealed class MovimientoEsperadoCrearRequest
{
    public Guid CuentaId { get; set; }
    public DateOnly FechaEsperada { get; set; }
    public decimal Monto { get; set; }
    public string? Divisa { get; set; }
    public string? Referencia { get; set; }
    public string? Concepto { get; set; }
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
