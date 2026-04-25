using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

public interface IDashboardService
{
    Task<DashboardPrincipalResponse> GetPrincipalAsync(Guid userId, string? divisaPrincipal, CancellationToken cancellationToken);
    Task<DashboardTitularResponse> GetTitularAsync(Guid userId, Guid titularId, string? divisaPrincipal, CancellationToken cancellationToken);
    Task<DashboardSaldosDivisaResponse> GetSaldosDivisaAsync(Guid userId, string? divisaPrincipal, Guid? titularId, CancellationToken cancellationToken);
    Task<DashboardEvolucionResponse> GetEvolucionAsync(Guid userId, string periodo, string? divisaPrincipal, Guid? titularId, CancellationToken cancellationToken);
}

public sealed class DashboardService : IDashboardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITiposCambioService _tiposCambioService;

    public DashboardService(AppDbContext dbContext, ITiposCambioService tiposCambioService)
    {
        _dbContext = dbContext;
        _tiposCambioService = tiposCambioService;
    }

    public async Task<DashboardPrincipalResponse> GetPrincipalAsync(Guid userId, string? divisaPrincipal, CancellationToken cancellationToken)
    {
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);
        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var chartColors = await ResolveChartColorsAsync(cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, null, cancellationToken);
        var metrics = await BuildMetricsAsync(cuentas, targetCurrency, cancellationToken);
        var plazosFijos = await BuildPlazosFijosResumenAsync(cuentas, metrics, targetCurrency, cancellationToken);

        var titulares = cuentas
            .GroupBy(x => new { x.TitularId, x.TitularNombre })
            .Select(group =>
            {
                var saldosPorDivisa = group
                    .GroupBy(x => x.Divisa)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Sum(c => metrics.SaldoByCuentaId.TryGetValue(c.CuentaId, out var saldo) ? saldo : 0m));

                var totalConvertido = group.Sum(c => metrics.SaldoConvertidoByCuentaId.TryGetValue(c.CuentaId, out var saldo) ? saldo : 0m);
                var inmovilizadoConvertido = group
                    .Where(c => c.TipoCuenta == TipoCuenta.PLAZO_FIJO)
                    .Sum(c => metrics.SaldoConvertidoByCuentaId.TryGetValue(c.CuentaId, out var saldo) ? saldo : 0m);
                var disponibleConvertido = totalConvertido - inmovilizadoConvertido;

                return new DashboardSaldoTitularResponse
                {
                    TitularId = group.Key.TitularId,
                    TitularNombre = group.Key.TitularNombre,
                    TipoTitular = group.First().TipoTitular.ToString(),
                    SaldosPorDivisa = saldosPorDivisa,
                    TotalConvertido = Decimal.Round(totalConvertido, 2),
                    SaldoInmovilizadoConvertido = Decimal.Round(inmovilizadoConvertido, 2),
                    SaldoDisponibleConvertido = Decimal.Round(disponibleConvertido, 2)
                };
            })
            .OrderBy(x => GetTipoTitularOrder(x.TipoTitular))
            .ThenByDescending(x => x.TotalConvertido)
            .ToList();

        return new DashboardPrincipalResponse
        {
            DivisaPrincipal = targetCurrency,
            SaldosPorDivisa = metrics.SaldosPorDivisa
                .ToDictionary(x => x.Key, x => Decimal.Round(x.Value, 2)),
            IngresosMes = Decimal.Round(metrics.IngresosMes, 2),
            EgresosMes = Decimal.Round(metrics.EgresosMes, 2),
            TotalConvertido = Decimal.Round(metrics.TotalConvertido, 2),
            PlazosFijos = plazosFijos,
            SaldosPorTitular = titulares,
            ChartColors = chartColors
        };
    }

    public async Task<DashboardTitularResponse> GetTitularAsync(Guid userId, Guid titularId, string? divisaPrincipal, CancellationToken cancellationToken)
    {
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);
        var canAccessTitular = await CanAccessTitularAsync(scope, titularId, cancellationToken);
        if (!canAccessTitular)
        {
            throw new DashboardAccessException("No tienes permisos para ver este titular", StatusCodes.Status403Forbidden);
        }

        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var chartColors = await ResolveChartColorsAsync(cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, titularId, cancellationToken);

        var titularNombre = cuentas.FirstOrDefault()?.TitularNombre
            ?? await _dbContext.Titulares
                .Where(x => x.Id == titularId)
                .Select(x => x.Nombre)
                .FirstOrDefaultAsync(cancellationToken)
            ?? "Titular";

        var metrics = await BuildMetricsAsync(cuentas, targetCurrency, cancellationToken);

        var saldosPorCuenta = cuentas
            .Select(c =>
            {
                var saldo = metrics.SaldoByCuentaId.TryGetValue(c.CuentaId, out var saldoActual) ? saldoActual : 0m;
                var saldoConvertido = metrics.SaldoConvertidoByCuentaId.TryGetValue(c.CuentaId, out var converted) ? converted : 0m;

                return new DashboardSaldoCuentaResponse
                {
                    CuentaId = c.CuentaId,
                    CuentaNombre = c.CuentaNombre,
                    BancoNombre = c.BancoNombre,
                    Divisa = c.Divisa,
                    EsEfectivo = c.EsEfectivo,
                    TipoCuenta = c.TipoCuenta.ToString(),
                    SaldoActual = Decimal.Round(saldo, 2),
                    SaldoConvertido = Decimal.Round(saldoConvertido, 2)
                };
            })
            .OrderByDescending(x => x.SaldoConvertido)
            .ToList();

        return new DashboardTitularResponse
        {
            TitularId = titularId,
            TitularNombre = titularNombre,
            DivisaPrincipal = targetCurrency,
            SaldosPorDivisa = metrics.SaldosPorDivisa
                .ToDictionary(x => x.Key, x => Decimal.Round(x.Value, 2)),
            IngresosMes = Decimal.Round(metrics.IngresosMes, 2),
            EgresosMes = Decimal.Round(metrics.EgresosMes, 2),
            TotalConvertido = Decimal.Round(metrics.TotalConvertido, 2),
            SaldosPorCuenta = saldosPorCuenta,
            ChartColors = chartColors
        };
    }

    public async Task<DashboardSaldosDivisaResponse> GetSaldosDivisaAsync(Guid userId, string? divisaPrincipal, Guid? titularId, CancellationToken cancellationToken)
    {
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);
        if (titularId.HasValue && !await CanAccessTitularAsync(scope, titularId.Value, cancellationToken))
        {
            throw new DashboardAccessException("No tienes permisos para ver este titular", StatusCodes.Status403Forbidden);
        }

        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, titularId, cancellationToken);
        var metrics = await BuildMetricsAsync(cuentas, targetCurrency, cancellationToken);

        var items = new List<DashboardSaldoDivisaResponse>();

        foreach (var entry in metrics.SaldosPorDivisa.OrderBy(x => x.Key))
        {
            var converted = await _tiposCambioService.ConvertAsync(entry.Value, entry.Key, targetCurrency, cancellationToken);
            var disponible = metrics.SaldosDisponiblesPorDivisa.GetValueOrDefault(entry.Key, 0m);
            var inmovilizado = metrics.SaldosInmovilizadosPorDivisa.GetValueOrDefault(entry.Key, 0m);
            items.Add(new DashboardSaldoDivisaResponse
            {
                Divisa = entry.Key,
                Saldo = Decimal.Round(entry.Value, 2),
                SaldoConvertido = Decimal.Round(converted, 2),
                SaldoDisponible = Decimal.Round(disponible, 2),
                SaldoInmovilizado = Decimal.Round(inmovilizado, 2),
                SaldoTotal = Decimal.Round(entry.Value, 2),
                SaldoTotalConvertido = Decimal.Round(converted, 2)
            });
        }

        return new DashboardSaldosDivisaResponse
        {
            DivisaPrincipal = targetCurrency,
            Divisas = items,
            TotalConvertido = Decimal.Round(metrics.TotalConvertido, 2)
        };
    }

    public async Task<DashboardEvolucionResponse> GetEvolucionAsync(Guid userId, string periodo, string? divisaPrincipal, Guid? titularId, CancellationToken cancellationToken)
    {
        var normalizedPeriodo = NormalizePeriodo(periodo);
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);

        if (titularId.HasValue && !await CanAccessTitularAsync(scope, titularId.Value, cancellationToken))
        {
            throw new DashboardAccessException("No tienes permisos para ver este titular", StatusCodes.Status403Forbidden);
        }

        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, titularId, cancellationToken);
        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();

        if (cuentaIds.Count == 0)
        {
            return new DashboardEvolucionResponse
            {
                Periodo = normalizedPeriodo,
                Granularidad = normalizedPeriodo == "1m" ? "diaria" : "semanal",
                DivisaPrincipal = targetCurrency,
                Puntos = []
            };
        }

        var now = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = GetPeriodStart(normalizedPeriodo, now);
        var buckets = BuildBuckets(start, now, normalizedPeriodo == "1m");

        var accountCurrency = cuentas.ToDictionary(x => x.CuentaId, x => x.Divisa);

        var baselineRows = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId) && x.Fecha < start)
            .GroupBy(x => x.CuentaId)
            .Select(g => g
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FilaNumero)
                .Select(x => new { x.CuentaId, x.Saldo })
                .First())
            .ToListAsync(cancellationToken);

        var currentSaldo = baselineRows.ToDictionary(x => x.CuentaId, x => x.Saldo);
        foreach (var id in cuentaIds)
        {
            if (!currentSaldo.ContainsKey(id))
            {
                currentSaldo[id] = 0m;
            }
        }

        var extracts = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId) && x.Fecha >= start && x.Fecha <= now)
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.FilaNumero)
            .Select(x => new EvolucionExtractRow
            {
                CuentaId = x.CuentaId,
                Fecha = x.Fecha,
                Monto = x.Monto,
                Saldo = x.Saldo
            })
            .ToListAsync(cancellationToken);

        var points = new List<DashboardPuntoEvolucionResponse>(buckets.Count);
        var index = 0;

        foreach (var bucket in buckets)
        {
            decimal ingresos = 0m;
            decimal egresos = 0m;

            while (index < extracts.Count && extracts[index].Fecha <= bucket.End)
            {
                var item = extracts[index];
                currentSaldo[item.CuentaId] = item.Saldo;

                if (accountCurrency.TryGetValue(item.CuentaId, out var divisa))
                {
                    var converted = await _tiposCambioService.ConvertAsync(item.Monto, divisa, targetCurrency, cancellationToken);
                    if (converted >= 0m)
                    {
                        ingresos += converted;
                    }
                    else
                    {
                        egresos += Math.Abs(converted);
                    }
                }

                index++;
            }

            decimal saldoTotal = 0m;
            foreach (var saldoEntry in currentSaldo)
            {
                if (!accountCurrency.TryGetValue(saldoEntry.Key, out var divisa))
                {
                    continue;
                }

                saldoTotal += await _tiposCambioService.ConvertAsync(saldoEntry.Value, divisa, targetCurrency, cancellationToken);
            }

            points.Add(new DashboardPuntoEvolucionResponse
            {
                Fecha = bucket.End,
                Ingresos = Decimal.Round(ingresos, 2),
                Egresos = Decimal.Round(egresos, 2),
                Neto = Decimal.Round(ingresos - egresos, 2),
                Saldo = Decimal.Round(saldoTotal, 2)
            });
        }

        return new DashboardEvolucionResponse
        {
            Periodo = normalizedPeriodo,
            Granularidad = normalizedPeriodo == "1m" ? "diaria" : "semanal",
            DivisaPrincipal = targetCurrency,
            Puntos = points
        };
    }

    private async Task<DashboardMetrics> BuildMetricsAsync(IReadOnlyList<CuentaScopeItem> cuentas, string targetCurrency, CancellationToken cancellationToken)
    {
        if (cuentas.Count == 0)
        {
            return new DashboardMetrics();
        }

        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var divisaByCuenta = cuentas.ToDictionary(x => x.CuentaId, x => x.Divisa);

        var latestRows = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId))
            .GroupBy(x => x.CuentaId)
            .Select(g => g
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FilaNumero)
                .Select(x => new { x.CuentaId, x.Saldo })
                .First())
            .ToListAsync(cancellationToken);

        var saldoByCuenta = latestRows.ToDictionary(x => x.CuentaId, x => x.Saldo);
        var saldoConvertidoByCuenta = new Dictionary<Guid, decimal>();
        var saldosPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var saldosDisponiblesPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var saldosInmovilizadosPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var cuenta in cuentas)
        {
            if (!saldoByCuenta.TryGetValue(cuenta.CuentaId, out var saldo))
            {
                saldo = 0m;
            }

            if (!saldosPorDivisa.ContainsKey(cuenta.Divisa))
            {
                saldosPorDivisa[cuenta.Divisa] = 0m;
            }

            saldosPorDivisa[cuenta.Divisa] += saldo;
            if (cuenta.TipoCuenta == TipoCuenta.PLAZO_FIJO)
            {
                saldosInmovilizadosPorDivisa[cuenta.Divisa] = saldosInmovilizadosPorDivisa.GetValueOrDefault(cuenta.Divisa, 0m) + saldo;
            }
            else
            {
                saldosDisponiblesPorDivisa[cuenta.Divisa] = saldosDisponiblesPorDivisa.GetValueOrDefault(cuenta.Divisa, 0m) + saldo;
            }

            var converted = await _tiposCambioService.ConvertAsync(saldo, cuenta.Divisa, targetCurrency, cancellationToken);
            saldoConvertidoByCuenta[cuenta.CuentaId] = converted;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var periodStart = today.AddMonths(-1);

        var monthRows = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId) && x.Fecha >= periodStart && x.Fecha <= today)
            .Select(x => new { x.CuentaId, x.Monto })
            .ToListAsync(cancellationToken);

        decimal ingresosMes = 0m;
        decimal egresosMes = 0m;

        foreach (var row in monthRows)
        {
            if (!divisaByCuenta.TryGetValue(row.CuentaId, out var divisa))
            {
                continue;
            }

            var converted = await _tiposCambioService.ConvertAsync(row.Monto, divisa, targetCurrency, cancellationToken);
            if (converted >= 0m)
            {
                ingresosMes += converted;
            }
            else
            {
                egresosMes += Math.Abs(converted);
            }
        }

        var totalConvertido = saldoConvertidoByCuenta.Values.Sum();

        return new DashboardMetrics
        {
            SaldosPorDivisa = saldosPorDivisa,
            SaldosDisponiblesPorDivisa = saldosDisponiblesPorDivisa,
            SaldosInmovilizadosPorDivisa = saldosInmovilizadosPorDivisa,
            SaldoByCuentaId = saldoByCuenta,
            SaldoConvertidoByCuentaId = saldoConvertidoByCuenta,
            IngresosMes = ingresosMes,
            EgresosMes = egresosMes,
            TotalConvertido = totalConvertido
        };
    }

    private async Task<DashboardPlazosFijosResumenResponse> BuildPlazosFijosResumenAsync(
        IReadOnlyList<CuentaScopeItem> cuentas,
        DashboardMetrics metrics,
        string targetCurrency,
        CancellationToken cancellationToken)
    {
        var plazoCuentaIds = cuentas
            .Where(c => c.TipoCuenta == TipoCuenta.PLAZO_FIJO)
            .Select(c => c.CuentaId)
            .ToHashSet();

        if (plazoCuentaIds.Count == 0)
        {
            return new DashboardPlazosFijosResumenResponse();
        }

        var cuentaDivisas = cuentas.ToDictionary(c => c.CuentaId, c => c.Divisa);
        var plazos = await _dbContext.PlazosFijos
            .AsNoTracking()
            .Where(p => plazoCuentaIds.Contains(p.CuentaId) && p.Estado != EstadoPlazoFijo.CANCELADO && p.Estado != EstadoPlazoFijo.RENOVADO)
            .Select(p => new
            {
                p.CuentaId,
                p.FechaVencimiento,
                p.InteresPrevisto
            })
            .ToListAsync(cancellationToken);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        decimal interesesConvertidos = 0m;
        foreach (var plazo in plazos)
        {
            if (!plazo.InteresPrevisto.HasValue || !cuentaDivisas.TryGetValue(plazo.CuentaId, out var divisa))
            {
                continue;
            }

            interesesConvertidos += await _tiposCambioService.ConvertAsync(plazo.InteresPrevisto.Value, divisa, targetCurrency, cancellationToken);
        }

        var proximo = plazos
            .Where(p => p.FechaVencimiento >= hoy)
            .OrderBy(p => p.FechaVencimiento)
            .FirstOrDefault();

        var montoTotal = plazoCuentaIds.Sum(id => metrics.SaldoConvertidoByCuentaId.GetValueOrDefault(id, 0m));

        return new DashboardPlazosFijosResumenResponse
        {
            MontoTotalConvertido = Decimal.Round(montoTotal, 2),
            InteresesPrevistosConvertidos = Decimal.Round(interesesConvertidos, 2),
            ProximoVencimiento = proximo?.FechaVencimiento,
            DiasHastaProximoVencimiento = proximo is null ? null : proximo.FechaVencimiento.DayNumber - hoy.DayNumber,
            TotalCuentas = plazoCuentaIds.Count
        };
    }

    private async Task<IReadOnlyList<CuentaScopeItem>> GetScopedCuentasAsync(DashboardScope scope, Guid? titularId, CancellationToken cancellationToken)
    {
        var query = from cuenta in _dbContext.Cuentas.AsNoTracking()
                    join titular in _dbContext.Titulares.AsNoTracking() on cuenta.TitularId equals titular.Id
                    select new CuentaScopeItem
                    {
                        CuentaId = cuenta.Id,
                        CuentaNombre = cuenta.Nombre,
                        BancoNombre = cuenta.BancoNombre,
                        TitularId = titular.Id,
                        TitularNombre = titular.Nombre,
                        Divisa = cuenta.Divisa,
                        EsEfectivo = cuenta.EsEfectivo,
                        TipoCuenta = cuenta.TipoCuenta == TipoCuenta.NORMAL && cuenta.EsEfectivo
                            ? TipoCuenta.EFECTIVO
                            : cuenta.TipoCuenta,
                        TipoTitular = titular.Tipo
                    };

        if (titularId.HasValue)
        {
            query = query.Where(x => x.TitularId == titularId.Value);
        }

        if (!scope.GlobalAccess)
        {
            query = query.Where(x => scope.ExplicitTitularIds.Contains(x.TitularId) || scope.CuentaIds.Contains(x.CuentaId));
        }

        return await query.OrderBy(x => x.TitularNombre).ThenBy(x => x.CuentaNombre).ToListAsync(cancellationToken);
    }

    private async Task<string> ResolveDivisaPrincipalAsync(string? requestedDivisa, CancellationToken cancellationToken)
    {
        var requested = NormalizeDivisa(requestedDivisa);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exists = await _dbContext.DivisasActivas
                .AsNoTracking()
                .AnyAsync(x => x.Codigo == requested && x.Activa, cancellationToken);

            if (exists)
            {
                return requested;
            }
        }

        var activeBase = await _dbContext.DivisasActivas
            .AsNoTracking()
            .Where(x => x.Activa && x.EsBase)
            .OrderBy(x => x.Codigo)
            .Select(x => x.Codigo)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeBase))
        {
            return activeBase;
        }

        var configValue = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x => x.Clave == "divisa_principal_default")
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        var fallback = NormalizeDivisa(configValue) ?? "EUR";
        var fallbackExists = await _dbContext.DivisasActivas
            .AsNoTracking()
            .AnyAsync(x => x.Codigo == fallback && x.Activa, cancellationToken);

        return fallbackExists ? fallback : "EUR";
    }

    private async Task<DashboardChartColorsResponse> ResolveChartColorsAsync(CancellationToken cancellationToken)
    {
        var values = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x =>
                x.Clave == "dashboard_color_ingresos" ||
                x.Clave == "dashboard_color_egresos" ||
                x.Clave == "dashboard_color_saldo")
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, cancellationToken);

        return new DashboardChartColorsResponse
        {
            Ingresos = values.GetValueOrDefault("dashboard_color_ingresos", "#43B430"),
            Egresos = values.GetValueOrDefault("dashboard_color_egresos", "#FF4757"),
            Saldo = values.GetValueOrDefault("dashboard_color_saldo", "#7B7B7B")
        };
    }

    private async Task<DashboardScope> GetAuthorizedScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId && x.Activo, cancellationToken);

        if (usuario is null)
        {
            throw new DashboardAccessException("Usuario no autorizado", StatusCodes.Status401Unauthorized);
        }

        if (usuario.Rol == RolUsuario.ADMIN)
        {
            return DashboardScope.GlobalForAdmin();
        }

        if (usuario.Rol != RolUsuario.GERENTE)
        {
            throw new DashboardAccessException("No tienes permisos para ver dashboards", StatusCodes.Status403Forbidden);
        }

        var permisos = await _dbContext.PermisosUsuario
            .AsNoTracking()
            .Where(x => x.UsuarioId == userId && x.PuedeVerDashboard)
            .Select(x => new { x.CuentaId, x.TitularId })
            .ToListAsync(cancellationToken);

        if (permisos.Count == 0)
        {
            throw new DashboardAccessException("No tienes permisos para ver dashboards", StatusCodes.Status403Forbidden);
        }

        var globalAccess = permisos.Any(x => x.CuentaId == null && x.TitularId == null);
        if (globalAccess)
        {
            return DashboardScope.GlobalForManager();
        }

        var titularIds = permisos.Where(x => x.TitularId.HasValue).Select(x => x.TitularId!.Value).ToHashSet();
        var cuentaIds = permisos.Where(x => x.CuentaId.HasValue).Select(x => x.CuentaId!.Value).ToHashSet();

        if (titularIds.Count == 0 && cuentaIds.Count == 0)
        {
            throw new DashboardAccessException("No tienes permisos para ver dashboards", StatusCodes.Status403Forbidden);
        }

        return new DashboardScope(false, titularIds, cuentaIds);
    }

    private async Task<bool> CanAccessTitularAsync(DashboardScope scope, Guid titularId, CancellationToken cancellationToken)
    {
        if (scope.GlobalAccess)
        {
            return true;
        }

        if (scope.ExplicitTitularIds.Contains(titularId))
        {
            return true;
        }

        if (scope.CuentaIds.Count == 0)
        {
            return false;
        }

        return await _dbContext.Cuentas
            .AsNoTracking()
            .AnyAsync(x => x.TitularId == titularId && scope.CuentaIds.Contains(x.Id), cancellationToken);
    }

    private static string NormalizePeriodo(string? periodo)
    {
        var normalized = (periodo ?? "1m").Trim().ToLowerInvariant();
        return normalized switch
        {
            "1m" => "1m",
            "3m" => "3m",
            "6m" => "6m",
            "9m" => "9m",
            "12m" => "12m",
            "18m" => "18m",
            "24m" => "24m",
            _ => "1m"
        };
    }

    private static DateOnly GetPeriodStart(string periodo, DateOnly now)
    {
        var months = periodo switch
        {
            "1m" => 1,
            "3m" => 3,
            "6m" => 6,
            "9m" => 9,
            "12m" => 12,
            "18m" => 18,
            "24m" => 24,
            _ => 1
        };

        return now.AddMonths(-months);
    }

    private static List<DateRange> BuildBuckets(DateOnly start, DateOnly end, bool daily)
    {
        var ranges = new List<DateRange>();
        if (daily)
        {
            var cursor = start;
            while (cursor <= end)
            {
                ranges.Add(new DateRange(cursor, cursor));
                cursor = cursor.AddDays(1);
            }

            return ranges;
        }

        var weeklyStart = AlignToMonday(start);
        while (weeklyStart <= end)
        {
            var bucketEnd = weeklyStart.AddDays(6);
            if (bucketEnd > end)
            {
                bucketEnd = end;
            }

            ranges.Add(new DateRange(weeklyStart, bucketEnd));
            weeklyStart = weeklyStart.AddDays(7);
        }

        return ranges;
    }

    private static DateOnly AlignToMonday(DateOnly date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var offset = dayOfWeek == 0 ? 6 : dayOfWeek - 1;
        return date.AddDays(-offset);
    }

    private static string? NormalizeDivisa(string? divisa)
    {
        if (string.IsNullOrWhiteSpace(divisa))
        {
            return null;
        }

        return divisa.Trim().ToUpperInvariant();
    }

    private static int GetTipoTitularOrder(string tipoTitular) =>
        tipoTitular switch
        {
            nameof(TipoTitular.EMPRESA) => 0,
            nameof(TipoTitular.AUTONOMO) => 1,
            nameof(TipoTitular.PARTICULAR) => 2,
            _ => 3
        };

    private sealed class DashboardMetrics
    {
        public Dictionary<string, decimal> SaldosPorDivisa { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> SaldosDisponiblesPorDivisa { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> SaldosInmovilizadosPorDivisa { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<Guid, decimal> SaldoByCuentaId { get; set; } = [];
        public Dictionary<Guid, decimal> SaldoConvertidoByCuentaId { get; set; } = [];
        public decimal IngresosMes { get; set; }
        public decimal EgresosMes { get; set; }
        public decimal TotalConvertido { get; set; }
    }

    private sealed class DashboardScope
    {
        public bool GlobalAccess { get; }
        public HashSet<Guid> ExplicitTitularIds { get; }
        public HashSet<Guid> CuentaIds { get; }

        public DashboardScope(bool globalAccess, HashSet<Guid> titularIds, HashSet<Guid> cuentaIds)
        {
            GlobalAccess = globalAccess;
            ExplicitTitularIds = titularIds;
            CuentaIds = cuentaIds;
        }

        public static DashboardScope GlobalForAdmin() => new(true, [], []);
        public static DashboardScope GlobalForManager() => new(true, [], []);
    }

    private sealed class CuentaScopeItem
    {
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string? BancoNombre { get; set; }
        public Guid TitularId { get; set; }
        public string TitularNombre { get; set; } = string.Empty;
        public string Divisa { get; set; } = "EUR";
        public bool EsEfectivo { get; set; }
        public TipoCuenta TipoCuenta { get; set; } = TipoCuenta.NORMAL;
        public TipoTitular TipoTitular { get; set; } = TipoTitular.EMPRESA;
    }

    private sealed class EvolucionExtractRow
    {
        public Guid CuentaId { get; set; }
        public DateOnly Fecha { get; set; }
        public decimal Monto { get; set; }
        public decimal Saldo { get; set; }
    }

    private readonly record struct DateRange(DateOnly Start, DateOnly End);
}

public sealed class DashboardAccessException : Exception
{
    public int StatusCode { get; }

    public DashboardAccessException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
