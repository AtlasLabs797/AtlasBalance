using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AtlasBalance.API.DTOs;

// Los nombres de propiedad se fijan de forma explicita porque el frontend envia
// este payload con navigator.sendBeacon en camelCase, y la politica global de
// serializacion del proyecto es SnakeCaseLower: sin [JsonPropertyName] el campo
// componentStack no bindearia.
//
// S-25: los topes de longitud coinciden con los que el controller aplica al
// loguear (Mensaje 500, Stack 4000, ComponentStack 4000, Path 200, Timestamp
// 40). Con [MaxLength], el model binding descarta los cuerpos sobredimensionados
// antes de que el controller los procese; sin el, el limite real era el del
// cuerpo de peticion de ASP.NET.
public sealed class ErrorClienteRequest
{
    [MaxLength(500)]
    [JsonPropertyName("mensaje")]
    public string? Mensaje { get; set; }

    [MaxLength(4000)]
    [JsonPropertyName("stack")]
    public string? Stack { get; set; }

    [MaxLength(4000)]
    [JsonPropertyName("componentStack")]
    public string? ComponentStack { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [MaxLength(40)]
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}
