namespace GestionCaja.API.DTOs;

public sealed class ExportacionListItemResponse
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public string CuentaNombre { get; set; } = string.Empty;
    public string TitularNombre { get; set; } = string.Empty;
    public DateTime FechaExportacion { get; set; }
    public string? RutaArchivo { get; set; }
    public long? TamanioBytes { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public Guid? IniciadoPorId { get; set; }
    public string? IniciadoPorNombre { get; set; }
}

public sealed class ExportacionManualRequest
{
    public Guid CuentaId { get; set; }
}
