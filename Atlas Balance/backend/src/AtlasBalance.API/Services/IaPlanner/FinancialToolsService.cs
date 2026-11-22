using System.Globalization;
using System.Text;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 3): herramientas financieras de solo lectura que el
// planificador de Fase 4 invoca contra el plan validado.

public enum FinancialDirection
{
    Gasto,
    Ingreso,
    Todos
}

public sealed record FinancialToolResult<T>
{
    public T Data { get; init; } = default!;
    public int FilasDevueltas { get; init; }
    public int FilasAnalizadas { get; init; }
    public string? Advertencia { get; init; }
    // A-6/B-10: la herramienta NO puede atender el plan (el filtro de
    // concepto o de importes no aplica a esta consulta, o la comparacion
    // mezclaria divisas distintas). En ese caso no se devuelve ninguna
    // cifra: el ejecutor (PlanExecutor) convierte el motivo de
    // Advertencia en un rechazo visible para el usuario.
    public bool Rechazo { get; init; }
}

public sealed record LatestTransaction(
    Guid ExtractoId,
    Guid CuentaId,
    string CuentaNombre,
    string TitularNombre,
    string Divisa,
    DateOnly Fecha,
    decimal Monto,
    decimal Saldo,
    string Concepto,
    bool CuentaInactiva);

public sealed record PeriodTotalsRow(
    string Clave,
    string Titular,
    string Cuenta,
    string Banco,
    string Divisa,
    decimal Ingresos,
    decimal Gastos,
    decimal Neto,
    int MovimientosGasto,
    int MovimientosIngreso,
    int MovimientosTotal);

public sealed record BalanceRow(
    Guid CuentaId,
    string CuentaNombre,
    string TitularNombre,
    string Banco,
    string Divisa,
    decimal Saldo,
    DateOnly Fecha,
    bool Inactiva);

public sealed record RankingRow(
    string Clave,
    string Titular,
    string Cuenta,
    string Banco,
    string Divisa,
    decimal Gastos,
    decimal Ingresos,
    decimal Neto,
    int Movimientos);

public sealed record RevisionItem(
    Guid ExtractoId,
    string Concepto,
    string CuentaNombre,
    string TitularNombre,
    string Divisa,
    DateOnly Fecha,
    decimal Monto,
    string Estado,
    string? Comentario);

public sealed record TrendPoint(
    int Year,
    int Month,
    string Divisa,
    decimal Ingresos,
    decimal Gastos,
    decimal Neto,
    int Movimientos);

public sealed record PendingMovement(
    Guid Id,
    Guid CuentaId,
    string CuentaNombre,
    string TitularNombre,
    string Divisa,
    DateOnly FechaEsperada,
    decimal Monto,
    string Estado,
    string? Concepto,
    string? ConciliacionEstado);

public sealed record SearchHit(
    Guid ExtractoId,
    Guid CuentaId,
    string CuentaNombre,
    string TitularNombre,
    string Divisa,
    DateOnly Fecha,
    decimal Monto,
    decimal Saldo,
    string Concepto);

public sealed record ComparisonSnapshot(
    string Etiqueta,
    DateOnly From,
    DateOnly To,
    decimal Ingresos,
    decimal Gastos,
    decimal Neto,
    int Movimientos,
    // B-10: un snapshot es de UNA sola divisa. Comparar importes de
    // distinta divisa no tiene sentido (1 EUR != 1 USD).
    string Divisa);

public sealed record ComparisonResult(
    ComparisonSnapshot Base,
    ComparisonSnapshot Referencia,
    decimal VariacionIngresos,
    decimal VariacionGastos,
    decimal VariacionNeto,
    decimal VariacionIngresosPct,
    decimal VariacionGastosPct,
    decimal VariacionNetoPct);

public sealed record Anomaly(
    string Tipo,
    string Severidad,
    string Descripcion,
    Guid? ExtractoId,
    Guid? CuentaId,
    string? CuentaNombre,
    string? TitularNombre,
    DateOnly? Fecha,
    decimal? Importe,
    string? Detalle);

public interface IFinancialToolsService
{
    Task<FinancialToolResult<LatestTransaction?>> GetLatestTransactionAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<PeriodTotalsRow>>> GetPeriodTotalsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<BalanceRow>>> GetBalancesAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<RankingRow>>> GetRankingAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<RevisionItem>>> GetRevisionItemsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<TrendPoint>>> GetExpenseTrendAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<PendingMovement>>> GetPendingMovementsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<SearchHit>>> SearchTransactionsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<ComparisonResult>> ComparePeriodsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);

    Task<FinancialToolResult<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken);
}

public sealed class FinancialToolsService : IFinancialToolsService
{
    public const decimal AnomalyHighFactor = 3m;
    public const int AnomalyHistoryMonths = 6;
    // B-10: dos movimientos con el mismo concepto, importe y cuenta solo
    // son "duplicado probable" si estan proximos en el tiempo. Un pago
    // recurrente legitimo (la prima de seguro de cada mes) NO es un
    // duplicado: esta constante fija la ventana de proximidad.
    public const int AnomalyDuplicateMaxDays = 3;

    private readonly AppDbContext _dbContext;
    private readonly IUserAccessService _userAccessService;

