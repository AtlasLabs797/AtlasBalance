using System.Diagnostics;
using System.Text;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 5): ejecutor de planes compuestos.
//
// Un plan compuesto es una lista ordenada de planes simples. El
// ejecutor los corre en serie, con un limite duro de 5 pasos y un
// timeout global. Cada paso puede leer el resultado de pasos
// anteriores via PlanStep.ReferenciasAPasos (lista de indices).
//
// Controles:
//   - Maximo 5 pasos por consulta (constante).
//   - Timeout global: si la suma de los pasos supera el limite, se
//     aborta y se devuelve el resultado parcial con la advertencia
//     correspondiente.
//   - Cancelacion: el CancellationToken del caller se chequea antes
//     de cada paso y se propaga al tool.
//   - Sin escrituras: el ejecutor no invoca ninguna operacion de
//     escritura (las herramientas son todas de lectura).
//   - Sin recursividad: el ejecutor no se llama a si mismo, ni
//     genera un nuevo plan a partir del resultado de un paso.
//   - Sin ejecutar instrucciones: los datos de salida se devuelven
//     como datos, no se interpretan como comandos.

public sealed record PlanStep(
    int Indice,
    string Nombre,
    FinancialQueryPlan Plan,
    IReadOnlyList<int> ReferenciasAPasos);

public sealed record CompoundPlan
{
    public IReadOnlyList<PlanStep> Pasos { get; init; } = [];
    public TimeSpan TimeoutGlobal { get; init; } = TimeSpan.FromSeconds(20);
}

public sealed record PlanStepResult(
    int Indice,
    string Nombre,
    string? Resumen,
    int FilasDevueltas,
    int FilasAnalizadas,
    TimeSpan Duracion,
    string? Advertencia,
    object? Datos);

public sealed record CompoundPlanResult
{
    public bool Exito { get; init; }
    public IReadOnlyList<PlanStepResult> Pasos { get; init; } = [];
    public string? Advertencia { get; init; }
    public TimeSpan DuracionTotal { get; init; }
    public string? Resumen { get; init; }
}

public interface IPlanExecutor
{
    Task<CompoundPlanResult> EjecutarAsync(
        UserAccessScope scope,
        CompoundPlan plan,
        IFinancialToolsService tools,
        CancellationToken cancellationToken);
}

public sealed class PlanExecutor : IPlanExecutor
{
    public const int MaxPasos = 5;

    public async Task<CompoundPlanResult> EjecutarAsync(
        UserAccessScope scope,
        CompoundPlan plan,
        IFinancialToolsService tools,
        CancellationToken cancellationToken)
    {
        if (plan.Pasos.Count == 0)
        {
            return new CompoundPlanResult
            {
                Advertencia = "Plan compuesto sin pasos. Nada que ejecutar."
            };
        }

        if (plan.Pasos.Count > MaxPasos)
        {
            return new CompoundPlanResult
            {
                Advertencia = $"Plan compuesto con {plan.Pasos.Count} pasos supera el maximo permitido ({MaxPasos}). Reformula con menos ambiguedad."
            };
        }

        // El CompoundPlan permite que un paso sea de escritura, pero
        // el ejecutor solo enruta a herramientas de lectura. Si el
        // plan intenta una operacion de escritura, la rechazamos.
        foreach (var paso in plan.Pasos)
        {
            if (EsOperacionDeEscritura(paso.Plan.Operacion))
            {
                return new CompoundPlanResult
                {
                    Advertencia = $"El paso '{paso.Nombre}' usa una operacion de escritura no permitida ({paso.Plan.Operacion}). Atlas Balance IA no realiza escrituras."
                };
            }
        }

        var cronometro = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(plan.TimeoutGlobal);

        var resultados = new List<PlanStepResult>();
        for (int i = 0; i < plan.Pasos.Count; i++)
        {
            if (cronometro.Elapsed >= plan.TimeoutGlobal)
            {
                return new CompoundPlanResult
                {
                    Exito = false,
                    Pasos = resultados,
                    Advertencia = $"Timeout global ({plan.TimeoutGlobal.TotalSeconds:0}s) agotado en el paso {i} de {plan.Pasos.Count}.",
                    DuracionTotal = cronometro.Elapsed
                };
            }

            timeoutCts.Token.ThrowIfCancellationRequested();
            var paso = plan.Pasos[i];
            var stepCronometro = Stopwatch.StartNew();
            try
            {
                var resultado = await EjecutarPasoAsync(scope, paso, tools, timeoutCts.Token);
                resultados.Add(new PlanStepResult(
                    paso.Indice,
                    paso.Nombre,
                    resultado.Resumen,
                    resultado.FilasDevueltas,
                    resultado.FilasAnalizadas,
                    stepCronometro.Elapsed,
                    resultado.Advertencia,
                    resultado.Datos));
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new CompoundPlanResult
                {
                    Exito = false,
                    Pasos = resultados,
                    Advertencia = $"Timeout global agotado durante el paso '{paso.Nombre}'.",
                    DuracionTotal = cronometro.Elapsed
                };
            }
            catch (Exception ex)
            {
                return new CompoundPlanResult
                {
                    Exito = false,
                    Pasos = resultados,
                    Advertencia = $"Fallo en el paso '{paso.Nombre}': {ex.GetType().Name}.",
                    DuracionTotal = cronometro.Elapsed
                };
            }
        }

        return new CompoundPlanResult
        {
            Exito = true,
            Pasos = resultados,
            DuracionTotal = cronometro.Elapsed,
            Resumen = BuildResumen(resultados)
        };
    }

