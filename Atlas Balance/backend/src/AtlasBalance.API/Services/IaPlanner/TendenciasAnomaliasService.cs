using System.Globalization;
using System.Text;
using AtlasBalance.API.Services.IaPlanner;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 7): servicio que arma la respuesta textual de
// tendencia y anomalias. Las herramientas (GetExpenseTrendAsync y
// DetectAnomaliesAsync) devuelven datos estructurados; este servicio
// los une y los formatea para que el proveedor solo tenga que
// "explicar" lo que ya esta calculado, no inventar numeros.
//
// Politica de umbral:
//   - Tendencia: ultimos 6 meses naturales completos. Agrupa
//     mensual por divisa. Compara los ultimos 3 meses contra los
//     3 anteriores y devuelve porcentaje + valores para que el
//     proveedor pueda etiquetar alta / baja / estable.
//   - Anomalias: la deteccion la hace la herramienta (Fase 3). Aqui
//     se formatea con el lenguaje "posible anomalia", "sugiere
//     revisar", "podria indicar". Nunca "es fraude" o "es un error":
//     el sistema no afirma, sugiere. Esto blinda contra falsos
//     positivos que el modelo podria amplificar.

public enum TrendVerdict
{
    Estable,
    Sube,
    Baja
}

public sealed record TrendVeredicto(
    string Divisa,
    TrendVerdict Veredicto,
    decimal VariacionPorcentaje,
    decimal IngresosUltimoTrimestre,
    decimal GastosUltimoTrimestre,
    decimal IngresosTrimestreAnterior,
    decimal GastosTrimestreAnterior,
    int MesesConDatos);

public sealed record TendenciasAnomaliasResultado
{
    public IReadOnlyList<TrendVeredicto> Tendencias { get; init; } = Array.Empty<TrendVeredicto>();
    public IReadOnlyList<string> Anomalias { get; init; } = Array.Empty<string>();
    public string Resumen { get; init; } = string.Empty;
    public string? Advertencia { get; init; }
}

public sealed class TendenciasAnomaliasService
{
    // Umbral para considerar que la variacion es "alta". Constante
    // documentada aqui para que los tests la pinzen. Por debajo de
    // este porcentaje, el sistema marca "estable".
    public const decimal VariacionSignificativaPorcentaje = 15m;

    private readonly IFinancialToolsService _tools;

    public TendenciasAnomaliasService(IFinancialToolsService tools)
    {
        _tools = tools;
    }