    public FinancialToolsService(AppDbContext dbContext, IUserAccessService userAccessService)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
    }

    private IQueryable<Models.Cuenta> CuentasScope(UserAccessScope scope, Guid? paisId)
    {
        return _userAccessService
            .ApplyCuentaScope(_dbContext.Cuentas.AsNoTracking(), scope)
            .ApplyPaisScope(paisId);
    }

    // Tipo interno para los resultados intermedios de GetLatestTransaction.
    // Evita dynamic y mantiene tipado fuerte en la traduccion LINQ.
    private sealed class ExtractoJoinRow
    {
        public Guid ExtractoId { get; set; }
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string Titular { get; set; } = string.Empty;
        public Guid TitularId { get; set; }
        public string Divisa { get; set; } = string.Empty;
        public DateOnly Fecha { get; set; }
        public decimal Monto { get; set; }
        public decimal Saldo { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public bool Activa { get; set; }
    }

    public async Task<FinancialToolResult<LatestTransaction?>> GetLatestTransactionAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        // Por defecto, la cuenta inactiva SI entra en el resultado: el
        // usuario quiere ver "el ultimo gasto", no el ultimo entre
        // cuentas activas. CuentasScope ya respeta eso (no filtra
        // inactivas).
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var direction = ResolveDirection(plan.Metrica, FinancialDirection.Todos);
        // V-02.09: el periodo por defecto (mes en curso, ver
        // IaPlanValidator.PeriodoPorDefecto) no debe acotar que movimiento es
        // "el ultimo": mismo criterio que GetBalancesAsync. Si acotaramos,
        // "¿cual fue el ultimo gasto?" no encontraria nada cuando el ultimo
        // gasto real es de un mes anterior. Solo un periodo pedido de forma
        // explicita (Explicito, MesAnterior, etc.) acota la ventana.
        var periodoEsPorDefecto = plan.Filtros.Periodo is null
            || plan.Filtros.Periodo.Tipo == FinancialPeriodKind.MesActual;
        var desde = periodoEsPorDefecto ? null : plan.Filtros.Periodo?.From;
        var hasta = periodoEsPorDefecto ? null : plan.Filtros.Periodo?.To;
        var cuentaIds = plan.Filtros.CuentaIds;
        var titularIds = plan.Filtros.TitularIds;
        var divisas = plan.Filtros.Divisas;
        var concepto = plan.Filtros.Concepto?.Trim();
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        IQueryable<ExtractoJoinRow> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            select new ExtractoJoinRow
            {
                ExtractoId = e.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                Titular = t.Nombre,
                TitularId = t.Id,
                Divisa = c.Divisa,
                Fecha = e.Fecha,
                Monto = e.Monto,
                Saldo = e.Saldo,
                Concepto = e.Concepto ?? string.Empty,
                Activa = c.Activa
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (cuentaIds is { Count: > 0 }) query = query.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));
        // A-6: el filtro por concepto y el rango de importes se aplican
        // igual que en SearchTransactionsAsync: "contains" insensible a
        // mayusculas sobre el concepto del extracto e importes
        // comparados contra el VALOR ABSOLUTO del movimiento (los
        // gastos se guardan en negativo, pero el usuario piensa en
        // magnitudes).
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            query = query.Where(x => x.Concepto.ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        IQueryable<ExtractoJoinRow> ordered = direction switch
        {
            FinancialDirection.Gasto => query.Where(x => x.Monto < 0m).OrderByDescending(x => x.Fecha).ThenByDescending(x => x.ExtractoId),
            FinancialDirection.Ingreso => query.Where(x => x.Monto > 0m).OrderByDescending(x => x.Fecha).ThenByDescending(x => x.ExtractoId),
            _ => query.OrderByDescending(x => x.Fecha).ThenByDescending(x => x.ExtractoId)
        };

        var raw = await ordered.Take(1).ToListAsync(cancellationToken);
        if (raw.Count == 0)
        {
            return new FinancialToolResult<LatestTransaction?>
            {
                FilasDevueltas = 0,
                FilasAnalizadas = await query.CountAsync(cancellationToken)
            };
        }
        var row = raw[0];
        var data = new LatestTransaction(
            row.ExtractoId, row.CuentaId, row.CuentaNombre, row.Titular, row.Divisa,
            row.Fecha, row.Monto, row.Saldo, row.Concepto, !row.Activa);
        return new FinancialToolResult<LatestTransaction?>
        {
            Data = data,
            FilasDevueltas = 1,
            FilasAnalizadas = raw.Count
        };
    }

    public async Task<FinancialToolResult<IReadOnlyList<PeriodTotalsRow>>> GetPeriodTotalsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var grupo = plan.Agrupaciones.FirstOrDefault();
        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;
        var cuentaIds = plan.Filtros.CuentaIds;
        var titularIds = plan.Filtros.TitularIds;
        var divisas = plan.Filtros.Divisas;
        var concepto = plan.Filtros.Concepto?.Trim();
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        IQueryable<PeriodTotalsProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            select new PeriodTotalsProjection
            {
                Monto = e.Monto,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                BancoNombre = c.BancoNombre ?? string.Empty,
                Divisa = c.Divisa,
                Fecha = e.Fecha,
                Concepto = e.Concepto ?? string.Empty,
                TitularId = t.Id
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (cuentaIds is { Count: > 0 }) query = query.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));
        // A-6: concepto y rango de importes (magnitudes) tambien acotan
        // los totales; sin esto, "¿cuanto he gastado en seguros?" salia
        // con el total de todos los gastos.
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            query = query.Where(x => x.Concepto.ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        var totalBase = await query.CountAsync(cancellationToken);

        // Materializamos los extractos crudos (sin GroupBy ni
        // OrderBy) y los agregamos en memoria. Para Postgres real la
        // consulta seria GroupBy SQL, pero el plan es agregar pocos
        // miles de filas como mucho dentro del scope del usuario y
        // el sobrecoste de ir y volver a SQL no compensa.
        var raw = await query
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.FilaNumeroForTest)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PeriodTotalsRow> data = grupo is FinancialGroupBy.Titular
            ? raw
                // TITULARES.nombre no es unico: se agrupa tambien por
                // TitularId para no fusionar titulares distintos que
                // comparten nombre y divisa.
                .GroupBy(x => new { x.TitularId, x.TitularNombre, x.Divisa })
                .Select(g => new PeriodTotalsRow(
                    g.Key.TitularNombre,
                    g.Key.TitularNombre,
                    string.Empty,
                    string.Empty,
                    g.Key.Divisa,
                    g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                    -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                    g.Sum(x => x.Monto),
                    g.Count(x => x.Monto < 0),
                    g.Count(x => x.Monto > 0),
                    g.Count()))
                .OrderByDescending(x => Math.Abs(x.Neto))
                .Take(plan.Limite)
                .ToList()
            : raw
                .GroupBy(x => new { x.CuentaId, x.CuentaNombre, x.TitularNombre, Banco = x.BancoNombre, x.Divisa })
                .Select(g => new PeriodTotalsRow(
                    g.Key.CuentaNombre,
                    g.Key.TitularNombre,
                    g.Key.CuentaNombre,
                    g.Key.Banco,
                    g.Key.Divisa,
                    g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                    -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                    g.Sum(x => x.Monto),
                    g.Count(x => x.Monto < 0),
                    g.Count(x => x.Monto > 0),
                    g.Count()))
                .OrderByDescending(x => Math.Abs(x.Neto))
                .Take(plan.Limite)
                .ToList();

        return new FinancialToolResult<IReadOnlyList<PeriodTotalsRow>>
        {
            Data = data,
            FilasDevueltas = data.Count,
            FilasAnalizadas = totalBase
        };
    }

    private sealed class PeriodTotalsProjection
    {
        public decimal Monto { get; set; }
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string TitularNombre { get; set; } = string.Empty;
        public string BancoNombre { get; set; } = string.Empty;
        public string Divisa { get; set; } = string.Empty;
        public DateOnly Fecha { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public Guid TitularId { get; set; }
        // No se proyecta desde EF (no hay columna), solo se usa en
        // tests para asegurar orden estable antes de agregar en
        // memoria. Se ignora en PostgreSQL.
        public int FilaNumeroForTest { get; set; }
    }

    public async Task<FinancialToolResult<IReadOnlyList<BalanceRow>>> GetBalancesAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var hoy = FechasOperativas.HoyLocal();
        // El periodo por defecto (mes en curso, ver IntentPlanner "saldo_actual"
        // e IaPlanValidator.PeriodoPorDefecto) no debe acotar que extracto se
        // considera "el saldo actual": una cuenta sin movimientos este mes (o
        // en los ultimos 3 anos) sigue teniendo un ultimo saldo valido, que es
        // su extracto mas reciente sin ninguna cota inferior. Solo un periodo
        // pedido de forma explicita acota la ventana de busqueda.
        var periodoEsPorDefecto = plan.Filtros.Periodo is null
            || plan.Filtros.Periodo.Tipo == FinancialPeriodKind.MesActual;
        DateOnly? desde = periodoEsPorDefecto ? null : plan.Filtros.Periodo?.From;
        DateOnly? hasta = periodoEsPorDefecto ? null : (plan.Filtros.Periodo?.To ?? hoy);
        var cuentaIds = plan.Filtros.CuentaIds;
        var divisas = plan.Filtros.Divisas;

        // A-6: un saldo no es un movimiento. Si el plan pide filtrar por
        // concepto o por rango de importes, esta herramienta no puede
        // devolver una cifra sin filtrar: se rechaza con motivo.
        if (HayFiltroDeMovimiento(plan.Filtros))
        {
            return new FinancialToolResult<IReadOnlyList<BalanceRow>>
            {
                Data = Array.Empty<BalanceRow>(),
                FilasDevueltas = 0,
                FilasAnalizadas = 0,
                Rechazo = true,
                Advertencia = $"No puedo aplicar el filtro ({DescribirFiltro(plan.Filtros)}) a los saldos: un saldo no es un movimiento. Reformula la pregunta o pide los movimientos filtrados."
            };
        }

        var latestKeys =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            where (!desde.HasValue || e.Fecha >= desde.Value) && (!hasta.HasValue || e.Fecha <= hasta.Value)
            group e by e.CuentaId
            into g
            select new
            {
                CuentaId = g.Key,
                FilaNumero = g.Max(x => x.FilaNumero)
            };

        var latest =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            join k in latestKeys on new { e.CuentaId, e.FilaNumero } equals new { k.CuentaId, k.FilaNumero }
            select new BalanceProjection
            {
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                Banco = c.BancoNombre ?? string.Empty,
                Divisa = c.Divisa,
                Saldo = e.Saldo,
                Fecha = e.Fecha,
                Activa = c.Activa
            };

        var raw = await latest
            .OrderBy(x => x.TitularNombre)
            .ThenBy(x => x.CuentaNombre)
            .ToListAsync(cancellationToken);

        var data = raw
            .Where(x => cuentaIds is null || cuentaIds.Count == 0 || cuentaIds.Contains(x.CuentaId))
            .Where(x => divisas is null || divisas.Count == 0 || divisas.Contains(x.Divisa))
            .Select(x => new BalanceRow(
                x.CuentaId, x.CuentaNombre, x.TitularNombre, x.Banco, x.Divisa,
                x.Saldo, x.Fecha, !x.Activa))
            .ToList();

        return new FinancialToolResult<IReadOnlyList<BalanceRow>>
        {
            Data = data,
            FilasDevueltas = data.Count,
            FilasAnalizadas = raw.Count
        };
    }

    private sealed class BalanceProjection
    {
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string TitularNombre { get; set; } = string.Empty;
        public string Banco { get; set; } = string.Empty;
        public string Divisa { get; set; } = string.Empty;
        public decimal Saldo { get; set; }
        public DateOnly Fecha { get; set; }
        public bool Activa { get; set; }
    }

    public async Task<FinancialToolResult<IReadOnlyList<RankingRow>>> GetRankingAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var grupo = plan.Agrupaciones.FirstOrDefault();
        var metrica = plan.Metrica;
        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;
        var cuentaIds = plan.Filtros.CuentaIds;
        var titularIds = plan.Filtros.TitularIds;
        var divisas = plan.Filtros.Divisas;
        var concepto = plan.Filtros.Concepto?.Trim();
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        IQueryable<RankingProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            select new RankingProjection
            {
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                Banco = c.BancoNombre ?? string.Empty,
                Divisa = c.Divisa,
                Monto = e.Monto,
                Fecha = e.Fecha,
                Concepto = e.Concepto ?? string.Empty,
                TitularId = t.Id
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (cuentaIds is { Count: > 0 }) query = query.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));
        // A-6: mismo criterio de concepto e importes que en el resto de
        // herramientas de movimientos.
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            query = query.Where(x => x.Concepto.ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        var raw = await query.ToListAsync(cancellationToken);

        IEnumerable<RankingRow> rows = grupo is FinancialGroupBy.Titular
            // TITULARES.nombre no es unico: se agrupa tambien por
            // TitularId para no fusionar titulares distintos que
            // comparten nombre y divisa.
            ? raw.GroupBy(x => new { x.TitularId, x.TitularNombre, x.Divisa })
                .Select(g => new RankingRow(
                    g.Key.TitularNombre, g.Key.TitularNombre, string.Empty, g.First().Banco, g.Key.Divisa,
                    -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                    g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                    g.Sum(x => x.Monto),
                    g.Count()))
            : raw.GroupBy(x => new { x.CuentaId, x.CuentaNombre, x.TitularNombre, Banco = x.Banco, x.Divisa })
                .Select(g => new RankingRow(
                    g.Key.CuentaNombre, g.Key.TitularNombre, g.Key.CuentaNombre, g.Key.Banco, g.Key.Divisa,
                    -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                    g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                    g.Sum(x => x.Monto),
                    g.Count()));

        rows = rows.Where(x => x.Gastos != 0m || x.Ingresos != 0m || x.Neto != 0m);

        // B-10: el top-N se aplica DENTRO de cada divisa. Comparar el
        // gasto de una cuenta en USD con el de otra en EUR no tiene
        // sentido. Si el plan fija una sola divisa, el filtro deja un
        // unico grupo y el resultado es el de siempre.
        var top = rows
            .GroupBy(x => x.Divisa)
            .SelectMany(g => OrdenarRanking(g, metrica).Take(plan.Limite))
            .ToList();

        return new FinancialToolResult<IReadOnlyList<RankingRow>>
        {
            Data = top,
            FilasDevueltas = top.Count,
            FilasAnalizadas = raw.Count
        };
    }

    private sealed class RankingProjection
    {
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string TitularNombre { get; set; } = string.Empty;
        public string Banco { get; set; } = string.Empty;
        public string Divisa { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateOnly Fecha { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public Guid TitularId { get; set; }
    }

    // B-10: el ranking ordena dentro de una sola divisa.
    private static IEnumerable<RankingRow> OrdenarRanking(IEnumerable<RankingRow> filas, FinancialMetric metrica) =>
        metrica switch
        {
            FinancialMetric.Gastos => filas.OrderByDescending(x => x.Gastos),
            FinancialMetric.Ingresos => filas.OrderByDescending(x => x.Ingresos),
            _ => filas.OrderByDescending(x => Math.Abs(x.Neto))
        };

    public async Task<FinancialToolResult<IReadOnlyList<RevisionItem>>> GetRevisionItemsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var estados = plan.Filtros.Estados is { Count: > 0 }
            ? plan.Filtros.Estados
            : new[] { "PENDIENTE" };

        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;
        var concepto = plan.Filtros.Concepto?.Trim();
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        IQueryable<RevisionProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            select new RevisionProjection
            {
                ExtractoId = e.Id,
                Concepto = e.Concepto ?? string.Empty,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                Divisa = c.Divisa,
                Fecha = e.Fecha,
                Monto = e.Monto,
                Flagged = e.Flagged,
                Checked = e.Checked,
                FlaggedNota = e.FlaggedNota
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        // A-6: los items de revision son movimientos: el concepto y el
        // rango de importes se aplican igual que en el resto.
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            query = query.Where(x => x.Concepto.ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        var raw = await query
            .OrderByDescending(x => x.Fecha)
            .Take(plan.Limite)
            .ToListAsync(cancellationToken);

        var items = raw
            .Select(x => new RevisionItem(
                x.ExtractoId, x.Concepto, x.CuentaNombre, x.TitularNombre, x.Divisa,
                x.Fecha, x.Monto,
                x.Flagged ? "PENDIENTE" : (x.Checked ? "REVISADO" : "PENDIENTE"),
                x.FlaggedNota))
            .Where(x => estados.Contains(x.Estado))
            .ToList();

        return new FinancialToolResult<IReadOnlyList<RevisionItem>>
        {
            Data = items,
            FilasDevueltas = items.Count,
            FilasAnalizadas = raw.Count
        };
    }

    private sealed class RevisionProjection
    {
        public Guid ExtractoId { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public string CuentaNombre { get; set; } = string.Empty;
        public string TitularNombre { get; set; } = string.Empty;
        public string Divisa { get; set; } = string.Empty;
        public DateOnly Fecha { get; set; }
        public decimal Monto { get; set; }
        public bool Flagged { get; set; }
        public bool Checked { get; set; }
        public string? FlaggedNota { get; set; }
    }

    public async Task<FinancialToolResult<IReadOnlyList<TrendPoint>>> GetExpenseTrendAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var earliest = FechasOperativas.HoyLocal().AddMonths(-AnomalyHistoryMonths);
        var desde = plan.Filtros.Periodo?.From ?? earliest;
        var hasta = plan.Filtros.Periodo?.To ?? FechasOperativas.HoyLocal();
        var divisas = plan.Filtros.Divisas;
        var concepto = plan.Filtros.Concepto?.Trim();
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        IQueryable<TrendProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            where (e.Fecha >= desde) && (e.Fecha <= hasta)
            select new TrendProjection
            {
                Fecha = e.Fecha,
                Divisa = c.Divisa,
                Monto = e.Monto,
                Concepto = e.Concepto ?? string.Empty
            };

        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));
        // A-6: la tendencia tambien acota por concepto y por importe.
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            query = query.Where(x => x.Concepto.ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        var raw = await query.ToListAsync(cancellationToken);

        var rows = raw
            .GroupBy(x => new { x.Fecha.Year, x.Fecha.Month, x.Divisa })
            .Select(g => new TrendPoint(
                g.Key.Year,
                g.Key.Month,
                g.Key.Divisa,
                g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                g.Sum(x => x.Monto),
                g.Count()))
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToList();

        return new FinancialToolResult<IReadOnlyList<TrendPoint>>
        {
            Data = rows,
            FilasDevueltas = rows.Count,
            FilasAnalizadas = rows.Sum(x => x.Movimientos)
        };
    }

    private sealed class TrendProjection
    {
        public DateOnly Fecha { get; set; }
        public string Divisa { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
    }

    public async Task<FinancialToolResult<IReadOnlyList<PendingMovement>>> GetPendingMovementsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;

        // A-6: los movimientos pendientes no son extractos: no se puede
        // filtrar por concepto ni por importe sin inventar el dato. Se
        // rechaza con motivo en vez de devolver una lista sin filtrar.
        if (HayFiltroDeMovimiento(plan.Filtros))
        {
            return new FinancialToolResult<IReadOnlyList<PendingMovement>>
            {
                Data = Array.Empty<PendingMovement>(),
                FilasDevueltas = 0,
                FilasAnalizadas = 0,
                Rechazo = true,
                Advertencia = $"No puedo aplicar el filtro ({DescribirFiltro(plan.Filtros)}) a los movimientos pendientes. Reformula la pregunta sin ese filtro."
            };
        }

        IQueryable<PendingProjection> movimientosEsperados =
            from m in _dbContext.MovimientosEsperados.AsNoTracking()
            join c in cuentas on m.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            where m.Estado == "pendiente"
                  && (desde == null || m.FechaEsperada >= desde)
                  && (hasta == null || m.FechaEsperada <= hasta)
            orderby m.FechaEsperada
            select new PendingProjection
            {
                Id = m.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                Divisa = c.Divisa,
                FechaEsperada = m.FechaEsperada,
                Monto = m.Monto,
                Estado = m.Estado,
                Concepto = m.Concepto,
                ConciliacionEstado = (string?)null
            };

        IQueryable<PendingProjection> conciliaciones =
            from c0 in _dbContext.Conciliaciones.AsNoTracking()
            join c in cuentas on c0.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            join m in _dbContext.MovimientosEsperados.AsNoTracking() on c0.MovimientoEsperadoId equals m.Id
            where (c0.Estado == "sugerida" || c0.Estado == "excepcion")
                  && (desde == null || m.FechaEsperada >= desde)
                  && (hasta == null || m.FechaEsperada <= hasta)
            orderby m.FechaEsperada
            select new PendingProjection
            {
                Id = m.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                Divisa = c.Divisa,
                FechaEsperada = m.FechaEsperada,
                Monto = m.Monto,
                Estado = m.Estado,
                Concepto = m.Concepto,
                ConciliacionEstado = (string?)c0.Estado
            };

        var unificado = await movimientosEsperados
            .Concat(conciliaciones)
            .OrderBy(x => x.FechaEsperada)
            .Take(plan.Limite)
            .ToListAsync(cancellationToken);

        var data = unificado
            .Select(x => new PendingMovement(
                x.Id, x.CuentaId, x.CuentaNombre, x.TitularNombre, x.Divisa,
                x.FechaEsperada, x.Monto, x.Estado, x.Concepto, x.ConciliacionEstado))
            .ToList();

        return new FinancialToolResult<IReadOnlyList<PendingMovement>>
        {
            Data = data,
            FilasDevueltas = data.Count,
            FilasAnalizadas = unificado.Count
        };
    }

    private sealed class PendingProjection
    {
        public Guid Id { get; set; }
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string TitularNombre { get; set; } = string.Empty;
        public string Divisa { get; set; } = string.Empty;
        public DateOnly FechaEsperada { get; set; }
        public decimal Monto { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string? Concepto { get; set; }
        public string? ConciliacionEstado { get; set; }
    }

    public async Task<FinancialToolResult<IReadOnlyList<SearchHit>>> SearchTransactionsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var termino = (plan.TerminoBusqueda ?? plan.Filtros.Concepto ?? string.Empty).Trim();
        if (termino.Length == 0)
        {
            return new FinancialToolResult<IReadOnlyList<SearchHit>>
            {
                Data = Array.Empty<SearchHit>(),
                FilasDevueltas = 0,
                FilasAnalizadas = 0,
                Advertencia = "Search sin termino: el validador deberia haber bloqueado este plan."
            };
        }

        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;
        var cuentaIds = plan.Filtros.CuentaIds;
        var titularIds = plan.Filtros.TitularIds;
        var divisas = plan.Filtros.Divisas;
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        IQueryable<SearchProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            select new SearchProjection
            {
                ExtractoId = e.Id,
                CuentaId = c.Id,
                CuentaNombre = c.Nombre,
                TitularNombre = t.Nombre,
                Divisa = c.Divisa,
                Fecha = e.Fecha,
                Monto = e.Monto,
                Saldo = e.Saldo,
                Concepto = e.Concepto ?? string.Empty,
                TitularId = t.Id
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (cuentaIds is { Count: > 0 }) query = query.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));
        // A-6: los importes son magnitudes: se comparan contra el valor
        // absoluto del movimiento (los gastos se guardan en negativo).
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        // Filtro de texto: se aplica en la propia consulta (case
        // insensitive via ToLower().Contains(), traducible tanto a
        // Postgres como al proveedor InMemory de los tests) ANTES del
        // Take, para que una coincidencia fuera de la ultima "rodaja"
        // de filas no se pierda.
        var terminoNormalizado = termino.ToLowerInvariant();
        query = query.Where(x =>
            x.Concepto.ToLower().Contains(terminoNormalizado)
            || x.CuentaNombre.ToLower().Contains(terminoNormalizado)
            || x.TitularNombre.ToLower().Contains(terminoNormalizado));

        var totalFiltrado = await query.CountAsync(cancellationToken);
        var raw = await query
            .OrderByDescending(x => x.Fecha)
            .Take(plan.Limite)
            .ToListAsync(cancellationToken);

        var data = raw
            .Select(x => new SearchHit(
                x.ExtractoId, x.CuentaId, x.CuentaNombre, x.TitularNombre, x.Divisa,
                x.Fecha, x.Monto, x.Saldo, x.Concepto))
            .ToList();

        return new FinancialToolResult<IReadOnlyList<SearchHit>>
        {
            Data = data,
            FilasDevueltas = data.Count,
            FilasAnalizadas = totalFiltrado
        };
    }

    private sealed class SearchProjection
    {
        public Guid ExtractoId { get; set; }
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string TitularNombre { get; set; } = string.Empty;
        public string Divisa { get; set; } = string.Empty;
        public DateOnly Fecha { get; set; }
        public decimal Monto { get; set; }
        public decimal Saldo { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public Guid TitularId { get; set; }
    }

    public async Task<FinancialToolResult<ComparisonResult>> ComparePeriodsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Comparacion is null)
        {
            return new FinancialToolResult<ComparisonResult>
            {
                Advertencia = "Compare sin comparacion definida. Se esperaba FinancialComparison."
            };
        }

        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var baseSnapshots = await SnapshotAsync(cuentas, plan.Comparacion.Base, "Base", plan.Filtros, cancellationToken);
        var refSnapshots = await SnapshotAsync(cuentas, plan.Comparacion.Referencia, "Referencia", plan.Filtros, cancellationToken);

        // B-10: nunca se suman ni comparan importes de distinta divisa.
        // Si los periodos tocados abarcan mas de una divisa, la
        // comparacion no tiene sentido: se rechaza con motivo en vez de
        // devolver un total mixto.
        var divisas = baseSnapshots.Select(x => x.Divisa)
            .Concat(refSnapshots.Select(x => x.Divisa))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (divisas.Count > 1)
        {
            return new FinancialToolResult<ComparisonResult>
            {
                FilasDevueltas = 0,
                FilasAnalizadas = 0,
                Rechazo = true,
                Advertencia = $"La comparacion abarca varias divisas ({string.Join(", ", divisas.OrderBy(x => x))}). No se pueden comparar importes de distinta divisa: indica una divisa en la pregunta o separa la consulta por divisas."
            };
        }

        var divisa = divisas.Count == 1 ? divisas[0] : string.Empty;
        var baseSnapshot = baseSnapshots.FirstOrDefault() ?? SnapshotVacio("Base", plan.Comparacion.Base, divisa);
        var refSnapshot = refSnapshots.FirstOrDefault() ?? SnapshotVacio("Referencia", plan.Comparacion.Referencia, divisa);

        var variacionIngresos = baseSnapshot.Ingresos - refSnapshot.Ingresos;
        var variacionGastos = baseSnapshot.Gastos - refSnapshot.Gastos;
        var variacionNeto = baseSnapshot.Neto - refSnapshot.Neto;

        var data = new ComparisonResult(
            baseSnapshot,
            refSnapshot,
            variacionIngresos,
            variacionGastos,
            variacionNeto,
            PctDelta(variacionIngresos, refSnapshot.Ingresos),
            PctDelta(variacionGastos, refSnapshot.Gastos),
            PctDelta(variacionNeto, refSnapshot.Neto));

        return new FinancialToolResult<ComparisonResult>
        {
            Data = data,
            FilasDevueltas = 2,
            FilasAnalizadas = baseSnapshot.Movimientos + refSnapshot.Movimientos
        };
    }

    public async Task<FinancialToolResult<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var hoy = FechasOperativas.HoyLocal();
        var inicio = hoy.AddMonths(-AnomalyHistoryMonths);
        // V-02.08: filtros de entidad validados (cuenta/titular/divisa) deben
        // acotar la deteccion igual que el resto de herramientas; sin esto,
        // una solicitud de anomalias en una cuenta especifica devolvia
        // anomalias de todas las cuentas accesibles del pais.
        var cuentaIds = plan.Filtros.CuentaIds;
        var titularIds = plan.Filtros.TitularIds;
        var divisas = plan.Filtros.Divisas;
        var concepto = plan.Filtros.Concepto?.Trim();
        var importeMin = plan.Filtros.ImporteMinimo;
        var importeMax = plan.Filtros.ImporteMaximo;

        var extractosQuery =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            where e.Fecha >= inicio && e.Fecha <= hoy
            select new
            {
                e.Id,
                e.CuentaId,
                Cuenta = c.Nombre,
                CuentaDivisa = c.Divisa,
                TitularId = t.Id,
                Titular = t.Nombre,
                e.Fecha,
                e.Monto,
                e.Concepto
            };
        if (cuentaIds is { Count: > 0 }) extractosQuery = extractosQuery.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) extractosQuery = extractosQuery.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) extractosQuery = extractosQuery.Where(x => divisas.Contains(x.CuentaDivisa));
        // A-6: concepto e importes acotan los movimientos analizados igual
        // que en el resto de herramientas ("contains" insensible a
        // mayusculas; importes contra el valor absoluto).
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            extractosQuery = extractosQuery.Where(x => (x.Concepto ?? string.Empty).ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) extractosQuery = extractosQuery.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) extractosQuery = extractosQuery.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        var extractos = await extractosQuery.ToListAsync(cancellationToken);

        var anomalias = new List<Anomaly>();

        // 1. Duplicado probable: mismo concepto + mismo importe + misma
        // cuenta, y ademas proximos en el tiempo. Un pago recurrente
        // legitimo (la prima de seguro de cada mes) NO es un duplicado:
        // sin la ventana de proximidad, B-10 lo marcaba como tal.
        var duplicados = extractos
            .Where(x => !string.IsNullOrWhiteSpace(x.Concepto))
            .GroupBy(x => new { x.CuentaId, x.Concepto, Importe = x.Monto })
            .Where(g => g.Count() > 1)
            .Where(g => SonProximos(g.Select(x => x.Fecha), AnomalyDuplicateMaxDays))
            .SelectMany(g => g.OrderBy(x => x.Fecha).Take(1))
            .Select(x => new Anomaly(
                "DUPLICADO_PROBABLE",
                "media",
                $"Movimiento repetido en {x.Cuenta}: '{x.Concepto}' por {x.Monto:0.00} aparece mas de una vez en {AnomalyDuplicateMaxDays} dias.",
                x.Id, x.CuentaId, x.Cuenta, x.Titular, x.Fecha, x.Monto, null));

        anomalias.AddRange(duplicados);

        // 2. Importe atipico: 3x la media historica del mismo titular.
        var porTitular = extractos
            .GroupBy(x => x.Titular)
            .Where(g => g.Count() >= 3);

        foreach (var grupo in porTitular)
        {
            var gastos = grupo.Where(x => x.Monto < 0).Select(x => -x.Monto).ToList();
            if (gastos.Count == 0) continue;
            var media = gastos.Average();
            if (media <= 0m) continue;
            var umbral = media * AnomalyHighFactor;
            var atipicos = grupo.Where(x => x.Monto < 0 && (-x.Monto) >= umbral);
            foreach (var a in atipicos)
            {
                anomalias.Add(new Anomaly(
                    "IMPORTE_ATIPICO",
                    "alta",
                    $"Movimiento de {a.Monto:0.00} en {a.Cuenta} supera {AnomalyHighFactor:0}x la media historica ({media:0.00}).",
                    a.Id, a.CuentaId, a.Cuenta, a.Titular, a.Fecha, a.Monto,
                    $"Media historica: {media:0.00}; umbral {AnomalyHighFactor:0}x: {umbral:0.00}"));
            }
        }

        // 3. Caida de saldo: la cuenta muestra una tendencia
        // descendente en los ultimos 3 meses consecutivos. La regla
        // compara el saldo (campo Saldo del extracto) del tercer mes
        // contra el saldo del primer mes: si cae mas de un 25%, se
        // marca. Constante documentada para que los tests la pinzen.
        var saldosRealesQuery =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            where e.Fecha >= inicio && e.Fecha <= hoy
            select new { e.Saldo, e.Fecha, e.CuentaId, Cuenta = c.Nombre, CuentaDivisa = c.Divisa, TitularId = t.Id, Titular = t.Nombre };
        if (cuentaIds is { Count: > 0 }) saldosRealesQuery = saldosRealesQuery.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) saldosRealesQuery = saldosRealesQuery.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) saldosRealesQuery = saldosRealesQuery.Where(x => divisas.Contains(x.CuentaDivisa));

        var saldosReales = await saldosRealesQuery
            .GroupBy(x => new
            {
                x.CuentaId,
                x.Cuenta,
                x.Titular,
                Mes = new DateOnly(x.Fecha.Year, x.Fecha.Month, 1)
            })
            .Select(g => new
            {
                g.Key.CuentaId,
                g.Key.Cuenta,
                g.Key.Titular,
                g.Key.Mes,
                SaldoFinal = g.OrderByDescending(e => e.Fecha).First().Saldo
            }).ToListAsync(cancellationToken);

        var saldoPorMesFiltrado = saldosReales
            .Where(x => x.Mes < new DateOnly(hoy.Year, hoy.Month, 1))
            .OrderBy(x => x.CuentaId)
            .ThenBy(x => x.Mes)
            .ToList();

        var umbralCaidaPct = 25m; // V-02.09: 25% en 3 meses marca caida
        var porCuenta = saldoPorMesFiltrado.GroupBy(x => new { x.CuentaId, x.Cuenta, x.Titular });
        foreach (var cuenta in porCuenta)
        {
            var meses = cuenta.OrderByDescending(x => x.Mes).Take(3).OrderBy(x => x.Mes).ToList();
            if (meses.Count < 3) continue;
            var primero = meses.First().SaldoFinal;
            var ultimo = meses.Last().SaldoFinal;
            if (primero <= 0m) continue;
            var variacion = (ultimo - primero) / Math.Abs(primero) * 100m;
            if (variacion < -umbralCaidaPct)
            {
                anomalias.Add(new Anomaly(
                    "SALDO_EN_CAIDA",
                    "media",
                    $"{cuenta.Key.Cuenta} muestra una caida de {Math.Abs(variacion):0.##}% en los ultimos 3 meses (de {primero:0.00} a {ultimo:0.00}).",
                    null, cuenta.Key.CuentaId, cuenta.Key.Cuenta, cuenta.Key.Titular,
                    meses.Last().Mes, null,
                    $"Saldo inicial 3 meses: {primero:0.00}; saldo final: {ultimo:0.00}; variacion: {variacion:0.##}%"));
            }
        }

        // 4. Gasto nuevo: un concepto de gasto (monto negativo) que
        // aparece en el mes en curso pero que NO aparecio en los 5
        // meses anteriores. Marca posibles suscripciones, cargos
        // recurrentes nuevos o compras inusuales.
        var inicioMesActual = new DateOnly(hoy.Year, hoy.Month, 1);
        var finMesAnterior = inicioMesActual.AddDays(-1);
        var inicioHist = finMesAnterior.AddMonths(-4);

        var gastosRecientes = extractos
            .Where(x => x.Fecha >= inicioMesActual && x.Fecha <= hoy && x.Monto < 0m && !string.IsNullOrWhiteSpace(x.Concepto))
            .GroupBy(x => new { x.CuentaId, x.Cuenta, x.Titular, Concepto = x.Concepto!.Trim() })
            .Select(g => new { g.Key.CuentaId, g.Key.Cuenta, g.Key.Titular, g.Key.Concepto, Importe = g.Min(x => x.Monto) })
            .ToList();

        var conceptosHistoricos = extractos
            .Where(x => x.Fecha >= inicioHist && x.Fecha <= finMesAnterior && x.Monto < 0m && !string.IsNullOrWhiteSpace(x.Concepto))
            .Select(x => new { x.CuentaId, Concepto = x.Concepto!.Trim() })
            .Distinct()
            .ToList();

        var setHistorico = conceptosHistoricos
            .Select(c => (c.CuentaId, c.Concepto.ToLowerInvariant()))
            .ToHashSet();

        foreach (var nuevo in gastosRecientes)
        {
            if (nuevo.Concepto.Length < 3) continue;
            if (setHistorico.Contains((nuevo.CuentaId, nuevo.Concepto.ToLowerInvariant()))) continue;
            anomalias.Add(new Anomaly(
                "GASTO_NUEVO",
                "baja",
                $"'{nuevo.Concepto}' aparece como gasto en {nuevo.Cuenta} por primera vez en los ultimos 6 meses.",
                null, nuevo.CuentaId, nuevo.Cuenta, nuevo.Titular,
                hoy, nuevo.Importe,
                $"Concepto no visto en {inicioHist:dd/MM/yyyy} - {finMesAnterior:dd/MM/yyyy}."));
        }

        return new FinancialToolResult<IReadOnlyList<Anomaly>>
        {
            Data = anomalias.Take(plan.Limite).ToList(),
            FilasDevueltas = anomalias.Count,
            FilasAnalizadas = extractos.Count
        };
    }

    // B-10: un snapshot por DIVISA. Antes agrupaba por 1 y sumaba EUR y
    // USD en el mismo total (y comparaba ese total mixto entre periodos).
    private async Task<IReadOnlyList<ComparisonSnapshot>> SnapshotAsync(
        IQueryable<Models.Cuenta> cuentas,
        FinancialPeriod periodo,
        string etiqueta,
        FinancialFilters filtros,
        CancellationToken cancellationToken)
    {
        if (periodo.From is null || periodo.To is null)
        {
            return Array.Empty<ComparisonSnapshot>();
        }

        var concepto = filtros.Concepto?.Trim();
        var importeMin = filtros.ImporteMinimo;
        var importeMax = filtros.ImporteMaximo;
        var divisas = filtros.Divisas;

        IQueryable<ComparisonProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            where e.Fecha >= periodo.From && e.Fecha <= periodo.To
            select new ComparisonProjection
            {
                Divisa = c.Divisa,
                Monto = e.Monto,
                Concepto = e.Concepto ?? string.Empty
            };

        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));
        // A-6: la comparacion tambien respeta el concepto y el rango de
        // importes del plan.
        if (concepto is { Length: > 0 })
        {
            var terminoNormalizado = concepto.ToLowerInvariant();
            query = query.Where(x => x.Concepto.ToLower().Contains(terminoNormalizado));
        }
        if (importeMin.HasValue) query = query.Where(x => Math.Abs(x.Monto) >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => Math.Abs(x.Monto) <= importeMax.Value);

        var rows = await query.ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.Divisa)
            .Select(g => new ComparisonSnapshot(
                $"{etiqueta} ({g.Key})",
                periodo.From.Value,
                periodo.To.Value,
                g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                g.Sum(x => x.Monto),
                g.Count(),
                g.Key))
            .ToList();
    }

    private sealed class ComparisonProjection
    {
        public string Divisa { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
    }

    // Snapshot sin movimientos: conserva el periodo y la divisa para que
    // la comparacion siga siendo legible.
    private static ComparisonSnapshot SnapshotVacio(string etiqueta, FinancialPeriod periodo, string divisa) =>
        new(etiqueta, periodo.From ?? default, periodo.To ?? default, 0, 0, 0, 0, divisa);

    private static decimal PctDelta(decimal variacion, decimal baseValor)
    {
        if (baseValor == 0m) return variacion == 0m ? 0m : 100m * Math.Sign(variacion);
        return Math.Round((variacion / Math.Abs(baseValor)) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static FinancialDirection ResolveDirection(FinancialMetric metrica, FinancialDirection fallback)
    {
        return metrica switch
        {
            FinancialMetric.Gastos => FinancialDirection.Gasto,
            FinancialMetric.Ingresos => FinancialDirection.Ingreso,
            _ => fallback
        };
    }

    // A-6: el plan pide un filtro de movimiento (concepto o rango de
    // importes). Una herramienta que no opera sobre movimientos no lo
    // puede aplicar.
    private static bool HayFiltroDeMovimiento(FinancialFilters filtros) =>
        !string.IsNullOrWhiteSpace(filtros.Concepto)
        || filtros.ImporteMinimo.HasValue
        || filtros.ImporteMaximo.HasValue;

    // A-6: descripcion del filtro rechazado para que el motivo que ve el
    // usuario sea concreto.
    private static string DescribirFiltro(FinancialFilters filtros)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(filtros.Concepto))
        {
            partes.Add($"concepto '{filtros.Concepto.Trim()}'");
        }
        if (filtros.ImporteMinimo.HasValue)
        {
            partes.Add($"importe minimo {filtros.ImporteMinimo.Value:0.##}");
        }
        if (filtros.ImporteMaximo.HasValue)
        {
            partes.Add($"importe maximo {filtros.ImporteMaximo.Value:0.##}");
        }
        return string.Join(", ", partes);
    }

    // B-10: hay dos elementos proximos si la separacion entre alguna pareja
    // consecutiva es de maxDias dias o menos. Se cuentan elementos, no
    // fechas distintas: dos movimientos en la misma fecha (diferencia 0)
    // son proximos.
    private static bool SonProximos(IEnumerable<DateOnly> fechas, int maxDias)
    {
        var ordenadas = fechas.OrderBy(x => x).ToList();
        for (int i = 1; i < ordenadas.Count; i++)
        {
            if (ordenadas[i].DayNumber - ordenadas[i - 1].DayNumber <= maxDias)
            {
                return true;
            }
        }
        return false;
    }
}
