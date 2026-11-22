using System.Runtime.CompilerServices;

namespace AtlasBalance.API.Constants;

public static class AiConfiguration
{
    public const int MaxQuestionLength = 500;
    public const string OpenRouterAutoModel = "openrouter/auto";
    public const string OpenRouterFreeModel = "openrouter/free";
    // P2 V-03.01: el default vuelve a un modelo que respeta ZDR (zdr/data_collection=deny).
    // Los modelos gratuitos de OpenRouter no garantizan retencion cero de datos y ahora
    // requieren opt-in explicito del admin (ai_allow_data_retention).
    public const string OpenRouterDefaultModel = OpenRouterAutoModel;
    public const string OpenRouterNemotronModel = "nvidia/nemotron-3-super-120b-a12b:free";
    public const string OpenRouterGptOss120BModel = "openai/gpt-oss-120b:free";
    public const string DefaultOpenAiModel = "gpt-4o-mini";
    public const string MiniMaxM31FlashModel = "MiniMax-M3.1-Flash";
    public const int MiniMaxM31ContextWindow = 1_000_000;
    public const string DefaultMiniMaxModel = MiniMaxM31FlashModel;
    public const string MiniMaxM3Model = "MiniMax-M3";
    public const string MiniMaxM27Model = "MiniMax-M2.7";

    // V-02.09 (Fase UI): modos de pensamiento admitidos por provider.
    // OpenAI expone reasoning_effort, MiniMax expone thinking.type,
    // OpenRouter acepta reasoning.effort en modelos concretos. Cada
    // provider tiene un set explicito para no enviar valores que el
    // backend del proveedor rechazaria con 400.
    public const string ThinkingModeAuto = "auto";
    public const string ThinkingModeLow = "low";
    public const string ThinkingModeMedium = "medium";
    public const string ThinkingModeHigh = "high";
    public const string ThinkingModeXHigh = "xhigh";
    public const string ThinkingModeMax = "max";
    public const string ThinkingModeOn = "on";
    public const string ThinkingModeOff = "off";

    private static readonly string[] ThinkingModesOpenAi =
    [
        ThinkingModeAuto,
        ThinkingModeLow,
        ThinkingModeMedium,
        ThinkingModeHigh
    ];

    private static readonly string[] ThinkingModesMiniMax =
    [
        ThinkingModeAuto,
        ThinkingModeOn,
        ThinkingModeOff
    ];

    private static readonly string[] ThinkingModesMiniMaxFlash =
    [ThinkingModeAuto, ThinkingModeLow, ThinkingModeMedium, ThinkingModeHigh, ThinkingModeXHigh, ThinkingModeMax];

    private static readonly string[] ThinkingModesOpenRouter =
    [
        ThinkingModeAuto
    ];

    private static readonly string[] AllowedThinkingModesOpenRouter =
    [
        ThinkingModeAuto,
        ThinkingModeLow,
        ThinkingModeMedium,
        ThinkingModeHigh
    ];

    private static readonly string[] SuggestedOpenRouterModels =
    [
        OpenRouterAutoModel,
        OpenRouterDefaultModel,
        // P2 V-03.01: OpenRouterDefaultModel ya no es el modelo gratuito (ahora es
        // OpenRouterAutoModel), pero el catalogo de modelos gratuitos se mantiene
        // disponible para cuando el admin active ai_allow_data_retention.
        OpenRouterFreeModel,
        OpenRouterNemotronModel,
        "google/gemma-4-31b-it:free",
        "minimax/minimax-m2.5:free",
        OpenRouterGptOss120BModel,
        "z-ai/glm-4.5-air:free",
        "qwen/qwen3-coder:free"
    ];

    // V-02-05 (CRIT-1): la allowlist es ahora EXPLICITA, no regex. Evita que un usuario
    // autenticado invoque modelos premium no suscritos en la cuenta del operador.
    // Para añadir un modelo: editar este array y redeployar.
    private static readonly string[] AllowedOpenRouterModels = SuggestedOpenRouterModels;