    public async Task<TendenciasAnomaliasResultado> AnalizarAsync(
        UserAccessScope scope,
        DateOnly anchor,
        Guid? paisId,
        CancellationToken cancellationToken)
    {
        var sixMonthStart = PrimerDiaDelMes(anchor.AddMonths(-5));
        var planTendencia = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Trend,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                PaisIds = paisId.HasValue ? new[] { paisId.Value } : null,
                Periodo = new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = sixMonthStart,
                    To = anchor,
                    Anchor = anchor
                }
            },
            Limite = 60
        };

        var trend = await _tools.GetExpenseTrendAsync(scope, planTendencia, cancellationToken);

        var tresMesesFin = anchor;
        var tresMesesInicio = PrimerDiaDelMes(anchor.AddMonths(-2));
        var seisMesesInicioAnterior = PrimerDiaDelMes(anchor.AddMonths(-5));
        var tresMesesFinAnterior = PrimerDiaDelMes(anchor.AddMonths(-2)).AddDays(-1);

        var veredictos = CalcularVeredictos(trend.Data, tresMesesInicio, tresMesesFin, seisMesesInicioAnterior, tresMesesFinAnterior);

        var planAnomalias = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                PaisIds = paisId.HasValue ? new[] { paisId.Value } : null
            }
        };

        var anomalias = await _tools.DetectAnomaliesAsync(scope, planAnomalias, cancellationToken);
        var lineas = FormatearAnomalias(anomalias.Data);

        return new TendenciasAnomaliasResultado
        {
            Tendencias = veredictos,
            Anomalias = lineas,
            Resumen = BuildResumen(veredictos, lineas),
            Advertencia = anomalias.Advertencia
        };
    }

    public static IReadOnlyList<TrendVeredicto> CalcularVeredictos(
        IReadOnlyList<TrendPoint> puntos,
        DateOnly desdeReciente,
        DateOnly hastaReciente,
        DateOnly desdeAnterior,
        DateOnly hastaAnterior)
    {
        if (puntos.Count == 0) return Array.Empty<TrendVeredicto>();

        var porDivisa = puntos
            .GroupBy(p => p.Divisa)
            .Select(g =>
            {
                var reciente = g.Where(p => p.FechaEnRango(desdeReciente, hastaReciente)).ToList();
                var anterior = g.Where(p => p.FechaEnRango(desdeAnterior, hastaAnterior)).ToList();

                var gastosReciente = reciente.Sum(x => x.Gastos);
                var gastosAnterior = anterior.Sum(x => x.Gastos);
                var ingresosReciente = reciente.Sum(x => x.Ingresos);
                var ingresosAnterior = anterior.Sum(x => x.Ingresos);

                // La variacion se calcula sobre los gastos porque
                // es el indicador mas estable para una pyme (los
                // ingresos pueden tener un unico cobro alto).
                decimal variacionPct = 0m;
                if (gastosAnterior > 0m)
                {
                    variacionPct = Math.Round(((gastosReciente - gastosAnterior) / gastosAnterior) * 100m, 2, MidpointRounding.AwayFromZero);
                }
                else if (gastosReciente > 0m)
                {
                    variacionPct = 100m;
                }

                var verdict = Math.Abs(variacionPct) switch
                {
                    var v when v < VariacionSignificativaPorcentaje => TrendVerdict.Estable,
                    var v when v >= VariacionSignificativaPorcentaje && variacionPct > 0 => TrendVerdict.Sube,
                    _ => TrendVerdict.Baja
                };

                return new TrendVeredicto(
                    g.Key,
                    verdict,
                    variacionPct,
                    ingresosReciente,
                    gastosReciente,
                    ingresosAnterior,
                    gastosAnterior,
                    reciente.Count + anterior.Count);
            })
            .ToList();

        return porDivisa;
    }

    public static IReadOnlyList<string> FormatearAnomalias(IReadOnlyList<Anomaly> anomalias)
    {
        if (anomalias.Count == 0) return Array.Empty<string>();
        var sb = new StringBuilder();
        var porTipo = anomalias.GroupBy(a => a.Tipo).ToList();
        foreach (var grupo in porTipo)
        {
            var primero = grupo.First();
            sb.Append("Posible anomalia (");
            sb.Append(grupo.Key switch
            {
                "DUPLICADO_PROBABLE" => "duplicado probable",
                "IMPORTE_ATIPICO" => "importe atipico",
                _ => grupo.Key.ToLowerInvariant()
            });
            sb.Append("): ");
            sb.Append(grupo.Count());
            sb.Append(" caso(s). ");
            sb.AppendLine(primero.Descripcion);
        }
        var lineas = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lineas;
    }

    private static string BuildResumen(IReadOnlyList<TrendVeredicto> veredictos, IReadOnlyList<string> anomalias)
    {
        var sb = new StringBuilder();
        if (veredictos.Count == 0 && anomalias.Count == 0)
        {
            return "Sin datos suficientes para calcular tendencia o anomalias en el periodo.";
        }
        foreach (var v in veredictos)
        {
            var texto = v.Veredicto switch
            {
                TrendVerdict.Estable => "estable",
                TrendVerdict.Sube => $"sube un {Math.Abs(v.VariacionPorcentaje):0.##}%",
                TrendVerdict.Baja => $"baja un {Math.Abs(v.VariacionPorcentaje):0.##}%",
                _ => v.Veredicto.ToString()
            };
            sb.Append("Tendencia en ").Append(v.Divisa).Append(": ").Append(texto).AppendLine();
        }
        if (anomalias.Count > 0)
        {
            sb.Append("Anomalias detectadas (sugiere revisar, no afirma):").AppendLine();
            foreach (var a in anomalias) sb.Append("- ").AppendLine(a);
        }
        return sb.ToString().Trim();
    }

    private static DateOnly PrimerDiaDelMes(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);
}

internal static class TrendPointExtensions
{
    public static bool FechaEnRango(this TrendPoint p, DateOnly desde, DateOnly hasta)
    {
        var fecha = new DateOnly(p.Year, p.Month, 1);
        return fecha >= desde && fecha <= hasta;
    }
}
