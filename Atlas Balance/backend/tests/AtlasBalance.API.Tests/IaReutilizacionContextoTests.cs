using System.Data.Common;
using System.Net;
using System.Text;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

// R-2: dentro de la misma pregunta, CONFIGURACIONES se leia dos veces (el
// servicio y el planificador semantico) y el mapa de seudonimos se
// construia dos veces (la factory del planificador y la ruta del proveedor,
// cuando el planificador rechaza el plan).
//
// Se mide con SQL real porque el proveedor InMemory de los tests no ejecuta
// comandos ni admite interceptores de comando.
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class IaReutilizacionContextoTests
{
    private readonly PostgresFixture _fixture;

    private string ConnectionString => new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
    {
        IncludeErrorDetail = true
    }.ToString();
    public IaReutilizacionContextoTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task La_Configuracion_Y_El_Mapa_De_Seudonimos_Se_Cargan_Una_Sola_Vez_Por_Pregunta()
    {
        var contador = new SqlContadorInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(contador)
            .Options;

        var userId = Guid.NewGuid();
        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = "ia.reutilizacion@atlasbalance.local",
                NombreCompleto = "IA Reutilizacion",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PuedeUsarIa = true
            });
            await UpsertConfigAsync(setup,
                ("ai_enabled", "true", "bool"),
                ("ai_provider", "OPENROUTER", "string"),
                ("ai_model", AtlasBalance.API.Constants.AiConfiguration.OpenRouterAutoModel, "string"),
                ("openrouter_api_key", "test-key", "secret"),
                ("ai_max_output_tokens", "100", "int"),
                ("ai_max_context_rows", "10", "int"));
            var titularId = Guid.NewGuid();
            setup.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
            var cuentaId = Guid.NewGuid();
            setup.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Operativa",
                Divisa = "EUR",
                Activa = true,
                BancoNombre = "Atlas Bank"
            });
            setup.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = FechasOperativas.HoyLocal(),
                Concepto = "movimiento",
                Monto = -100m,
                Saldo = 1000m,
                FilaNumero = 1
            });
            await setup.SaveChangesAsync();
        }

        contador.Reiniciar();

        await using var db = new AppDbContext(options);
        var http = new SecuenciaHttpClientFactory();
        var sut = BuildSut(db, http);

        await sut.AskAsync(
            new UserAccessScope { UserId = userId, IsAdmin = true, HasPermissions = true, HasGlobalAccess = true },
            "detalle de los seguros de este mes",
            "127.0.0.1",
            CancellationToken.None);

        // La carga completa de CONFIGURACION (sin WHERE) solo puede ocurrir una
        // vez por pregunta: el servicio y el planificador comparten el snapshot.
        contador.SelectsDeConfiguracion.Should().Be(1, "SQL capturada: " + string.Join(" | ", contador.Sql));
        // El mapa de seudonimos consulta TITULARES una vez por construccion.
        // Antes se construia dos veces (factory del planificador + ruta del
        // proveedor) y ahora se construye una sola.
        contador.SelectsDeTitulares.Should().Be(1, "SQL de titulares capturada: " + string.Join(" | ", contador.Sql.Where(x => x.Contains("from titulares", StringComparison.Ordinal))));
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

    private static AtlasAiService BuildSut(AppDbContext db, IHttpClientFactory httpClientFactory)
    {
        var protector = new PlainTextSecretProtector();
        var userAccess = new UserAccessService(
            db,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()));
        var planificador = new IntentPlanner(
            new SemanticPlannerClient(db, httpClientFactory, protector),
            NullLogger<IntentPlanner>.Instance);

        return new AtlasAiService(
            db,
            httpClientFactory,
            protector,
            userAccess,
            TestAuditService.Create(db),
            NullLogger<AtlasAiService>.Instance,
            planificador,
            new PlanExecutor(),
            new FinancialToolsService(db, userAccess),
            new DocumentationHelpService(string.Empty),
            new InMemoryConversationMemory());
    }

    private sealed class SqlContadorInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _sql = new();

        public int SelectsDeConfiguracion => _sql.Count(x =>
            x.StartsWith("select", StringComparison.Ordinal) &&
            x.Contains("from configuracion", StringComparison.Ordinal) &&
            !x.Contains(" where ", StringComparison.Ordinal));

        // Solo la consulta de titulares del mapa de seudonimos empieza asi
        // ("select distinct t.nombre from titulares ..."). Las subconsultas del
        // contexto financiero tambien mencionan titulares, pero no con esta
        // forma.
        public int SelectsDeTitulares => _sql.Count(x =>
            x.StartsWith("select distinct t.nombre from titulares", StringComparison.Ordinal));

        public IReadOnlyList<string> Sql => _sql;

        public void Reiniciar() => _sql.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Registrar(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Registrar(command.CommandText);
            return ValueTask.FromResult(result);
        }

        private void Registrar(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;
            // Postgres recibe los identificadores entrecomillados ("CONFIGURACION"):
            // se quitan las comillas y se pasa a minuscula para poder comparar.
            var normalizado = sql.Replace("\"", string.Empty).ToLowerInvariant();
            _sql.Add(System.Text.RegularExpressions.Regex.Replace(normalizado, @"\s+", " ").Trim());
        }
    }

    // La primera llamada (planificador semantico) devuelve un plan invalido:
    // el planificador lo rechaza y el flujo sigue hacia la ruta del proveedor,
    // que es el caso en el que el mapa de seudonimos se construia dos veces.
    private sealed class SecuenciaHttpClientFactory : IHttpClientFactory
    {
        private readonly SecuenciaHandler _handler = new();

        public HttpClient CreateClient(string name) => new(_handler)
        {
            BaseAddress = new Uri("https://openrouter.test/api/v1/")
        };
    }

    private sealed class SecuenciaHandler : HttpMessageHandler
    {
        private const string RespuestaPlanInvalido =
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"operacion\\\":\\\"OperacionInventada\\\",\\\"metrica\\\":\\\"Saldo\\\"}\"}}]}";
        private const string RespuestaProveedor =
            "{\"choices\":[{\"message\":{\"content\":\"Seguros detectados: 100,00 EUR.\"}}],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":20}}";

        private int _llamadas;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = Interlocked.Increment(ref _llamadas) == 1 ? RespuestaPlanInvalido : RespuestaProveedor;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            });
        }
    }
}