    private static readonly string[] AllowedOpenAiModels =
    [
        "gpt-4.1-mini",
        "gpt-4o-mini",
        "gpt-4o"
    ];

    private static readonly string[] AllowedMiniMaxModels =
    [
        DefaultMiniMaxModel,
        MiniMaxM3Model,
        MiniMaxM27Model
    ];

    public static IReadOnlyList<string> OpenRouterModels => SuggestedOpenRouterModels;
    public static IReadOnlyList<string> OpenAiModels => AllowedOpenAiModels;
    public static IReadOnlyList<string> MiniMaxModels => AllowedMiniMaxModels;

    public static IReadOnlyList<string> GetThinkingModesForProvider(string? provider, string? model = null)
    {
        var normalized = NormalizeProvider(provider);
        if (normalized == "MINIMAX" && model == MiniMaxM31FlashModel) return ThinkingModesMiniMaxFlash;
        return normalized switch
        {
            "OPENAI" => ThinkingModesOpenAi,
            "MINIMAX" => ThinkingModesMiniMax,
            _ => ThinkingModesOpenRouter
        };
    }

    public static bool IsAllowedThinkingMode(string? provider, string? mode, string? model = null)
    {
        var normalized = NormalizeProvider(provider);
        var trimmed = mode?.Trim()?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        return normalized switch
        {
            "OPENAI" => ThinkingModesOpenAi.Any(x => string.Equals(x, trimmed, StringComparison.Ordinal)),
            "MINIMAX" => GetThinkingModesForProvider(normalized, model).Any(x => string.Equals(x, trimmed, StringComparison.Ordinal)),
            _ => AllowedThinkingModesOpenRouter.Any(x => string.Equals(x, trimmed, StringComparison.Ordinal))
        };
    }

