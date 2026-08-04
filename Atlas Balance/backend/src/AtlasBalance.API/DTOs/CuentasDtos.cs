using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.DTOs;

public sealed class PlazoFijoResponse
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public Guid? CuentaReferenciaId { get; set; }
    public string? CuentaReferenciaNombre { get; set; }
    public DateOnly FechaInicio { get; set; }
    public DateOnly FechaVencimiento { get; set; }
    public decimal? InteresPrevisto { get; set; }
    public bool Renovable { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateOnly? FechaUltimaNotificacion { get; set; }
    public DateOnly? FechaRenovacion { get; set; }
    public string? Notas { get; set; }
}

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
    public Guid? PaisId { get; set; }
    public string? PaisNombre { get; set; }
    public bool EsEfectivo { get; set; }
    public string TipoCuenta { get; set; } = "NORMAL";
    public string TitularTipo { get; set; } = string.Empty;
    public PlazoFijoResponse? PlazoFijo { get; set; }
    public bool Activa { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class CuentaResumenResponse
{
    public Guid CuentaId { get; set; }
    public string CuentaNombre { get; set; } = string.Empty;
    public string? Iban { get; set; }
    public string? BancoNombre { get; set; }
    public string Divisa { get; set; } = "EUR";
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

public sealed class SaveCuentaRequest
{
    public Guid TitularId { get; set; }
    [Required]
    [MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;
    [MaxLength(64)]
    public string? NumeroCuenta { get; set; }
    [MaxLength(34)]
    public string? Iban { get; set; }
    [MaxLength(200)]
    public string? BancoNombre { get; set; }
    [Required]
    [MaxLength(8)]
    public string Divisa { get; set; } = "EUR";
    public Guid? FormatoId { get; set; }
    public Guid? PaisId { get; set; }
    // V-02.07: ver el comentario de RolUsuario en UsuariosDtos. Un entero fuera
    // de rango pasa el converter y muere en Npgsql como 500; esto lo baja a 400.
    [EnumDataType(typeof(TipoCuenta))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoCuenta? TipoCuenta { get; set; }
    public bool EsEfectivo { get; set; }
    public bool Activa { get; set; } = true;
    [MaxLength(4000)]
    public string? Notas { get; set; }
    public SavePlazoFijoRequest? PlazoFijo { get; set; }
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public decimal? InteresPrevisto { get; set; }
    public bool? Renovable { get; set; }
    public Guid? CuentaReferenciaId { get; set; }
    [MaxLength(4000)]
    public string? PlazoFijoNotas { get; set; }
}

public sealed class UpdateCuentaNotasRequest
{
    [MaxLength(4000)]
    public string? Notas { get; set; }
}

public sealed class SavePlazoFijoRequest
{
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public decimal? InteresPrevisto { get; set; }
    public bool Renovable { get; set; }
    public Guid? CuentaReferenciaId { get; set; }
    [MaxLength(4000)]
    public string? Notas { get; set; }
}

public sealed class RenovarPlazoFijoRequest
{
    public DateOnly NuevaFechaInicio { get; set; }
    public DateOnly NuevaFechaVencimiento { get; set; }
    public decimal? InteresPrevisto { get; set; }
    public bool Renovable { get; set; }
    [MaxLength(4000)]
    public string? Notas { get; set; }
}
