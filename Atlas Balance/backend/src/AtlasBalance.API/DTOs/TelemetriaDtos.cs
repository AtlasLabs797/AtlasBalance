using System.Text.Json.Serialization;

namespace AtlasBalance.API.DTOs;

// Los nombres de propiedad se fijan de forma explicita porque el frontend envia
// este payload con navigator.sendBeacon en camelCase, y la politica global de
// serializacion del proyecto es SnakeCaseLower: sin [JsonPropertyName] el campo
// componentStack no bindearia.
public sealed class ErrorClienteRequest
{
    [JsonPropertyName("mensaje")]
    public string? Mensaje { get; set; }

    [JsonPropertyName("stack")]
    public string? Stack { get; set; }

    [JsonPropertyName("componentStack")]
    public string? ComponentStack { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}