    public static string? NormalizeThinkingMode(string? provider, string? mode, string? model = null)
    {
        var trimmed = mode?.Trim()?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, ThinkingModeAuto, StringComparison.Ordinal))
        {
            return ThinkingModeAuto;
        }

        return IsAllowedThinkingMode(provider, trimmed, model) ? trimmed : null;
    }

    public static bool IsAllowedOpenRouterModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }
        var normalized = model.Trim();
        return AllowedOpenRouterModels.Any(x => string.Equals(x, normalized, StringComparison.Ordinal));
    }

    public static bool IsSuggestedOpenRouterModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return SuggestedOpenRouterModels.Any(x => string.Equals(x, model.Trim(), StringComparison.Ordinal));
    }

    public static bool IsAllowedOpenAiModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return AllowedOpenAiModels.Any(x => string.Equals(x, model.Trim(), StringComparison.Ordinal));
    }

    public static bool IsAllowedMiniMaxModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return AllowedMiniMaxModels.Any(x => string.Equals(x, model.Trim(), StringComparison.Ordinal));
    }

    public static bool IsAllowedModel(string? provider, string? model)
    {
        var normalized = NormalizeProvider(provider);
        return normalized switch
        {
            "OPENROUTER" => IsAllowedOpenRouterModel(model),
            "OPENAI" => IsAllowedOpenAiModel(model),
            "MINIMAX" => IsAllowedMiniMaxModel(model),
            _ => false
        };
    }

    public static bool IsSupportedProvider(string? provider)
    {
        var normalized = NormalizeProvider(provider);
        return normalized is "OPENROUTER" or "OPENAI" or "MINIMAX";
    }

    public static string NormalizeModel(string? provider, string? model)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedModel = model?.Trim() ?? string.Empty;
        return normalizedProvider switch
        {
            "OPENROUTER" => IsValidOpenRouterModelId(normalizedModel) ? normalizedModel : OpenRouterDefaultModel,
            "OPENAI" => IsAllowedOpenAiModel(normalizedModel) ? normalizedModel : DefaultOpenAiModel,
            "MINIMAX" => IsAllowedMiniMaxModel(normalizedModel) ? normalizedModel : DefaultMiniMaxModel,
            _ => normalizedModel
        };
    }

    public static string NormalizeGlobalConfigModel(string? provider, string? model)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedModel = model?.Trim() ?? string.Empty;
        return normalizedProvider switch
        {
            "OPENROUTER" => IsSuggestedOpenRouterModel(normalizedModel) ? normalizedModel : OpenRouterDefaultModel,
            _ => NormalizeModel(normalizedProvider, normalizedModel)
        };
    }

    public static string NormalizeStoredModel(string? provider, string? model)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedModel = model?.Trim() ?? string.Empty;
        return NormalizeModel(normalizedProvider, normalizedModel);
    }

    public static bool IsOpenRouterAutoModel(string? model)
    {
        return string.Equals(model?.Trim(), OpenRouterAutoModel, StringComparison.Ordinal);
    }

    public static string ResolveOpenRouterRuntimeModel(string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        return IsValidOpenRouterModelId(normalized) ? normalized : OpenRouterDefaultModel;
    }

    public static bool IsOpenRouterFreeModel(string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        return string.Equals(normalized, OpenRouterFreeModel, StringComparison.Ordinal) ||
               normalized.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
    }

    // A-5: bloque de privacidad de OpenRouter (retencion cero de datos y
    // rechazo de la recogida de datos). Vive aqui para que TODAS las llamadas
    // a OpenRouter usen el MISMO constructor: la del servicio principal y la
    // del planificador semantico.
    // Devuelve null cuando el modelo en/runtime es gratuito: los endpoints
    // `openrouter/free` y `*:free` no garantizan rutas compatibles con
    // ZDR/data_collection=deny y OpenRouter responde 404 (LOG_ERRORES_INCIDENCIAS,
    // entrada 2026-09-21 "OpenRouter gratuito rechazado por restricciones ZDR").
    public static IReadOnlyDictionary<string, object?>? OpenRouterPrivacyProvider(string? provider, string? runtimeModel)
    {
        if (NormalizeProvider(provider) != "OPENROUTER" || IsOpenRouterFreeModel(runtimeModel))
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["zdr"] = true,
            ["data_collection"] = "deny"
        };
    }

    public static string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "OPENROUTER" : normalized;
    }

    public static bool IsValidOpenRouterModelId(string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        if (normalized.Length is < 3 or > 160)
        {
            return false;
        }

        if (normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.All(ch =>
            char.IsAsciiLetterOrDigit(ch) ||
            ch is '/' or '-' or '_' or '.' or ':' or '+');
    }
}

// R-2: snapshot de CONFIGURACIONES compartido dentro de la misma peticion.
//
// Se cachea por instancia del DbContext (scoped): AtlasAiService y
// SemanticPlannerClient reciben el MISMO contexto scoped, asi que la tabla
// CONFIGURACIONES se lee una sola vez por pregunta en vez de dos.
//
// Se eligio el snapshot por contexto y no un cache de proceso con TTL porque
// el TTL dejaria una ventana de configuracion obsoleta (el admin cambia el
// modelo y el servicio sigue sirviendo el anterior hasta que caduque). Aqui
// el snapshot caduca con el scope: no hay contenido obsoleto posible.
public static class AiConfigSnapshot
{
    private sealed class Holder
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public IReadOnlyDictionary<string, string>? Valores;
    }

    private static readonly ConditionalWeakTable<object, Holder> Holders = new();

    public static async Task<IReadOnlyDictionary<string, string>> GetAsync(
        object cacheKey,
        Func<Task<IReadOnlyDictionary<string, string>>> loader,
        CancellationToken cancellationToken)
    {
        var holder = Holders.GetValue(cacheKey, _ => new Holder());
        await holder.Gate.WaitAsync(cancellationToken);
        try
        {
            if (holder.Valores is not null)
            {
                return holder.Valores;
            }

            var valores = await loader();
            holder.Valores = valores;
            return valores;
        }
        finally
        {
            holder.Gate.Release();
        }
    }
}
