using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

// A-5: el bloque de privacidad de OpenRouter (zdr + data_collection=deny) se
// construye en un UNICO sitio (AiConfiguration.OpenRouterPrivacyProvider) y lo
// usan tanto la llamada principal del servicio como la del planificador
// semantico. Los modelos gratuitos se quedan sin bloque: sus endpoints no
// garantizan rutas compatibles con ZDR (LOG_ERRORES_INCIDENCIAS 2026-09-21).
public class IaPrivacidadYContadoresTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task AskAsync_Con_OpenRouter_De_Pago_Envia_El_Bloque_De_Privacidad()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, AiConfiguration.OpenRouterAutoModel);
        var http = new CapturingHttpClientFactory();
        var sut = BuildSut(db, http);

        var result = await sut.AskAsync(AdminScope(userId), "detalle de los seguros de este mes", "127.0.0.1", CancellationToken.None);

        result.Origen.Should().Be("proveedor");
        using var document = JsonDocument.Parse(http.LastPayload);
        var root = document.RootElement;
        root.TryGetProperty("provider", out var privacy).Should().BeTrue();
        privacy.GetProperty("zdr").GetBoolean().Should().BeTrue();
        privacy.GetProperty("data_collection").GetString().Should().Be("deny");
    }

    [Fact]
    public async Task AskAsync_Con_Modelo_Gratuito_No_Envia_El_Bloque_De_Privacidad()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, AiConfiguration.OpenRouterFreeModel, permitirRetencionDatos: true);
        var http = new CapturingHttpClientFactory();
        var sut = BuildSut(db, http);

        var result = await sut.AskAsync(AdminScope(userId), "detalle de los seguros de este mes", "127.0.0.1", CancellationToken.None);

        result.Origen.Should().Be("proveedor");
        using var document = JsonDocument.Parse(http.LastPayload);
        document.RootElement.TryGetProperty("provider", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Planificador_Semantic_Envia_El_Mismo_Bloque_De_Privacidad_Que_El_Servicio()
    {
        await using var dbServicio = BuildDbContext();
        var userId = await SeedAsync(dbServicio, AiConfiguration.OpenRouterAutoModel);
        var httpServicio = new CapturingHttpClientFactory();
        var sut = BuildSut(dbServicio, httpServicio);

        await sut.AskAsync(AdminScope(userId), "detalle de los seguros de este mes", "127.0.0.1", CancellationToken.None);

        await using var dbPlanificador = BuildDbContext();
        await SeedAsync(dbPlanificador, AiConfiguration.OpenRouterAutoModel);
        var httpPlanificador = new CapturingHttpClientFactory();
        var planificador = new SemanticPlannerClient(dbPlanificador, httpPlanificador, new PlainTextSecretProtector());

        await planificador.PlanToJsonAsync(
            "cual es mi saldo",
            IntentPlanner.OperacionesPermitidas,
            CancellationToken.None);

        using var documentoServicio = JsonDocument.Parse(httpServicio.LastPayload);
        using var documentoPlanificador = JsonDocument.Parse(httpPlanificador.LastPayload);

        documentoPlanificador.RootElement.TryGetProperty("provider", out var privacidadPlanificador).Should().BeTrue();
        documentoServicio.RootElement.TryGetProperty("provider", out var privacidadServicio).Should().BeTrue();
        privacidadPlanificador.GetRawText().Should().Be(privacidadServicio.GetRawText());
    }

    [Fact]
    public async Task AskAsync_Con_OpenAi_No_Envia_El_Bloque_De_Privacidad_De_OpenRouter()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "ia.privacidad.openai@atlasbalance.local",
            NombreCompleto = "IA Privacidad OpenAI",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = "OPENAI", Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "ai_model", Valor = "gpt-4o-mini", Tipo = "string", Descripcion = "Modelo IA" },
            new Configuracion { Clave = "openai_api_key", Valor = "test-key", Tipo = "secret", Descripcion = "API key" },
            new Configuracion { Clave = "ai_max_output_tokens", Valor = "100", Tipo = "int", Descripcion = "Salida" },
            new Configuracion { Clave = "ai_max_context_rows", Valor = "10", Tipo = "int", Descripcion = "Contexto" });
        await db.SaveChangesAsync();
        var http = new CapturingHttpClientFactory();
        var sut = BuildSut(db, http);

        var result = await sut.AskAsync(AdminScope(userId), "detalle de los seguros de este mes", "127.0.0.1", CancellationToken.None);

        result.Origen.Should().Be("proveedor");
        using var document = JsonDocument.Parse(http.LastPayload);
        document.RootElement.TryGetProperty("provider", out _).Should().BeFalse();
    }

    private static AtlasAiService BuildSut(AppDbContext db, IHttpClientFactory httpClientFactory) =>
        new(
            db,
            httpClientFactory,
            new PlainTextSecretProtector(),
            new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())),
            TestAuditService.Create(db),
            NullLogger<AtlasAiService>.Instance);

    private static UserAccessScope AdminScope(Guid userId) => new()
    {
        UserId = userId,
        IsAdmin = true,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static async Task<Guid> SeedAsync(AppDbContext db, string model, bool permitirRetencionDatos = false)
    {
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "ia.privacidad@atlasbalance.local",
            NombreCompleto = "IA Privacidad",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = "OPENROUTER", Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "ai_model", Valor = model, Tipo = "string", Descripcion = "Modelo IA" },
            new Configuracion { Clave = "openrouter_api_key", Valor = "test-key", Tipo = "secret", Descripcion = "API key" },
            new Configuracion { Clave = "ai_max_output_tokens", Valor = "100", Tipo = "int", Descripcion = "Salida" },
            new Configuracion { Clave = "ai_max_context_rows", Valor = "10", Tipo = "int", Descripcion = "Contexto" },
            new Configuracion { Clave = "ai_allow_data_retention", Valor = permitirRetencionDatos ? "true" : "false", Tipo = "bool", Descripcion = "Permitir modelos gratuitos" });
        await db.SaveChangesAsync();
        return userId;
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly CapturingHandler _handler = new();

        public string LastPayload => _handler.LastPayload;

        public HttpClient CreateClient(string name) => new(_handler)
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

        public string LastPayload { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPayload = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DefaultResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}

