namespace GestionCaja.API.DTOs;

public sealed class CuentaListItemResponse
{
    public Guid Id { get; set; }
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? NumeroCuenta { get; set; }
    public string? Iban { get; set; }
    public string? BancoNombre { get; set; }
    public string Divisa { get; set; } = "EUR";
    public Guid? FormatoId { get; set; }
    public bool EsEfectivo { get; set; }
    public bool Activa { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class CuentaResumenResponse
{
    public Guid CuentaId { get; set; }
    public decimal SaldoActual { get; set; }
    public decimal IngresosMes { get; set; }
    public decimal EgresosMes { get; set; }
}

public sealed class SaveCuentaRequest
{
    public Guid TitularId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? NumeroCuenta { get; set; }
    public string? Iban { get; set; }
    public string? BancoNombre { get; set; }
    public string Divisa { get; set; } = "EUR";
    public Guid? FormatoId { get; set; }
    public bool EsEfectivo { get; set; }
    public bool Activa { get; set; } = true;
    public string? Notas { get; set; }
}

public sealed class UpdateCuentaNotasRequest
{
    public string? Notas { get; set; }
}
