namespace GestionCaja.API.DTOs;

public sealed class NotificacionesAdminResumenResponse
{
    public int ExportacionesPendientes { get; set; }
    public int TotalPendientes { get; set; }
}

public sealed class MarcarNotificacionesLeidasRequest
{
    public string? Tipo { get; set; }
}
