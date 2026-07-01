using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IDashboardService
{
    Task<DashboardPrincipalResponse> GetPrincipalAsync(Guid userId, string? divisaPrincipal, Guid? paisId, CancellationToken cancellationToken);
    Task<DashboardTitularResponse> GetTitularAsync(Guid userId, Guid titularId, string? divisaPrincipal, Guid? paisId, CancellationToken cancellationToken);
    Task<DashboardSaldosDivisaResponse> GetSaldosDivisaAsync(Guid userId, string? divisaPrincipal, Guid? titularId, Guid? paisId, CancellationToken cancellationToken);
    Task<DashboardEvolucionResponse> GetEvolucionAsync(Guid userId, string periodo, string? divisaPrincipal, Guid? titularId, Guid? paisId, CancellationToken cancellationToken);
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

    public async Task<DashboardPrincipalResponse> GetPrincipalAsync(Guid userId, string? divisaPrincipal, Guid? paisId, CancellationToken cancellationToken)
    {
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);
        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var chartColors = await ResolveChartColorsAsync(cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, null, paisId, cancellationToken);
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

        var concentracionBancos = BuildConcentracionBancos(cuentas, metrics);
        var saldosPorCuenta = BuildSaldosPorCuenta(cuentas, metrics);
        var saldosPorPais = BuildSaldosPorPais(cuentas, metrics);

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
            SaldosPorCuenta = saldosPorCuenta,
            SaldosPorPais = saldosPorPais,
            ConcentracionBancos = concentracionBancos,
            ChartColors = chartColors
        };
    }

    public async Task<DashboardTitularResponse> GetTitularAsync(Guid userId, Guid titularId, string? divisaPrincipal, Guid? paisId, CancellationToken cancellationToken)
    {
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);
        var canAccessTitular = await CanAccessTitularAsync(scope, titularId, cancellationToken);
        if (!canAccessTitular)
        {
            throw new DashboardAccessException("No tienes permisos para ver este titular", StatusCodes.Status403Forbidden);
        }

        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var chartColors = await ResolveChartColorsAsync(cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, titularId, paisId, cancellationToken);
        if (paisId.HasValue && cuentas.Count == 0)
        {
            throw new DashboardAccessException("Titular no encontrado para el pais seleccionado", StatusCodes.Status404NotFound);
        }

        var titularNombre = cuentas.FirstOrDefault()?.TitularNombre
            ?? await _dbContext.Titulares
                .Where(x => x.Id == titularId)
                .Select(x => x.Nombre)
                .FirstOrDefaultAsync(cancellationToken)
            ?? "Titular";

        var metrics = await BuildMetricsAsync(cuentas, targetCurrency, cancellationToken);

        var saldosPorCuenta = BuildSaldosPorCuenta(cuentas, metrics);
        var saldosPorPais = BuildSaldosPorPais(cuentas, metrics);

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
            SaldosPorPais = saldosPorPais,
            ChartColors = chartColors
        };
    }

    private static IReadOnlyList<DashboardSaldoCuentaResponse> BuildSaldosPorCuenta(
        IReadOnlyList<CuentaScopeItem> cuentas,
        DashboardMetrics metrics)
    {
        return cuentas
            .Select(c =>
            {
                var saldo = metrics.SaldoByCuentaId.TryGetValue(c.CuentaId, out var saldoActual) ? saldoActual : 0m;
                var saldoConvertido = metrics.SaldoConvertidoByCuentaId.TryGetValue(c.CuentaId, out var converted) ? converted : 0m;

                return new DashboardSaldoCuentaResponse
                {
                    CuentaId = c.CuentaId,
                    CuentaNombre = c.CuentaNombre,
                    TitularId = c.TitularId,
                    TitularNombre = c.TitularNombre,
                    PaisId = c.PaisId,
                    PaisNombre = c.PaisNombre,
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
    }

    public async Task<DashboardSaldosDivisaResponse> GetSaldosDivisaAsync(Guid userId, string? divisaPrincipal, Guid? titularId, Guid? paisId, CancellationToken cancellationToken)
    {
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);
        if (titularId.HasValue && !await CanAccessTitularAsync(scope, titularId.Value, cancellationToken))
        {
            throw new DashboardAccessException("No tienes permisos para ver este titular", StatusCodes.Status403Forbidden);
        }

        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, titularId, paisId, cancellationToken);
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

    public async Task<DashboardEvolucionResponse> GetEvolucionAsync(Guid userId, string periodo, string? divisaPrincipal, Guid? titularId, Guid? paisId, CancellationToken cancellationToken)
    {
        var normalizedPeriodo = NormalizePeriodo(periodo);
        var scope = await GetAuthorizedScopeAsync(userId, cancellationToken);

        if (titularId.HasValue && !await CanAccessTitularAsync(scope, titularId.Value, cancellationToken))
        {
            throw new DashboardAccessException("No tienes permisos para ver este titular", StatusCodes.Status403Forbidden);
        }

        var targetCurrency = await ResolveDivisaPrincipalAsync(divisaPrincipal, cancellationToken);
        var cuentas = await GetScopedCuentasAsync(scope, titularId, paisId, cancellationToken);
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

        // Convencion de saldo (V-02-04): saldo "a fecha de corte" (snapshot historico
        // con filtro Fecha < start) ordena por Fecha DESC como criterio primario, para
        // tomar la fila con la fecha mas reciente antes del corte. NO usar FilaNumero
        // primario aqui: una correccion retroactiva (fecha vieja, fila_numero alto)
        // pasaria a considerarse el saldo del corte por error. El saldo "ahora" (sin
        // filtro de fecha) si usa FilaNumero DESC primario (ver BuildMetricsAsync).
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

        var pfCuentaIds = cuentas
            .Where(c => c.TipoCuenta == TipoCuenta.PLAZO_FIJO)
            .Select(c => c.CuentaId)
            .ToHashSet();

        decimal saldoInicioPeriodo = 0m;
        decimal disponibleInicioPeriodo = 0m;
        decimal inmovilizadoInicioPeriodo = 0m;
        foreach (var entry in currentSaldo)
        {
            if (accountCurrency.TryGetValue(entry.Key, out var divisa))
            {
                var converted = await _tiposCambioService.ConvertAsync(entry.Value, divisa, targetCurrency, cancellationToken);
                saldoInicioPeriodo += converted;
                if (pfCuentaIds.Contains(entry.Key))
                    inmovilizadoInicioPeriodo += converted;
                else
                    disponibleInicioPeriodo += converted;
            }
        }

        var prevStart = GetPeriodStart(normalizedPeriodo, start);
        var prevExtractos = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId) && x.Fecha >= prevStart && x.Fecha < start)
            .Select(x => new { x.CuentaId, x.Monto })
            .ToListAsync(cancellationToken);

        decimal ingresosAnterior = 0m;
        decimal egresosAnterior = 0m;
        foreach (var row in prevExtractos)
        {
            if (accountCurrency.TryGetValue(row.CuentaId, out var divisa))
            {
                var converted = await _tiposCambioService.ConvertAsync(row.Monto, divisa, targetCurrency, cancellationToken);
                if (converted >= 0m)
                    ingresosAnterior += converted;
                else
                    egresosAnterior += Math.Abs(converted);
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
            SaldoInicioPeriodo = Decimal.Round(saldoInicioPeriodo, 2),
            DisponibleInicioPeriodo = Decimal.Round(disponibleInicioPeriodo, 2),
            InmovilizadoInicioPeriodo = Decimal.Round(inmovilizadoInicioPeriodo, 2),
            IngresosAnterior = Decimal.Round(ingresosAnterior, 2),
            EgresosAnterior = Decimal.Round(egresosAnterior, 2),
            Puntos = points
        };
    }

    private static IReadOnlyList<DashboardConcentracionBancoResponse> BuildConcentracionBancos(
        IReadOnlyList<CuentaScopeItem> cuentas,
        DashboardMetrics metrics)
    {
        if (metrics.TotalConvertido == 0m) return [];

        return cuentas
            .GroupBy(c => c.BancoNombre?.Trim() is { Length: > 0 } nombre
                ? nombre
                : (c.EsEfectivo ? "Efectivo" : "Sin banco"))
            .Select(g =>
            {
                var saldo = g.Sum(c => metrics.SaldoConvertidoByCuentaId.GetValueOrDefault(c.CuentaId, 0m));
                return new DashboardConcentracionBancoResponse
                {
                    BancoNombre = g.Key,
                    SaldoConvertido = Decimal.Round(saldo, 2),
                    Porcentaje = Decimal.Round(metrics.TotalConvertido > 0 ? saldo / metrics.TotalConvertido * 100m : 0m, 1)
                };
            })
            .OrderByDescending(x => x.SaldoConvertido)
            .ToList();
    }

    private static IReadOnlyList<DashboardSaldoPaisResponse> BuildSaldosPorPais(
        IReadOnlyList<CuentaScopeItem> cuentas,
        DashboardMetrics metrics)
    {
        return cuentas
            .GroupBy(c => new
            {
                c.PaisId,
                PaisNombre = string.IsNullOrWhiteSpace(c.PaisNombre) ? "Sin pais" : c.PaisNombre!
            })
            .Select(group =>
            {
                var saldosPorDivisa = group
                    .GroupBy(x => x.Divisa)
                    .ToDictionary(
                        x => x.Key,
                        x => Decimal.Round(x.Sum(c => metrics.SaldoByCuentaId.TryGetValue(c.CuentaId, out var saldo) ? saldo : 0m), 2));
                var totalConvertido = group.Sum(c => metrics.SaldoConvertidoByCuentaId.GetValueOrDefault(c.CuentaId, 0m));

                return new DashboardSaldoPaisResponse
                {
                    PaisId = group.Key.PaisId,
                    PaisNombre = group.Key.PaisNombre,
                    SaldosPorDivisa = saldosPorDivisa,
                    TotalConvertido = Decimal.Round(totalConvertido, 2),
                    TotalCuentas = group.Count()
                };
            })
            .OrderByDescending(x => x.TotalConvertido)
            .ThenBy(x => x.PaisNombre)
            .ToList();
    }

    private async Task<DashboardMetrics> BuildMetricsAsync(IReadOnlyList<CuentaScopeItem> cuentas, string targetCurrency, CancellationToken cancellationToken)
    {
        if (cuentas.Count == 0)
        {
            return new DashboardMetrics();
        }

        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var divisaByCuenta = cuentas.ToDictionary(x => x.CuentaId, x => x.Divisa);

        // Convencion de saldo (V-02-04): saldo "ahora" = ultima fila fisica (FilaNumero
        // DESC primario), porque FilaNumero es el orden de insercion autoritativo del
        // extracto. Fecha rompe empates. Difiere a proposito del snapshot a-fecha-de-corte
        // (que ordena por Fecha primario). Ver GetEvolucionAsync.
        var latestRows = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId))
            .GroupBy(x => x.CuentaId)
            .Select(g => g
                .OrderByDescending(x => x.FilaNumero)
                .ThenByDescending(x => x.Fecha)
                .Select(x => new { x.CuentaId, x.Saldo })
                .First())
            .ToListAsync(cancellationToken);

        var saldoByCuenta = latestRows.ToDictionary(x => x.CuentaId, x => x.Saldo);
        var saldoConvertidoByCuenta = new Dictionary<Guid, decimal>();
        var saldosPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var saldosDisponiblesPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var saldosInmovilizadosPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // V-02-03 (H4): precomputar las tasas de cambio por par (origen, destino)
        // una sola vez para evitar N awaits por cuenta. Tambien tolera tasas
        // faltantes: si no existe tasa, marca la fila con tasa_pendiente y la
        // omite del total convertido sin abortar el dashboard completo.
        var uniqueDivisasInvolucradas = cuentas
            .Select(x => x.Divisa)
            .Where(x => !string.Equals(x, targetCurrency, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tasaPorDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var div in uniqueDivisasInvolucradas)
        {
            try
            {
                tasaPorDivisa[div] = await _tiposCambioService.ConvertAsync(1m, div, targetCurrency, cancellationToken);
            }
            catch (TipoCambioMissingException)
            {
                tasaPorDivisa[div] = 0m; // marcador de tasa faltante
            }
        }

        foreach (var cuenta in cuentas)
        {
            var saldo = saldoByCuenta.GetValueOrDefault(cuenta.CuentaId, 0m);
            saldosPorDivisa.TryGetValue(cuenta.Divisa, out var acumuladoDivisa);
            saldosPorDivisa[cuenta.Divisa] = acumuladoDivisa + saldo;
            if (cuenta.TipoCuenta == TipoCuenta.PLAZO_FIJO)
            {
                saldosInmovilizadosPorDivisa[cuenta.Divisa] = saldosInmovilizadosPorDivisa.GetValueOrDefault(cuenta.Divisa, 0m) + saldo;
            }
            else
            {
                saldosDisponiblesPorDivisa[cuenta.Divisa] = saldosDisponiblesPorDivisa.GetValueOrDefault(cuenta.Divisa, 0m) + saldo;
            }

            saldoConvertidoByCuenta[cuenta.CuentaId] = string.Equals(cuenta.Divisa, targetCurrency, StringComparison.OrdinalIgnoreCase)
                ? saldo
                : tasaPorDivisa.TryGetValue(cuenta.Divisa, out var tasa) && tasa > 0m
                    ? saldo * tasa
                    : 0m;
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

        // V-02-03 (H4): agregamos primero por divisa, despues convertimos una
        // sola vez por divisa usando las tasas ya precomputadas. Antes N+async.
        var monthIngresosByDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var monthEgresosByDivisa = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in monthRows)
        {
            if (!divisaByCuenta.TryGetValue(row.CuentaId, out var divisa))
            {
                continue;
            }

            if (row.Monto >= 0m)
            {
                monthIngresosByDivisa[divisa] = monthIngresosByDivisa.GetValueOrDefault(divisa, 0m) + row.Monto;
            }
            else
            {
                monthEgresosByDivisa[divisa] = monthEgresosByDivisa.GetValueOrDefault(divisa, 0m) + Math.Abs(row.Monto);
            }
        }

        foreach (var (divisa, monto) in monthIngresosByDivisa)
        {
            ingresosMes += ConvertPrecomputed(monto, divisa, targetCurrency, tasaPorDivisa);
        }

        foreach (var (divisa, monto) in monthEgresosByDivisa)
        {
            egresosMes += ConvertPrecomputed(monto, divisa, targetCurrency, tasaPorDivisa);
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

    private static decimal ConvertPrecomputed(decimal amount, string divisa, string targetCurrency, IReadOnlyDictionary<string, decimal> tasaPorDivisa)
    {
        if (string.Equals(divisa, targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return amount;
        }

        return tasaPorDivisa.TryGetValue(divisa, out var tasa) && tasa > 0m
            ? amount * tasa
            : 0m;
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

    private async Task<IReadOnlyList<CuentaScopeItem>> GetScopedCuentasAsync(DashboardScope scope, Guid? titularId, Guid? paisId, CancellationToken cancellationToken)
    {
        var query = from cuenta in _dbContext.Cuentas.AsNoTracking()
                    join titular in _dbContext.Titulares.AsNoTracking() on cuenta.TitularId equals titular.Id
                    join pais in _dbContext.Paises.AsNoTracking().IgnoreQueryFilters() on cuenta.PaisId equals pais.Id into paises
                    from pais in paises.DefaultIfEmpty()
                    select new CuentaScopeItem
                    {
                        CuentaId = cuenta.Id,
                        CuentaNombre = cuenta.Nombre,
                        BancoNombre = cuenta.BancoNombre,
                        TitularId = titular.Id,
                        TitularNombre = titular.Nombre,
                        PaisId = cuenta.PaisId,
                        PaisNombre = pais != null ? pais.Nombre : null,
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

        if (paisId.HasValue)
        {
            query = query.Where(x => x.PaisId == paisId.Value);
        }

        if (!scope.GlobalAccess)
        {
            query = query.Where(x => scope.CuentaIds.Contains(x.CuentaId));
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

        var permisos = await _dbContext.PermisosUsuario
            .AsNoTracking()
            .Where(x => x.UsuarioId == userId)
            .Select(x => new
            {
                x.CuentaId,
                x.TitularId,
                x.PaisId,
                x.PuedeVerCuentas,
                x.PuedeAgregarLineas,
                x.PuedeEditarLineas,
                x.PuedeEliminarLineas,
                x.PuedeImportar,
                x.PuedeVerDashboard
            })
            .ToListAsync(cancellationToken);

        permisos = permisos
            .Where(x =>
                usuario.Rol == RolUsuario.GERENTE
                    ? GrantsAccountDataAccess(x.PuedeVerCuentas, x.PuedeAgregarLineas, x.PuedeEditarLineas, x.PuedeEliminarLineas, x.PuedeImportar)
                    : x.PuedeVerDashboard && GrantsAccountDataAccess(x.PuedeVerCuentas, x.PuedeAgregarLineas, x.PuedeEditarLineas, x.PuedeEliminarLineas, x.PuedeImportar))
            .ToList();

        if (permisos.Count == 0)
        {
            throw new DashboardAccessException("No tienes permisos para ver dashboards", StatusCodes.Status403Forbidden);
        }

        var globalAccess = permisos.Any(x =>
            x.PaisId == null &&
            x.CuentaId == null &&
            x.TitularId == null &&
            GrantsAccountDataAccess(x.PuedeVerCuentas, x.PuedeAgregarLineas, x.PuedeEditarLineas, x.PuedeEliminarLineas, x.PuedeImportar));
        if (globalAccess)
        {
            return DashboardScope.GlobalForManager();
        }

        var cuentaIdsList = await _dbContext.Cuentas
            .AsNoTracking()
            .Where(c => _dbContext.PermisosUsuario.Any(p =>
                p.UsuarioId == userId &&
                (usuario.Rol == RolUsuario.GERENTE || p.PuedeVerDashboard) &&
                (p.PuedeVerCuentas || p.PuedeAgregarLineas || p.PuedeEditarLineas || p.PuedeEliminarLineas || p.PuedeImportar) &&
                (p.PaisId == null || p.PaisId == c.PaisId) &&
                (p.TitularId == null || p.TitularId == c.TitularId) &&
                (p.CuentaId == null || p.CuentaId == c.Id)))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        var cuentaIds = cuentaIdsList.ToHashSet();

        if (cuentaIds.Count == 0)
        {
            throw new DashboardAccessException("No tienes permisos para ver dashboards", StatusCodes.Status403Forbidden);
        }

        return new DashboardScope(false, cuentaIds);
    }

    private static bool GrantsAccountDataAccess(
        bool puedeVerCuentas,
        bool puedeAgregarLineas,
        bool puedeEditarLineas,
        bool puedeEliminarLineas,
        bool puedeImportar) =>
        puedeVerCuentas || puedeAgregarLineas || puedeEditarLineas || puedeEliminarLineas || puedeImportar;

    private async Task<bool> CanAccessTitularAsync(DashboardScope scope, Guid titularId, CancellationToken cancellationToken)
    {
        if (scope.GlobalAccess)
        {
            return true;
        }

        if (scope.CuentaIds.Count == 0)
        {
            return false;
        }

        return await _dbContext.Cuentas
            .AsNoTracking()
            .AnyAsync(
                x => x.TitularId == titularId && scope.CuentaIds.Contains(x.Id),
                cancellationToken);
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
        public HashSet<Guid> CuentaIds { get; }

        public DashboardScope(bool globalAccess, HashSet<Guid> cuentaIds)
        {
            GlobalAccess = globalAccess;
            CuentaIds = cuentaIds;
        }

        public static DashboardScope GlobalForAdmin() => new(true, []);
        public static DashboardScope GlobalForManager() => new(true, []);
    }

    private sealed class CuentaScopeItem
    {
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string? BancoNombre { get; set; }
        public Guid TitularId { get; set; }
        public string TitularNombre { get; set; } = string.Empty;
        public Guid? PaisId { get; set; }
        public string? PaisNombre { get; set; }
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
