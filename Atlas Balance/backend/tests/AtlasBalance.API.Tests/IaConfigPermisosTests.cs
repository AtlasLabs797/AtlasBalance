using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

// S-4: GET /api/ia/config devolvia presupuesto y coste GLOBAL a cualquier
// usuario. Para un usuario que no es ADMIN la respuesta solo lleva su propio
// uso y si puede usar la IA; los campos globales se OMITEN del JSON (null),
// no se rellenan con 0 (que seria informacion falsa).
public class IaConfigPermisosTests
{
    // Mismas opciones que Program.cs (AddControllers -> AddJsonOptions).
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetConfigAsync_Con_Admin_Incluye_Los_Campos_Globales()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, puedeUsarIa: true);

        var response = await BuildSut(db).GetConfigAsync(AdminScope(userId), CancellationToken.None);
        var json = JsonSerializer.Serialize(response, ApiJsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("presupuesto_mensual_eur").GetDecimal().Should().Be(25m);
        root.GetProperty("presupuesto_total_eur").GetDecimal().Should().Be(100m);
        root.GetProperty("coste_mes_estimado_eur").GetDecimal().Should().Be(3m);
        root.GetProperty("coste_total_estimado_eur").GetDecimal().Should().Be(7m);
        root.GetProperty("requests_globales_por_dia").GetInt32().Should().Be(300);
        root.GetProperty("requests_por_minuto").GetInt32().Should().Be(6);
        root.GetProperty("porcentaje_aviso_presupuesto").GetInt32().Should().Be(80);
        root.GetProperty("input_cost_per_million_tokens_eur").GetDecimal().Should().Be(1m);
        root.GetProperty("output_cost_per_million_tokens_eur").GetDecimal().Should().Be(2m);
    }

    [Fact]
    public async Task GetConfigAsync_Sin_Admin_No_Incluye_Los_Campos_Globales()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, puedeUsarIa: true);

        var response = await BuildSut(db).GetConfigAsync(EmpleadoScope(userId), CancellationToken.None);
        var json = JsonSerializer.Serialize(response, ApiJsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        foreach (var campo in new[]
                 {
                     "presupuesto_mensual_eur",
                     "presupuesto_total_eur",
                     "coste_mes_estimado_eur",
                     "coste_total_estimado_eur",
                     "requests_globales_por_dia",
                     "requests_por_minuto",
                     "requests_por_hora",
                     "requests_por_dia",
                     "porcentaje_aviso_presupuesto",
                     "input_cost_per_million_tokens_eur",
                     "output_cost_per_million_tokens_eur",
                     "openrouter_api_key_configurada",
                     "openai_api_key_configurada",
                     "minimax_api_key_configurada",
                     "max_input_tokens",
                     "max_output_tokens",
                     "max_context_rows"
                 })
        {
            root.TryGetProperty(campo, out _).Should().BeFalse($"el campo global '{campo}' no debe salir para un usuario que no es ADMIN");
        }
    }

    [Fact]
    public async Task GetConfigAsync_Sin_Admin_Incluye_Su_Propio_Uso_Y_Su_Estado()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, puedeUsarIa: true);

        var response = await BuildSut(db).GetConfigAsync(EmpleadoScope(userId), CancellationToken.None);
        var json = JsonSerializer.Serialize(response, ApiJsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("provider").GetString().Should().Be("OPENROUTER");
        root.GetProperty("model").GetString().Should().Be(AiConfiguration.OpenRouterAutoModel);
        root.GetProperty("habilitada").GetBoolean().Should().BeTrue();
        root.GetProperty("usuario_puede_usar").GetBoolean().Should().BeTrue();
        root.GetProperty("configurada").GetBoolean().Should().BeTrue();
        root.GetProperty("mensaje_estado").GetString().Should().Be("IA configurada.");
        root.GetProperty("permite_retencion_datos").GetBoolean().Should().BeFalse();
        root.GetProperty("modelos_permitidos").GetArrayLength().Should().Be(1);
        root.GetProperty("thinking_modes").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("presupuesto_mensual_usuario_eur").GetDecimal().Should().Be(5m);
        root.GetProperty("coste_mes_usuario_estimado_eur").GetDecimal().Should().Be(0.25m);
        root.GetProperty("requests_mes_usuario").GetInt32().Should().Be(4);
        root.GetProperty("tokens_entrada_mes_usuario").GetInt64().Should().Be(120);
        root.GetProperty("tokens_salida_mes_usuario").GetInt64().Should().Be(30);
    }

    [Fact]
    public async Task GetConfigAsync_Usuario_Sin_Permiso_No_Puede_Usar_La_IA()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, puedeUsarIa: false);

        var response = await BuildSut(db).GetConfigAsync(EmpleadoScope(userId), CancellationToken.None);
        var json = JsonSerializer.Serialize(response, ApiJsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("usuario_puede_usar").GetBoolean().Should().BeFalse();
        root.GetProperty("configurada").GetBoolean().Should().BeFalse();
        root.GetProperty("mensaje_estado").GetString().Should().Be("Tu usuario no tiene permiso para usar IA.");
    }

    private static AtlasAiService BuildSut(AppDbContext db) =>
        new(
            db,
            new StaticHttpClientFactory(),
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

    private static UserAccessScope EmpleadoScope(Guid userId) => new()
    {
        UserId = userId,
        IsAdmin = false,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static async Task<Guid> SeedAsync(AppDbContext db, bool puedeUsarIa)
    {
        var userId = Guid.NewGuid();
        var monthKey = FechasOperativas.HoyLocal().ToString("yyyy-MM");
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "ia.config@atlasbalance.local",
            NombreCompleto = "IA Config",
            PasswordHash = "hash",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PuedeUsarIa = puedeUsarIa
        });
        db.IaUsoUsuarios.Add(new IaUsoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            MonthKey = monthKey,
            Requests = 4,
            InputTokens = 120,
            OutputTokens = 30,
            CosteEstimadoEur = 0.25m
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = "OPENROUTER", Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "ai_model", Valor = AiConfiguration.OpenRouterAutoModel, Tipo = "string", Descripcion = "Modelo IA" },
            new Configuracion { Clave = "openrouter_api_key", Valor = "test-key", Tipo = "secret", Descripcion = "API key" },
            new Configuracion { Clave = "ai_monthly_budget_eur", Valor = "25", Tipo = "decimal", Descripcion = "Presupuesto mensual" },
            new Configuracion { Clave = "ai_user_monthly_budget_eur", Valor = "5", Tipo = "decimal", Descripcion = "Presupuesto mensual usuario" },
            new Configuracion { Clave = "ai_total_budget_eur", Valor = "100", Tipo = "decimal", Descripcion = "Presupuesto total" },
            new Configuracion { Clave = "ai_usage_month_key", Valor = monthKey, Tipo = "string", Descripcion = "Mes de uso" },
            new Configuracion { Clave = "ai_usage_month_cost_eur", Valor = "3", Tipo = "decimal", Descripcion = "Coste del mes" },
            new Configuracion { Clave = "ai_usage_total_cost_eur", Valor = "7", Tipo = "decimal", Descripcion = "Coste total" },
            new Configuracion { Clave = "ai_input_cost_per_1m_tokens_eur", Valor = "1", Tipo = "decimal", Descripcion = "Coste entrada" },
            new Configuracion { Clave = "ai_output_cost_per_1m_tokens_eur", Valor = "2", Tipo = "decimal", Descripcion = "Coste salida" });
        await db.SaveChangesAsync();
        return userId;
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
