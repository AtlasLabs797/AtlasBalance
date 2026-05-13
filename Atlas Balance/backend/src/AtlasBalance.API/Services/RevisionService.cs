using System.Globalization;
using System.Text;
using System.Linq.Expressions;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IRevisionService
{
    Task<PaginatedResponse<RevisionComisionItemResponse>> GetComisionesAsync(UserAccessScope scope, RevisionQueryRequest request, CancellationToken cancellationToken);
    Task<PaginatedResponse<RevisionSeguroItemResponse>> GetSegurosAsync(UserAccessScope scope, RevisionQueryRequest request, CancellationToken cancellationToken);
    Task SetEstadoAsync(UserAccessScope scope, Guid extractoId, string tipo, string estado, CancellationToken cancellationToken);
    Task<RevisionSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);
}

public sealed class RevisionService : IRevisionService
{
    public const string TipoComision = "COMISION";
    public const string TipoSeguro = "SEGURO";
    public const string EstadoPendiente = "PENDIENTE";
    public const string EstadoDevuelta = "DEVUELTA";
    public const string EstadoCorrecto = "CORRECTO";
    public const string EstadoDescartada = "DESCARTADA";

    private static readonly string[] ComisionTerms =
    [
        "comision",
        "cuota",
        "mantenimiento",
        "administracion",
        "servicio",
        "reclamacion",
        "descubierto",
        "tarjeta",
        "transferencia",
        "gastos bancarios"
    ];

    private static readonly string[] ComisionSearchTerms =
    [
        "comision",
        "comisión",
        "cuota",
        "mantenimiento",
        "administracion",
        "administración",
        "servicio",
        "reclamacion",
        "reclamación",
        "descubierto",
        "tarjeta",
        "transferencia",
        "gastos bancarios"
    ];

    private static readonly string[] SeguroTerms =
    [
        "seguro",
        "aseguradora",
        "poliza",
        "prima",
        "mapfre",
        "allianz",
        "axa",
        "catalana occidente",
        "generali",
        "zurich",
        "mutua",
        "occidente"
    ];

    private static readonly string[] SeguroSearchTerms =
    [
        "seguro",
        "aseguradora",
        "poliza",
        "póliza",
        "prima",
        "mapfre",
        "allianz",
        "axa",
        "catalana occidente",
        "generali",
        "zurich",
        "mutua",
        "occidente"
    ];

    private readonly AppDbContext _dbContext;
    private readonly IUserAccessService _userAccessService;

