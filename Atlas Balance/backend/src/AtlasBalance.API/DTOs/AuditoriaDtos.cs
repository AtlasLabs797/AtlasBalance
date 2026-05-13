namespace AtlasBalance.API.DTOs;

public sealed class AuditoriaListItemResponse
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public string TipoAccion { get; set; } = string.Empty;
    public string? EntidadTipo { get; set; }
    public Guid? EntidadId { get; set; }
    public Guid? CuentaId { get; set; }
    public string? CuentaNombre { get; set; }
    public Guid? TitularId { get; set; }
    public string? TitularNombre { get; set; }
    public string? CeldaReferencia { get; set; }
    public string? ColumnaNombre { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string? IpAddress { get; set; }
    public string? DetallesJson { get; set; }
}

public sealed class AuditoriaUsuarioFiltroResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public sealed class AuditoriaCuentaFiltroResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
}

public sealed class AuditoriaFiltrosResponse
{
    public IReadOnlyList<AuditoriaUsuarioFiltroResponse> Usuarios { get; set; } = [];
    public IReadOnlyList<AuditoriaCuentaFiltroResponse> Cuentas { get; set; } = [];
    public IReadOnlyList<string> TiposAccion { get; set; } = [];
}
