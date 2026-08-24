namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 2): contrato tipado para que el modelo de IA pueda expresar
// la intencion de una consulta financiera sin SQL, sin campos arbitrarios y
// sin acceso a tablas. El modelo solo puede combinar estas operaciones
// y metricas cerradas; el resto se rechaza en IaPlanValidator.
//
// Las operaciones de escritura (update, delete, insert) no estan en la
// enum: cualquier intento de pedirlas se traduce a un plan vacio que el
// validador rechaza.

public enum FinancialOperation
{
    // Devuelve un solo movimiento (ultimo gasto, ultimo ingreso, ultima
    // comision, etc.). Combina con FinancialFilters.Periodo.
    GetLatest,
    // Lista de movimientos crudos. Combina con filtros y orden.
    List,
    // Agregado de totales (gastos, ingresos, neto, count). El destino
    // puede ser agrupado (FinancialGroupBy).
    Sum,
    // Recuento puro. Equivalente a Sum con metrica = Count.
    Count,
    // Igual que Sum pero explicito: el cliente quiere ver grupos.
    Group,
    // Compara dos periodos (FinancialComparison) o dos titulares/cuentas.
    Compare,
    // Serie temporal. Periodo.From debe ser un mes natural completo.
    Trend,
    // Busqueda por concepto, importe, fecha, cuenta o titular.
    Search,
    // Anomalias: duplicados, importes atipicos, cargos recurrentes que
    // cambian de importe, nuevas comisiones.
    Anomalies,
    // Ranking top-N de gastos/ingresos/saldo por cuenta o titular.
    Ranking
}

public enum FinancialMetric
{
    Gastos,
    Ingresos,
    Neto,
    Saldo,
    Count,
    ImporteMedio,
    ImporteMaximo,
    ImporteMinimo,
    Variacion
}

public enum FinancialGroupBy
{
    None,
    Cuenta,
    Titular,
    Banco,
    Divisa,
    Mes,
    Categoria
}

public enum FinancialSort
{
    Fecha,
    Importe,
    Variacion,
    Saldo
}

// Ventana temporal. Si Tipo = Explicito se usan los From/To en DateOnly.
// Si Tipo = Natural, From/To se calculan respecto a "hoy" (la capa de
// ejecucion resuelve las formulas). Se rechaza cualquier combinacion que
// resulte ambigua (p.ej. Natural.Year con Month = null).
public enum FinancialPeriodKind
{
    Explicito,
    MesActual,
    MesAnterior,
    TrimestreActual,
    AnoActual,
    Ultimos30Dias,
    Personalizado
}

public sealed record FinancialPeriod
{
    public FinancialPeriodKind Tipo { get; init; } = FinancialPeriodKind.Explicito;
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    // Anclaje a "hoy" para los calculos de periodo natural. UTC.
    public DateOnly Anchor { get; init; }
}

public sealed record FinancialFilters
{
    public IReadOnlyList<Guid>? CuentaIds { get; init; }
    public IReadOnlyList<Guid>? TitularIds { get; init; }
    public IReadOnlyList<Guid>? PaisIds { get; init; }
    public IReadOnlyList<string>? Divisas { get; init; }
    public IReadOnlyList<string>? Categorias { get; init; }
    public IReadOnlyList<string>? Estados { get; init; }
    public string? Concepto { get; init; }
    // Rango de importe cerrado y tipado (en lugar de un string abierto).
    public decimal? ImporteMinimo { get; init; }
    public decimal? ImporteMaximo { get; init; }
    public FinancialPeriod? Periodo { get; init; }
}

public sealed record FinancialComparison
{
    public FinancialPeriod Base { get; init; } = new();
    public FinancialPeriod Referencia { get; init; } = new();
    // "vs" o "anterior". Solo documentativo; la logica de "anterior" la
    // resuelve la capa de ejecucion.
    public string Modo { get; init; } = "anterior";
}

public sealed record FinancialQueryPlan
{
    public FinancialOperation Operacion { get; init; }
    public FinancialMetric Metrica { get; init; }
    public FinancialFilters Filtros { get; init; } = new();
    public IReadOnlyList<FinancialGroupBy> Agrupaciones { get; init; } = [];
    public FinancialSort? Orden { get; init; }
    public bool Descendente { get; init; } = true;
    // Limite de filas devueltas. El validador lo recorta si es excesivo
    // o si la operacion no lo soporta.
    public int Limite { get; init; } = 50;
    public FinancialComparison? Comparacion { get; init; }
    // Solo se usa cuando la operacion es Search.
    public string? TerminoBusqueda { get; init; }
    // Texto libre que el modelo intento meter y el validador rechazo.
    // Se conserva para depuracion y para devolver feedback al usuario.
    public IReadOnlyList<string>? CamposRechazados { get; init; }
}

public enum FinancialPlanStatus
{
    Ok,
    AclaracionRequerida,
    Rechazado
}

public sealed class FinancialPlanEvaluation
{
    public FinancialPlanStatus Estado { get; init; }
    public FinancialQueryPlan? Plan { get; init; }
    public string? Motivo { get; init; }
    // Lista de preguntas concretas que el sistema necesita para resolver
    // una ambiguedad. Cada item es una opcion seleccionable en el UI.
    public IReadOnlyList<FinancialClarificationOption>? Opciones { get; init; }
}

public sealed class FinancialClarificationOption
{
    public string Etiqueta { get; init; } = string.Empty;
    public string Valor { get; init; } = string.Empty;
}
