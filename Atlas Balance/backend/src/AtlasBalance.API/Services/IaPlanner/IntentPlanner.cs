using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasBalance.API.Logging;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 4): planificador de intenciones con 3 niveles.
//
//   Nivel 1 (local): patrones deterministicos para las preguntas
//     mas frecuentes. Si matchea, devolvemos un FinancialQueryPlan
//     listo para ejecutar. NO consume cuota del proveedor.
//
//   Nivel 2 (semantico): el modelo recibe la pregunta + esquema del
//     plan y devuelve un JSON que el validador acepta o rechaza.
//     La capa de produccion (Fase 4.2) envia la peticion al
//     proveedor; los tests pueden inyectar un stub deterministico.
//
//   Nivel 3 (aclaracion): si el plan tiene multiples interpretaciones
//     razonables, devolvemos opciones estructuradas para que el
//     frontend las presente como botones.
//
// El resultado es siempre un objeto inmutable. El plan final siempre
// pasa por IaPlanValidator antes de llegar a las herramientas.

public enum PlanResolutionSource
{
    Local,
    Semantic,
    Clarification,
    Rejected
}

public sealed record PlanResolution
{
    public FinancialPlanEvaluation Evaluacion { get; init; } = new();
    public PlanResolutionSource Origen { get; init; }
    public string? TextoOriginal { get; init; }
    public string? TextoNormalizado { get; init; }
    public string? PatronLocal { get; init; }
    public string? ModelRaw { get; init; }
}

public interface IIntentPlanner
{
    Task<PlanResolution> ResolverAsync(string pregunta, DateOnly anchor, CancellationToken cancellationToken);
}

// Stub del nivel 2: en produccion sera una clase que llame al
// proveedor; en tests inyectamos un stub que devuelve un plan
// concreto. La interfaz permite implementar el nivel 2 sin tocar
// el flujo del nivel 1/3.
public interface ISemanticPlannerClient
{
    Task<string?> PlanToJsonAsync(string pregunta, IReadOnlyList<string> allowedOperations, CancellationToken cancellationToken);
}

public sealed class NullSemanticPlannerClient : ISemanticPlannerClient
{
    public Task<string?> PlanToJsonAsync(string pregunta, IReadOnlyList<string> allowedOperations, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}

public sealed class IntentPlanner : IIntentPlanner
{
    // Operaciones que el planificador local sabe resolver. Si la
    // pregunta no encaja en ninguna, se intenta el nivel 2.
    public static readonly IReadOnlyList<string> OperacionesPermitidas = new[]
    {
        nameof(FinancialOperation.GetLatest),
        nameof(FinancialOperation.List),
        nameof(FinancialOperation.Sum),
        nameof(FinancialOperation.Count),
        nameof(FinancialOperation.Compare),
        nameof(FinancialOperation.Trend),
        nameof(FinancialOperation.Search),
        nameof(FinancialOperation.Anomalies),
        nameof(FinancialOperation.Ranking)
    };

    private readonly ISemanticPlannerClient _semantic;
    private readonly ILogger _logger;

    public IntentPlanner(ISemanticPlannerClient semantic, ILogger<IntentPlanner> logger)
    {
        _semantic = semantic;
        _logger = logger;
    }

