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
    int Movimientos);

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
        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;
        var cuentaIds = plan.Filtros.CuentaIds;
        var titularIds = plan.Filtros.TitularIds;
        var divisas = plan.Filtros.Divisas;

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
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Any(id => x.Titular != null && x.Titular.Length > 0));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));

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
                TitularId = t.Id
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (cuentaIds is { Count: > 0 }) query = query.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));

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
                .GroupBy(x => new { x.TitularNombre, x.Divisa })
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
        var earliest = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddYears(-3);
        var desde = plan.Filtros.Periodo?.From ?? earliest;
        var hasta = plan.Filtros.Periodo?.To ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var cuentaIds = plan.Filtros.CuentaIds;
        var divisas = plan.Filtros.Divisas;

        var latestKeys =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            where (e.Fecha >= desde) && (e.Fecha <= hasta)
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
                TitularId = t.Id
            };

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (cuentaIds is { Count: > 0 }) query = query.Where(x => cuentaIds.Contains(x.CuentaId));
        if (titularIds is { Count: > 0 }) query = query.Where(x => titularIds.Contains(x.TitularId));
        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));

        var raw = await query.ToListAsync(cancellationToken);

        IEnumerable<RankingRow> rows = grupo is FinancialGroupBy.Titular
            ? raw.GroupBy(x => new { x.TitularNombre, x.Divisa })
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

        var ordered = metrica switch
        {
            FinancialMetric.Gastos => rows.OrderByDescending(x => x.Gastos),
            FinancialMetric.Ingresos => rows.OrderByDescending(x => x.Ingresos),
            _ => rows.OrderByDescending(x => Math.Abs(x.Neto))
        };

        var top = ordered.Take(plan.Limite).ToList();

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
        public Guid TitularId { get; set; }
    }

    public async Task<FinancialToolResult<IReadOnlyList<RevisionItem>>> GetRevisionItemsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var estados = plan.Filtros.Estados is { Count: > 0 }
            ? plan.Filtros.Estados
            : new[] { "PENDIENTE" };

        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;

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
        var earliest = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(-AnomalyHistoryMonths);
        var desde = plan.Filtros.Periodo?.From ?? earliest;
        var hasta = plan.Filtros.Periodo?.To ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var divisas = plan.Filtros.Divisas;

        IQueryable<TrendProjection> query =
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            where (e.Fecha >= desde) && (e.Fecha <= hasta)
            select new TrendProjection
            {
                Fecha = e.Fecha,
                Divisa = c.Divisa,
                Monto = e.Monto
            };

        if (divisas is { Count: > 0 }) query = query.Where(x => divisas.Contains(x.Divisa));

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
    }

    public async Task<FinancialToolResult<IReadOnlyList<PendingMovement>>> GetPendingMovementsAsync(
        UserAccessScope scope, FinancialQueryPlan plan, CancellationToken cancellationToken)
    {
        var cuentas = CuentasScope(scope, plan.Filtros.PaisIds?.FirstOrDefault());
        var desde = plan.Filtros.Periodo?.From;
        var hasta = plan.Filtros.Periodo?.To;

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
        var like = $"%{termino}%";

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
        if (importeMin.HasValue) query = query.Where(x => x.Monto >= importeMin.Value);
        if (importeMax.HasValue) query = query.Where(x => x.Monto <= importeMax.Value);

        // Filtro de texto: para Postgres usamos ILike (case
        // insensitive). Para InMemory, comparamos con Contains.
        // Materializamos los resultados sin texto y aplicamos el
        // filtro en memoria. El InMemory no implementa ILike.
        var totalSinTexto = await query.CountAsync(cancellationToken);
        var raw = await query
            .OrderByDescending(x => x.Fecha)
            .Take(plan.Limite * 4) // pedimos algo mas para compensar el filtro
            .ToListAsync(cancellationToken);

        var filtrado = raw
            .Where(x => CoincideTermino(x.Concepto, termino)
                        || CoincideTermino(x.CuentaNombre, termino)
                        || CoincideTermino(x.TitularNombre, termino))
            .Take(plan.Limite)
            .ToList();

        var data = filtrado
            .Select(x => new SearchHit(
                x.ExtractoId, x.CuentaId, x.CuentaNombre, x.TitularNombre, x.Divisa,
                x.Fecha, x.Monto, x.Saldo, x.Concepto))
            .ToList();

        return new FinancialToolResult<IReadOnlyList<SearchHit>>
        {
            Data = data,
            FilasDevueltas = data.Count,
            FilasAnalizadas = totalSinTexto
        };
    }

    private static bool CoincideTermino(string? texto, string termino)
    {
        if (string.IsNullOrEmpty(texto)) return false;
        return texto.Contains(termino, StringComparison.OrdinalIgnoreCase);
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
        var baseSnapshot = await SnapshotAsync(cuentas, plan.Comparacion.Base, "Base", cancellationToken);
        var refSnapshot = await SnapshotAsync(cuentas, plan.Comparacion.Referencia, "Referencia", cancellationToken);

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
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var inicio = hoy.AddMonths(-AnomalyHistoryMonths);

        var extractos = await (
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            where e.Fecha >= inicio && e.Fecha <= hoy
            select new
            {
                e.Id,
                e.CuentaId,
                Cuenta = c.Nombre,
                Titular = t.Nombre,
                e.Fecha,
                e.Monto,
                e.Concepto
            }).ToListAsync(cancellationToken);

        var anomalias = new List<Anomaly>();

        // 1. Duplicado probable: mismo concepto + mismo importe + misma
        // cuenta, dentro de la ventana de historico.
        var duplicados = extractos
            .Where(x => !string.IsNullOrWhiteSpace(x.Concepto))
            .GroupBy(x => new { x.CuentaId, x.Concepto, Importe = x.Monto })
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(x => x.Fecha).Take(1))
            .Select(x => new Anomaly(
                "DUPLICADO_PROBABLE",
                "media",
                $"Movimiento repetido en {x.Cuenta}: '{x.Concepto}' por {x.Monto:0.00} aparece mas de una vez en los ultimos {AnomalyHistoryMonths} meses.",
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
        var saldosReales = await (
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            where e.Fecha >= inicio && e.Fecha <= hoy
            group new { e.Saldo, e.Fecha, e.CuentaId } by new
            {
                e.CuentaId,
                Cuenta = c.Nombre,
                Titular = t.Nombre,
                Mes = new DateOnly(e.Fecha.Year, e.Fecha.Month, 1)
            }
            into g
            select new
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

    private async Task<ComparisonSnapshot> SnapshotAsync(
        IQueryable<Models.Cuenta> cuentas,
        FinancialPeriod periodo,
        string etiqueta,
        CancellationToken cancellationToken)
    {
        if (periodo.From is null || periodo.To is null)
        {
            return new ComparisonSnapshot(etiqueta, default, default, 0, 0, 0, 0);
        }
        var rows = await (
            from e in _dbContext.Extractos.AsNoTracking()
            join c in cuentas on e.CuentaId equals c.Id
            where e.Fecha >= periodo.From && e.Fecha <= periodo.To
            group e by 1
            into g
            select new
            {
                Ingresos = g.Where(x => x.Monto > 0).Sum(x => x.Monto),
                Gastos = -g.Where(x => x.Monto < 0).Sum(x => x.Monto),
                Neto = g.Sum(x => x.Monto),
                Movimientos = g.Count()
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new ComparisonSnapshot(etiqueta, periodo.From.Value, periodo.To.Value, 0, 0, 0, 0);
        }

        var r = rows[0];
        return new ComparisonSnapshot(etiqueta, periodo.From.Value, periodo.To.Value,
            r.Ingresos, r.Gastos, r.Neto, r.Movimientos);
    }

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
}