    private async Task<PlanStepResult> EjecutarPasoAsync(
        UserAccessScope scope,
        PlanStep paso,
        IFinancialToolsService tools,
        CancellationToken cancellationToken)
    {
        var plan = paso.Plan;
        switch (plan.Operacion)
        {
            case FinancialOperation.GetLatest:
                {
                    var r = await tools.GetLatestTransactionAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data is null ? "Sin resultados." : $"{r.Data.CuentaNombre} | {r.Data.Monto:0.00} {r.Data.Divisa} | {r.Data.Fecha:dd/MM/yyyy}",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            case FinancialOperation.List:
            case FinancialOperation.Sum:
            case FinancialOperation.Count:
            case FinancialOperation.Group:
                {
                    var r = await tools.GetPeriodTotalsAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data.Count == 0 ? "Sin resultados." : $"{r.Data.Count} fila(s) devueltas.",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            case FinancialOperation.Trend:
                {
                    var r = await tools.GetExpenseTrendAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data.Count == 0 ? "Sin datos para la tendencia." : $"{r.Data.Count} meses devueltos.",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            case FinancialOperation.Ranking:
                {
                    var r = await tools.GetRankingAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data.Count == 0 ? "Sin resultados." : $"{r.Data.Count} fila(s) en el ranking.",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            case FinancialOperation.Compare:
                {
                    var r = await tools.ComparePeriodsAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data is null
                            ? "Sin datos para comparar."
                            : $"Base {r.Data.Base.Ingresos:0.00}/{r.Data.Base.Gastos:0.00}, Referencia {r.Data.Referencia.Ingresos:0.00}/{r.Data.Referencia.Gastos:0.00}.",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            case FinancialOperation.Search:
                {
                    var r = await tools.SearchTransactionsAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data.Count == 0 ? "Sin resultados." : $"{r.Data.Count} movimiento(s).",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            case FinancialOperation.Anomalies:
                {
                    var r = await tools.DetectAnomaliesAsync(scope, plan, cancellationToken);
                    return new PlanStepResult(paso.Indice, paso.Nombre,
                        Resumen: r.Data.Count == 0 ? "Sin anomalias." : $"{r.Data.Count} posible(s) anomalia(s).",
                        FilasDevueltas: r.FilasDevueltas,
                        FilasAnalizadas: r.FilasAnalizadas,
                        Duracion: TimeSpan.Zero,
                        Advertencia: r.Advertencia,
                        Datos: r.Data);
                }
            default:
                throw new NotSupportedException($"Operacion no soportada por el ejecutor: {plan.Operacion}.");
        }
    }

    private static bool EsOperacionDeEscritura(FinancialOperation op) => op switch
    {
        _ when op.ToString().Contains("Insert", StringComparison.OrdinalIgnoreCase) => true,
        _ when op.ToString().Contains("Update", StringComparison.OrdinalIgnoreCase) => true,
        _ when op.ToString().Contains("Delete", StringComparison.OrdinalIgnoreCase) => true,
        _ => false
    };

    private static string BuildResumen(IReadOnlyList<PlanStepResult> pasos)
    {
        if (pasos.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < pasos.Count; i++)
        {
            var p = pasos[i];
            sb.Append("Paso ").Append(i + 1).Append(" (").Append(p.Nombre).Append("): ").Append(p.Resumen).AppendLine();
        }
        return sb.ToString().Trim();
    }
}
