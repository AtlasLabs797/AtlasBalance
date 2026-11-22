using FluentAssertions;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// B-11: el timeout del planificador semantico escapaba como 500 (el
// TaskCanceledException del HttpClient deriva de OperationCanceledException
// y el catch del planificador solo excluia exactamente ese tipo) y la
// llamada ya facturada no se contabilizaba (SemanticCallAttempted se
// perdia).
public class IntentPlannerTimeoutTests
{
    private static readonly DateOnly Anchor = new(2026, 8, 5);

    private sealed class ThrowingSemanticPlannerClient : ISemanticPlannerClient
    {
        private readonly Func<Exception> _excepcion;
        public ThrowingSemanticPlannerClient(Func<Exception> excepcion) { _excepcion = excepcion; }
        public Task<SemanticPlanResponse> PlanToJsonAsync(
            string pregunta,
            IReadOnlyList<string> allowedOperations,
            CancellationToken cancellationToken,
            AiPseudonymMap? pseudonyms = null)
        {
            throw _excepcion();
        }
    }

    [Fact]
    public async Task Resolver_Con_Timeout_Del_Proveedor_Debe_Devolver_Rechazado_Contabilizado()
    {
        var planner = new IntentPlanner(
            new ThrowingSemanticPlannerClient(() => new TimeoutException("timeout del proveedor")),
            NullLogger<IntentPlanner>.Instance);

        var resultado = await planner.ResolverAsync(
            "Pregunta que agota el tiempo de espera", Anchor, CancellationToken.None);

        resultado.Origen.Should().Be(PlanResolutionSource.Rejected);
        resultado.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
        resultado.Evaluacion.Motivo.Should().NotBeNullOrEmpty();
        resultado.Evaluacion.Motivo.Should().Contain("respondio a tiempo");
        // La llamada al proveedor ya se hizo: se contabiliza el coste.
        resultado.SemanticCallAttempted.Should().BeTrue();
    }

    [Fact]
    public async Task Resolver_Con_TaskCanceled_Que_No_Viene_Del_Llamante_Debe_Ser_Timeout()
    {
        // Es lo que lanza el HttpClient cuando se agota su timeout: un
        // TaskCanceledException. Antes escapaba como 500.
        var planner = new IntentPlanner(
            new ThrowingSemanticPlannerClient(() => new TaskCanceledException("timeout del proveedor")),
            NullLogger<IntentPlanner>.Instance);

        var resultado = await planner.ResolverAsync(
            "Pregunta que agota el tiempo de espera", Anchor, CancellationToken.None);

        resultado.Origen.Should().Be(PlanResolutionSource.Rejected);
        resultado.Evaluacion.Estado.Should().Be(FinancialPlanStatus.Rechazado);
        resultado.Evaluacion.Motivo.Should().Contain("respondio a tiempo");
        resultado.SemanticCallAttempted.Should().BeTrue();
    }

    [Fact]
    public async Task Resolver_Con_Cancelacion_Del_Llamante_Debe_Propagar_La_Excepcion()
    {
        var planner = new IntentPlanner(
            new ThrowingSemanticPlannerClient(() => new OperationCanceledException("cancelled por el llamante")),
            NullLogger<IntentPlanner>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var accion = async () => await planner.ResolverAsync(
            "Pregunta cancelada por el usuario", Anchor, cts.Token);

        // Una cancelacion real del llamante no se convierte en un
        // "Rechazado": se propaga para que la peticion se cancele.
        await accion.Should().ThrowAsync<OperationCanceledException>();
    }
}
