using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AtlasBalance.API.DTOs;

public sealed class IaConfigResponse
{
    public string Provider { get; set; } = "OPENROUTER";
    public string Model { get; set; } = string.Empty;
    public bool Habilitada { get; set; }
    public bool UsuarioPuedeUsar { get; set; }
    [JsonPropertyName("openrouter_api_key_configurada")]
    public bool OpenRouterApiKeyConfigurada { get; set; }
    [JsonPropertyName("openai_api_key_configurada")]
    public bool OpenAiApiKeyConfigurada { get; set; }
    [JsonPropertyName("minimax_api_key_configurada")]
    public bool MiniMaxApiKeyConfigurada { get; set; }
    public bool Configurada { get; set; }
    public string MensajeEstado { get; set; } = string.Empty;
    public int RequestsPorMinuto { get; set; } = AiConfigurationDefaults.RequestsPerMinute;
    public int RequestsPorHora { get; set; } = AiConfigurationDefaults.RequestsPerHour;
    public int RequestsPorDia { get; set; } = AiConfigurationDefaults.RequestsPerDay;
    public int RequestsGlobalesPorDia { get; set; } = AiConfigurationDefaults.GlobalRequestsPerDay;
    public decimal PresupuestoMensualEur { get; set; }
    public decimal PresupuestoMensualUsuarioEur { get; set; }
    public decimal PresupuestoTotalEur { get; set; }
    public decimal CosteMesEstimadoEur { get; set; }
    public decimal CosteMesUsuarioEstimadoEur { get; set; }
    public decimal CosteTotalEstimadoEur { get; set; }
    public int RequestsMesUsuario { get; set; }
    public long TokensEntradaMesUsuario { get; set; }
    public long TokensSalidaMesUsuario { get; set; }
    public int PorcentajeAvisoPresupuesto { get; set; } = AiConfigurationDefaults.BudgetWarningPercent;
    public decimal InputCostPerMillionTokensEur { get; set; }
    public decimal OutputCostPerMillionTokensEur { get; set; }
    public int MaxInputTokens { get; set; } = AiConfigurationDefaults.MaxInputTokens;
    public int MaxOutputTokens { get; set; } = AiConfigurationDefaults.MaxOutputTokens;
    public int MaxContextRows { get; set; } = AiConfigurationDefaults.MaxContextRows;
    // V-02.09 (Fase UI): modos de pensamiento que admite el provider
    // configurado. El frontend los usa para pintar el selector del composer.
    [JsonPropertyName("thinking_modes")]
    public IReadOnlyList<IaThinkingModeOption> ThinkingModes { get; set; } = Array.Empty<IaThinkingModeOption>();
}

public sealed class IaThinkingModeOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class UpdateIaConfigRequest
{
    [MaxLength(32)]
    public string Provider { get; set; } = "OPENROUTER";
    [MaxLength(256)]
    public string Model { get; set; } = string.Empty;
    public bool Habilitada { get; set; }
    [JsonPropertyName("openrouter_api_key")]
    [MaxLength(1024)]
    public string OpenRouterApiKey { get; set; } = string.Empty;
    [JsonPropertyName("openai_api_key")]
    [MaxLength(1024)]
    public string OpenAiApiKey { get; set; } = string.Empty;
    [JsonPropertyName("minimax_api_key")]
    [MaxLength(1024)]
    public string MiniMaxApiKey { get; set; } = string.Empty;
    public int RequestsPorMinuto { get; set; } = AiConfigurationDefaults.RequestsPerMinute;
    public int RequestsPorHora { get; set; } = AiConfigurationDefaults.RequestsPerHour;
    public int RequestsPorDia { get; set; } = AiConfigurationDefaults.RequestsPerDay;
    public int RequestsGlobalesPorDia { get; set; } = AiConfigurationDefaults.GlobalRequestsPerDay;
    public decimal PresupuestoMensualEur { get; set; }
    public decimal PresupuestoMensualUsuarioEur { get; set; }
    public decimal PresupuestoTotalEur { get; set; }
    public int PorcentajeAvisoPresupuesto { get; set; } = AiConfigurationDefaults.BudgetWarningPercent;
    public decimal InputCostPerMillionTokensEur { get; set; }
    public decimal OutputCostPerMillionTokensEur { get; set; }
    public int MaxInputTokens { get; set; } = AiConfigurationDefaults.MaxInputTokens;
    public int MaxOutputTokens { get; set; } = AiConfigurationDefaults.MaxOutputTokens;
    public int MaxContextRows { get; set; } = AiConfigurationDefaults.MaxContextRows;
}

