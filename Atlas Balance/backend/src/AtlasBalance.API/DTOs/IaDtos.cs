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
}

public sealed class UpdateIaConfigRequest
{
    public string Provider { get; set; } = "OPENROUTER";
    public string Model { get; set; } = string.Empty;
    public bool Habilitada { get; set; }
    [JsonPropertyName("openrouter_api_key")]
    public string OpenRouterApiKey { get; set; } = string.Empty;
    [JsonPropertyName("openai_api_key")]
    public string OpenAiApiKey { get; set; } = string.Empty;
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
    public string Pregunta { get; set; } = string.Empty;
    public string? Model { get; set; }
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