// B-11: los contadores de coste se actualizan con lectura-modificacion-
// escritura sobre CONFIGURACION e IA_USO_USUARIOS. Con peticiones
// concurrentes del mismo usuario se perdian incrementos.
//
// Necesita PostgreSQL de verdad: el proveedor InMemory resuelve las consultas
// de forma sincrona, asi que nunca se entrelazan dos actualizaciones y la
// carrera no se podria reproducir.
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class IaContadoresConcurrenciaTests
{
    private const int Peticiones = 8;

    private readonly PostgresFixture _fixture;

    private string ConnectionString => new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
    {
        IncludeErrorDetail = true
    }.ToString();
    public IaContadoresConcurrenciaTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Las_Actualizaciones_Concurrentes_No_Pierden_Incrementos()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var userId = Guid.NewGuid();
        var monthKey = FechasOperativas.HoyLocal().ToString("yyyy-MM");

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = "ia.contadores@atlasbalance.local",
                NombreCompleto = "IA Contadores",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PuedeUsarIa = true
            });
            setup.IaUsoUsuarios.Add(new IaUsoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                MonthKey = monthKey
            });
            await UpsertConfigAsync(setup,
                ("ai_enabled", "true", "bool"),
                ("ai_provider", "OPENROUTER", "string"),
                ("ai_model", AiConfiguration.OpenRouterAutoModel, "string"),
                ("openrouter_api_key", "test-key", "secret"),
                ("ai_max_output_tokens", "100", "int"),
                ("ai_max_context_rows", "10", "int"),
                ("ai_requests_per_minute", "1000", "int"),
                ("ai_requests_per_hour", "1000", "int"),
                ("ai_requests_per_day", "1000", "int"),
                ("ai_global_requests_per_day", "1000", "int"),
                // Los contadores de uso se crean de antemano: si no, las ocho
                // peticiones concurrentes los insertarian a la vez y el fallo
                // seria de clave duplicada, no de incremento perdido.
                ("ai_usage_month_key", monthKey, "string"),
                ("ai_usage_month_cost_eur", "0", "decimal"),
                ("ai_usage_total_cost_eur", "0", "decimal"),
                ("ai_usage_total_requests", "0", "int"),
                ("ai_usage_last_user_id", "", "string"),
                ("ai_usage_last_at_utc", "", "datetime"));
            await setup.SaveChangesAsync();
        }

        var http = new CapturingHttpClientFactory();
        var scope = new UserAccessScope { UserId = userId, IsAdmin = true, HasPermissions = true, HasGlobalAccess = true };

        var tareas = Enumerable.Range(0, Peticiones).Select(async _ =>
        {
            await using var db = new AppDbContext(options);
            var sut = BuildSut(db, http);
            await sut.AskAsync(scope, "detalle de los seguros de este mes", "127.0.0.1", CancellationToken.None);
        });

        await Task.WhenAll(tareas);

        await using var verify = new AppDbContext(options);
        var peticionesContadas = await verify.IaUsoUsuarios
            .Where(x => x.UsuarioId == userId)
            .SumAsync(x => x.Requests);
        var totalSolicitudes = await verify.Configuraciones
            .Where(x => x.Clave == "ai_usage_total_requests")
            .Select(x => x.Valor)
            .SingleAsync();

        peticionesContadas.Should().Be(Peticiones, "cada peticion debe sumar su incremento al contador del usuario");
        totalSolicitudes.Should().Be(Peticiones.ToString(CultureInfo.InvariantCulture), "cada peticion debe sumar su incremento al contador global");
    }

    // Las migraciones ya siembran filas de CONFIGURACION (ai_enabled, ...):
    // hay que actualizar las existentes en vez de insertar duplicadas.
    private static async Task UpsertConfigAsync(AppDbContext db, params (string Clave, string Valor, string Tipo)[] filas)
    {
        foreach (var (clave, valor, tipo) in filas)
        {
            var fila = await db.Configuraciones.FirstOrDefaultAsync(x => x.Clave == clave);
            if (fila is null)
            {
                db.Configuraciones.Add(new Configuracion { Clave = clave, Valor = valor, Tipo = tipo, Descripcion = "Test IA" });
            }
            else
            {
                fila.Valor = valor;
                fila.Tipo = tipo;
            }
        }
    }

    private static AtlasAiService BuildSut(AppDbContext db, IHttpClientFactory httpClientFactory) =>
        new(
            db,
            httpClientFactory,
            new PlainTextSecretProtector(),
            new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())),
            TestAuditService.Create(db),
            NullLogger<AtlasAiService>.Instance);

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private const string DefaultResponseBody =
            "{\"choices\":[{\"message\":{\"content\":\"Seguros detectados: 100,00 EUR.\"}}],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":20}}";

        public HttpClient CreateClient(string name) => new HttpClient(new StubHandler())
        {
            BaseAddress = new Uri("https://openrouter.test/api/v1/")
        };

        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DefaultResponseBody, Encoding.UTF8, "application/json")
                });
        }
    }
}
