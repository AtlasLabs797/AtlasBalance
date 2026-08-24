using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Services;

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
    // V-02.08: true cuando ResolverAsync llego a despachar (o intento
    // despachar) la llamada facturable al planificador semantico. El
    // caller usa esta senal para registrar el uso en los contadores de
    // limite/presupuesto, que de otro modo no verian esta peticion.
    public bool SemanticCallAttempted { get; init; }
}

public interface IIntentPlanner
{
    // V-02.08: parametros opcionales anadidos al final para no romper a
    // los callers existentes (tests incluidos) que solo pasan pregunta,
    // anchor y cancellationToken.
    //   - pseudonymsFactory: construye el mapa de seudonimizacion justo
    //     antes de despachar al planificador semantico (nivel 2), para no
    //     pagar esa consulta en preguntas que el nivel 1 ya resuelve gratis.
    //   - previousContext: memoria conversacional del usuario, para
    //     resolver seguimientos ("¿y el mes anterior?") sin repetir toda
    //     la intencion.
    //   - beforeSemanticDispatchAsync: hook que el caller usa para aplicar
    //     limites de tasa y presupuesto de IA ANTES de la llamada
    //     facturable al planificador semantico. Si lanza, la excepcion se
    //     propaga sin ser absorbida por el catch-all del nivel 2.
    Task<PlanResolution> ResolverAsync(
        string pregunta,
        DateOnly anchor,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<AiPseudonymMap>>? pseudonymsFactory = null,
        ConversationContext? previousContext = null,
        Func<CancellationToken, Task>? beforeSemanticDispatchAsync = null);
}

// Stub del nivel 2: en produccion sera una clase que llame al
// proveedor; en tests inyectamos un stub que devuelve un plan
// concreto. La interfaz permite implementar el nivel 2 sin tocar
// el flujo del nivel 1/3.
// V-02.08: Dispatched distingue "se hizo una llamada de red real y
// facturable al proveedor" de "PlanToJsonAsync no llego a llamar a la red"
// (proveedor no soportado, sin API key, DLP en fail-closed). Sin esta
// distincion, IntentPlanner no puede saber si debe registrar coste/uso: un
// Json nulo significa lo mismo en ambos casos.
public sealed record SemanticPlanResponse(string? Json, bool Dispatched);

public interface ISemanticPlannerClient
{
    Task<SemanticPlanResponse> PlanToJsonAsync(
        string pregunta,
        IReadOnlyList<string> allowedOperations,
        CancellationToken cancellationToken,
        AiPseudonymMap? pseudonyms = null);
}

