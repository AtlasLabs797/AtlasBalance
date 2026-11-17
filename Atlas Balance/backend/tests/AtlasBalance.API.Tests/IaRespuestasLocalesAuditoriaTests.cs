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
using Xunit;

namespace AtlasBalance.API.Tests;

// S-6: las respuestas locales del planificador (ayuda de documentacion,
// aclaracion, fallo de ejecucion y respuesta del plan) se devolvian ANTES de
// cualquier control de limites y sin fila de auditoria. Repetir "cual es mi
// saldo" no agotaba los limites de IA y no dejaba rastro.
public class IaRespuestasLocalesAuditoriaTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Respuesta_Local_Del_Planificador_Deja_Fila_De_Auditoria_Con_Origen_Local()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = BuildSut(db);

        var result = await sut.AskAsync(AdminScope(userId), "cual es mi saldo", "127.0.0.1", CancellationToken.None);

        result.Origen.Should().Be("local");
        var auditoria = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.IaConsulta);
        using var document = JsonDocument.Parse(auditoria.DetallesJson!);
        document.RootElement.GetProperty("origen").GetString().Should().Be("local");
        document.RootElement.GetProperty("schema_version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task La_Fila_De_Auditoria_Local_No_Contiene_El_Prompt_Ni_Datos_De_La_Respuesta()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = BuildSut(db);

        await sut.AskAsync(AdminScope(userId), "cual es mi saldo", "127.0.0.1", CancellationToken.None);

        var auditoria = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.IaConsulta);
        auditoria.DetallesJson.Should().NotContain("cual es mi saldo");
        auditoria.DetallesJson.Should().NotContain("Atlas Labs");
        auditoria.DetallesJson.Should().NotContain("Cuenta Operativa");
        auditoria.DetallesJson.Should().NotContain("@atlasbalance.local");
    }

    [Fact]
    public async Task La_Respuesta_Local_Cuenta_Contra_Los_Contadores_De_Uso_Del_Usuario()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = BuildSut(db);

        await sut.AskAsync(AdminScope(userId), "cual es mi saldo", "127.0.0.1", CancellationToken.None);

        var uso = await db.IaUsoUsuarios.SingleAsync(x => x.UsuarioId == userId);
        uso.Requests.Should().Be(1);
    }

    [Fact]
    public async Task Al_Superar_El_Limite_Por_Minuto_La_Pregunta_Local_Ya_No_Se_Responde()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = BuildSut(db);

        // El limite por minuto por defecto es 6. Cada respuesta local deja una
        // fila de auditoria, asi que a partir de la septima ya no se responde.
        for (int i = 0; i < 6; i++)
        {
            var respuesta = await sut.AskAsync(AdminScope(userId), "cual es mi saldo", "127.0.0.1", CancellationToken.None);
            respuesta.Origen.Should().Be("local");
        }

        var act = () => sut.AskAsync(AdminScope(userId), "cual es mi saldo", "127.0.0.1", CancellationToken.None);

        await act.Should().ThrowAsync<IaLimitExceededException>();
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

    private static async Task<Guid> SeedAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "ia.local@atlasbalance.local",
            NombreCompleto = "IA Local",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "ai_enabled", Valor = "true", Tipo = "bool", Descripcion = "IA habilitada" },
            new Configuracion { Clave = "ai_provider", Valor = "OPENROUTER", Tipo = "string", Descripcion = "Proveedor IA" },
            new Configuracion { Clave = "ai_model", Valor = AtlasBalance.API.Constants.AiConfiguration.OpenRouterAutoModel, Tipo = "string", Descripcion = "Modelo IA" },
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

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