    public async Task<PlanResolution> ResolverAsync(string pregunta, DateOnly anchor, CancellationToken cancellationToken)
    {
        var texto = pregunta?.Trim() ?? string.Empty;
        if (texto.Length == 0)
        {
            return new PlanResolution
            {
                Evaluacion = new FinancialPlanEvaluation
                {
                    Estado = FinancialPlanStatus.Rechazado,
                    Motivo = "Pregunta vacia."
                },
                Origen = PlanResolutionSource.Rejected,
                TextoOriginal = pregunta
            };
        }

        var normalizado = Normalizar(texto);

        // Nivel 1: local.
        var local = TryLocalMatch(normalizado, anchor);
        if (local is not null)
        {
            var validado = IaPlanValidator.ValidarCompleto(local.Plan, anchor, OperacionesPermitidas);
            if (validado.Estado is FinancialPlanStatus.Ok)
            {
                return new PlanResolution
                {
                    Evaluacion = validado,
                    Origen = PlanResolutionSource.Local,
                    TextoOriginal = pregunta,
                    TextoNormalizado = normalizado,
                    PatronLocal = local.Patron
                };
            }
            // El match local dio un plan que el validador rechaza:
            // caemos a aclaracion o rechazo.
            return new PlanResolution
            {
                Evaluacion = validado,
                Origen = validado.Estado is FinancialPlanStatus.AclaracionRequerida
                    ? PlanResolutionSource.Clarification
                    : PlanResolutionSource.Rejected,
                TextoOriginal = pregunta,
                TextoNormalizado = normalizado,
                PatronLocal = local.Patron
            };
        }

        // Nivel 3: antes de gastar cuota del proveedor, vemos si la
        // pregunta es ambigua y podemos pedir aclaracion local.
        var aclaracionLocal = TryLocalClarification(normalizado);
        if (aclaracionLocal is not null)
        {
            return new PlanResolution
            {
                Evaluacion = aclaracionLocal,
                Origen = PlanResolutionSource.Clarification,
                TextoOriginal = pregunta,
                TextoNormalizado = normalizado
            };
        }

        // Nivel 2: semantico.
        try
        {
            var raw = await _semantic.PlanToJsonAsync(texto, OperacionesPermitidas, cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new PlanResolution
                {
                    Evaluacion = new FinancialPlanEvaluation
                    {
                        Estado = FinancialPlanStatus.Rechazado,
                        Motivo = "El proveedor no devolvio un plan valido."
                    },
                    Origen = PlanResolutionSource.Rejected,
                    TextoOriginal = pregunta,
                    TextoNormalizado = normalizado
                };
            }

            var plan = ParsePlanFromJson(raw);
            if (plan is null)
            {
                _logger.LogWarning("Planificador semantico devolvio JSON no parseable: {Raw}", LogScrubber.Scrub(raw));
                return new PlanResolution
                {
                    Evaluacion = new FinancialPlanEvaluation
                    {
                        Estado = FinancialPlanStatus.Rechazado,
                        Motivo = "El proveedor devolvio un JSON que no se pudo parsear."
                    },
                    Origen = PlanResolutionSource.Rejected,
                    TextoOriginal = pregunta,
                    TextoNormalizado = normalizado,
                    ModelRaw = raw
                };
            }

            var validado = IaPlanValidator.ValidarCompleto(plan, anchor, OperacionesPermitidas);
            return new PlanResolution
            {
                Evaluacion = validado,
                Origen = validado.Estado is FinancialPlanStatus.Ok
                    ? PlanResolutionSource.Semantic
                    : (validado.Estado is FinancialPlanStatus.AclaracionRequerida
                        ? PlanResolutionSource.Clarification
                        : PlanResolutionSource.Rejected),
                TextoOriginal = pregunta,
                TextoNormalizado = normalizado,
                ModelRaw = raw
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Planificador semantico fallo: {Message}", ex.Message);
            return new PlanResolution
            {
                Evaluacion = new FinancialPlanEvaluation
                {
                    Estado = FinancialPlanStatus.Rechazado,
                    Motivo = "El proveedor no respondio. Reintenta en unos segundos."
                },
                Origen = PlanResolutionSource.Rejected,
                TextoOriginal = pregunta,
                TextoNormalizado = normalizado
            };
        }
    }

    private static string Normalizar(string texto)
    {
        var sinDiacriticos = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(sinDiacriticos.Length);
        foreach (var c in sinDiacriticos)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly LocalPattern[] Patrones = new[]
    {
        // "saldo actual" / "cual es mi saldo" / "saldo de mis cuentas"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:cual|que)\s+es\s+mi\s+saldo(?:\s+actual)?(?:\s+de\s+mis\s+cuentas)?\s*\?*\s*$",
            "saldo_actual",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.List,
                Metrica = FinancialMetric.Saldo,
                Filtros = new FinancialFilters
                {
                    Periodo = IaPlanValidator.PeriodoPorDefecto(anchor)
                },
                Limite = 50
            }),

        // "ultimo gasto" / "cual fue el ultimo gasto"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:cual\s+es|cual\s+fue|que\s+fue)\s+el\s+ultimo\s+gasto\s*\?*\s*$",
            "ultimo_gasto",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.GetLatest,
                Metrica = FinancialMetric.Gastos,
                Filtros = new FinancialFilters
                {
                    Periodo = IaPlanValidator.PeriodoPorDefecto(anchor)
                },
                Limite = 1
            }),