public sealed class NullSemanticPlannerClient : ISemanticPlannerClient
{
    public Task<SemanticPlanResponse> PlanToJsonAsync(
        string pregunta,
        IReadOnlyList<string> allowedOperations,
        CancellationToken cancellationToken,
        AiPseudonymMap? pseudonyms = null)
    {
        return Task.FromResult(new SemanticPlanResponse(null, false));
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

    public async Task<PlanResolution> ResolverAsync(
        string pregunta,
        DateOnly anchor,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<AiPseudonymMap>>? pseudonymsFactory = null,
        ConversationContext? previousContext = null,
        Func<CancellationToken, Task>? beforeSemanticDispatchAsync = null)
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

        // V-02.08: seguimiento de memoria conversacional. Preguntas cortas
        // como "¿y el mes anterior?" no encajan en ningun patron de nivel 1
        // (no llevan metrica), pero heredan operacion/metrica/divisas de la
        // ultima intencion resuelta para este usuario si hay una sesion viva.
        if (previousContext is not null)
        {
            var seguimiento = TryFollowUpMatch(normalizado, anchor, previousContext);
            if (seguimiento is not null)
            {
                var validadoSeguimiento = IaPlanValidator.ValidarCompleto(seguimiento, anchor, OperacionesPermitidas);
                if (validadoSeguimiento.Estado is FinancialPlanStatus.Ok)
                {
                    return new PlanResolution
                    {
                        Evaluacion = validadoSeguimiento,
                        Origen = PlanResolutionSource.Local,
                        TextoOriginal = pregunta,
                        TextoNormalizado = normalizado,
                        PatronLocal = "seguimiento_memoria"
                    };
                }
            }
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

        // Nivel 2: semantico. El chequeo de limites/presupuesto se hace
        // FUERA del try/catch de abajo a proposito: si el caller decide
        // bloquear la peticion (limite de tasa o presupuesto agotado), esa
        // excepcion debe propagarse tal cual, no ser absorbida por el
        // catch-all y convertida en un "Rechazado" silencioso.
        if (beforeSemanticDispatchAsync is not null)
        {
            await beforeSemanticDispatchAsync(cancellationToken);
        }

        try
        {
            var pseudonyms = pseudonymsFactory is not null
                ? await pseudonymsFactory(cancellationToken)
                : null;
            var semanticResponse = await _semantic.PlanToJsonAsync(texto, OperacionesPermitidas, cancellationToken, pseudonyms);
            var raw = semanticResponse.Json;
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
                    TextoNormalizado = normalizado,
                    SemanticCallAttempted = semanticResponse.Dispatched
                };
            }

            var plan = ParsePlanFromJson(raw, anchor);
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
                    ModelRaw = raw,
                    SemanticCallAttempted = semanticResponse.Dispatched
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
                ModelRaw = raw,
                SemanticCallAttempted = true
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
                TextoNormalizado = normalizado,
                SemanticCallAttempted = true
            };
        }
    }

    // V-02.08: reconstruye un plan a partir de una pregunta de seguimiento
    // corta ("¿y el mes anterior?") combinando la operacion/metrica/divisas
    // de la ultima intencion resuelta con el periodo que pide la pregunta.
    // Solo cubre seguimientos de periodo (el caso descrito en la auditoria);
    // si el usuario cambia de metrica o de dimension, esto no matchea y el
    // flujo sigue por nivel 2/3 como una pregunta nueva.
    private static FinancialQueryPlan? TryFollowUpMatch(string normalizado, DateOnly anchor, ConversationContext previous)
    {
        if (string.IsNullOrWhiteSpace(previous.UltimaOperacion) || string.IsNullOrWhiteSpace(previous.UltimaMetrica))
        {
            return null;
        }

        if (!Enum.TryParse<FinancialOperation>(previous.UltimaOperacion, out var operacion) ||
            !Enum.TryParse<FinancialMetric>(previous.UltimaMetrica, out var metrica))
        {
            return null;
        }

        FinancialPeriodKind? periodo = null;
        if (RegexMatchFollowUp(normalizado, @"y\s+(?:el\s+|la\s+|del\s+)?mes\s+(?:anterior|pasado)"))
        {
            periodo = FinancialPeriodKind.MesAnterior;
        }
        else if (RegexMatchFollowUp(normalizado, @"y\s+(?:el\s+|este\s+|del\s+)?mes\s+(?:actual|en\s+curso)"))
        {
            periodo = FinancialPeriodKind.MesActual;
        }
        else if (RegexMatchFollowUp(normalizado, @"y\s+(?:el\s+|este\s+)?trimestre(?:\s+actual)?"))
        {
            periodo = FinancialPeriodKind.TrimestreActual;
        }
        else if (RegexMatchFollowUp(normalizado, @"y\s+(?:el\s+|este\s+)?ano(?:\s+actual)?"))
        {
            periodo = FinancialPeriodKind.AnoActual;
        }

        if (periodo is null)
        {
            return null;
        }

        return new FinancialQueryPlan
        {
            Operacion = operacion,
            Metrica = metrica,
            Filtros = new FinancialFilters
            {
                Periodo = new FinancialPeriod { Tipo = periodo.Value, Anchor = anchor },
                Divisas = previous.UltimasDivisas is { Count: > 0 } ? previous.UltimasDivisas : null
            },
            Limite = 50
        };
    }

    private static bool RegexMatchFollowUp(string normalizado, string nucleo) =>
        Regex.IsMatch(
            normalizado,
            $@"^[\s\?\!¡¿]*{nucleo}\s*\?*\s*$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));

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
        // V-02.08: sin Periodo (no acotar al mes actual): si el ultimo gasto
        // real es de un mes anterior, GetLatestTransactionAsync debe seguir
        // encontrandolo en vez de reportar que no hay resultados.
        new LocalPattern(
            @"^[\s\?\!¡¿]*(?:cual\s+es|cual\s+fue|que\s+fue)\s+el\s+ultimo\s+gasto\s*\?*\s*$",
            "ultimo_gasto",
            (q, anchor) => new FinancialQueryPlan
            {
                Operacion = FinancialOperation.GetLatest,
                Metrica = FinancialMetric.Gastos,
                Filtros = new FinancialFilters(),
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
                Filtros = new FinancialFilters(),
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

    private static FinancialQueryPlan? ParsePlanFromJson(string raw, DateOnly anchor)
    {
        try
        {
            // Solo operaciones + metrica + filtros basicos llegan del
            // modelo. El resto (agrupacion) lo completa el validador.
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.EnumerateObject().Any(x => x.Name is not ("operacion" or "metrica" or "filtros"))) return null;
            if (!root.TryGetProperty("operacion", out var op)) return null;
            if (!root.TryGetProperty("metrica", out var met)) return null;
            if (!Enum.TryParse<FinancialOperation>(op.GetString(), ignoreCase: true, out var opVal)) return null;
            if (!Enum.TryParse<FinancialMetric>(met.GetString(), ignoreCase: true, out var metVal)) return null;

            var filtros = ParseFiltros(root, anchor);
            if (filtros is null) return null;
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

    private static FinancialFilters? ParseFiltros(JsonElement root, DateOnly anchor)
    {
        if (!root.TryGetProperty("filtros", out var f) || f.ValueKind != JsonValueKind.Object)
        {
            return new FinancialFilters();
        }

        if (f.EnumerateObject().Any(x => x.Name is not ("divisas" or "concepto" or "periodo")))
        {
            return null;
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

        FinancialPeriod? periodo = null;
        if (f.TryGetProperty("periodo", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            periodo = ParsePeriodo(p, anchor);
            if (periodo is null) return null;
        }

        return new FinancialFilters
        {
            Divisas = divisas,
            Concepto = concepto,
            Periodo = periodo
        };
    }

    // V-02.08: traduce el periodo devuelto por el planificador semantico
    // (tipo natural o rango explicito) a FinancialPeriod. Un periodo con
    // forma reconocible pero valores invalidos rechaza el plan entero en
    // vez de normalizarlo silenciosamente al mes actual.
    private static FinancialPeriod? ParsePeriodo(JsonElement p, DateOnly anchor)
    {
        if (p.TryGetProperty("tipo", out var tipoEl) && tipoEl.ValueKind == JsonValueKind.String)
        {
            var tipo = tipoEl.GetString()?.Trim().ToLowerInvariant();
            var kind = tipo switch
            {
                "mes_actual" => FinancialPeriodKind.MesActual,
                "mes_anterior" => FinancialPeriodKind.MesAnterior,
                "trimestre_actual" => FinancialPeriodKind.TrimestreActual,
                "ano_actual" or "año_actual" => FinancialPeriodKind.AnoActual,
                "ultimos_30_dias" => FinancialPeriodKind.Ultimos30Dias,
                _ => (FinancialPeriodKind?)null
            };
            if (kind is null) return null;
            return new FinancialPeriod { Tipo = kind.Value, Anchor = anchor };
        }

        if (p.TryGetProperty("desde", out var desdeEl) && desdeEl.ValueKind == JsonValueKind.String &&
            p.TryGetProperty("hasta", out var hastaEl) && hastaEl.ValueKind == JsonValueKind.String)
        {
            if (!DateOnly.TryParse(desdeEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var desde)) return null;
            if (!DateOnly.TryParse(hastaEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var hasta)) return null;
            if (hasta < desde) return null;
            return new FinancialPeriod { Tipo = FinancialPeriodKind.Explicito, From = desde, To = hasta, Anchor = anchor };
        }

        return null;
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
