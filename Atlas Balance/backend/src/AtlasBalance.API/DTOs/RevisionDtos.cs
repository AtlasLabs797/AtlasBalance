namespace AtlasBalance.API.DTOs;

public sealed class RevisionSettingsResponse
{
    public decimal ComisionesImporteMinimo { get; set; } = 1m;
}

public sealed class RevisionQueryRequest
{
    public string? Estado { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class RevisionComisionItemResponse
{
    public Guid ExtractoId { get; set; }
    public Guid CuentaId { get; set; }
    public Guid TitularId { get; set; }
    public string Titular { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public decimal Monto { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string EstadoDevolucion { get; set; } = "PENDIENTE";
}

public sealed class RevisionSeguroItemResponse
{
    public Guid ExtractoId { get; set; }
    public Guid CuentaId { get; set; }
    public Guid TitularId { get; set; }
    public string Titular { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public decimal Importe { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDIENTE";
}

public sealed class UpdateRevisionEstadoRequest
{
    public string Estado { get; set; } = string.Empty;
}
