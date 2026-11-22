using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

// B-16: "hoy" se calculaba con DateTime.UtcNow y el resumen anual dependia de
// un ano escrito a mano ("2026"). Entre las 00:00 y la 01:00 (invierno) o las
// 02:00 (verano) de la zona operativa el dia UTC es el anterior: el corte de
// mes de los contadores, el "hoy" del contexto y el ancla del planificador
// razonaban sobre el dia equivocado.
public class IaFechasOperativasTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Una_Pregunta_Con_Ano_Produce_El_Resumen_Del_Ano_En_Curso()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var http = new CapturingHttpClientFactory();
        var sut = BuildSut(db, http);
        var hoy = FechasOperativas.HoyLocal();

        await sut.AskAsync(AdminScope(userId), "detalle de los seguros de este ano", "127.0.0.1", CancellationToken.None);

        var contexto = ExtraerContexto(http.LastPayload);
        contexto.Should().Contain($"PERIODO 01/01/{hoy.Year} - {hoy:dd/MM/yyyy}");
    }

    [Fact]
    public void El_Codigo_No_Depende_De_Un_Ano_Literal()
    {
        // No se puede manipular el reloj del proceso en los tests, asi que se
        // comprueba directamente que el detector del resumen anual no lleva un
        // ano escrito a mano: el ano se calcula desde la fecha operativa.
        var fuente = TestSourceLocator.Find("AtlasBalance.API", "Services", "AtlasAiService.cs");
        var contenido = File.ReadAllText(fuente);

        Regex.Matches(contenido, "\"[12][0-9]{3}\"").Should().BeEmpty("el servicio no debe llevar anos literales");
        contenido.Should().Contain("today.Year");
    }

    [Fact]
    public void El_Servicio_Usa_La_Fecha_Operativa_Local_Y_No_El_Dia_Utc()
    {
        // El reloj del proceso no se puede manipular en los tests, asi que se
        // comprueba el cableado: el servicio debe pedir la fecha operativa al
        // helper (FechasOperativas) y no a DateTime.UtcNow.
        var fuente = TestSourceLocator.Find("AtlasBalance.API", "Services", "AtlasAiService.cs");
        var contenido = File.ReadAllText(fuente);

        contenido.Should().Contain("FechasOperativas.AhoraLocal()");
        contenido.Should().Contain("FechasOperativas.HoyLocal()");
    }

    [Fact]
    public void Las_Herramientas_Del_Planificador_Tambien_Usan_La_Fecha_Operativa()
    {
        // El mismo motivo que en el servicio: saldos, tendencias, anomalias y el
        // ancla del plan tienen que razonar sobre el dia local. Con el dia UTC,
        // entre las 00:00 y la 01:00 (invierno) o las 02:00 (verano) el mes y el
        // dia currentes serian los anteriores.
        var herramientas = TestSourceLocator.Find("AtlasBalance.API", "Services", "IaPlanner", "FinancialToolsService.cs");
        var contenidoHerramientas = File.ReadAllText(herramientas);

        contenidoHerramientas.Should().NotContain("DateTime.UtcNow");
        contenidoHerramientas.Should().Contain("FechasOperativas.HoyLocal()");

        var ejecutor = TestSourceLocator.Find("AtlasBalance.API", "Services", "IaPlanner", "PlanExecutor.cs");
        var contenidoEjecutor = File.ReadAllText(ejecutor);

        contenidoEjecutor.Should().NotContain("DateTime.UtcNow");
        contenidoEjecutor.Should().Contain("FechasOperativas.HoyLocal()");
    }

    [Fact]
    public async Task El_Corte_De_Mes_De_Los_Contadores_Usa_La_Fecha_Operativa_Local()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = BuildSut(db, new CapturingHttpClientFactory());
        var mesOperativo = FechasOperativas.HoyLocal().ToString("yyyy-MM");

        await sut.AskAsync(AdminScope(userId), "cual es mi saldo", "127.0.0.1", CancellationToken.None);

        var uso = await db.IaUsoUsuarios.SingleAsync(x => x.UsuarioId == userId);
        uso.MonthKey.Should().Be(mesOperativo);
    }

    [Fact]
    public void La_Fecha_Operativa_Coincide_Con_La_Conversion_Explicita_A_Europe_Madrid()
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById(FechasOperativas.ZonaHorariaId);
        var esperado = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zona);

        FechasOperativas.HoyLocal().Should().Be(DateOnly.FromDateTime(esperado));
        FechasOperativas.InicioDeMes(FechasOperativas.HoyLocal())
            .Should().Be(new DateOnly(esperado.Year, esperado.Month, 1));
    }

    private static string ExtraerContexto(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var mensajes = document.RootElement.GetProperty("messages");
        var indice = -1;
        for (int i = 0; i < mensajes.GetArrayLength(); i++)
        {
            var contenido = mensajes[i].GetProperty("content").GetString() ?? string.Empty;
            if (contenido.StartsWith("CONTEXTO_FINANCIERO_NO_CONFIABLE", StringComparison.Ordinal))
            {
                indice = i;
                break;
            }
        }

        indice.Should().BeGreaterThanOrEqualTo(0, "el contexto financiero debe enviarse al proveedor");
        return mensajes[indice].GetProperty("content").GetString() ?? string.Empty;
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

    private static async Task<Guid> SeedAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "ia.fechas@atlasbalance.local",
            NombreCompleto = "IA Fechas",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = "OPENROUTER", Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "ai_model", Valor = AiConfiguration.OpenRouterAutoModel, Tipo = "string", Descripcion = "Modelo IA" },
            new Configuracion { Clave = "openrouter_api_key", Valor = "test-key", Tipo = "secret", Descripcion = "API key" });
        var titularId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        var cuentaId = Guid.NewGuid();
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Operativa",
            Divisa = "EUR",
            Activa = true,
            BancoNombre = "Atlas Bank"
        });
        var hoy = FechasOperativas.HoyLocal();
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = hoy,
            Concepto = "movimiento",
            Monto = -100m,
            Saldo = 1000m,
            FilaNumero = 1
        });
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
