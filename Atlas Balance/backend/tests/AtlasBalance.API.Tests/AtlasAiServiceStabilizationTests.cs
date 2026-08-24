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

// V-02.09 (Fase 1): las cancelaciones se propagan via TestContext.Current
// (recomendacion xUnit1051), pero los tests existentes en el repositorio usan
// CancellationToken.None directamente. Mantenemos la convencion del repo para
// no introducir divergencia.
#pragma warning disable xUnit1051

// V-02.09 (Fase 1): tests de estabilizacion de AtlasAiService.
// Cubre los seis arreglos del plan:
//  - 1.1 DbContext concurrente (Task.WhenAll) + resumen anual perdido
//  - 1.2 "ultimo mes" pasa a ser el mes natural anterior
//  - 1.3 sin contenido de Fase 1.3 (eso vive en el frontend)
//  - 1.4 sin texto libre del proveedor en logs ni auditoria
//  - 1.5 catalogo OpenRouter filtrado a solo permitidos
public class AtlasAiServiceStabilizationTests
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
        string model = AiConfiguration.OpenRouterDefaultModel,
        string provider = "OPENROUTER",
        int maxContextRows = 10,
        IReadOnlyList<Configuracion>? extraConfig = null)
    {
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "ai.stabilization@atlasbalance.local",
            NombreCompleto = "AI Stabilization",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = provider, Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "openrouter_api_key", Valor = "test-key", Tipo = "secret", Descripcion = "API key" },
            new Configuracion { Clave = "openai_api_key", Valor = "test-openai-key", Tipo = "secret", Descripcion = "API key OpenAI" },
            new Configuracion { Clave = "minimax_api_key", Valor = "test-minimax-key", Tipo = "secret", Descripcion = "API key MiniMax" },
            new Configuracion { Clave = "ai_model", Valor = model, Tipo = "string", Descripcion = "Modelo IA" },
            new Configuracion { Clave = "ai_max_output_tokens", Valor = "100", Tipo = "int", Descripcion = "Salida" },
            new Configuracion { Clave = "ai_max_context_rows", Valor = maxContextRows.ToString(), Tipo = "int", Descripcion = "Contexto" });
        if (extraConfig is not null)
        {
            db.Configuraciones.AddRange(extraConfig);
        }

        await db.SaveChangesAsync();
        return userId;
    }

    // V-02.09 (Fase 1.2): "ultimo mes" debe ser el mes natural anterior,
    // no los ultimos 30 dias. En un mes de 31 dias como enero, la ventana
    // de 30 dias incluiria parte de enero y dejaria fuera los dias 1-2.
    // Solo se prueban preguntas que disparan el camino deterministico
    // (contienen "cuenta/cuentas/titular/titulares" + "mas/mayor/.../top").
    [Theory]
    [InlineData("Que cuentas han tenido mas gastos el ultimo mes?", "Cuentas con mas gastos en el mes anterior")]
    [InlineData("Top 5 titulares con mas gastos del ultimo mes", "Titulares con mas gastos en el mes anterior")]
    public async Task AskAsync_DeterministicRanking_LastMonth_Should_Use_Previous_Calendar_Month(string question, string responseHeader)
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var previousMonthStart = monthStart.AddMonths(-1);
        var previousMonthEnd = monthStart.AddDays(-1);
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Operativa", Divisa = "EUR", Activa = true });

        // Gasto dentro del mes natural anterior -> debe entrar.
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = previousMonthStart.AddDays(5),
            Concepto = "Gasto mes anterior",
            Monto = -100m,
            Saldo = 900m,
            FilaNumero = 1
        });
        // Gasto fuera del mes natural anterior pero dentro de los ultimos 30
        // dias (caso del bug): NO debe entrar porque ahora "ultimo mes" es
        // el mes natural anterior.
        var recentOutOfPreviousMonth = today.AddDays(-3);
        if (recentOutOfPreviousMonth > previousMonthEnd)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = recentOutOfPreviousMonth,
                Concepto = "Gasto reciente fuera del mes anterior",
                Monto = -500m,
                Saldo = 400m,
                FilaNumero = 2
            });
        }
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.AskAsync(AdminScope(userId), question, "127.0.0.1", CancellationToken.None);

        result.Respuesta.Should().Contain(responseHeader);
        // El rango mostrado debe ser el mes natural anterior.
        result.Respuesta.Should().Contain(previousMonthStart.ToString("dd/MM/yyyy"));
        result.Respuesta.Should().Contain(previousMonthEnd.ToString("dd/MM/yyyy"));
        // El gasto del mes anterior SI aparece (100,00 EUR).
        result.Respuesta.Should().Contain("100,00 EUR");
        // El gasto fuera del mes anterior NO aparece (500,00 EUR).
        result.Respuesta.Should().NotContain("500,00");
        result.Respuesta.Should().NotContain("fuera del mes anterior");
    }

    // V-02.09 (Fase 1.1): el resumen anual antes se anadia a periodTasks
    // DESPUES del await Task.WhenAll, asi que nunca llegaba al contexto
    // del proveedor. Ahora la condicion "ano" SI se evalua y SI entra
    // en el bloque. Verificamos que la consulta al proveedor lleva el
    // PERIODO del ano actual.
    [Fact]
    public async Task AskAsync_Context_For_Annual_Question_Should_Include_Annual_Summary()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db);
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Operativa", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        var result = await sut.AskAsync(AdminScope(userId), "Resumen de gastos de este ano", "127.0.0.1", CancellationToken.None);

        httpFactory.RequestCount.Should().Be(1);
        // El contexto enviado al proveedor debe contener el PERIODO del ano actual,
        // no solo los totales por mes. La cabecera "PERIODO" aparece con cada
        // bloque de periodo que AppendPeriodSummaryAsync emite.
        httpFactory.LastPayload.Should().Contain("CONTEXTO_FINANCIERO_NO_CONFIABLE");
        var currentYear = DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        httpFactory.LastPayload.Should().Contain($"PERIODO 01/01/{currentYear}");
    }

    // V-02.09 (Fase 1.1): antes los period summary se ejecutaban con
    // Task.WhenAll contra el mismo DbContext. InMemory no detecta el
    // problema; el patron sigue funcionando, pero la verificacion es que
    // el orden de los bloques y la suma total cuadran. Esto blinda contra
    // una regresion a paralelizar.
    [Fact]
    public async Task AskAsync_Context_Should_Include_All_Matched_Periods_In_Order()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db);
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Operativa", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var httpFactory = new CapturingHttpClientFactory();
        var sut = BuildSut(db, httpFactory);

        // "mes" + "trimestre" + "ano" + "mes anterior" coinciden todos.
        var result = await sut.AskAsync(
            AdminScope(userId),
            "Cual ha sido el resumen de gastos del mes, trimestre, ano y mes anterior?",
            "127.0.0.1",
            CancellationToken.None);

        httpFactory.RequestCount.Should().Be(1);
        var payload = httpFactory.LastPayload;
        // El bloque "mes actual" debe estar antes que el del "mes anterior"
        // porque el codigo los procesa en ese orden.
        var idxMesActual = payload.IndexOf("PERIODO 01/", StringComparison.Ordinal);
        var idxMesAnterior = payload.IndexOf("PERIODO ", idxMesActual + 1, StringComparison.Ordinal);
        idxMesAnterior.Should().BeGreaterThan(idxMesActual);
        // Y el bloque anual debe existir (antes se perdia silenciosamente).
        var currentYear = DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        payload.Should().Contain($"PERIODO 01/01/{currentYear}");
        // Trimestre actual.
        var currentQuarterStartMonth = (((DateTime.UtcNow.Month - 1) / 3) * 3 + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        payload.Should().Contain($"PERIODO 01/{currentQuarterStartMonth}/{currentYear}");
    }

    // V-02.09 (Fase 1.4): incluso si la pregunta del usuario lleva
    // PII (email, telefono, IBAN), ni la auditoria ni el log del
    // servidor la persisten. El prompt completo se queda en memoria
    // y seudonimiza antes de salir al proveedor, pero el registro
    // de auditoria solo guarda "pregunta_caracteres".
    [Theory]
    [InlineData("Resumen para el cliente juan.perez@empresa.com con IBAN ES91 2100 0418 4502 0005 1332")]
    [InlineData("Dime gastos de la nomina del empleado con DNI 12345678Z")]
    [InlineData("Ultima factura con tarjeta 4111 1111 1111 1111 del titular")]
    public async Task AskAsync_Audit_Should_Not_Contain_Pii_From_Prompt(string prompt)
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db);
        var sut = BuildSut(db);

        var result = await sut.AskAsync(AdminScope(userId), prompt, "127.0.0.1", CancellationToken.None);

        // V-02.08: si el planificador semantico intenta y rechaza un plan,
        // AskAsync sigue hacia el camino normal y queda mas de una fila de
        // auditoria IaConsulta (una por cada llamada a proveedor realmente
        // facturada). Ninguna de ellas debe llevar PII del prompt.
        var audits = await db.Auditorias.Where(x => x.TipoAccion == AuditActions.IaConsulta).ToListAsync();
        audits.Should().NotBeEmpty();
        foreach (var audit in audits)
        {
            // Solo el tamano de la pregunta, no el contenido.
            audit.DetallesJson.Should().Contain("\"pregunta_caracteres\":");
            audit.DetallesJson.Should().NotContain("juan.perez@empresa.com");
            audit.DetallesJson.Should().NotContain("ES91 2100 0418 4502 0005 1332");
            audit.DetallesJson.Should().NotContain("12345678Z");
            audit.DetallesJson.Should().NotContain("4111 1111 1111 1111");
        }
    }

    // V-02.09 (Fase 1.5): el catalogo de OpenRouter solo expone los
    // modelos que pasan la allowlist local. Antes llegaban ~80
    // modelos libres y el usuario podia seleccionar uno que el
    // backend rechazaba luego con 400.
    [Fact]
    public async Task GetModelsAsync_OpenRouter_Should_Only_Return_Allowed_Models()
    {
        await using var db = BuildDbContext();
        // No hace falta sembrar usuario; GetModelsAsync no consulta permisos.
        var sut = BuildSut(db, new StaticHttpClientFactory(openRouterModelsBody: """
            {"data":[
              {"id":"openrouter/auto","name":"Auto"},
              {"id":"nvidia/nemotron-3-super-120b-a12b:free","name":"Nemotron"},
              {"id":"openai/gpt-oss-120b:free","name":"gpt-oss-120b"},
              {"id":"google/gemma-4-31b-it:free","name":"Gemma 4"},
              {"id":"minimax/minimax-m2.5:free","name":"MiniMax M2.5"},
              {"id":"z-ai/glm-4.5-air:free","name":"GLM 4.5 Air"},
              {"id":"qwen/qwen3-coder:free","name":"Qwen3 Coder"},
              {"id":"anthropic/claude-3.5-sonnet","name":"Claude 3.5"},
              {"id":"openai/gpt-4o","name":"GPT-4o"},
              {"id":"meta-llama/llama-3.1-70b-instruct","name":"Llama 3.1"}
            ]}
            """));

        var models = await sut.GetModelsAsync("OPENROUTER", null, CancellationToken.None);

        models.Should().NotBeEmpty();
        // Todos los modelos devueltos deben estar en la allowlist.
        foreach (var m in models)
        {
            AiConfiguration.IsAllowedOpenRouterModel(m.Id).Should().BeTrue($"model {m.Id} should be in allowlist");
            m.Permitido.Should().BeTrue();
        }
        // Y especificamente, los modelos de pago que NO estan en la allowlist
        // (Claude 3.5, GPT-4o, Llama 3.1) NO deben aparecer.
        models.Should().NotContain(x => x.Id == "anthropic/claude-3.5-sonnet");
        models.Should().NotContain(x => x.Id == "openai/gpt-4o");
        models.Should().NotContain(x => x.Id == "meta-llama/llama-3.1-70b-instruct");
    }

    // V-02.09 (Fase 1.5): OpenAI y MiniMax tambien marcan permitido=true
    // en sus catalogos estaticos.
    [Fact]
    public async Task GetModelsAsync_OpenAi_And_MiniMax_Should_Mark_All_Models_Allowed()
    {
        await using var db = BuildDbContext();
        var sut = BuildSut(db);

        var openAi = await sut.GetModelsAsync("OPENAI", null, CancellationToken.None);
        openAi.Should().NotBeEmpty();
        openAi.Should().OnlyContain(m => m.Permitido);

        var miniMax = await sut.GetModelsAsync("MINIMAX", null, CancellationToken.None);
        miniMax.Should().NotBeEmpty();
        miniMax.Should().OnlyContain(m => m.Permitido);
    }

    // V-02.09 (Fase 1.4): incluso cuando el error es 4xx/5xx con un
    // payload que parece creible, la auditoria guarda solo los campos
    // estructurados. El mensaje del usuario se construye via
    // clasificacion (BuildProviderHttpErrorMessage) sin arrastrar
    // el texto crudo.
    [Fact]
    public async Task AskAsync_ProviderHttpError_Audit_Should_Not_Contain_Provider_Free_Text()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db);
        var httpFactory = new CapturingHttpClientFactory(
            statusCode: HttpStatusCode.InternalServerError,
            responseBody: "{\"error\":{\"message\":\"Database connection failed at db-internal-123.example.com:5432 with creds postgres:s3cr3t-p4ss\"}}");
        var sut = BuildSut(db, httpFactory);

        var act = () => sut.AskAsync(AdminScope(userId), "Resumen de gastos", "127.0.0.1", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<IaProviderException>();
        exception.Which.Message.Should().NotContain("Database connection");
        exception.Which.Message.Should().NotContain("db-internal-123");
        exception.Which.Message.Should().NotContain("s3cr3t-p4ss");
        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.IaConsultaError);
        audit.DetallesJson.Should().Contain("provider_http_error");
        audit.DetallesJson.Should().Contain("500");
        audit.DetallesJson.Should().NotContain("Database connection");
        audit.DetallesJson.Should().NotContain("db-internal-123");
        audit.DetallesJson.Should().NotContain("s3cr3t-p4ss");
        audit.DetallesJson.Should().NotContain("5432");
    }

    // V-02.09 (Fase 1.4): el campo de auditoria "extra" ya no debe
    // contener la clave "provider_error". Antes se serializaba el
    // payload del proveedor y quedaba accesible para cualquier
    // administrador con acceso a AUDITORIAS.
    [Fact]
    public async Task AskAsync_ProviderHttpError_Audit_Extra_Should_Not_Have_ProviderError_Key()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAiUserAndConfigAsync(db);
        var httpFactory = new CapturingHttpClientFactory(
            statusCode: HttpStatusCode.TooManyRequests,
            responseBody: "{\"error\":{\"message\":\"Rate limit exceeded\"}}",
            retryAfterSeconds: 30);
        var sut = BuildSut(db, httpFactory);

        var act = () => sut.AskAsync(AdminScope(userId), "Resumen de gastos", "127.0.0.1", CancellationToken.None);

        await act.Should().ThrowAsync<IaProviderException>();
        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.IaConsultaError);
        var parsed = JsonDocument.Parse(audit.DetallesJson!);
        parsed.RootElement.TryGetProperty("extra", out var extra).Should().BeTrue();
        extra.TryGetProperty("provider_error", out _).Should().BeFalse(
            "V-02.09 (Fase 1.4): provider_error NO debe persistirse en auditoria.");
        // retry_after_seconds SI se mantiene (campo estructurado, no texto libre).
        extra.TryGetProperty("retry_after_seconds", out var retry).Should().BeTrue();
        retry.GetInt32().Should().Be(30);
    }

    // Captura la salida del proveedor con un body configurable.
    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly CapturingHandler _handler;

        public CapturingHttpClientFactory(
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string? responseBody = null,
            int? retryAfterSeconds = null)
        {
            _handler = new CapturingHandler(statusCode, responseBody, retryAfterSeconds);
        }

        public int RequestCount => _handler.RequestCount;
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
            "{\"choices\":[{\"message\":{\"content\":\"Seguros detectados: 100,00 EUR.\"}}],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":20}}";

        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly int? _retryAfterSeconds;

        public CapturingHandler(HttpStatusCode statusCode, string? responseBody, int? retryAfterSeconds)
        {
            _statusCode = statusCode;
            _responseBody = responseBody ?? DefaultResponseBody;
            _retryAfterSeconds = retryAfterSeconds;
        }

        public int RequestCount { get; private set; }
        public string LastPayload { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Content is not null)
            {
                LastPayload = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            };
            if (_retryAfterSeconds is > 0)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(_retryAfterSeconds.Value));
            }
            return response;
        }
    }
}

