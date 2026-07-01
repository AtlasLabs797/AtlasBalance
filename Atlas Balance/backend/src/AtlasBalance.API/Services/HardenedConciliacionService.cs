using System.Globalization;
using System.Text;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public sealed class HardenedConciliacionService : IConciliacionService
{
    private const string ToleranceAmountKey = "conciliacion_tolerance_amount";
    private const string TolerancePercentKey = "conciliacion_tolerance_percent";
    private const decimal DefaultToleranceAmount = 2m;
    private const decimal DefaultTolerancePercent = 0.01m;

    private static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly ConciliacionService _inner;
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public HardenedConciliacionService(ConciliacionService inner, AppDbContext dbContext, IAuditService auditService)
    {
        _inner = inner;
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public Task<IReadOnlyList<MovimientoEsperadoResponse>> ListarMovimientosEsperadosAsync(Guid usuarioId, string rol, Guid? cuentaId, string? estado, CancellationToken cancellationToken) =>
        _inner.ListarMovimientosEsperadosAsync(usuarioId, rol, cuentaId, estado, cancellationToken);

    public Task<MovimientoEsperadoResponse> CrearMovimientoEsperadoAsync(Guid usuarioId, string rol, MovimientoEsperadoCrearRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
        _inner.CrearMovimientoEsperadoAsync(usuarioId, rol, request, httpContext, cancellationToken);

    public Task<IReadOnlyList<ConciliacionResponse>> ListarConciliacionesAsync(Guid usuarioId, string rol, Guid? cuentaId, string? estado, CancellationToken cancellationToken) =>
        _inner.ListarConciliacionesAsync(usuarioId, rol, cuentaId, estado, cancellationToken);

    public Task<ConciliacionResponse> ConfirmarAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
        _inner.ConfirmarAsync(usuarioId, rol, id, request, httpContext, cancellationToken);

    public Task<ConciliacionResponse> MarcarExcepcionAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
        _inner.MarcarExcepcionAsync(usuarioId, rol, id, request, httpContext, cancellationToken);

    public Task<ConciliacionResponse> ResolverAsync(Guid usuarioId, string rol, Guid id, ConciliacionCambiarEstadoRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
        _inner.ResolverAsync(usuarioId, rol, id, request, httpContext, cancellationToken);

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
            await EnsureCuentaPermitidaAsync(usuarioId, rol, request.CuentaId.Value, cancellationToken);
        }

        var movimientosQuery = ApplyCuentaScope(_dbContext.MovimientosEsperados, usuarioId, rol)
            .Where(x => x.Estado == "pendiente" || x.Estado == "sugerida");
        if (request.CuentaId.HasValue)
        {
            movimientosQuery = movimientosQuery.Where(x => x.CuentaId == request.CuentaId.Value);
        }

        var movimientos = await movimientosQuery
            .OrderBy(x => x.FechaEsperada)
            .Take(1000)
            .ToListAsync(cancellationToken);
        var created = new List<Conciliacion>();

        foreach (var movimiento in movimientos)
        {
            var best = await FindBestMatchAsync(movimiento, ventanaDias, cancellationToken);
            if (best is null)
            {
                continue;
            }

            var existing = await _dbContext.Conciliaciones
                .FirstOrDefaultAsync(x =>
                    x.MovimientoEsperadoId == movimiento.Id &&
                    x.ExtractoId == best.Extracto.Id,
                    cancellationToken);
            if (existing is not null)
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

    private async Task<MatchCandidate?> FindBestMatchAsync(MovimientoEsperado movimiento, int ventanaDias, CancellationToken cancellationToken)
    {
        var start = movimiento.FechaEsperada.AddDays(-ventanaDias);
        var end = movimiento.FechaEsperada.AddDays(ventanaDias);
        var tolerance = await GetToleranceAsync(cancellationToken);
        var amountTolerance = Math.Max(tolerance.Amount, Math.Abs(movimiento.Monto) * tolerance.Percent);
        var minAmount = movimiento.Monto - amountTolerance;
        var maxAmount = movimiento.Monto + amountTolerance;
        var alreadyMatchedExtractos = _dbContext.Conciliaciones
            .Where(x => x.ExtractoId != null && x.Estado == "conciliada")
            .Select(x => x.ExtractoId!.Value);
        var extractos = await _dbContext.Extractos
            .AsNoTracking()
            .Where(x =>
                x.CuentaId == movimiento.CuentaId &&
                x.Monto >= minAmount &&
                x.Monto <= maxAmount &&
                x.Fecha >= start &&
                x.Fecha <= end &&
                !alreadyMatchedExtractos.Contains(x.Id))
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.FilaNumero)
            .ToListAsync(cancellationToken);

        return extractos
            .Select(extracto => Score(movimiento, extracto, amountTolerance))
            .Where(x => x.Score >= 70)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Math.Abs(x.DiferenciaDias))
            .ThenBy(x => x.Extracto.Fecha)
            .FirstOrDefault();
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

    private IQueryable<MovimientoEsperado> ApplyCuentaScope(IQueryable<MovimientoEsperado> query, Guid usuarioId, string rol)
    {
        if (string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        return query.Where(m =>
            _dbContext.Cuentas.Any(c =>
                c.Id == m.CuentaId &&
                _dbContext.PermisosUsuario.Any(p =>
                    p.UsuarioId == usuarioId &&
                    (p.PuedeConciliar || p.PuedeCerrarConciliacion) &&
                    (p.PaisId == null || p.PaisId == c.PaisId) &&
                    (p.TitularId == null || p.TitularId == c.TitularId) &&
                    (p.CuentaId == null || p.CuentaId == c.Id))));
    }

    private async Task EnsureCuentaPermitidaAsync(Guid usuarioId, string rol, Guid cuentaId, CancellationToken cancellationToken)
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
            return;
        }

        var allowed = await _dbContext.PermisosUsuario.AnyAsync(p =>
            p.UsuarioId == usuarioId &&
            (p.PuedeConciliar || p.PuedeCerrarConciliacion) &&
            (p.PaisId == null || p.PaisId == cuenta.PaisId) &&
            (p.TitularId == null || p.TitularId == cuenta.TitularId) &&
            (p.CuentaId == null || p.CuentaId == cuenta.Id),
            cancellationToken);
        if (!allowed)
        {
            throw new ConciliacionException("No tienes permiso para conciliar esta cuenta", StatusCodes.Status403Forbidden);
        }
    }

    private async Task<IReadOnlyList<ConciliacionResponse>> MapConciliacionesAsync(IReadOnlyList<Conciliacion> conciliaciones, CancellationToken cancellationToken)
    {
        var cuentaIds = conciliaciones.Select(x => x.CuentaId).Distinct().ToList();
        var movimientoIds = conciliaciones.Select(x => x.MovimientoEsperadoId).Distinct().ToList();
        var extractoIds = conciliaciones.Where(x => x.ExtractoId.HasValue).Select(x => x.ExtractoId!.Value).Distinct().ToList();
        var cuentas = await _dbContext.Cuentas.AsNoTracking().Where(x => cuentaIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);
        var movimientos = await _dbContext.MovimientosEsperados.AsNoTracking().Where(x => movimientoIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var extractos = await _dbContext.Extractos.AsNoTracking().Where(x => extractoIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var movimientoResponses = movimientos.Values.Select(x => new MovimientoEsperadoResponse
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
        }).ToDictionary(x => x.Id);

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

    private sealed record ConciliacionTolerance(decimal Amount, decimal Percent);

    private sealed record MatchCandidate(Extracto Extracto, int Score, int DiferenciaDias, string? ReferenciaNormalizada, string? ConceptoNormalizado);
}
