using System.Net;
using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using AtlasBalance.API.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase UI): cobertura del nuevo parametro `thinking_mode` en
// /ia/chat. Aplica solo a las llamadas a proveedor externo (OpenAI,
// MiniMax, OpenRouter con modelos concretos); el camino local ignora
// el parametro porque no hay proveedor al que mandarselo.
//
// El contrato:
//  - OpenAI: reasoning_effort in {low, medium, high}
//  - MiniMax: thinking.type in {enabled, disabled}
//  - OpenRouter: reasoning.effort in {low, medium, high} (solo en
//    modelos concretos; "openrouter/auto" lo ignora silenciosamente)
//  - Valores no admitidos por el provider se ignoran y se degradan a "auto"
public class AtlasAiServiceThinkingModeTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AtlasAiService BuildSut(AppDbContext db, IHttpClientFactory? httpFactory = null)
    {
        return new AtlasAiService(
            db,
            httpFactory ?? new StaticHttpClientFactory(),
            new PlainTextSecretProtector(),
            new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())),
            TestAuditService.Create(db),
            NullLogger<AtlasAiService>.Instance);
    }

    private static UserAccessScope AdminScope(Guid userId) => new()
    {
        UserId = userId,
        IsAdmin = true,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static async Task<Guid> SeedAiUserAndConfigAsync(
        AppDbContext db,
        string provider,
        string model)
    {
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = $"ai.thinking.{provider.ToLowerInvariant()}@atlasbalance.local",
            NombreCompleto = "AI ThinkingMode",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = provider, Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "openrouter_api_key", Valor = "test-key", Tipo = "secret", Descripcion = "API key OR" },
            new Configuracion { Clave = "openai_api_key", Valor = "test-openai-key", Tipo = "secret", Descripcion = "API key OpenAI" },
            new Configuracion { Clave = "minimax_api_key", Valor = "test-minimax-key", Tipo = "secret", Descripcion = "API key MiniMax" },
            new Configuracion { Clave = "ai_model", Valor = model, Tipo = "string", Descripcion = "Modelo IA" },
            new Configuracion { Clave = "ai_max_output_tokens", Valor = "100", Tipo = "int", Descripcion = "Salida" },
            new Configuracion { Clave = "ai_max_context_rows", Valor = "5", Tipo = "int", Descripcion = "Contexto" });

        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task AskAsync_MiniMax_On_Should_Send_Thinking_Type_Enabled()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "MINIMAX", AiConfiguration.DefaultMiniMaxModel);
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedModel: null,
            paisId: null,
            requestedThinkingMode: "on");

        response.ThinkingModeAplicado.Should().Be("on");
        httpFactory.LastPayload.Should().Contain("\"thinking\"");
        httpFactory.LastPayload.Should().Contain("\"type\":\"enabled\"");
        httpFactory.LastPayload.Should().Contain("\"reasoning_split\":true");
    }

    [Fact]
    public async Task AskAsync_MiniMax_Off_Should_Send_Thinking_Type_Disabled()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "MINIMAX", AiConfiguration.DefaultMiniMaxModel);
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "off");

        response.ThinkingModeAplicado.Should().Be("off");
        httpFactory.LastPayload.Should().Contain("\"thinking\"");
        httpFactory.LastPayload.Should().Contain("\"type\":\"disabled\"");
    }

    [Fact]
    public async Task AskAsync_MiniMax_Invalid_Should_Degrade_To_Auto()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "MINIMAX", AiConfiguration.DefaultMiniMaxModel);
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "high");

        response.ThinkingModeAplicado.Should().Be("auto");
        // El default aplicado a MiniMax-M3 es disabled porque "auto" se
        // traduce a la opcion mas conservadora del provider.
        httpFactory.LastPayload.Should().Contain("\"type\":\"disabled\"");

        var audit = await db.Auditorias
            .Where(x => x.TipoAccion == AuditActions.IaConsultaBloqueada || x.TipoAccion == AuditActions.IaConsultaError)
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.DetallesJson.Should().Contain("rejected_thinking_mode");
        audit.DetallesJson.Should().Contain("\"requested_thinking_mode\":\"high\"");
    }

    [Fact]
    public async Task AskAsync_OpenAI_High_Should_Send_ReasoningEffort_High()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "OPENAI", "gpt-4.1-mini");
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "high");

        response.ThinkingModeAplicado.Should().Be("high");
        httpFactory.LastPayload.Should().Contain("\"reasoning_effort\":\"high\"");
    }

    [Fact]
    public async Task AskAsync_OpenAI_Low_Should_Send_ReasoningEffort_Low()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "OPENAI", "gpt-4.1-mini");
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "low");

        response.ThinkingModeAplicado.Should().Be("low");
        httpFactory.LastPayload.Should().Contain("\"reasoning_effort\":\"low\"");
    }

    [Fact]
    public async Task AskAsync_OpenAI_Invalid_On_Off_Should_Degrade_To_Auto()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "OPENAI", "gpt-4.1-mini");
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "on");

        response.ThinkingModeAplicado.Should().Be("auto");
        // "auto" en OpenAI = sin reasoning_effort en el payload.
        httpFactory.LastPayload.Should().NotContain("reasoning_effort");
    }

    [Fact]
    public async Task AskAsync_OpenRouter_High_Should_Send_Reasoning_Effort()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "OPENROUTER", "openai/gpt-oss-120b:free");
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "high");

        response.ThinkingModeAplicado.Should().Be("high");
        httpFactory.LastPayload.Should().Contain("\"reasoning\"");
        httpFactory.LastPayload.Should().Contain("\"effort\":\"high\"");
    }

    [Fact]
    public async Task AskAsync_OpenRouter_Auto_Should_Send_Reasoning_Exclude()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "OPENROUTER", "openai/gpt-oss-120b:free");
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: null);

        response.ThinkingModeAplicado.Should().Be("auto");
        httpFactory.LastPayload.Should().Contain("\"reasoning\"");
        httpFactory.LastPayload.Should().Contain("\"exclude\":true");
    }

    [Fact]
    public async Task AskAsync_OpenRouter_On_Off_Should_Degrade_To_Auto()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db, "OPENROUTER", "openai/gpt-oss-120b:free");
        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var response = await sut.AskAsync(
            AdminScope(userId),
            "Cual es el saldo actual?",
            "127.0.0.1",
            CancellationToken.None,
            requestedThinkingMode: "on");

        response.ThinkingModeAplicado.Should().Be("auto");
        httpFactory.LastPayload.Should().Contain("\"exclude\":true");
    }

    [Fact]
    public void GetConfig_Should_Publish_ThinkingModes_For_MiniMax()
    {
        AiConfiguration.GetThinkingModesForProvider("MINIMAX")
            .Should().BeEquivalentTo(new[] { "auto", "on", "off" });
        AiConfiguration.IsAllowedThinkingMode("MINIMAX", "on").Should().BeTrue();
        AiConfiguration.IsAllowedThinkingMode("MINIMAX", "low").Should().BeFalse();
        AiConfiguration.NormalizeThinkingMode("MINIMAX", "low").Should().BeNull();
        AiConfiguration.NormalizeThinkingMode("MINIMAX", "OFF").Should().Be("off");
        AiConfiguration.NormalizeThinkingMode("MINIMAX", null).Should().Be("auto");
    }

    [Fact]
    public void GetConfig_Should_Publish_ThinkingModes_For_OpenAi()
    {
        AiConfiguration.GetThinkingModesForProvider("OPENAI")
            .Should().BeEquivalentTo(new[] { "auto", "low", "medium", "high" });
        AiConfiguration.IsAllowedThinkingMode("OPENAI", "high").Should().BeTrue();
        AiConfiguration.IsAllowedThinkingMode("OPENAI", "on").Should().BeFalse();
    }

    [Fact]
    public void GetConfig_Should_Publish_ThinkingModes_For_OpenRouter()
    {
        AiConfiguration.GetThinkingModesForProvider("OPENROUTER")
            .Should().BeEquivalentTo(new[] { "auto" });
    }

    // Reutiliza los handlers definidos en AtlasAiServiceStabilizationTests.
    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly CapturingHandler _handler;

        public CapturingHttpClientFactory()
        {
            _handler = new CapturingHandler();
        }

        public string LastPayload => _handler.LastPayload;

        public HttpClient CreateClient(string name) =>
            new HttpClient(_handler)
            {
                BaseAddress = new Uri(name switch
                {
                    "openai" => "https://openai.test/v1/",
                    "minimax" => "https://minimax.test/v1/",
                    _ => "https://openrouter.test/api/v1/"
                })
            };
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private const string DefaultResponseBody =
            "{\"choices\":[{\"message\":{\"content\":\"Saldo actual: 1.000,00 EUR.\"}}],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":20}}";

        public string LastPayload { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastPayload = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DefaultResponseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