// V-02.09 (Fase 1.5): HttpClientFactory que sirve el body del catalogo
// de OpenRouter para los tests de filtrado.
internal class StaticHttpClientFactory : IHttpClientFactory
{
    private readonly string? _openRouterModelsBody;

    public StaticHttpClientFactory(string? openRouterModelsBody = null)
    {
        _openRouterModelsBody = openRouterModelsBody;
    }

    public HttpClient CreateClient(string name)
    {
        var handler = new StaticHandler(_openRouterModelsBody);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(name switch
            {
                "openai" => "https://openai.test/v1/",
                "minimax" => "https://minimax.test/v1/",
                _ => "https://openrouter.test/api/v1/"
            })
        };
    }
}

internal class StaticHandler : HttpMessageHandler
{
    private const string DefaultOpenRouterModels = """
        {"data":[
          {"id":"openrouter/auto","name":"OpenRouter Auto"},
          {"id":"nvidia/nemotron-3-super-120b-a12b:free","name":"Nemotron"},
          {"id":"openai/gpt-oss-120b:free","name":"gpt-oss-120b"},
          {"id":"google/gemma-4-31b-it:free","name":"Gemma 4"},
          {"id":"minimax/minimax-m2.5:free","name":"MiniMax M2.5"},
          {"id":"z-ai/glm-4.5-air:free","name":"GLM 4.5 Air"},
          {"id":"qwen/qwen3-coder:free","name":"Qwen3 Coder"}
        ]}
        """;

    private const string DefaultChatResponse =
        "{\"choices\":[{\"message\":{\"content\":\"Seguros detectados: 100,00 EUR.\"}}],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":20}}";

    private readonly string? _modelsBody;

    public StaticHandler(string? modelsBody)
    {
        _modelsBody = modelsBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // GET /models -> catalogo, POST /chat/completions -> respuesta del chat.
        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_modelsBody ?? DefaultOpenRouterModels, System.Text.Encoding.UTF8, "application/json")
            });
        }
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(DefaultChatResponse, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
