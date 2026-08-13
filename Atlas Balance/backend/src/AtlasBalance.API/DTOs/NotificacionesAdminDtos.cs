using System.ComponentModel.DataAnnotations;

namespace AtlasBalance.API.DTOs;

public sealed class NotificacionesAdminResumenResponse
{
    public int ExportacionesPendientes { get; set; }
    public int TotalPendientes { get; set; }
}

public sealed class MarcarNotificacionesLeidasRequest
{
    // V-02.09: tope declarativo para que [ApiController] rechace con 400 un
    // payload con Tipo de varios MB antes de pasar por el servicio. El servicio
    // ya recortaba y normalizaba, pero la cota tiene que vivir en el DTO.
    [MaxLength(32)]
    public string? Tipo { get; set; }
}
