using System.Globalization;
using System.Text;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IConciliacionService
{
    Task<IReadOnlyList<MovimientoEsperadoResponse>> ListarMovimientosEsperadosAsync(Guid usuarioId, string rol, Guid? cuentaId, string? estado, CancellationToken cancellationToken);
    Task<MovimientoEsperadoResponse> CrearMovimientoEsperadoAsync(Guid usuarioId, string rol, MovimientoEsperadoCrearRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConciliacionResponse>> ListarConciliacionesAsync(Guid usuarioId, string rol, Guid? cuentaId, string? estado, CancellationToken cancellationToken);
    Task<ConciliacionSugerenciasResponse> SugerirAsync(Guid usuarioId, string rol, ConciliacionSugerirRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ConciliacionResponse> ConfirmarAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ConciliacionResponse> MarcarExcepcionAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    Task<ConciliacionResponse> ResolverAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
}

public sealed class ConciliacionService : IConciliacionService
{
    private const string ToleranceAmountKey = "conciliacion_tolerance_amount";
    private const string TolerancePercentKey = "conciliacion_tolerance_percent";
    private const decimal DefaultToleranceAmount = 2m;
    private const decimal DefaultTolerancePercent = 0.01m;

    internal static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public ConciliacionService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<MovimientoEsperadoResponse>> ListarMovimientosEsperadosAsync(
        Guid usuarioId,
        string rol,
        Guid? cuentaId,
        string? estado,
        CancellationToken cancellationToken)
    {
        if (cuentaId.HasValue)
        {
            await EnsureCuentaPermitidaAsync(usuarioId, rol, cuentaId.Value, cerrar: false, cancellationToken);
        }

        var query = ApplyCuentaScope(_dbContext.MovimientosEsperados.AsNoTracking(), usuarioId, rol);
        if (cuentaId.HasValue)
        {
            query = query.Where(x => x.CuentaId == cuentaId.Value);
        }

        var normalizedEstado = NormalizeEstado(estado);
        if (normalizedEstado is not null)
        {
            query = query.Where(x => x.Estado == normalizedEstado);
        }

        var rows = await query
            .OrderByDescending(x => x.FechaEsperada)
            .ThenByDescending(x => x.FechaCreacion)
            .Take(500)
            .ToListAsync(cancellationToken);

        return await MapMovimientosAsync(rows, cancellationToken);
    }

    public async Task<MovimientoEsperadoResponse> CrearMovimientoEsperadoAsync(
        Guid usuarioId,
        string rol,
        MovimientoEsperadoCrearRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var cuenta = await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId, cerrar: false, cancellationToken);
        if (request.FechaEsperada == default)
        {
            throw new ConciliacionException("La fecha esperada es obligatoria", StatusCodes.Status400BadRequest);
        }

        if (request.Monto == 0m)
        {
            throw new ConciliacionException("El importe esperado no puede ser cero", StatusCodes.Status400BadRequest);
        }

        var movimiento = new MovimientoEsperado
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            FechaEsperada = request.FechaEsperada,
            Monto = request.Monto,
            Divisa = string.IsNullOrWhiteSpace(request.Divisa) ? cuenta.Divisa : request.Divisa.Trim().ToUpperInvariant(),
            Referencia = NormalizeOptionalText(request.Referencia),
            Concepto = NormalizeOptionalText(request.Concepto),
            Estado = "pendiente",
            Origen = string.IsNullOrWhiteSpace(request.Origen) ? "manual" : request.Origen.Trim().ToLowerInvariant(),
            UsuarioCreacionId = usuarioId,
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.MovimientosEsperados.Add(movimiento);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuarioId,
            "conciliacion_movimiento_esperado_creado",
            "MOVIMIENTOS_ESPERADOS",
            movimiento.Id,
            httpContext,
            JsonSerializer.Serialize(new { movimiento.CuentaId, movimiento.Monto, movimiento.FechaEsperada }, SnakeCaseJsonOptions),
            cancellationToken);

        return (await MapMovimientosAsync([movimiento], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<ConciliacionResponse>> ListarConciliacionesAsync(
        Guid usuarioId,
        string rol,
        Guid? cuentaId,
        string? estado,
        CancellationToken cancellationToken)
    {
        if (cuentaId.HasValue)
        {
            await EnsureCuentaPermitidaAsync(usuarioId, rol, cuentaId.Value, cerrar: false, cancellationToken);
        }

        var query = ApplyCuentaScope(_dbContext.Conciliaciones.AsNoTracking(), usuarioId, rol);
        if (cuentaId.HasValue)
        {
            query = query.Where(x => x.CuentaId == cuentaId.Value);
        }

        var normalizedEstado = NormalizeEstado(estado);
        if (normalizedEstado is not null)
        {
            query = query.Where(x => x.Estado == normalizedEstado);
        }

        var rows = await query
            .OrderByDescending(x => x.FechaCreacion)
            .Take(500)
            .ToListAsync(cancellationToken);

        return await MapConciliacionesAsync(rows, cancellationToken);
    }

    public async Task<ConciliacionSugerenciasResponse> SugerirAsync(
        Guid usuarioId,
        string rol,
        ConciliacionSugerirRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ventanaDias = Math.Clamp(request.VentanaDias, 0, 10);
        if (request.CuentaId.HasValue)
        {
            await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId.Value, cerrar: false, cancellationToken);
        }

        var movimientosQuery = ApplyCuentaScope(_dbContext.MovimientosEsperados, usuarioId, rol, soloPuedeConciliar: true)
            .Where(x => x.Estado == "pendiente" || x.Estado == "sugerida");
        if (request.CuentaId.HasValue)
        {
            movimientosQuery = movimientosQuery.Where(x => x.CuentaId == request.CuentaId.Value);
        }

        var movimientos = await movimientosQuery
            .OrderBy(x => x.FechaEsperada)
            .Take(1000)
            .ToListAsync(cancellationToken);
        if (movimientos.Count == 0)
        {
            return new ConciliacionSugerenciasResponse
            {
                MovimientosEvaluados = 0,
                SugerenciasCreadas = 0,
                Sugerencias = []
            };
        }

        var tolerance = await GetToleranceAsync(cancellationToken);

        // V-02.06 (MED-16): cargar TODOS los extractos candidatos en UNA sola
        // query que cubra la ventana minima y maxima del lote, en lugar de N
        // awaits por movimiento. Luego emparejamos en memoria por
        // (CuentaId, Monto) usando tolerancia individual. Mantenemos la
        // exclusion de extractos ya conciliados en la misma query.
        var minFecha = movimientos.Min(x => x.FechaEsperada).AddDays(-ventanaDias);
        var maxFecha = movimientos.Max(x => x.FechaEsperada).AddDays(ventanaDias);
        var cuentaIds = movimientos.Select(x => x.CuentaId).Distinct().ToList();
        var minMonto = movimientos.Min(x => x.Monto);
        var maxMonto = movimientos.Max(x => x.Monto);
        // V-02.06 (PR F2): el rango global usa el maximo absoluto del lote
        // (negativos y positivos). Antes se tomaba `maxMonto` (monto firmado)
        // y despues se aplicaba Abs, lo que truncaba candidatos para lotes
        // con negativos grandes y positivos pequenos (ej: lote con
        // `-100000`/`10` -> ventana global de +-10 ignoraba un extracto a
        // `-99500` aun siendo valido).
        var maxAbsoluteMonto = movimientos.Max(x => Math.Abs(x.Monto));
        var globalAmountTolerance = Math.Max(tolerance.Amount, maxAbsoluteMonto * tolerance.Percent);
        var minAmountGlobal = Math.Min(minMonto, maxMonto) - globalAmountTolerance;
        var maxAmountGlobal = Math.Max(minMonto, maxMonto) + globalAmountTolerance;

        var alreadyMatchedExtractos = _dbContext.Conciliaciones
            .Where(x => x.ExtractoId != null && x.Estado == "conciliada")
            .Select(x => x.ExtractoId!.Value);

        var extractosCandidatos = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x =>
                cuentaIds.Contains(x.CuentaId) &&
                x.Monto >= minAmountGlobal &&
                x.Monto <= maxAmountGlobal &&
                x.Fecha >= minFecha &&
                x.Fecha <= maxFecha &&
                !alreadyMatchedExtractos.Contains(x.Id))
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.FilaNumero)
            .ToListAsync(cancellationToken);

        var extractosPorCuentaYMonto = extractosCandidatos
            .GroupBy(x => (x.CuentaId, x.Monto))
            .ToDictionary(g => g.Key, g => g.ToList());

        var existingPairs = await _dbContext.Conciliaciones
            .Where(x => movimientos.Select(m => m.Id).Contains(x.MovimientoEsperadoId))
            .ToDictionaryAsync(
                x => (x.MovimientoEsperadoId, x.ExtractoId ?? Guid.Empty),
                cancellationToken);

        var created = new List<Conciliacion>();
        foreach (var movimiento in movimientos)
        {
            var best = FindBestMatchInMemory(movimiento, ventanaDias, tolerance, extractosPorCuentaYMonto);
            if (best is null)
            {
                continue;
            }

            if (existingPairs.TryGetValue((movimiento.Id, best.Extracto.Id), out var existing))
            {
                existing.Score = best.Score;
                existing.DiferenciaDias = best.DiferenciaDias;
                existing.ReferenciaNormalizada = best.ReferenciaNormalizada;
                existing.ConceptoNormalizado = best.ConceptoNormalizado;
                existing.FechaModificacion = DateTime.UtcNow;
                created.Add(existing);
                continue;
            }

            var conciliacion = new Conciliacion
            {
                Id = Guid.NewGuid(),
                CuentaId = movimiento.CuentaId,
                MovimientoEsperadoId = movimiento.Id,
                ExtractoId = best.Extracto.Id,
                Estado = "sugerida",
                Score = best.Score,
                Regla = "deterministica-v1",
                DiferenciaDias = best.DiferenciaDias,
                ReferenciaNormalizada = best.ReferenciaNormalizada,
                ConceptoNormalizado = best.ConceptoNormalizado,
                UsuarioSugerenciaId = usuarioId,
                FechaSugerencia = DateTime.UtcNow,
                FechaCreacion = DateTime.UtcNow
            };
            _dbContext.Conciliaciones.Add(conciliacion);
            movimiento.Estado = "sugerida";
            movimiento.FechaModificacion = DateTime.UtcNow;
            movimiento.UsuarioModificacionId = usuarioId;
            created.Add(conciliacion);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuarioId,
            "conciliacion_sugerencias_generadas",
            "CONCILIACIONES",
            request.CuentaId,
            httpContext,
            JsonSerializer.Serialize(new { movimientos_evaluados = movimientos.Count, sugerencias = created.Count, ventana_dias = ventanaDias }, SnakeCaseJsonOptions),
            cancellationToken);

        return new ConciliacionSugerenciasResponse
        {
            MovimientosEvaluados = movimientos.Count,
            SugerenciasCreadas = created.Count,
            Sugerencias = await MapConciliacionesAsync(created, cancellationToken)
        };
    }

    public Task<ConciliacionResponse> ConfirmarAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        return SetEstadoAsync(usuarioId, rol, id, "conciliada", request, httpContext, cerrar: false, cancellationToken);
    }

    public Task<ConciliacionResponse> MarcarExcepcionAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        return SetEstadoAsync(usuarioId, rol, id, "excepcion", request, httpContext, cerrar: false, cancellationToken);
    }

    public Task<ConciliacionResponse> ResolverAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        return SetEstadoAsync(usuarioId, rol, id, "resuelta", request, httpContext, cerrar: true, cancellationToken);
    }

    private async Task<ConciliacionResponse> SetEstadoAsync(
        Guid usuarioId,
        string rol,
        Guid id,
        string estado,
        ConciliacionCambiarEstadoRequest request,
        HttpContext httpContext,
        bool cerrar,
        CancellationToken cancellationToken)
    {
        var conciliacion = await _dbContext.Conciliaciones.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (conciliacion is null)
        {
            throw new ConciliacionException("Conciliacion no encontrada", StatusCodes.Status404NotFound);
        }

        await EnsureCuentaPermitidaAsync(usuarioId, rol, conciliacion.CuentaId, cerrar, cancellationToken);
        var movimiento = await _dbContext.MovimientosEsperados.FirstAsync(x => x.Id == conciliacion.MovimientoEsperadoId, cancellationToken);
        conciliacion.Estado = estado;
        conciliacion.Observacion = NormalizeOptionalText(request.Observacion);
        conciliacion.FechaModificacion = DateTime.UtcNow;
        movimiento.Estado = estado;
        movimiento.FechaModificacion = DateTime.UtcNow;
        movimiento.UsuarioModificacionId = usuarioId;

        if (estado == "conciliada")
        {
            conciliacion.UsuarioConfirmacionId = usuarioId;
            conciliacion.FechaConfirmacion = DateTime.UtcNow;
        }
        else if (estado == "resuelta")
        {
            conciliacion.UsuarioResolucionId = usuarioId;
            conciliacion.FechaResolucion = DateTime.UtcNow;
        }

        if (movimiento.UsuarioCreacionId == usuarioId && estado is "conciliada" or "resuelta")
        {
            _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
            {
                Id = Guid.NewGuid(),
                Tipo = "maker_checker_conciliacion",
                Mensaje = "El mismo usuario creo y cerro una conciliacion.",
                Fecha = DateTime.UtcNow,
                DetallesJson = JsonSerializer.Serialize(new { conciliacion_id = id, usuario_id = usuarioId, estado }, SnakeCaseJsonOptions)
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuarioId,
            $"conciliacion_{estado}",
            "CONCILIACIONES",
            id,
            httpContext,
            JsonSerializer.Serialize(new { conciliacion.CuentaId, estado, conciliacion.Score, maker_checker_warning = movimiento.UsuarioCreacionId == usuarioId }, SnakeCaseJsonOptions),
            cancellationToken);

        return (await MapConciliacionesAsync([conciliacion], cancellationToken))[0];
    }

    private static MatchCandidate? FindBestMatchInMemory(
        MovimientoEsperado movimiento,
        int ventanaDias,
        ConciliacionTolerance tolerance,
        Dictionary<(Guid CuentaId, decimal Monto), List<Extracto>> extractosPorCuentaYMonto)
    {
        var start = movimiento.FechaEsperada.AddDays(-ventanaDias);
        var end = movimiento.FechaEsperada.AddDays(ventanaDias);
        var amountTolerance = Math.Max(tolerance.Amount, Math.Abs(movimiento.Monto) * tolerance.Percent);

        // Empezamos probando con el monto exacto; si no hay match dentro de la
        // ventana temporal, probamos con tolerancia ampliando el margen por
        // (CuentaId) sobre los extractos ya cargados.
        if (extractosPorCuentaYMonto.TryGetValue((movimiento.CuentaId, movimiento.Monto), out var exactos))
        {
            var candidato = exactos
                .Where(x => x.Fecha >= start && x.Fecha <= end)
                .Select(extracto => Score(movimiento, extracto, amountTolerance))
                .Where(x => x.Score >= 70)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => Math.Abs(x.DiferenciaDias))
                .ThenBy(x => x.Extracto.Fecha)
                .FirstOrDefault();
            if (candidato is not null)
            {
                return candidato;
            }
        }

        // Tolerancia: para cada extracto de la misma cuenta dentro del monto
        // global (ya cargado), evaluamos el score individual.
        var dentroDeVentana = extractosPorCuentaYMonto
            .Where(kv => kv.Key.CuentaId == movimiento.CuentaId)
            .SelectMany(kv => kv.Value)
            .Where(x => x.Fecha >= start && x.Fecha <= end)
            .Select(extracto => Score(movimiento, extracto, amountTolerance))
            .Where(x => x.Score >= 70)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Math.Abs(x.DiferenciaDias))
            .ThenBy(x => x.Extracto.Fecha)
            .FirstOrDefault();
        return dentroDeVentana;
    }

    private async Task<ConciliacionTolerance> GetToleranceAsync(CancellationToken cancellationToken)
    {
        var values = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x => x.Clave == ToleranceAmountKey || x.Clave == TolerancePercentKey)
            .Select(x => new { x.Clave, x.Valor })
            .ToListAsync(cancellationToken);
        var byKey = values.ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        return new ConciliacionTolerance(
            ReadNonNegativeDecimal(byKey, ToleranceAmountKey, DefaultToleranceAmount),
            ReadNonNegativeDecimal(byKey, TolerancePercentKey, DefaultTolerancePercent));
    }

    private static decimal ReadNonNegativeDecimal(IReadOnlyDictionary<string, string> values, string key, decimal fallback)
    {
        if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var normalized = raw.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0m
            ? value
            : fallback;
    }

    private static MatchCandidate Score(MovimientoEsperado movimiento, Extracto extracto, decimal amountTolerance)
    {
        var diffDays = (extracto.Fecha.ToDateTime(TimeOnly.MinValue) - movimiento.FechaEsperada.ToDateTime(TimeOnly.MinValue)).Days;
        var amountDiff = Math.Abs(extracto.Monto - movimiento.Monto);
        var amountPenalty = amountTolerance <= 0m
            ? amountDiff == 0m ? 0 : 100
            : (int)Math.Round(Math.Min(15m, amountDiff / amountTolerance * 15m), MidpointRounding.AwayFromZero);
        var referencia = NormalizeForMatch(movimiento.Referencia);
        var expectedConcept = NormalizeForMatch(movimiento.Concepto);
        var extractConcept = NormalizeForMatch(extracto.Concepto);
        var score = 70 + Math.Max(0, 15 - Math.Abs(diffDays) * 3) - amountPenalty;

        if (!string.IsNullOrWhiteSpace(referencia) && extractConcept.Contains(referencia, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }
        else if (!string.IsNullOrWhiteSpace(expectedConcept) && TextOverlaps(expectedConcept, extractConcept))
        {
            score += 10;
        }

        return new MatchCandidate(
            extracto,
            Math.Clamp(score, 0, 100),
            diffDays,
            referencia,
            string.IsNullOrWhiteSpace(expectedConcept) ? extractConcept : expectedConcept);
    }

    private IQueryable<T> ApplyCuentaScope<T>(IQueryable<T> query, Guid usuarioId, string rol, bool soloPuedeConciliar = false)
        where T : class
    {
        if (string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        // V-02-05 (MED-17): precomputar las cuentas accesibles en un HashSet
        // con un solo join SQL, en lugar del EXISTS por cada fila.
        var accessibleCuentas = ResolveAccessibleCuentas(usuarioId, soloPuedeConciliar);
        if (accessibleCuentas.Count == 0)
        {
            return query.Where(_ => false);
        }

        if (typeof(T) == typeof(MovimientoEsperado))
        {
            return (IQueryable<T>)((IQueryable<MovimientoEsperado>)query).Where(m => accessibleCuentas.Contains(m.CuentaId));
        }
        if (typeof(T) == typeof(Conciliacion))
        {
            return (IQueryable<T>)((IQueryable<Conciliacion>)query).Where(m => accessibleCuentas.Contains(m.CuentaId));
        }
        return query;
    }

    private HashSet<Guid> ResolveAccessibleCuentas(Guid usuarioId, bool soloPuedeConciliar = false)
    {
        // V-02-05 (MED-17): una sola query con join cruzado PermisosUsuario x Cuentas.
        // V-02.08: las cuentas ya excluyen borradas por el query filter global
        // ISoftDelete, pero faltaba exigir TITULAR activo (mismo criterio que
        // UserAccessService.ApplyActiveTitularCuentaScope); sin ello, un usuario
        // seguia viendo movimientos/conciliaciones de cuentas cuyo titular fue
        // eliminado logicamente.
        // `soloPuedeConciliar` restringe a quien puede generar sugerencias
        // (SugerirAsync): un usuario que solo puede cerrar conciliaciones
        // (PuedeCerrarConciliacion) no debe entrar en el lote de sugerencias.
        var rows = (
            from p in _dbContext.PermisosUsuario.AsNoTracking()
            where p.UsuarioId == usuarioId
                  && (soloPuedeConciliar ? p.PuedeConciliar : (p.PuedeConciliar || p.PuedeCerrarConciliacion))
            from c in _dbContext.Cuentas.AsNoTracking()
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            where (p.PaisId == null || p.PaisId == c.PaisId)
               && (p.TitularId == null || p.TitularId == c.TitularId)
               && (p.CuentaId == null || p.CuentaId == c.Id)
               && t.DeletedAt == null
            select c.Id)
            .Distinct()
            .ToList();

        return new HashSet<Guid>(rows);
    }

    private async Task<Cuenta> EnsureCuentaPermitidaAsync(Guid usuarioId, string rol, Guid cuentaId, bool cerrar, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cuentaId && x.Activa, cancellationToken);
        if (cuenta is null)
        {
            throw new ConciliacionException("Cuenta no encontrada o inactiva", StatusCodes.Status404NotFound);
        }

        if (string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return cuenta;
        }

        // V-02.08: mismo criterio de titular activo que la lectura
        // (ResolveAccessibleCuentas / UserAccessService); el ADMIN conserva su
        // bypass de arriba.
        var titularActivo = await _dbContext.Titulares.AsNoTracking()
            .AnyAsync(t => t.Id == cuenta.TitularId && t.DeletedAt == null, cancellationToken);
        if (!titularActivo)
        {
            throw new ConciliacionException("Cuenta no encontrada o inactiva", StatusCodes.Status404NotFound);
        }

        var allowed = await _dbContext.PermisosUsuario.AnyAsync(p =>
            p.UsuarioId == usuarioId &&
            (cerrar
                ? p.PuedeCerrarConciliacion
                : p.PuedeConciliar) &&
            (p.PaisId == null || p.PaisId == cuenta.PaisId) &&
            (p.TitularId == null || p.TitularId == cuenta.TitularId) &&
            (p.CuentaId == null || p.CuentaId == cuenta.Id),
            cancellationToken);
        if (!allowed)
        {
            throw new ConciliacionException("No tienes permisos para conciliar esta cuenta", StatusCodes.Status403Forbidden);
        }

        return cuenta;
    }

    private async Task<IReadOnlyList<MovimientoEsperadoResponse>> MapMovimientosAsync(IReadOnlyList<MovimientoEsperado> movimientos, CancellationToken cancellationToken)
    {
        var cuentaIds = movimientos.Select(x => x.CuentaId).Distinct().ToList();
        var cuentas = await _dbContext.Cuentas
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);

        return movimientos.Select(x => new MovimientoEsperadoResponse
        {
            Id = x.Id,
            CuentaId = x.CuentaId,
            CuentaNombre = cuentas.GetValueOrDefault(x.CuentaId),
            FechaEsperada = x.FechaEsperada,
            Monto = x.Monto,
            Divisa = x.Divisa,
            Referencia = x.Referencia,
            Concepto = x.Concepto,
            Estado = x.Estado,
            Origen = x.Origen,
            UsuarioCreacionId = x.UsuarioCreacionId,
            FechaCreacion = x.FechaCreacion
        }).ToList();
    }

    private async Task<IReadOnlyList<ConciliacionResponse>> MapConciliacionesAsync(IReadOnlyList<Conciliacion> conciliaciones, CancellationToken cancellationToken)
    {
        var cuentaIds = conciliaciones.Select(x => x.CuentaId).Distinct().ToList();
        var movimientoIds = conciliaciones.Select(x => x.MovimientoEsperadoId).Distinct().ToList();
        var extractoIds = conciliaciones.Where(x => x.ExtractoId.HasValue).Select(x => x.ExtractoId!.Value).Distinct().ToList();
        var cuentas = await _dbContext.Cuentas
            .AsNoTracking()
            .Where(x => cuentaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);
        var movimientos = await _dbContext.MovimientosEsperados
            .AsNoTracking()
            .Where(x => movimientoIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var extractos = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x => extractoIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var movimientoResponses = (await MapMovimientosAsync(movimientos.Values.ToList(), cancellationToken)).ToDictionary(x => x.Id);

        return conciliaciones.Select(x => new ConciliacionResponse
        {
            Id = x.Id,
            CuentaId = x.CuentaId,
            CuentaNombre = cuentas.GetValueOrDefault(x.CuentaId),
            MovimientoEsperadoId = x.MovimientoEsperadoId,
            ExtractoId = x.ExtractoId,
            Estado = x.Estado,
            Score = x.Score,
            Regla = x.Regla,
            DiferenciaDias = x.DiferenciaDias,
            ReferenciaNormalizada = x.ReferenciaNormalizada,
            ConceptoNormalizado = x.ConceptoNormalizado,
            Observacion = x.Observacion,
            FechaCreacion = x.FechaCreacion,
            FechaConfirmacion = x.FechaConfirmacion,
            FechaResolucion = x.FechaResolucion,
            MovimientoEsperado = movimientoResponses.GetValueOrDefault(x.MovimientoEsperadoId),
            Extracto = x.ExtractoId.HasValue && extractos.TryGetValue(x.ExtractoId.Value, out var extracto)
                ? new ExtractoConciliacionResponse
                {
                    Id = extracto.Id,
                    Fecha = extracto.Fecha,
                    Concepto = extracto.Concepto,
                    Monto = extracto.Monto,
                    Saldo = extracto.Saldo,
                    FilaNumero = extracto.FilaNumero
                }
                : null
        }).ToList();
    }

    private static string? NormalizeEstado(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
        {
            return null;
        }

        var normalized = estado.Trim().ToLowerInvariant();
        return normalized is "pendiente" or "sugerida" or "conciliada" or "excepcion" or "resuelta"
            ? normalized
            : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool TextOverlaps(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var tokens = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 4)
            .ToList();
        return tokens.Count > 0 && tokens.Count(actual.Contains) >= Math.Max(1, tokens.Count / 2);
    }

    private sealed record MatchCandidate(
        Extracto Extracto,
        int Score,
        int DiferenciaDias,
        string? ReferenciaNormalizada,
        string? ConceptoNormalizado);

    private sealed record ConciliacionTolerance(decimal Amount, decimal Percent);
}

public sealed class ConciliacionException : Exception
{
    public ConciliacionException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
