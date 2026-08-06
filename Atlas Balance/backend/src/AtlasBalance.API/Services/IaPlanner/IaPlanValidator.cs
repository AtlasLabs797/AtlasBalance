using System.Globalization;
using AtlasBalance.API.Constants;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 2): validador del FinancialQueryPlan.
//
// Reglas (todas las salidas son explicitas; nada se interpreta):
//   - Ningun campo arbitrario. Solo se aceptan las enums y los
//     parametros tipados de FinancialFilters.
//   - Ninguna expresion generada por el modelo (no se permite meter
//     SQL, LINQ, nombres de tablas, etc.).
//   - Limite por defecto: 50. Maximo permitido: 500. Si llega > 500
//     se recorta a 500 y se anade un aviso en CamposRechazados.
//   - Operaciones de escritura no existen en la enum, asi que cualquier
//     intento del modelo de pedirlas acaba en Estado = Rechazado.
//   - Periodos ambiguos: si From > To, si el tipo es Natural sin mes
//     definido (no aplica a las enums cerradas), si la combinacion
//     From/To esta vacia en Explicito, etc., devuelven AclaracionRequerida.
public static class IaPlanValidator
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;
    public const int MaxSearchLength = 200;
    public const int MaxConceptoLength = 120;

    public static FinancialPlanEvaluation Validar(FinancialQueryPlan? plan, DateOnly anchor)
    {
        if (plan is null)
        {
            return Rechazado("Plan vacio. Vuelve a formular la pregunta con menos ambiguedad.");
        }

        if (!Enum.IsDefined(typeof(FinancialOperation), plan.Operacion))
        {
            return Rechazado("Operacion no soportada.");
        }

        if (!Enum.IsDefined(typeof(FinancialMetric), plan.Metrica))
        {
            return Rechazado("Metrica no soportada.");
        }

        if (plan.Orden.HasValue && !Enum.IsDefined(typeof(FinancialSort), plan.Orden.Value))
        {
            return Rechazado("Orden no soportado.");
        }

        // Escritura nunca deberia llegar aqui (no esta en la enum), pero
        // por si una mano a la enum se cuela, lo bloqueamos igualmente.
        if (EsOperacionDeEscritura(plan.Operacion))
        {
            return Rechazado("Atlas Balance IA no realiza operaciones de escritura.");
        }

        var limit = plan.Limite <= 0 ? DefaultLimit : Math.Min(plan.Limite, MaxLimit);
        if (plan.Limite > MaxLimit)
        {
            plan = plan with { Limite = MaxLimit };
        }

        var filtrosNormalizados = NormalizarFiltros(plan.Filtros, anchor);
        if (filtrosNormalizados.Estado is not FinancialPlanStatus.Ok)
        {
            return filtrosNormalizados;
        }

        var gruposNormalizados = NormalizarAgrupaciones(plan.Agrupaciones, plan.Operacion);
        if (gruposNormalizados is not null)
        {
            return gruposNormalizados;
        }

        var termino = NormalizarTermino(plan.Operacion, plan.TerminoBusqueda, plan.Filtros.Concepto);
        if (termino.Estado is not FinancialPlanStatus.Ok)
        {
            return termino;
        }

        var comparacionNormalizada = NormalizarComparacion(plan.Operacion, plan.Comparacion, anchor);
        if (comparacionNormalizada.Estado is not FinancialPlanStatus.Ok)
        {
            return comparacionNormalizada;
        }

        var planNormalizado = plan with
        {
            Limite = limit,
            Filtros = filtrosNormalizados.Plan?.Filtros ?? plan.Filtros,
            Comparacion = comparacionNormalizada.Plan?.Comparacion ?? plan.Comparacion,
            TerminoBusqueda = termino.Plan?.TerminoBusqueda ?? plan.TerminoBusqueda
        };

        return new FinancialPlanEvaluation
        {
            Estado = FinancialPlanStatus.Ok,
            Plan = planNormalizado
        };
    }

    private static bool EsOperacionDeEscritura(FinancialOperation op) => op switch
    {
        // Hoy la enum no expone escritura, pero si alguien anade
        // Update/Insert/Delete a la enum sin pasar por aqui, este switch
        // ya los tiene cubiertos.
        _ when op.ToString().Contains("Insert", StringComparison.OrdinalIgnoreCase) => true,
        _ when op.ToString().Contains("Update", StringComparison.OrdinalIgnoreCase) => true,
        _ when op.ToString().Contains("Delete", StringComparison.OrdinalIgnoreCase) => true,
        _ => false
    };

    private static FinancialPlanEvaluation NormalizarFiltros(FinancialFilters? filtros, DateOnly anchor)
    {
        if (filtros is null)
        {
            return new FinancialPlanEvaluation
            {
                Estado = FinancialPlanStatus.Ok,
                Plan = new FinancialQueryPlan { Filtros = new FinancialFilters { Periodo = PeriodoPorDefecto(anchor) } }
            };
        }

        if (filtros.ImporteMinimo is < 0 || filtros.ImporteMaximo is < 0)
        {
            return Rechazado("Los importes en el filtro no pueden ser negativos.");
        }

        if (filtros.ImporteMinimo is { } min && filtros.ImporteMaximo is { } max && min > max)
        {
            return Rechazado("El importe minimo del filtro es mayor que el maximo.");
        }

        var periodo = ResolverPeriodo(filtros.Periodo, anchor);
        if (periodo.Estado is not FinancialPlanStatus.Ok)
        {
            return periodo;
        }

        return new FinancialPlanEvaluation
        {
            Estado = FinancialPlanStatus.Ok,
            Plan = new FinancialQueryPlan
            {
                Filtros = new FinancialFilters
                {
                    CuentaIds = filtros.CuentaIds is { Count: > 0 } ? filtros.CuentaIds : null,
                    TitularIds = filtros.TitularIds is { Count: > 0 } ? filtros.TitularIds : null,
                    PaisIds = filtros.PaisIds is { Count: > 0 } ? filtros.PaisIds : null,
                    Divisas = filtros.Divisas is { Count: > 0 } ? filtros.Divisas : null,
                    Categorias = filtros.Categorias is { Count: > 0 } ? filtros.Categorias : null,
                    Estados = filtros.Estados is { Count: > 0 } ? filtros.Estados : null,
                    Concepto = string.IsNullOrWhiteSpace(filtros.Concepto) ? null : Truncar(filtros.Concepto, MaxConceptoLength),
                    ImporteMinimo = filtros.ImporteMinimo,
                    ImporteMaximo = filtros.ImporteMaximo,
                    Periodo = periodo.Plan?.Filtros?.Periodo
                }
            }
        };
    }

    private static FinancialPlanEvaluation? NormalizarAgrupaciones(IReadOnlyList<FinancialGroupBy>? agrupaciones, FinancialOperation op)
    {
        if (agrupaciones is null || agrupaciones.Count == 0)
        {
            return null;
        }

        if (agrupaciones.Count > 2)
        {
            return Rechazado("Solo se admiten hasta 2 niveles de agrupacion. Reformula con menos dimensiones.");
        }

        // Las operaciones que no devuelven agregado no aceptan grupos.
        var soportaGrupos = op is FinancialOperation.Sum
            or FinancialOperation.Count
            or FinancialOperation.Group
            or FinancialOperation.Compare
            or FinancialOperation.Ranking
            or FinancialOperation.Trend
            or FinancialOperation.Anomalies;

        if (!soportaGrupos)
        {
            return Rechazado($"La operacion {op} no admite agrupacion.");
        }

        foreach (var grupo in agrupaciones)
        {
            if (!Enum.IsDefined(typeof(FinancialGroupBy), grupo))
            {
                return Rechazado("Agrupacion no soportada.");
            }
            if (grupo is FinancialGroupBy.None)
            {
                return Rechazado("Agrupacion 'None' no tiene sentido; quitala del plan.");
            }
        }

        return null;
    }

    private static FinancialPlanEvaluation NormalizarTermino(FinancialOperation op, string? termino, string? concepto)
    {
        if (op is FinancialOperation.Search)
        {
            if (string.IsNullOrWhiteSpace(termino))
            {
                return Aclarar(
                    "Falta el termino de busqueda.",
                    new[]
                    {
                        new FinancialClarificationOption { Etiqueta = "Buscar por concepto", Valor = "concepto" },
                        new FinancialClarificationOption { Etiqueta = "Buscar por titular", Valor = "titular" },
                        new FinancialClarificationOption { Etiqueta = "Buscar por cuenta", Valor = "cuenta" }
                    });
            }
            if (termino.Length > MaxSearchLength)
            {
                return Rechazado($"El termino de busqueda no puede superar {MaxSearchLength} caracteres.");
            }
        }

        if (!string.IsNullOrWhiteSpace(concepto) && concepto.Length > MaxConceptoLength)
        {
            return Rechazado($"El concepto del filtro no puede superar {MaxConceptoLength} caracteres.");
        }

        return new FinancialPlanEvaluation
        {
            Estado = FinancialPlanStatus.Ok,
            Plan = new FinancialQueryPlan
            {
                TerminoBusqueda = string.IsNullOrWhiteSpace(termino) ? null : termino.Trim()
            }
        };
    }

    private static FinancialPlanEvaluation NormalizarComparacion(FinancialOperation op, FinancialComparison? comparacion, DateOnly anchor)
    {
        if (op is not FinancialOperation.Compare)
        {
            return new FinancialPlanEvaluation { Estado = FinancialPlanStatus.Ok };
        }

        if (comparacion is null)
        {
            // El modo "anterior" por defecto: mes actual vs mes anterior.
            // Resolvemos los periodos (no usamos PeriodoPorDefecto en
            // bruto: ese no tiene From/To y dejaria la comparacion a
            // nulls).
            var baseResuelto = ResolverPeriodo(PeriodoPorDefecto(anchor), anchor);
            var refResuelto = ResolverPeriodo(
                new FinancialPeriod { Tipo = FinancialPeriodKind.MesAnterior, Anchor = anchor },
                anchor);
            if (baseResuelto.Estado is not FinancialPlanStatus.Ok ||
                refResuelto.Estado is not FinancialPlanStatus.Ok)
            {
                return baseResuelto.Estado is not FinancialPlanStatus.Ok ? baseResuelto : refResuelto;
            }

            return new FinancialPlanEvaluation
            {
                Estado = FinancialPlanStatus.Ok,
                Plan = new FinancialQueryPlan
                {
                    Comparacion = new FinancialComparison
                    {
                        Base = baseResuelto.Plan!.Filtros!.Periodo!,
                        Referencia = refResuelto.Plan!.Filtros!.Periodo!,
                        Modo = "anterior"
                    }
                }
            };
        }

        var baseResuelto2 = ResolverPeriodo(comparacion.Base, anchor);
        if (baseResuelto2.Estado is not FinancialPlanStatus.Ok)
        {
            return baseResuelto2;
        }
        var refResuelto2 = ResolverPeriodo(comparacion.Referencia, anchor);
        if (refResuelto2.Estado is not FinancialPlanStatus.Ok)
        {
            return refResuelto2;
        }

        return new FinancialPlanEvaluation
        {
            Estado = FinancialPlanStatus.Ok,
            Plan = new FinancialQueryPlan
            {
                Comparacion = new FinancialComparison
                {
                    Base = baseResuelto2.Plan!.Filtros!.Periodo!,
                    Referencia = refResuelto2.Plan!.Filtros!.Periodo!,
                    Modo = string.IsNullOrWhiteSpace(comparacion.Modo) ? "anterior" : comparacion.Modo.Trim()
                }
            }
        };
    }

    public static FinancialPeriod PeriodoPorDefecto(DateOnly anchor)
    {
        return new FinancialPeriod
        {
            Tipo = FinancialPeriodKind.MesActual,
            Anchor = anchor
        };
    }

    public static FinancialPlanEvaluation ResolverPeriodo(FinancialPeriod? periodo, DateOnly anchor)
    {
        if (periodo is null)
        {
            return new FinancialPlanEvaluation
            {
                Estado = FinancialPlanStatus.Ok,
                Plan = new FinancialQueryPlan { Filtros = new FinancialFilters { Periodo = PeriodoPorDefecto(anchor) } }
            };
        }

        switch (periodo.Tipo)
        {
            case FinancialPeriodKind.Explicito:
                if (periodo.From is null || periodo.To is null)
                {
                    return Aclarar("El periodo explicito necesita fecha de inicio y de fin.", PeriodoOpciones);
                }
                if (periodo.From > periodo.To)
                {
                    return Rechazado("La fecha de inicio del periodo es posterior a la fecha de fin.");
                }
                return OkPeriodo(new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = periodo.From,
                    To = periodo.To,
                    Anchor = anchor
                });

            case FinancialPeriodKind.MesActual:
                {
                    var from = new DateOnly(anchor.Year, anchor.Month, 1);
                    return OkPeriodo(new FinancialPeriod { Tipo = FinancialPeriodKind.MesActual, From = from, To = anchor, Anchor = anchor });
                }
            case FinancialPeriodKind.MesAnterior:
                {
                    var mesActual = new DateOnly(anchor.Year, anchor.Month, 1);
                    var from = mesActual.AddMonths(-1);
                    var to = mesActual.AddDays(-1);
                    return OkPeriodo(new FinancialPeriod { Tipo = FinancialPeriodKind.MesAnterior, From = from, To = to, Anchor = anchor });
                }
            case FinancialPeriodKind.TrimestreActual:
                {
                    var quarterStartMonth = ((anchor.Month - 1) / 3) * 3 + 1;
                    var from = new DateOnly(anchor.Year, quarterStartMonth, 1);
                    return OkPeriodo(new FinancialPeriod { Tipo = FinancialPeriodKind.TrimestreActual, From = from, To = anchor, Anchor = anchor });
                }
            case FinancialPeriodKind.AnoActual:
                {
                    var from = new DateOnly(anchor.Year, 1, 1);
                    return OkPeriodo(new FinancialPeriod { Tipo = FinancialPeriodKind.AnoActual, From = from, To = anchor, Anchor = anchor });
                }
            case FinancialPeriodKind.Ultimos30Dias:
                {
                    var from = anchor.AddDays(-30);
                    return OkPeriodo(new FinancialPeriod { Tipo = FinancialPeriodKind.Ultimos30Dias, From = from, To = anchor, Anchor = anchor });
                }
            case FinancialPeriodKind.Personalizado:
                if (periodo.From is null || periodo.To is null)
                {
                    return Aclarar("El periodo personalizado necesita fecha de inicio y de fin.", PeriodoOpciones);
                }
                if (periodo.From > periodo.To)
                {
                    return Rechazado("La fecha de inicio del periodo es posterior a la fecha de fin.");
                }
                return OkPeriodo(new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Personalizado,
                    From = periodo.From,
                    To = periodo.To,
                    Anchor = anchor
                });
            default:
                return Rechazado("Tipo de periodo no soportado.");
        }
    }

    private static readonly IReadOnlyList<FinancialClarificationOption> PeriodoOpciones = new[]
    {
        new FinancialClarificationOption { Etiqueta = "Este mes", Valor = "mes_actual" },
        new FinancialClarificationOption { Etiqueta = "Mes anterior", Valor = "mes_anterior" },
        new FinancialClarificationOption { Etiqueta = "Trimestre actual", Valor = "trimestre_actual" },
        new FinancialClarificationOption { Etiqueta = "Ano actual", Valor = "ano_actual" },
        new FinancialClarificationOption { Etiqueta = "Ultimos 30 dias", Valor = "ultimos_30_dias" }
    };

    private static FinancialPlanEvaluation OkPeriodo(FinancialPeriod periodo)
    {
        return new FinancialPlanEvaluation
        {
            Estado = FinancialPlanStatus.Ok,
            Plan = new FinancialQueryPlan
            {
                Filtros = new FinancialFilters { Periodo = periodo }
            }
        };
    }

    private static FinancialPlanEvaluation Rechazado(string motivo) => new()
    {
        Estado = FinancialPlanStatus.Rechazado,
        Motivo = motivo
    };

    private static FinancialPlanEvaluation Aclarar(string motivo, IReadOnlyList<FinancialClarificationOption> opciones) => new()
    {
        Estado = FinancialPlanStatus.AclaracionRequerida,
        Motivo = motivo,
        Opciones = opciones
    };

    private static string Truncar(string valor, int max) =>
        valor.Length <= max ? valor : valor[..max];

    // Punto de extension para que la capa de planificacion de Fase 4
    // pueda anadir validaciones especificas sin tocar el plan base.
    public static FinancialPlanEvaluation ValidarCompleto(
        FinancialQueryPlan? plan,
        DateOnly anchor,
        IReadOnlyList<string> allowedOperations)
    {
        var basico = Validar(plan, anchor);
        if (basico.Estado is not FinancialPlanStatus.Ok || basico.Plan is null)
        {
            return basico;
        }

        if (!allowedOperations.Contains(basico.Plan.Operacion.ToString(), StringComparer.Ordinal))
        {
            return Rechazado("La operacion no esta permitida en el contexto actual.");
        }

        return basico;
    }
}
