using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using AtlasBalance.API.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
namespace AtlasBalance.API.Tests;

// B-10 (iv) y (v): los seguimientos ("¿y el mes anterior?") perdian los
// filtros de la pregunta anterior (concepto, cuentas y titulares) y la
// busqueda semantica nunca rellenaba el termino de busqueda.
//
// A-6 (2): el rango de importes tiene que ser alcanzable: el
// planificador semantico puede expresarlo en filtros.
public class ConversationContextFiltrosTests
{
    private static readonly DateOnly Anchor = new(2026, 8, 5);

    private sealed class StubSemanticPlannerClient : ISemanticPlannerClient
    {
        private readonly string? _json;
        public StubSemanticPlannerClient(string? json) { _json = json; }
        public Task<SemanticPlanResponse> PlanToJsonAsync(
            string pregunta,
            IReadOnlyList<string> allowedOperations,
            CancellationToken cancellationToken,
            AiPseudonymMap? pseudonyms = null)
        {
            return Task.FromResult(new SemanticPlanResponse(_json, true));
        }
    }

    [Fact]
    public async Task Seguimiento_Debe_Restaurar_Concepto_Cuentas_Y_Titulares()
    {
        var cuentaId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var contexto = new ConversationContext
        {
            UsuarioId = Guid.NewGuid(),
            UltimaOperacion = nameof(FinancialOperation.Sum),
            UltimaMetrica = nameof(FinancialMetric.Gastos),
            UltimoConcepto = "seguro",
            UltimasCuentas = new[] { cuentaId.ToString() },
            UltimosTitulares = new[] { titularId.ToString() },
            UltimasDivisas = new[] { "EUR" }
        };
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);

        var resultado = await planner.ResolverAsync(
            "¿Y el mes anterior?", Anchor, CancellationToken.None, previousContext: contexto);

        resultado.Origen.Should().Be(PlanResolutionSource.Local);
        resultado.PatronLocal.Should().Be("seguimiento_memoria");
        resultado.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Ok);
        var plan = resultado.Evaluacion.Plan!;
        plan.Filtros.Concepto.Should().Be("seguro");
        plan.Filtros.CuentaIds.Should().Contain(cuentaId);
        plan.Filtros.TitularIds.Should().Contain(titularId);
        plan.Filtros.Divisas.Should().Contain("EUR");
        plan.Filtros.Periodo!.Tipo.Should().Be(FinancialPeriodKind.MesAnterior);
    }

    [Fact]
    public void Memoria_Conversacional_Debe_Guardar_El_Concepto()
    {
        var mem = new InMemoryConversationMemory();
        var userId = Guid.NewGuid();

        mem.Actualizar(userId, null, ctx => ctx with { UltimoConcepto = "seguro" });

        mem.Obtener(userId, null)!.UltimoConcepto.Should().Be("seguro");
    }

    [Fact]
    public async Task Planificador_Semantico_Debe_Aceptar_Importes_Minimo_Y_Maximo()
    {
        var stub = new StubSemanticPlannerClient(
            """{"operacion":"Sum","metrica":"Gastos","filtros":{"importe_minimo":100,"importe_maximo":500}}""");
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);

        var resultado = await planner.ResolverAsync(
            "Cuanto he gastado entre 100 y 500?", Anchor, CancellationToken.None);

        resultado.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Evaluacion.Plan!.Filtros.ImporteMinimo.Should().Be(100m);
        resultado.Evaluacion.Plan.Filtros.ImporteMaximo.Should().Be(500m);
    }

    [Fact]
    public async Task Planificador_Semantico_Debe_Rechazar_Importes_Negativos()
    {
        var stub = new StubSemanticPlannerClient(
            """{"operacion":"Sum","metrica":"Gastos","filtros":{"importe_minimo":-5}}""");
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);

        var resultado = await planner.ResolverAsync(
            "Cuanto he gastado en importes negativos?", Anchor, CancellationToken.None);

        resultado.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
        resultado.Evaluacion.Motivo.Should().Contain("negativ");
    }

    [Fact]
    public async Task Busqueda_Semantica_Con_Concepto_Debe_Llegar_A_La_Herramienta()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var titularId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "busqueda@atlasbalance.local",
            NombreCompleto = "Busqueda User",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        var cuentaId = Guid.NewGuid();
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "C1",
            Divisa = "EUR",
            Activa = true,
            BancoNombre = "Atlas Bank"
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Pago de seguro anual",
            Monto = -250m,
            Saldo = 750m,
            FilaNumero = 1
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Compra material oficina",
            Monto = -30m,
            Saldo = 720m,
            FilaNumero = 2
        });
        await db.SaveChangesAsync();

        // El modelo devuelve el termino en filtros.concepto, no en un
        // campo de termino de busqueda.
        var stub = new StubSemanticPlannerClient(
            """{"operacion":"Search","metrica":"Gastos","filtros":{"concepto":"seguro"}}""");
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);
        // Ancla en hoy: el periodo por defecto (mes actual) debe
        // incluir los extractos de hoy.
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var resultado = await planner.ResolverAsync(
            "Busca los pagos de seguro", hoy, CancellationToken.None);

        // Antes el plan se quedaba en "Aclarar: falta el termino de
        // busqueda" aunque el modelo si habia dado el concepto.
        resultado.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Evaluacion.Plan!.TerminoBusqueda.Should().Be("seguro");

        var tools = new FinancialToolsService(
            db,
            new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())));
        var scope = new UserAccessScope
        {
            UserId = userId,
            IsAdmin = true,
            HasPermissions = true,
            HasGlobalAccess = true
        };

        var busqueda = await tools.SearchTransactionsAsync(scope, resultado.Evaluacion.Plan!, CancellationToken.None);

        busqueda.FilasDevueltas.Should().Be(1);
        busqueda.Data![0].Concepto.Should().Contain("seguro");
    }
}
