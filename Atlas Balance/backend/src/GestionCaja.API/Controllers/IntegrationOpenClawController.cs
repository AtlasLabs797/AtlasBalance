using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Middleware;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Controllers;

[ApiController]
[Route("api/integration/openclaw")]
public sealed class IntegrationOpenClawController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IIntegrationAuthorizationService _integrationAuthorizationService;
    private readonly ITiposCambioService _tiposCambioService;

    public IntegrationOpenClawController(
        AppDbContext dbContext,
        IIntegrationAuthorizationService integrationAuthorizationService,
        ITiposCambioService tiposCambioService)
    {
        _dbContext = dbContext;
        _integrationAuthorizationService = integrationAuthorizationService;
        _tiposCambioService = tiposCambioService;
    }

    [HttpGet("titulares")]
    public async Task<IActionResult> Titulares(
        [FromQuery] string? format = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveFormat(format, out var isSimple))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: El parametro format debe ser 'full' o 'simple'");
        }

        var (_, scope, error) = await ResolveReadScopeAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var cuentas = await GetScopedAccountsAsync(scope, null, null, cancellationToken);
        var titulares = cuentas
            .GroupBy(x => new { x.TitularId, x.TitularNombre, x.TitularTipo })
            .OrderBy(x => x.Key.TitularNombre)
            .Select(group => new
            {
                id = group.Key.TitularId,
                nombre = group.Key.TitularNombre,
                tipo = group.Key.TitularTipo,
                cuentas = group
                    .OrderBy(x => x.CuentaNombre)
                    .Select(x => new
                    {
                        id = x.CuentaId,
                        nombre = x.CuentaNombre,
                        iban = x.Iban,
                        numero_cuenta = x.NumeroCuenta,
                        divisa = x.Divisa,
                        es_efectivo = x.EsEfectivo,
                        activa = x.Activa
                    })
                    .ToList()
            })
            .ToList();

        if (isSimple)
        {
            return Ok(IntegrationApiResponses.Success(new
            {
                titulares = titulares.Select(x => new
                {
                    x.id,
                    x.nombre,
                    cuentas = x.cuentas.Select(c => new
                    {
                        c.id,
                        c.nombre,
                        c.divisa
                    }).ToList()
                }).ToList()
            }));
        }

        return Ok(IntegrationApiResponses.Success(new
        {
            titulares,
            total_titulares = titulares.Count,
            total_cuentas = cuentas.Count
        }));
    }

    [HttpGet("saldos")]
    public async Task<IActionResult> Saldos(
        [FromQuery] string? format = null,
        [FromQuery] string? divisa = null,
        [FromQuery(Name = "titular_id")] Guid? titularId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveFormat(format, out var isSimple))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: El parametro format debe ser 'full' o 'simple'");
        }

        var (_, scope, error) = await ResolveReadScopeAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var cuentas = await GetScopedAccountsAsync(scope, titularId, null, cancellationToken);
        if (titularId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a ese titular");
        }

        var normalizedDivisa = NormalizeDivisa(divisa);
        if (!string.IsNullOrWhiteSpace(normalizedDivisa))
        {
            cuentas = cuentas
                .Where(x => x.Divisa.Equals(normalizedDivisa, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var latestByCuenta = await GetLatestExtractsByCuentaAsync(cuentaIds, cancellationToken);
        var monthTotals = await GetMonthTotalsByCuentaAsync(cuentaIds, cancellationToken);
        var alertas = await GetResolvedAlertsByCuentaAsync(cuentaIds, cancellationToken);
        var principalCurrency = await ResolvePrincipalCurrencyAsync(cancellationToken);
        var rateSnapshot = await BuildRateSnapshotAsync(principalCurrency, cuentas.Select(x => x.Divisa).Distinct(), cancellationToken);

        var cuentasData = new List<object>(cuentas.Count);
        decimal totalConvertido = 0m;

        foreach (var cuenta in cuentas)
        {
            latestByCuenta.TryGetValue(cuenta.CuentaId, out var latest);
            monthTotals.TryGetValue(cuenta.CuentaId, out var totals);
            alertas.TryGetValue(cuenta.CuentaId, out var alerta);

            var saldoActual = latest?.Saldo ?? 0m;
            totalConvertido += await _tiposCambioService.ConvertAsync(saldoActual, cuenta.Divisa, principalCurrency, cancellationToken);

            cuentasData.Add(new
            {
                id = cuenta.CuentaId,
                titular = new
                {
                    id = cuenta.TitularId,
                    nombre = cuenta.TitularNombre,
                    tipo = cuenta.TitularTipo
                },
                nombre = cuenta.CuentaNombre,
                iban = cuenta.Iban,
                es_efectivo = cuenta.EsEfectivo,
                divisa = cuenta.Divisa,
                saldo_actual = Decimal.Round(saldoActual, 2),
                ingresos_mes = Decimal.Round(totals?.Ingresos ?? 0m, 2),
                egresos_mes = Decimal.Round(Math.Abs(totals?.Egresos ?? 0m), 2),
                saldo_minimo_configurado = alerta?.SaldoMinimo,
                estado_alerta = ResolveAlertState(saldoActual, alerta),
                fecha_ultimo_movimiento = latest?.Fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            });
        }

        var totalesPorDivisa = cuentas
            .GroupBy(x => x.Divisa)
            .ToDictionary(
                x => x.Key,
                x => Decimal.Round(x.Sum(c =>
                    latestByCuenta.TryGetValue(c.CuentaId, out var latest) ? latest.Saldo : 0m), 2));

        if (isSimple)
        {
            return Ok(IntegrationApiResponses.Success(new
            {
                totales_por_divisa = totalesPorDivisa,
                total_convertido = new
                {
                    divisa = principalCurrency,
                    monto = Decimal.Round(totalConvertido, 2)
                }
            }));
        }

        return Ok(IntegrationApiResponses.Success(new
        {
            totales_por_divisa = totalesPorDivisa,
            total_convertido = new
            {
                divisa_principal = principalCurrency,
                monto = Decimal.Round(totalConvertido, 2)
            },
            tipo_cambio = rateSnapshot,
            cuentas = cuentasData,
            generado_en = DateTime.UtcNow
        }));
    }

    [HttpGet("extractos")]
    public async Task<IActionResult> Extractos(
        [FromQuery] string? format = null,
        [FromQuery(Name = "cuenta_id")] Guid? cuentaId = null,
        [FromQuery(Name = "titular_id")] Guid? titularId = null,
        [FromQuery(Name = "fecha_desde")] DateOnly? fechaDesde = null,
        [FromQuery(Name = "fecha_hasta")] DateOnly? fechaHasta = null,
        [FromQuery(Name = "limite")] int limite = 100,
        [FromQuery(Name = "pagina")] int pagina = 1,
        [FromQuery(Name = "ordenar_por")] string ordenarPor = "fecha",
        [FromQuery(Name = "orden")] string orden = "desc",
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveFormat(format, out var isSimple))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: El parametro format debe ser 'full' o 'simple'");
        }

        var (_, scope, error) = await ResolveReadScopeAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (fechaDesde.HasValue && fechaHasta.HasValue && fechaDesde.Value > fechaHasta.Value)
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: fecha_desde no puede ser mayor que fecha_hasta");
        }

        limite = Math.Clamp(limite, 1, 1000);
        pagina = Math.Max(1, pagina);

        var cuentas = await GetScopedAccountsAsync(scope, titularId, cuentaId, cancellationToken);
        if (titularId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a ese titular");
        }

        if (cuentaId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a esa cuenta");
        }

        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var query = _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId));

        if (fechaDesde.HasValue)
        {
            query = query.Where(x => x.Fecha >= fechaDesde.Value);
        }

        if (fechaHasta.HasValue)
        {
            query = query.Where(x => x.Fecha <= fechaHasta.Value);
        }

        var desc = string.Equals(orden, "desc", StringComparison.OrdinalIgnoreCase);
        var sort = (ordenarPor ?? "fecha").Trim().ToLowerInvariant();

        if (sort is not ("fecha" or "monto" or "saldo"))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: ordenar_por debe ser fecha, monto o saldo");
        }

        query = (sort, desc) switch
        {
            ("fecha", false) => query.OrderBy(x => x.Fecha).ThenBy(x => x.FilaNumero),
            ("fecha", true) => query.OrderByDescending(x => x.Fecha).ThenByDescending(x => x.FilaNumero),
            ("monto", false) => query.OrderBy(x => x.Monto).ThenBy(x => x.Fecha).ThenBy(x => x.FilaNumero),
            ("monto", true) => query.OrderByDescending(x => x.Monto).ThenByDescending(x => x.Fecha).ThenByDescending(x => x.FilaNumero),
            ("saldo", false) => query.OrderBy(x => x.Saldo).ThenBy(x => x.Fecha).ThenBy(x => x.FilaNumero),
            _ => query.OrderByDescending(x => x.Saldo).ThenByDescending(x => x.Fecha).ThenByDescending(x => x.FilaNumero)
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .ToListAsync(cancellationToken);

        var userIds = rows
            .Where(x => x.UsuarioCreacionId.HasValue)
            .Select(x => x.UsuarioCreacionId!.Value)
            .Distinct()
            .ToList();
        var usersById = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Email, IsDeleted = x.DeletedAt != null })
            .ToDictionaryAsync(x => x.Id, x => x.IsDeleted ? "usuario-eliminado" : x.Email, cancellationToken);
        var cuentasById = cuentas.ToDictionary(x => x.CuentaId);

        var extractos = rows.Select(x =>
        {
            var cuenta = cuentasById[x.CuentaId];
            return new
            {
                id = x.Id,
                cuenta = new
                {
                    id = cuenta.CuentaId,
                    nombre = cuenta.CuentaNombre,
                    divisa = cuenta.Divisa
                },
                titular = new
                {
                    id = cuenta.TitularId,
                    nombre = cuenta.TitularNombre
                },
                fecha = x.Fecha,
                concepto = x.Concepto,
                comentarios = x.Comentarios,
                monto = Decimal.Round(x.Monto, 2),
                tipo_movimiento = ResolveMovementType(x.Monto),
                saldo = Decimal.Round(x.Saldo, 2),
                fila_numero = x.FilaNumero,
                @checked = x.Checked,
                flagged = x.Flagged,
                usuario_creacion = x.UsuarioCreacionId.HasValue && usersById.TryGetValue(x.UsuarioCreacionId.Value, out var email)
                    ? email
                    : null,
                fecha_creacion = x.FechaCreacion
            };
        }).ToList();

        var summaryRows = _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId));
        if (fechaDesde.HasValue)
        {
            summaryRows = summaryRows.Where(x => x.Fecha >= fechaDesde.Value);
        }

        if (fechaHasta.HasValue)
        {
            summaryRows = summaryRows.Where(x => x.Fecha <= fechaHasta.Value);
        }

        var resumen = await summaryRows
            .GroupBy(_ => 1)
            .Select(g => new
            {
                total_ingresos = Decimal.Round(g.Where(x => x.Monto > 0).Sum(x => x.Monto), 2),
                total_egresos = Decimal.Round(g.Where(x => x.Monto < 0).Sum(x => x.Monto), 2),
                saldo_neto = Decimal.Round(g.Sum(x => x.Monto), 2)
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? new
            {
                total_ingresos = 0m,
                total_egresos = 0m,
                saldo_neto = 0m
            };

        if (isSimple)
        {
            return Ok(IntegrationApiResponses.Success(new
            {
                total_registros = total,
                pagina,
                registros_por_pagina = limite,
                paginas_totales = (int)Math.Ceiling(total / (double)limite),
                extractos = extractos.Select(x => new
                {
                    x.id,
                    cuenta = x.cuenta.nombre,
                    titular = x.titular.nombre,
                    x.fecha,
                    x.concepto,
                    x.monto,
                    x.tipo_movimiento,
                    x.saldo
                }).ToList()
            }));
        }

        return Ok(IntegrationApiResponses.Success(new
        {
            total_registros = total,
            pagina,
            registros_por_pagina = limite,
            paginas_totales = (int)Math.Ceiling(total / (double)limite),
            extractos,
            resumen
        }));
    }

    [HttpGet("grafica-evolucion")]
    public async Task<IActionResult> GraficaEvolucion(
        [FromQuery] string? format = null,
        [FromQuery] string periodo = "1m",
        [FromQuery(Name = "titular_id")] Guid? titularId = null,
        [FromQuery(Name = "cuenta_id")] Guid? cuentaId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveFormat(format, out var isSimple))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: El parametro format debe ser 'full' o 'simple'");
        }

        var (_, scope, error) = await ResolveReadScopeAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var normalizedPeriodo = NormalizePeriodo(periodo);
        if (normalizedPeriodo is null)
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: periodo debe ser 1m, 3m, 6m, 9m, 12m, 18m o 24m");
        }

        var cuentas = await GetScopedAccountsAsync(scope, titularId, cuentaId, cancellationToken);
        if (titularId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a ese titular");
        }

        if (cuentaId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a esa cuenta");
        }

        var principalCurrency = await ResolvePrincipalCurrencyAsync(cancellationToken);
        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var isDaily = normalizedPeriodo == "1m";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = GetPeriodStart(normalizedPeriodo, today);
        var buckets = BuildBuckets(start, today, isDaily);
        var currencyByCuenta = cuentas.ToDictionary(x => x.CuentaId, x => x.Divisa);

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
        foreach (var cuentaIdValue in cuentaIds)
        {
            currentSaldo.TryAdd(cuentaIdValue, 0m);
        }

        var saldoInicial = await ConvertSaldoTotalAsync(currentSaldo, currencyByCuenta, principalCurrency, cancellationToken);

        var extracts = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId) && x.Fecha >= start && x.Fecha <= today)
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

        var points = new List<EvolucionPoint>(buckets.Count);
        var index = 0;

        foreach (var bucket in buckets)
        {
            decimal ingresos = 0m;
            decimal egresos = 0m;

            while (index < extracts.Count && extracts[index].Fecha <= bucket.End)
            {
                var item = extracts[index];
                currentSaldo[item.CuentaId] = item.Saldo;

                if (currencyByCuenta.TryGetValue(item.CuentaId, out var currency))
                {
                    var converted = await _tiposCambioService.ConvertAsync(item.Monto, currency, principalCurrency, cancellationToken);
                    if (converted >= 0m)
                    {
                        ingresos += converted;
                    }
                    else
                    {
                        egresos += converted;
                    }
                }

                index++;
            }

            var saldoTotal = await ConvertSaldoTotalAsync(currentSaldo, currencyByCuenta, principalCurrency, cancellationToken);
            points.Add(new EvolucionPoint
            {
                Fecha = bucket.End,
                Saldo = Decimal.Round(saldoTotal, 2),
                Ingresos = Decimal.Round(ingresos, 2),
                Egresos = Decimal.Round(egresos, 2),
                Neto = Decimal.Round(ingresos + egresos, 2)
            });
        }

        var saldoFinal = points.LastOrDefault()?.Saldo ?? saldoInicial;
        var saldos = points.Count > 0 ? points.Select(x => x.Saldo).ToList() : [saldoInicial];
        var totalIngresos = points.Sum(x => x.Ingresos);
        var totalEgresos = points.Sum(x => x.Egresos);
        var variacionNeta = saldoFinal - saldoInicial;
        var variacionPorcentaje = saldoInicial == 0m
            ? 0m
            : Decimal.Round((variacionNeta / Math.Abs(saldoInicial)) * 100m, 2);

        if (isSimple)
        {
            return Ok(IntegrationApiResponses.Success(new
            {
                periodo = normalizedPeriodo,
                tipo_agregacion = isDaily ? "diario" : "semanal",
                puntos_datos = points.Select(x => new
                {
                    fecha = x.Fecha,
                    saldo = x.Saldo,
                    ingresos = x.Ingresos,
                    egresos = x.Egresos,
                    neto = x.Neto
                }).ToList()
            }));
        }

        return Ok(IntegrationApiResponses.Success(new
        {
            periodo = normalizedPeriodo,
            tipo_agregacion = isDaily ? "diario" : "semanal",
            moneda_principal = principalCurrency,
            puntos_datos = points.Select(x => new
            {
                fecha = x.Fecha,
                saldo = x.Saldo,
                ingresos = x.Ingresos,
                egresos = x.Egresos,
                neto = x.Neto
            }).ToList(),
            estadisticas = new
            {
                saldo_inicial = Decimal.Round(saldoInicial, 2),
                saldo_final = Decimal.Round(saldoFinal, 2),
                saldo_promedio = Decimal.Round(saldos.Average(), 2),
                saldo_minimo = Decimal.Round(saldos.Min(), 2),
                saldo_maximo = Decimal.Round(saldos.Max(), 2),
                total_ingresos = Decimal.Round(totalIngresos, 2),
                total_egresos = Decimal.Round(totalEgresos, 2),
                variacion_neta = Decimal.Round(variacionNeta, 2),
                variacion_porcentaje = variacionPorcentaje
            }
        }));
    }

    [HttpGet("alertas")]
    public async Task<IActionResult> Alertas(
        [FromQuery] string? format = null,
        [FromQuery] string estado = "activa",
        [FromQuery(Name = "titular_id")] Guid? titularId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveFormat(format, out var isSimple))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: El parametro format debe ser 'full' o 'simple'");
        }

        var normalizedEstado = (estado ?? "activa").Trim().ToLowerInvariant();
        if (normalizedEstado != "activa" && normalizedEstado != "inactiva" && normalizedEstado != "todos")
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: estado debe ser activa, inactiva o todos");
        }

        var (_, scope, error) = await ResolveReadScopeAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var cuentas = await GetScopedAccountsAsync(scope, titularId, null, cancellationToken);
        if (titularId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a ese titular");
        }

        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var latestByCuenta = await GetLatestExtractsByCuentaAsync(cuentaIds, cancellationToken);
        var alertas = await GetResolvedAlertsByCuentaAsync(cuentaIds, cancellationToken);

        var data = cuentas
            .Select(cuenta =>
            {
                latestByCuenta.TryGetValue(cuenta.CuentaId, out var latest);
                alertas.TryGetValue(cuenta.CuentaId, out var alerta);
                var saldoActual = latest?.Saldo ?? 0m;
                var estadoAlerta = ResolveAlertState(saldoActual, alerta);

                return new
                {
                    cuenta_id = cuenta.CuentaId,
                    cuenta_nombre = cuenta.CuentaNombre,
                    titular_id = cuenta.TitularId,
                    titular_nombre = cuenta.TitularNombre,
                    divisa = cuenta.Divisa,
                    saldo_actual = Decimal.Round(saldoActual, 2),
                    saldo_minimo = alerta?.SaldoMinimo,
                    estado = estadoAlerta,
                    activa = alerta?.Activa ?? false
                };
            })
            .Where(x => normalizedEstado switch
            {
                "activa" => x.activa,
                "inactiva" => !x.activa && x.saldo_minimo.HasValue,
                _ => x.saldo_minimo.HasValue
            })
            .ToList();

        if (isSimple)
        {
            return Ok(IntegrationApiResponses.Success(data.Select(x => new
            {
                cuenta = x.cuenta_nombre,
                x.saldo_actual,
                x.saldo_minimo,
                x.estado
            }).ToList()));
        }

        return Ok(IntegrationApiResponses.Success(new
        {
            alertas = data,
            total = data.Count,
            total_activadas = data.Count(x => x.estado == "activada")
        }));
    }

    [HttpGet("auditoria")]
    public async Task<IActionResult> Auditoria(
        [FromQuery] string? format = null,
        [FromQuery(Name = "cuenta_id")] Guid? cuentaId = null,
        [FromQuery(Name = "titular_id")] Guid? titularId = null,
        [FromQuery(Name = "fecha_desde")] DateOnly? fechaDesde = null,
        [FromQuery(Name = "fecha_hasta")] DateOnly? fechaHasta = null,
        [FromQuery(Name = "tipo_accion")] string tipoAccion = "all",
        [FromQuery(Name = "limite")] int limite = 100,
        [FromQuery(Name = "pagina")] int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveFormat(format, out var isSimple))
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: El parametro format debe ser 'full' o 'simple'");
        }

        if (!cuentaId.HasValue && !titularId.HasValue)
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: Debe indicar cuenta_id o titular_id");
        }

        if (fechaDesde.HasValue && fechaHasta.HasValue && fechaDesde.Value > fechaHasta.Value)
        {
            return IntegrationError(StatusCodes.Status400BadRequest, "BAD_REQUEST: fecha_desde no puede ser mayor que fecha_hasta");
        }

        var normalizedTipoAccion = (tipoAccion ?? "all").Trim();
        limite = Math.Clamp(limite, 1, 1000);
        pagina = Math.Max(1, pagina);

        var (_, scope, error) = await ResolveReadScopeAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var cuentas = await GetScopedAccountsAsync(scope, titularId, cuentaId, cancellationToken);
        if (titularId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a ese titular");
        }

        if (cuentaId.HasValue && cuentas.Count == 0)
        {
            return IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: Sin permisos para acceder a esa cuenta");
        }

        var cuentaIds = cuentas.Select(x => x.CuentaId).ToHashSet();
        var extractosScope = _dbContext.Extractos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId))
            .Select(x => new { x.Id, x.CuentaId });

        var extractoMap = await extractosScope.ToDictionaryAsync(x => x.Id, x => x.CuentaId, cancellationToken);
        var extractoIds = extractoMap.Keys.ToList();

        var query = _dbContext.Auditorias
            .AsNoTracking()
            .Where(x =>
                (x.EntidadTipo == "EXTRACTOS" && x.EntidadId.HasValue && extractoIds.Contains(x.EntidadId.Value)) ||
                (x.EntidadTipo == "CUENTAS" && x.EntidadId.HasValue && cuentaIds.Contains(x.EntidadId.Value)));

        if (!string.Equals(normalizedTipoAccion, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.TipoAccion == normalizedTipoAccion);
        }

        if (fechaDesde.HasValue)
        {
            var from = fechaDesde.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.Timestamp >= from);
        }

        if (fechaHasta.HasValue)
        {
            var untilExclusive = fechaHasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.Timestamp < untilExclusive);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .ToListAsync(cancellationToken);

        var cuentaById = cuentas.ToDictionary(x => x.CuentaId);
        var data = rows.Select(x =>
        {
            Guid? resolvedCuentaId = null;
            if (x.EntidadTipo == "CUENTAS" && x.EntidadId.HasValue)
            {
                resolvedCuentaId = x.EntidadId.Value;
            }
            else if (x.EntidadTipo == "EXTRACTOS" && x.EntidadId.HasValue && extractoMap.TryGetValue(x.EntidadId.Value, out var cuentaIdValue))
            {
                resolvedCuentaId = cuentaIdValue;
            }

            cuentaById.TryGetValue(resolvedCuentaId ?? Guid.Empty, out var cuenta);

            return new
            {
                id = x.Id,
                timestamp = x.Timestamp,
                tipo_accion = x.TipoAccion,
                entidad_tipo = x.EntidadTipo,
                entidad_id = x.EntidadId,
                cuenta_id = resolvedCuentaId,
                cuenta_nombre = cuenta?.CuentaNombre,
                titular_id = cuenta?.TitularId,
                titular_nombre = cuenta?.TitularNombre,
                celda_referencia = x.CeldaReferencia,
                columna_nombre = x.ColumnaNombre,
                valor_anterior = x.ValorAnterior,
                valor_nuevo = x.ValorNuevo,
                ip_address = x.IpAddress != null ? x.IpAddress.ToString() : null
            };
        }).ToList();

        if (isSimple)
        {
            return Ok(IntegrationApiResponses.Success(new
            {
                total_registros = total,
                pagina,
                registros = data.Select(x => new
                {
                    x.timestamp,
                    x.tipo_accion,
                    cuenta = x.cuenta_nombre,
                    x.celda_referencia,
                    x.valor_nuevo
                }).ToList()
            }));
        }

        return Ok(IntegrationApiResponses.Success(new
        {
            total_registros = total,
            pagina,
            registros_por_pagina = limite,
            paginas_totales = (int)Math.Ceiling(total / (double)limite),
            auditoria = data
        }));
    }

    private async Task<(IntegrationToken? Token, IntegrationAccessScope Scope, IActionResult? Error)> ResolveReadScopeAsync(CancellationToken cancellationToken)
    {
        if (!TryGetToken(out var token))
        {
            return (null, new IntegrationAccessScope(), IntegrationError(StatusCodes.Status401Unauthorized, "UNAUTHORIZED: Token de integracion no autenticado"));
        }

        var scope = await _integrationAuthorizationService.GetScopeAsync(token.Id, cancellationToken, "lectura");
        if (!scope.HasPermissions)
        {
            return (token, scope, IntegrationError(StatusCodes.Status403Forbidden, "FORBIDDEN: El token no tiene permisos de lectura para estos datos"));
        }

        return (token, scope, null);
    }

    private async Task<List<ScopedCuentaInfo>> GetScopedAccountsAsync(
        IntegrationAccessScope scope,
        Guid? titularId,
        Guid? cuentaId,
        CancellationToken cancellationToken)
    {
        var cuentasQuery = _integrationAuthorizationService.ApplyCuentaScope(_dbContext.Cuentas.AsNoTracking(), scope);
        if (titularId.HasValue)
        {
            cuentasQuery = cuentasQuery.Where(x => x.TitularId == titularId.Value);
        }

        if (cuentaId.HasValue)
        {
            cuentasQuery = cuentasQuery.Where(x => x.Id == cuentaId.Value);
        }

        var cuentas = await cuentasQuery
            .OrderBy(x => x.Nombre)
            .ToListAsync(cancellationToken);

        var titularIds = cuentas.Select(x => x.TitularId).Distinct().ToList();
        var titulares = await _integrationAuthorizationService
            .ApplyTitularScope(_dbContext.Titulares.AsNoTracking(), scope)
            .Where(x => titularIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return cuentas
            .Where(x => titulares.ContainsKey(x.TitularId))
            .Select(x =>
            {
                var titular = titulares[x.TitularId];
                return new ScopedCuentaInfo
                {
                    CuentaId = x.Id,
                    CuentaNombre = x.Nombre,
                    Iban = x.Iban,
                    NumeroCuenta = x.NumeroCuenta,
                    Divisa = x.Divisa,
                    EsEfectivo = x.EsEfectivo,
                    Activa = x.Activa,
                    TitularId = titular.Id,
                    TitularNombre = titular.Nombre,
                    TitularTipo = titular.Tipo.ToString()
                };
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, LatestExtractInfo>> GetLatestExtractsByCuentaAsync(
        IReadOnlyCollection<Guid> cuentaIds,
        CancellationToken cancellationToken)
    {
        if (cuentaIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId))
            .GroupBy(x => x.CuentaId)
            .Select(g => g
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FilaNumero)
                .Select(x => new LatestExtractInfo
                {
                    CuentaId = x.CuentaId,
                    Saldo = x.Saldo,
                    Fecha = x.Fecha,
                    FilaNumero = x.FilaNumero
                })
                .First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.CuentaId);
    }

    private async Task<Dictionary<Guid, MonthTotalsInfo>> GetMonthTotalsByCuentaAsync(
        IReadOnlyCollection<Guid> cuentaIds,
        CancellationToken cancellationToken)
    {
        if (cuentaIds.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var rows = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.CuentaId) && x.Fecha >= monthStart && x.Fecha <= today)
            .Select(x => new { x.CuentaId, x.Monto })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.CuentaId)
            .ToDictionary(
                x => x.Key,
                x => new MonthTotalsInfo
                {
                    Ingresos = x.Where(item => item.Monto > 0m).Sum(item => item.Monto),
                    Egresos = x.Where(item => item.Monto < 0m).Sum(item => item.Monto)
                });
    }

    private async Task<Dictionary<Guid, ResolvedAlertInfo>> GetResolvedAlertsByCuentaAsync(
        IReadOnlyCollection<Guid> cuentaIds,
        CancellationToken cancellationToken)
    {
        if (cuentaIds.Count == 0)
        {
            return [];
        }

        var alerts = await _dbContext.AlertasSaldo
            .AsNoTracking()
            .Where(x => x.CuentaId == null || (x.CuentaId.HasValue && cuentaIds.Contains(x.CuentaId.Value)))
            .OrderByDescending(x => x.FechaCreacion)
            .ToListAsync(cancellationToken);

        var global = alerts.FirstOrDefault(x => x.CuentaId == null);
        var byCuenta = alerts
            .Where(x => x.CuentaId.HasValue)
            .GroupBy(x => x.CuentaId!.Value)
            .ToDictionary(x => x.Key, x => x.First());

        var result = new Dictionary<Guid, ResolvedAlertInfo>();
        foreach (var cuentaIdValue in cuentaIds)
        {
            var alert = byCuenta.GetValueOrDefault(cuentaIdValue) ?? global;
            if (alert is null)
            {
                continue;
            }

            result[cuentaIdValue] = new ResolvedAlertInfo
            {
                Activa = alert.Activa,
                SaldoMinimo = alert.SaldoMinimo
            };
        }

        return result;
    }

    private async Task<string> ResolvePrincipalCurrencyAsync(CancellationToken cancellationToken)
    {
        var activeBase = await _dbContext.DivisasActivas
            .AsNoTracking()
            .Where(x => x.Activa && x.EsBase)
            .OrderBy(x => x.Codigo)
            .Select(x => x.Codigo)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeBase))
        {
            return activeBase.Trim().ToUpperInvariant();
        }

        var configValue = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x => x.Clave == "divisa_principal_default")
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return NormalizeDivisa(configValue) ?? "EUR";
    }

    private async Task<Dictionary<string, object?>> BuildRateSnapshotAsync(
        string principalCurrency,
        IEnumerable<string> currencies,
        CancellationToken cancellationToken)
    {
        var distinctCurrencies = currencies
            .Select(NormalizeDivisa)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals(principalCurrency, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var rates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (distinctCurrencies.Count == 0)
        {
            rates["fecha_actualizacion"] = null;
            return rates;
        }

        var rateRows = await _dbContext.TiposCambio
            .AsNoTracking()
            .Where(x =>
                (x.DivisaOrigen == principalCurrency && distinctCurrencies.Contains(x.DivisaDestino)) ||
                (x.DivisaDestino == principalCurrency && distinctCurrencies.Contains(x.DivisaOrigen)))
            .ToListAsync(cancellationToken);

        foreach (var currency in distinctCurrencies)
        {
            var rate = await _tiposCambioService.ConvertAsync(1m, principalCurrency, currency, cancellationToken);
            rates[$"{principalCurrency}_{currency}"] = Decimal.Round(rate, 8);
        }

        rates["fecha_actualizacion"] = rateRows.Count == 0 ? null : rateRows.Max(x => x.FechaActualizacion);
        return rates;
    }

    private async Task<decimal> ConvertSaldoTotalAsync(
        IReadOnlyDictionary<Guid, decimal> saldos,
        IReadOnlyDictionary<Guid, string> currencyByCuenta,
        string principalCurrency,
        CancellationToken cancellationToken)
    {
        decimal total = 0m;
        foreach (var entry in saldos)
        {
            if (!currencyByCuenta.TryGetValue(entry.Key, out var currency))
            {
                continue;
            }

            total += await _tiposCambioService.ConvertAsync(entry.Value, currency, principalCurrency, cancellationToken);
        }

        return total;
    }

    private bool TryGetToken(out IntegrationToken token)
    {
        token = null!;
        if (!HttpContext.Items.TryGetValue(IntegrationHttpContextItemKeys.CurrentIntegrationToken, out var item))
        {
            return false;
        }

        token = item as IntegrationToken ?? null!;
        return token is not null;
    }

    private IActionResult IntegrationError(int statusCode, string error)
    {
        return StatusCode(statusCode, IntegrationApiResponses.Failure(error));
    }

    private static bool TryResolveFormat(string? format, out bool isSimple)
    {
        var normalized = (format ?? "full").Trim().ToLowerInvariant();
        isSimple = normalized == "simple";
        return normalized is "full" or "simple";
    }

    private static string ResolveMovementType(decimal monto) => monto >= 0m ? "INGRESO" : "EGRESO";

    private static string ResolveAlertState(decimal saldoActual, ResolvedAlertInfo? alerta)
    {
        if (alerta is null)
        {
            return "sin_configuracion";
        }

        if (!alerta.Activa)
        {
            return "inactiva";
        }

        return saldoActual < alerta.SaldoMinimo ? "activada" : "ok";
    }

    private static string? NormalizePeriodo(string? periodo)
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
            _ => null
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

    private sealed class ScopedCuentaInfo
    {
        public Guid CuentaId { get; set; }
        public string CuentaNombre { get; set; } = string.Empty;
        public string? Iban { get; set; }
        public string? NumeroCuenta { get; set; }
        public string Divisa { get; set; } = "EUR";
        public bool EsEfectivo { get; set; }
        public bool Activa { get; set; }
        public Guid TitularId { get; set; }
        public string TitularNombre { get; set; } = string.Empty;
        public string TitularTipo { get; set; } = string.Empty;
    }

    private sealed class LatestExtractInfo
    {
        public Guid CuentaId { get; set; }
        public decimal Saldo { get; set; }
        public DateOnly Fecha { get; set; }
        public int FilaNumero { get; set; }
    }

    private sealed class MonthTotalsInfo
    {
        public decimal Ingresos { get; set; }
        public decimal Egresos { get; set; }
    }

    private sealed class ResolvedAlertInfo
    {
        public bool Activa { get; set; }
        public decimal SaldoMinimo { get; set; }
    }

    private sealed class EvolucionExtractRow
    {
        public Guid CuentaId { get; set; }
        public DateOnly Fecha { get; set; }
        public decimal Monto { get; set; }
        public decimal Saldo { get; set; }
    }

    private sealed class EvolucionPoint
    {
        public DateOnly Fecha { get; set; }
        public decimal Saldo { get; set; }
        public decimal Ingresos { get; set; }
        public decimal Egresos { get; set; }
        public decimal Neto { get; set; }
    }

    private readonly record struct DateRange(DateOnly Start, DateOnly End);
}