    public RevisionService(AppDbContext dbContext, IUserAccessService userAccessService)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
    }

    public async Task<PaginatedResponse<RevisionComisionItemResponse>> GetComisionesAsync(
        UserAccessScope scope,
        RevisionQueryRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var estadoFiltro = NormalizeEstadoFilter(request.Estado);
        var page = NormalizePage(request.Page);
        var pageSize = NormalizePageSize(request.PageSize);
        var query = BuildRevisionBaseQuery(scope, TipoComision, ComisionSearchTerms)
            .Where(x => x.Monto > settings.ComisionesImporteMinimo || x.Monto < -settings.ComisionesImporteMinimo)
            .Select(x => new RevisionComisionItemResponse
            {
                ExtractoId = x.ExtractoId,
                CuentaId = x.CuentaId,
                TitularId = x.TitularId,
                Titular = x.Titular,
                Cuenta = x.Cuenta,
                Divisa = x.Divisa,
                Fecha = x.Fecha,
                Monto = x.Monto,
                Concepto = x.Concepto,
                EstadoDevolucion = x.Estado
            });

        if (estadoFiltro is not null)
        {
            query = query.Where(x => x.EstadoDevolucion == estadoFiltro);
        }
        else
        {
            query = query.Where(x => x.EstadoDevolucion != EstadoDescartada);
        }

        query = query
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Monto < 0 ? -x.Monto : x.Monto);

        return await ToPaginatedResponseAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<PaginatedResponse<RevisionSeguroItemResponse>> GetSegurosAsync(
        UserAccessScope scope,
        RevisionQueryRequest request,
        CancellationToken cancellationToken)
    {
        var estadoFiltro = NormalizeEstadoFilter(request.Estado);
        var page = NormalizePage(request.Page);
        var pageSize = NormalizePageSize(request.PageSize);
        var query = BuildRevisionBaseQuery(scope, TipoSeguro, SeguroSearchTerms)
            .Select(x => new RevisionSeguroItemResponse
            {
                ExtractoId = x.ExtractoId,
                CuentaId = x.CuentaId,
                TitularId = x.TitularId,
                Titular = x.Titular,
                Cuenta = x.Cuenta,
                Divisa = x.Divisa,
                Fecha = x.Fecha,
                Importe = x.Monto,
                Concepto = x.Concepto,
                Estado = x.Estado
            });

        if (estadoFiltro is not null)
        {
            query = query.Where(x => x.Estado == estadoFiltro);
        }
        else
        {
            query = query.Where(x => x.Estado != EstadoDescartada);
        }

        query = query
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Importe < 0 ? -x.Importe : x.Importe);

        return await ToPaginatedResponseAsync(query, page, pageSize, cancellationToken);
    }

    public async Task SetEstadoAsync(UserAccessScope scope, Guid extractoId, string tipo, string estado, CancellationToken cancellationToken)
    {
        var normalizedTipo = NormalizeTipo(tipo);
        var normalizedEstado = NormalizeEstado(normalizedTipo, estado);
        var extracto = await _dbContext.Extractos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == extractoId, cancellationToken);

        if (extracto is null)
        {
            throw new InvalidOperationException("Extracto no encontrado.");
        }

        if (!await _userAccessService.CanEditCuentaAsync(extracto.CuentaId, scope, cancellationToken))
        {
            throw new UnauthorizedAccessException();
        }

        var current = await _dbContext.RevisionExtractoEstados
            .FirstOrDefaultAsync(x => x.ExtractoId == extractoId && x.Tipo == normalizedTipo, cancellationToken);

        if (current is null)
        {
            current = new RevisionExtractoEstado
            {
                Id = Guid.NewGuid(),
                ExtractoId = extractoId,
                Tipo = normalizedTipo
            };
            _dbContext.RevisionExtractoEstados.Add(current);
        }

        current.Estado = normalizedEstado;
        current.FechaModificacion = DateTime.UtcNow;
        current.UsuarioModificacionId = scope.UserId == Guid.Empty ? null : scope.UserId;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RevisionSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var raw = await _dbContext.Configuraciones
            .Where(x => x.Clave == "revision_comisiones_importe_minimo")
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        var threshold = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1m;

        return new RevisionSettingsResponse
        {
            ComisionesImporteMinimo = Math.Max(0m, threshold)
        };
    }

    public static bool IsCommissionConcept(string? concept) => ContainsAnyTerm(concept, ComisionTerms);

    public static bool IsInsuranceConcept(string? concept) => ContainsAnyTerm(concept, SeguroTerms);

    private IQueryable<RevisionRawRow> BuildRevisionBaseQuery(UserAccessScope scope, string tipo, IReadOnlyList<string> terms)
    {
        var cuentasQuery = _userAccessService.ApplyCuentaScope(_dbContext.Cuentas.AsNoTracking(), scope);

        return
            from e in _dbContext.Extractos.AsNoTracking()
                .Where(BuildConceptPredicate(terms))
            join c in cuentasQuery on e.CuentaId equals c.Id
            join t in _dbContext.Titulares.AsNoTracking() on c.TitularId equals t.Id
            join estado in _dbContext.RevisionExtractoEstados.AsNoTracking().Where(x => x.Tipo == tipo)
                on e.Id equals estado.ExtractoId into estados
            from estado in estados.DefaultIfEmpty()
            select new RevisionRawRow
            {
                ExtractoId = e.Id,
                CuentaId = c.Id,
                TitularId = t.Id,
                Titular = t.Nombre,
                Cuenta = c.Nombre,
                Divisa = c.Divisa,
                Fecha = e.Fecha,
                Monto = e.Monto,
                Concepto = e.Concepto ?? string.Empty,
                Estado = estado == null ? EstadoPendiente : estado.Estado
            };
    }

    private static async Task<PaginatedResponse<T>> ToPaginatedResponseAsync<T>(
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedResponse<T>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    private static int NormalizePage(int page) => Math.Max(1, page);

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 10, 200);

    private static Expression<Func<Extracto, bool>> BuildConceptPredicate(IReadOnlyList<string> terms)
    {
        var extracto = Expression.Parameter(typeof(Extracto), "extracto");
        var concepto = Expression.Property(extracto, nameof(Extracto.Concepto));
        var notNull = Expression.NotEqual(concepto, Expression.Constant(null, typeof(string)));
        var notEmpty = Expression.NotEqual(concepto, Expression.Constant(string.Empty));
        var lower = Expression.Call(concepto, nameof(string.ToLower), Type.EmptyTypes);
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

        Expression? anyTerm = null;
        foreach (var term in terms.Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            var contains = Expression.Call(lower, containsMethod, Expression.Constant(term));
            anyTerm = anyTerm is null ? contains : Expression.OrElse(anyTerm, contains);
        }

        anyTerm ??= Expression.Constant(false);
        return Expression.Lambda<Func<Extracto, bool>>(
            Expression.AndAlso(Expression.AndAlso(notNull, notEmpty), anyTerm),
            extracto);
    }

    private static string NormalizeTipo(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            TipoComision or "COMISIONES" => TipoComision,
            TipoSeguro or "SEGUROS" => TipoSeguro,
            _ => throw new InvalidOperationException("Tipo de revision invalido.")
        };
    }

    private static string NormalizeEstado(string tipo, string value)
    {
        var normalized = NormalizeEstadoFilter(value) ?? EstadoPendiente;
        if (tipo == TipoComision && normalized is EstadoPendiente or EstadoDevuelta or EstadoDescartada)
        {
            return normalized;
        }

        if (tipo == TipoSeguro && normalized is EstadoPendiente or EstadoCorrecto or EstadoDescartada)
        {
            return normalized;
        }

        throw new InvalidOperationException("Estado de revision invalido.");
    }

    private static string? NormalizeEstadoFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("TODAS", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "PENDIENTE" or "PENDIENTES" => EstadoPendiente,
            "DEVUELTA" or "DEVUELTAS" => EstadoDevuelta,
            "CORRECTO" or "CORRECTOS" => EstadoCorrecto,
            "DESCARTADA" or "DESCARTADAS" or "DESCARTADO" or "DESCARTADOS" or "IGNORADA" or "IGNORADAS" or "IGNORADO" or "IGNORADOS" or "NO_ES_COMISION" or "NO_ES_SEGURO" => EstadoDescartada,
            _ => normalized
        };
    }

    private static bool ContainsAnyTerm(string? concept, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(concept))
        {
            return false;
        }

        var normalized = RemoveDiacritics(concept).ToLowerInvariant();
        return terms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class RevisionRawRow
    {
        public Guid ExtractoId { get; init; }
        public Guid CuentaId { get; init; }
        public Guid TitularId { get; init; }
        public string Titular { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public string Divisa { get; init; } = string.Empty;
        public DateOnly Fecha { get; init; }
        public decimal Monto { get; init; }
        public string Concepto { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
    }
}
