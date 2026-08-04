using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.DTOs;

public sealed class SaveAlertaSaldoRequest
{
    public Guid? CuentaId { get; set; }
    // V-02.07: el enum es nativo de Postgres; un entero fuera de rango pasaba el
    // converter y moria en Npgsql como 500. Ver UsuariosDtos.
    [EnumDataType(typeof(TipoTitular))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoTitular? TipoTitular { get; set; }
    // V-02.07: el controller solo rechazaba negativos, sin techo. La columna es
    // HasPrecision(18,4), asi que un valor enorme reventaba en SaveChangesAsync
    // como 500 en vez de dar un 400.
    [Range(typeof(decimal), "0", "9999999999.9999", ParseLimitsInInvariantCulture = true)]
    public decimal SaldoMinimo { get; set; }
    public bool Activa { get; set; } = true;
    [MaxLength(100)]
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
    public string Alcance { get; set; } = "GLOBAL";
    public string? TipoTitular { get; set; }
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
    public string TipoTitular { get; set; } = string.Empty;
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
