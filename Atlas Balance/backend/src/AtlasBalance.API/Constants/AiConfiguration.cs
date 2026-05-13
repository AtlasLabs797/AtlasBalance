namespace AtlasBalance.API.Constants;

public static class AiConfiguration
{
    public const int MaxQuestionLength = 500;
    public const string OpenRouterAutoModel = "openrouter/auto";
    public const string OpenRouterDefaultModel = "nvidia/nemotron-3-super-120b-a12b:free";
    public const string OpenRouterGptOss120BModel = "openai/gpt-oss-120b:free";
    public const string OpenRouterOpenInferenceProvider = "open-inference/int8";
    public const string OpenRouterGoogleAiStudioProvider = "google-ai-studio";
    public const int OpenRouterMaxFallbackModels = 3;
    public const string DefaultOpenAiModel = "gpt-4o-mini";

    private static readonly string[] AllowedOpenRouterModels =
    [
        OpenRouterAutoModel,
        OpenRouterDefaultModel,
        "google/gemma-4-31b-it:free",
        "minimax/minimax-m2.5:free",
        OpenRouterGptOss120BModel,
        "z-ai/glm-4.5-air:free",
        "qwen/qwen3-coder:free"
    ];

    private static readonly string[] AllowedOpenAiModels =
    [
        "gpt-4.1-mini",
        "gpt-4o-mini",
        "gpt-4o"
    ];

    private static readonly string[] OpenRouterAutoFallbackModelCandidates =
    [
        OpenRouterDefaultModel,
        "google/gemma-4-31b-it:free",
        "minimax/minimax-m2.5:free"
    ];

    private static readonly string[] DeprecatedOpenRouterModels =
    [
        "anthropic/claude-3.5-sonnet",
        "openai/gpt-5.1",
        "openai/gpt-4o-mini",
        "google/gemini-3.1-pro-preview",
        "google/gemini-2.0-flash-001",
        "google/gemini-2.5-flash",
        "google/gemini-2.5-flash-lite",
        "google/gemini-2.5-pro",
        "openai/gpt-4.1-mini",
        "openai/gpt-4.1",
        "anthropic/claude-sonnet-4.5",
        "deepseek/deepseek-v3.2",
        "meta-llama/llama-3.3-70b-instruct"
    ];

    public static IReadOnlyList<string> OpenRouterModels => AllowedOpenRouterModels;
    public static IReadOnlyList<string> OpenAiModels => AllowedOpenAiModels;
    public static IReadOnlyList<string> OpenRouterAutoFallbackModels =>
        OpenRouterAutoFallbackModelCandidates
            .Take(OpenRouterMaxFallbackModels)
            .ToArray();

    public static bool IsAllowedOpenRouterModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return AllowedOpenRouterModels.Any(x => string.Equals(x, model.Trim(), StringComparison.Ordinal));
    }

    public static bool IsAllowedOpenAiModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return AllowedOpenAiModels.Any(x => string.Equals(x, model.Trim(), StringComparison.Ordinal));
    }

    public static bool IsAllowedModel(string? provider, string? model)
    {
        var normalized = NormalizeProvider(provider);
        return normalized switch
        {
            "OPENROUTER" => IsAllowedOpenRouterModel(model),
            "OPENAI" => IsAllowedOpenAiModel(model),
            _ => false
        };
    }

    public static bool IsSupportedProvider(string? provider)
    {
        var normalized = NormalizeProvider(provider);
        return normalized is "OPENROUTER" or "OPENAI";
    }

    public static string NormalizeModel(string? provider, string? model)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedModel = model?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedModel) && IsAllowedModel(normalizedProvider, normalizedModel))
        {
            return normalizedModel;
        }

        return normalizedProvider switch
        {
            "OPENROUTER" => OpenRouterAutoModel,
            "OPENAI" => DefaultOpenAiModel,
            _ => normalizedModel
        };
    }

    public static string NormalizeStoredModel(string? provider, string? model)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedModel = model?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedModel) || IsAllowedModel(normalizedProvider, normalizedModel))
        {
            return NormalizeModel(normalizedProvider, normalizedModel);
        }

        if (normalizedProvider == "OPENROUTER" &&
            DeprecatedOpenRouterModels.Any(x => string.Equals(x, normalizedModel, StringComparison.Ordinal)))
        {
            return OpenRouterAutoModel;
        }

        return normalizedModel;
    }

    public static bool IsOpenRouterAutoModel(string? model)
    {
        return string.Equals(model?.Trim(), OpenRouterAutoModel, StringComparison.Ordinal);
    }

    public static bool IsOpenRouterDefaultModel(string? model)
    {
        return string.Equals(model?.Trim(), OpenRouterDefaultModel, StringComparison.Ordinal);
    }

    public static string ResolveOpenRouterRuntimeModel(string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        return IsOpenRouterAutoModel(normalized) ? OpenRouterDefaultModel : normalized;
    }

    public static bool IsOpenRouterFreeModel(string? model)
    {
        var normalized = model?.Trim();
        return AllowedOpenRouterModels.Any(x =>
            !string.Equals(x, OpenRouterAutoModel, StringComparison.Ordinal) &&
            string.Equals(x, normalized, StringComparison.Ordinal));
    }

    public static bool IsOpenRouterFreeRoute(string? configuredModel, string? runtimeModel)
    {
        return IsOpenRouterAutoModel(configuredModel) || IsOpenRouterFreeModel(runtimeModel);
    }

    public static bool TryGetOpenRouterPinnedProvider(string? model, out string provider)
    {
        provider = string.Empty;
        var normalized = model?.Trim();
        if (string.Equals(normalized, "google/gemma-4-31b-it:free", StringComparison.Ordinal))
        {
            provider = OpenRouterGoogleAiStudioProvider;
            return true;
        }

        if (string.Equals(normalized, "minimax/minimax-m2.5:free", StringComparison.Ordinal) ||
            string.Equals(normalized, OpenRouterGptOss120BModel, StringComparison.Ordinal))
        {
            provider = OpenRouterOpenInferenceProvider;
            return true;
        }

        return false;
    }

    public static string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "OPENROUTER" : normalized;
    }
}
