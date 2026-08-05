using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Services.IaPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 4): tests del planificador de intenciones. Verifican
// los tres niveles:
//   - Nivel 1: local para preguntas frecuentes
//   - Nivel 3: aclaracion para preguntas ambiguas
//   - Nivel 2: el stub semantico inyecta un plan y el planificador
//     lo valida
public class IntentPlannerTests
{
    private static readonly DateOnly Anchor = new(2026, 8, 5);

    [Fact]
    public async Task Resolver_Pregunta_Vacia_Rechaza()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync(string.Empty, Anchor, CancellationToken.None);

        result.Origen.Should().Be(PlanResolutionSource.Rejected);
        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public async Task Resolver_Saldo_Actual_Devuelve_Plan_Lista_Saldo()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Cual es mi saldo actual", Anchor, CancellationToken.None);

        result.Origen.Should().Be(PlanResolutionSource.Local);
        result.PatronLocal.Should().Be("saldo_actual");
        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Ok);
        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.List);
        result.Evaluacion.Plan!.Metrica.Should().Be(FinancialMetric.Saldo);
    }

    [Fact]
    public async Task Resolver_Ultimo_Gasto_Devuelve_Plan_GetLatest()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Cual fue el ultimo gasto?", Anchor, CancellationToken.None);

        result.Origen.Should().Be(PlanResolutionSource.Local);
        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.GetLatest);
        result.Evaluacion.Plan!.Metrica.Should().Be(FinancialMetric.Gastos);
    }

    [Fact]
    public async Task Resolver_Ultimo_Ingreso_Devuelve_Plan_Ingresos()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Cual fue el ultimo ingreso?", Anchor, CancellationToken.None);

        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.GetLatest);
        result.Evaluacion.Plan!.Metrica.Should().Be(FinancialMetric.Ingresos);
    }

    [Fact]
    public async Task Resolver_Comisiones_Pendientes_Devuelve_Plan_Revision()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Cuales son las comisiones pendientes?", Anchor, CancellationToken.None);

        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.List);
        result.Evaluacion.Plan!.Filtros.Estados.Should().Contain("PENDIENTE");
    }

    [Fact]
    public async Task Resolver_Tendencia_Gastos_Devuelve_Plan_Trend()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Tendencia de gastos", Anchor, CancellationToken.None);

        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.Trend);
    }

    [Fact]
    public async Task Resolver_Ranking_Cuentas_Devuelve_Plan_Ranking()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Que cuentas han tenido mas gastos", Anchor, CancellationToken.None);

        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.Ranking);
        result.Evaluacion.Plan!.Agrupaciones.Should().Contain(FinancialGroupBy.Cuenta);
    }

    [Fact]
    public async Task Resolver_Que_Cuenta_Va_Peor_Devuelve_Aclaracion()
    {
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Que cuenta va peor", Anchor, CancellationToken.None);

        result.Origen.Should().Be(PlanResolutionSource.Clarification);
        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.AclaracionRequerida);
        result.Evaluacion.Opciones.Should().NotBeNull();
        result.Evaluacion.Opciones!.Count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task Resolver_Nivel_Semantico_Con_Plan_Valido_Pasa()
    {
        var stub = new StubSemanticPlannerClient(
            """{"operacion":"Sum","metrica":"Gastos","filtros":{}}""");
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Pregunta no contemplada por patrones locales", Anchor, CancellationToken.None);

        result.Origen.Should().Be(PlanResolutionSource.Semantic);
        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Ok);
        result.Evaluacion.Plan!.Operacion.Should().Be(FinancialOperation.Sum);
    }

    [Fact]
    public async Task Resolver_Nivel_Semantico_Con_Operacion_Invalida_Rechaza()
    {
        var stub = new StubSemanticPlannerClient(
            """{"operacion":"BorrarTodo","metrica":"Gastos"}""");
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Borra todos los extractos", Anchor, CancellationToken.None);

        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public async Task Resolver_Nivel_Semantico_Sin_Json_Rechaza()
    {
        var stub = new StubSemanticPlannerClient(null);
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Pregunta totalmente nueva", Anchor, CancellationToken.None);

        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
        result.Evaluacion.Motivo.Should().Contain("proveedor");
    }

    [Fact]
    public async Task Resolver_Nivel_Semantico_Con_Json_Mal_Formado_Rechaza()
    {
        var stub = new StubSemanticPlannerClient("esto no es json");
        var planner = new IntentPlanner(stub, NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("Pregunta cualquiera", Anchor, CancellationToken.None);

        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public async Task Resolver_Pregunta_Normalizada_Con_Acentos_Y_Mayusculas()
    {
        // Las mayusculas, acentos y dobles espacios no deben romper el
        // match local.
        var planner = new IntentPlanner(new NullSemanticPlannerClient(), NullLogger<IntentPlanner>.Instance);
        var result = await planner.ResolverAsync("  ¿Cuál    fue el ÚLTIMO gasto?  ", Anchor, CancellationToken.None);

        result.Origen.Should().Be(PlanResolutionSource.Local);
        result.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Ok);
    }

    private sealed class StubSemanticPlannerClient : ISemanticPlannerClient
    {
        private readonly string? _json;
        public StubSemanticPlannerClient(string? json) { _json = json; }
        public Task<string?> PlanToJsonAsync(string pregunta, IReadOnlyList<string> allowedOperations, CancellationToken cancellationToken)
        {
            return Task.FromResult(_json);
        }
    }
}