public sealed class IaChatRequest
{
    // V-02.09: tope declarativo para espejar el limite de negocio
    // (AiConfiguration.MaxQuestionLength = 500). El servicio ya validaba por
    // codigo, pero la cota tiene que vivir en el DTO para que ModelState la
    // vea y rechace con el mismo error generico del resto de campos.
    [Required, MaxLength(500)]
    public string Pregunta { get; set; } = string.Empty;
    // V-02.09: el modelo lo valida el servicio contra una allowlist, pero
    // acotamos a 256 caracteres para que un payload gigante no llegue al
    // servicio ni al proveedor upstream.
    [MaxLength(256)]
    public string? Model { get; set; }
    public Guid? PaisId { get; set; }
    // V-02.09 (Fase UI): modo de pensamiento solicitado por el usuario.
    // "auto" = default del provider; "low"/"medium"/"high" = reasoning_effort
    // (OpenAI / OpenRouter en modelos compatibles); "on"/"off" = thinking.type
    // (MiniMax). Valores invalidos o no soportados por el provider se ignoran.
    // V-02.09: tope declarativo, los valores validos caben en 16 caracteres
    // (auto, low, medium, high, on, off) y se normalizan en el servicio.
    [JsonPropertyName("thinking_mode")]
    [MaxLength(16)]
    public string? ThinkingMode { get; set; }
}

public sealed class IaModelResponse
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int? ContextLength { get; set; }
    // V-02.09 (Fase 1.5): true si el modelo esta en la allowlist del backend.
    // Antes, el catalogo de OpenRouter mostraba ~80 modelos libres y los usuarios
    // podian intentar usar uno que no estaba en AllowedOpenRouterModels, recibiendo
    // un 400 al preguntar. Ahora el backend filtra a solo los permitidos y marca
    // el campo para que el frontend pueda etiquetar la entrada.
    public bool Permitido { get; set; } = true;
}

public sealed class IaChatResponse
{
    public string Respuesta { get; set; } = string.Empty;
    public string Provider { get; set; } = "OPENROUTER";
    public string Model { get; set; } = string.Empty;
    public int MovimientosAnalizados { get; set; }
    public int TokensEntradaEstimados { get; set; }
    public int TokensSalidaEstimados { get; set; }
    public decimal CosteEstimadoEur { get; set; }
    public bool AvisoPresupuesto { get; set; }
    public string? Aviso { get; set; }
    public string Origen { get; set; } = "proveedor";
    public IReadOnlyList<IaClarificationOptionResponse>? OpcionesAclaracion { get; set; }
    // V-02.09 (Fase UI): modo de pensamiento que finalmente se ha aplicado
    // (puede diferir del pedido si el provider/modelo no lo soporta).
    [JsonPropertyName("thinking_mode_aplicado")]
    public string? ThinkingModeAplicado { get; set; }
}

public sealed class IaClarificationOptionResponse
{
    public string Etiqueta { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
}

public static class AiConfigurationDefaults
{
    public const int RequestsPerMinute = 6;
    public const int RequestsPerHour = 30;
    public const int RequestsPerDay = 60;
    public const int GlobalRequestsPerDay = 300;
    public const int BudgetWarningPercent = 80;
    public const int MaxInputTokens = 6000;
    public const int MaxOutputTokens = 700;
    public const int MaxContextRows = 80;
    public const int MaxContextYears = 3;
    public const int MaxContextCharacters = 24000;
}
