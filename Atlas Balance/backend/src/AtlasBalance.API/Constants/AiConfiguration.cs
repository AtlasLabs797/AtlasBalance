namespace AtlasBalance.API.Constants;

public static class AiConfiguration
{
    public const int MaxQuestionLength = 500;
    public const string OpenRouterAutoModel = "openrouter/auto";
    public const string OpenRouterDefaultModel = "nvidia/nemotron-3-super-120b-a12b:free";
    public const string OpenRouterGptOss120BModel = "openai/gpt-oss-120b:free";
    public const string DefaultOpenAiModel = "gpt-4o-mini";
    public const string DefaultMiniMaxModel = "MiniMax-M3";
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

    private static readonly string[] ThinkingModesOpenRouter =
    [
        ThinkingModeAuto
    ];

    private static readonly string[] SuggestedOpenRouterModels =
    [
        OpenRouterAutoModel,
        OpenRouterDefaultModel,
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
        MiniMaxM27Model
    ];

    public static IReadOnlyList<string> OpenRouterModels => SuggestedOpenRouterModels;
    public static IReadOnlyList<string> OpenAiModels => AllowedOpenAiModels;
    public static IReadOnlyList<string> MiniMaxModels => AllowedMiniMaxModels;

    public static IReadOnlyList<string> GetThinkingModesForProvider(string? provider)
    {
        var normalized = NormalizeProvider(provider);
        return normalized switch
        {
            "OPENAI" => ThinkingModesOpenAi,
            "MINIMAX" => ThinkingModesMiniMax,
            _ => ThinkingModesOpenRouter
        };
    }

    public static bool IsAllowedThinkingMode(string? provider, string? mode)
    {
        var normalized = NormalizeProvider(provider);
        var trimmed = mode?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        return normalized switch
        {
            "OPENAI" => ThinkingModesOpenAi.Any(x => string.Equals(x, trimmed, StringComparison.Ordinal)),
            "MINIMAX" => ThinkingModesMiniMax.Any(x => string.Equals(x, trimmed, StringComparison.Ordinal)),
            _ => ThinkingModesOpenRouter.Any(x => string.Equals(x, trimmed, StringComparison.Ordinal))
        };
    }

    public static string? NormalizeThinkingMode(string? provider, string? mode)
    {
        var trimmed = mode?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, ThinkingModeAuto, StringComparison.Ordinal))
        {
            return ThinkingModeAuto;
        }

        return IsAllowedThinkingMode(provider, trimmed) ? trimmed : null;
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
            "OPENROUTER" => IsValidOpenRouterModelId(normalizedModel) ? normalizedModel : OpenRouterAutoModel,
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
            "OPENROUTER" => IsSuggestedOpenRouterModel(normalizedModel) ? normalizedModel : OpenRouterAutoModel,
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
        return IsValidOpenRouterModelId(normalized) ? normalized : OpenRouterAutoModel;
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