        // "ultimo ingreso" / "ultimo cobro"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:cual\s+es|cual\s+fue|que\s+fue)\s+(?:el\s+)?ultim[oa]\s+(ingreso|cobro|abono)\s*\?*\s*$",
            "ultimo_ingreso",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.GetLatest,
                Metrica = FinancialMetric.Ingresos,
                Filtros = new FinancialFilters
                {
                    Periodo = IaPlanValidator.PeriodoPorDefecto(anchor)
                },
                Limite = 1
            }),

        // "total del mes" / "cuanto he gastado este mes"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:total|cuanto|cuanta)\s+(?:he\s+)?(?:gastado|ingresado|en\s+gastos|en\s+ingresos)\s+(?:del?|en\s+el)?\s*mes(?:\s+actual)?(?:\s+pasado|\s+anterior)?\s*\?*\s*$",
            "total_mes",
            (q, anchor) => PeriodoMatcher.MatchMes(q, anchor, FinancialMetric.Gastos)),

        // "comisiones pendientes" / "comisiones por revisar"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:que|cuales|cuales\s+son\s+las)\s+comisiones\s+(?:pendientes|por\s+revisar|en\s+revision)\s*\?*\s*$",
            "comisiones_pendientes",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.List,
                Metrica = FinancialMetric.Gastos,
                Filtros = new FinancialFilters
                {
                    Estados = new[] { "PENDIENTE" }
                },
                Limite = 50
            }),

        // "tendencia de gastos" / "evolucion de gastos"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:tendencia|evolucion|como\s+evolucionan?)\s+(?:de\s+)?(?:los?\s+)?gastos?\s*\?*\s*$",
            "tendencia_gastos",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.Trend,
                Metrica = FinancialMetric.Gastos,
                Filtros = new FinancialFilters
                {
                    Periodo = new FinancialPeriod
                    {
                        Tipo = FinancialPeriodKind.Ultimos30Dias,
                        Anchor = anchor
                    }
                },
                Limite = 24
            }),

        // "ranking por cuentas" / "que cuentas han tenido mas gastos"
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:ranking|que\s+cuentas|cuales\s+cuentas|principales\s+cuentas)\b.*\b(gastos|ingresos|neto)\s*\?*\s*$",
            "ranking_cuentas",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.Ranking,
                Metrica = ContainsAny(q, "ingreso", "cobro", "abono")
                    ? FinancialMetric.Ingresos
                    : FinancialMetric.Gastos,
                Filtros = new FinancialFilters
                {
                    Periodo = IaPlanValidator.PeriodoPorDefecto(anchor)
                },
                Agrupaciones = new[] { FinancialGroupBy.Cuenta },
                Limite = 10
            })
    };

    private static string NormalizarQuery(string texto) => Normalizar(texto);

    private LocalMatch? TryLocalMatch(string normalizado, DateOnly anchor)
    {
        foreach (var patron in Patrones)
        {
            var match = Regex.Match(normalizado, patron.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (match.Success)
            {
                var plan = patron.Build(normalizado, anchor);
                if (plan is not null)
                {
                    return new LocalMatch(patron.Name, plan);
                }
            }
        }
        return null;
    }

    private FinancialPlanEvaluation? TryLocalClarification(string normalizado)
    {
        // "que cuenta va peor" / "que titular va peor" sin
        // especificar la metrica. Pedimos aclaracion.
        if (Regex.IsMatch(normalizado, @"\b(va|van|esta|estan)\s+(peor|mal|regular)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
        {
            return new FinancialPlanEvaluation
            {
                Estado = FinancialPlanStatus.AclaracionRequerida,
                Motivo = "Para 'que cuenta va peor' necesito saber que entiendes por 'peor'.",
                Opciones = new[]
                {
                    new FinancialClarificationOption { Etiqueta = "Menor saldo", Valor = "saldo" },
                    new FinancialClarificationOption { Etiqueta = "Mayor gasto", Valor = "gastos" },
                    new FinancialClarificationOption { Etiqueta = "Peor tendencia", Valor = "tendencia" },
                    new FinancialClarificationOption { Etiqueta = "Mas pendientes", Valor = "pendientes" }
                }
            };
        }
        return null;
    }

    private static FinancialQueryPlan? ParsePlanFromJson(string raw)
    {
        try
        {
            // Solo operaciones + metrica + filtros basicos llegan del
            // modelo. El resto (periodo, agrupacion) lo completa el
            // validador.
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("operacion", out var op)) return null;
            if (!root.TryGetProperty("metrica", out var met)) return null;
            if (!Enum.TryParse<FinancialOperation>(op.GetString(), ignoreCase: true, out var opVal)) return null;
            if (!Enum.TryParse<FinancialMetric>(met.GetString(), ignoreCase: true, out var metVal)) return null;

            var filtros = ParseFiltros(root);
            return new FinancialQueryPlan
            {
                Operacion = opVal,
                Metrica = metVal,
                Filtros = filtros
            };
        }
        catch
        {
            return null;
        }
    }

    private static FinancialFilters ParseFiltros(JsonElement root)
    {
        if (!root.TryGetProperty("filtros", out var f) || f.ValueKind != JsonValueKind.Object)
        {
            return new FinancialFilters();
        }

        IReadOnlyList<string>? divisas = null;
        if (f.TryGetProperty("divisas", out var d) && d.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in d.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? string.Empty);
            }
            divisas = list.Count > 0 ? list : null;
        }

        string? concepto = null;
        if (f.TryGetProperty("concepto", out var c) && c.ValueKind == JsonValueKind.String)
        {
            concepto = c.GetString();
        }

        return new FinancialFilters
        {
            Divisas = divisas,
            Concepto = concepto
        };
    }

    private static bool ContainsAny(string texto, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (texto.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static class PeriodoMatcher
    {
        public static FinancialQueryPlan? MatchMes(string q, DateOnly anchor, FinancialMetric metrica)
        {
            var esAnterior = q.Contains("pasado", StringComparison.OrdinalIgnoreCase)
                || q.Contains("anterior", StringComparison.OrdinalIgnoreCase);
            var tipo = esAnterior ? FinancialPeriodKind.MesAnterior : FinancialPeriodKind.MesActual;
            return new FinancialQueryPlan
            {
                Operacion = FinancialOperation.Sum,
                Metrica = metrica,
                Filtros = new FinancialFilters
                {
                    Periodo = new FinancialPeriod { Tipo = tipo, Anchor = anchor }
                },
                Limite = 50
            };
        }
    }

    private sealed record LocalPattern(string Pattern, string Name, Func<string, DateOnly, FinancialQueryPlan?> Build);

    private sealed record LocalMatch(string Patron, FinancialQueryPlan Plan);
}
