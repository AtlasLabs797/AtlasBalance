using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services.IaPlanner;

// El proveedor recibe solo la pregunta saneada y el contrato cerrado. Nunca
// recibe contexto financiero, entidades locales ni resultados de herramientas.
public sealed class SemanticPlannerClient : ISemanticPlannerClient
{
    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretProtector _secretProtector;

    public SemanticPlannerClient(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ISecretProtector secretProtector)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _secretProtector = secretProtector;
    }

    public async Task<string?> PlanToJsonAsync(
        string pregunta,
        IReadOnlyList<string> allowedOperations,
        CancellationToken cancellationToken,
        AiPseudonymMap? pseudonyms = null)
    {
        var config = await _dbContext.Configuraciones.AsNoTracking()
            .Where(x => x.Clave == "ai_provider" || x.Clave == "ai_model" ||
                        x.Clave == "openrouter_api_key" || x.Clave == "openai_api_key" ||
                        x.Clave == "minimax_api_key")
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, cancellationToken);
        var provider = AiConfiguration.NormalizeProvider(Get(config, "ai_provider", "OPENROUTER"));
        var model = AiConfiguration.NormalizeStoredModel(provider, Get(config, "ai_model"));
        if (!AiConfiguration.IsSupportedProvider(provider) || !AiConfiguration.IsAllowedModel(provider, model))
        {
            return null;
        }

        var protectedKey = provider switch
        {
            "OPENAI" => Get(config, "openai_api_key"),
            "MINIMAX" => Get(config, "minimax_api_key"),
            _ => Get(config, "openrouter_api_key")
        };
        if (string.IsNullOrWhiteSpace(protectedKey)) return null;

        string apiKey;
        try
        {
            apiKey = _secretProtector.UnprotectFromStorage(protectedKey) ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        // V-02.08: el mapa de seudonimos lo construye el caller (mismas
        // entidades que ve el resto del flujo de IA, dentro del scope del
        // usuario) y se pasa aqui para redactar tambien nombres de cuenta y
        // de titular, no solo los patrones regex (IBAN, email) que ya
        // cubria DlpScrubber. Si no llega mapa (p.ej. tests), se mantiene
        // el comportamiento anterior con un mapa vacio.
        var safeQuestion = new DlpScrubber(pseudonyms ?? new AiPseudonymMap(Array.Empty<(string Nombre, string Tipo)>()))
            .Escanear(pregunta, "planificador");
        if (safeQuestion.FalloCerrado) return null;

        var client = _httpClientFactory.CreateClient(provider.ToLowerInvariant());
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (provider == "OPENROUTER")
        {
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "Atlas Balance");
        }
        request.Content = JsonContent.Create(new
        {
            model = provider == "OPENROUTER" ? AiConfiguration.ResolveOpenRouterRuntimeModel(model) : model,
            temperature = 0,
            max_tokens = 500,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = BuildSystemInstruction(allowedOperations) },
                new { role = "user", content = JsonSerializer.Serialize(safeQuestion.Texto) }
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractContent(payload);
    }

    private static string BuildSystemInstruction(IReadOnlyList<string> operations) =>
        "Devuelve exclusivamente un objeto JSON sin markdown con operacion, metrica y filtros. " +
        "No uses SQL, tablas, expresiones ni campos no listados. Operaciones permitidas: " +
        string.Join(", ", operations) + ".";

    private static string? ExtractContent(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            {
                return content.GetString();
            }
            return root.TryGetProperty("output_text", out var output) ? output.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> config, string key, string fallback = "") =>
        config.TryGetValue(key, out var value) ? value : fallback;
}
