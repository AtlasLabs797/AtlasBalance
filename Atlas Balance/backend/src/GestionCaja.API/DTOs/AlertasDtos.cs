namespace GestionCaja.API.DTOs;

public sealed class SaveAlertaSaldoRequest
{
    public Guid? CuentaId { get; set; }
    public decimal SaldoMinimo { get; set; }
    public bool Activa { get; set; } = true;
    public IReadOnlyList<Guid> DestinatarioUsuarioIds { get; set; } = [];
}

public sealed class AlertaDestinatarioItemResponse
{
    public Guid UsuarioId { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string EmailLogin { get; set; } = string.Empty;
}

public sealed class AlertaSaldoItemResponse
{
    public Guid Id { get; set; }
    public Guid? CuentaId { get; set; }
    public string? CuentaNombre { get; set; }
    public Guid? TitularId { get; set; }
    public string? TitularNombre { get; set; }
    public string? Divisa { get; set; }
    public decimal SaldoMinimo { get; set; }
    public bool Activa { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaUltimaAlerta { get; set; }
    public IReadOnlyList<AlertaDestinatarioItemResponse> Destinatarios { get; set; } = [];
}

public sealed class AlertaActivaItemResponse
{
    public Guid AlertaId { get; set; }
    public Guid CuentaId { get; set; }
    public Guid TitularId { get; set; }
    public string CuentaNombre { get; set; } = string.Empty;
    public string TitularNombre { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public decimal SaldoActual { get; set; }
    public decimal SaldoMinimo { get; set; }
}

public sealed class AlertaContextoCuentaResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
}

public sealed class AlertaContextoUsuarioResponse
{
    public Guid Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
