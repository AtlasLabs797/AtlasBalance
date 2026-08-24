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
    Task<VerificarDevolucionResponse> VerificarDevolucionAsync(UserAccessScope scope, Guid extractoId, CancellationToken cancellationToken);
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
        "comisi\u00f3n",
        "mantenimiento",
        "administracion",
        "administraci\u00f3n",
        "reclamacion",
        "reclamaci\u00f3n",
        "descubierto",
        "gastos bancarios"
    ];

    private static readonly string[] ComisionSearchTerms =
    [
        "comision",
        "comisi\u00f3n",
        "comisión",
        "mantenimiento",
        "administracion",
        "administraci\u00f3n",
        "administración",
        "reclamacion",
        "reclamaci\u00f3n",
        "reclamación",
        "descubierto",
        "gastos bancarios"
    ];

    private static readonly string[] ComisionExcludedTerms = [];
    private static readonly string[] ComisionExcludedSearchTerms = [];

    private static readonly string[] SeguroTerms =
    [
        "seguro",
        "aseguradora",
        "poliza",
        "p\u00f3liza",
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
        "p\u00f3liza",
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

    private static readonly string[] SeguroExcludedTerms =
    [
        "seguridad social",
        "seguro social",
        "seguros sociales",
        "tgss",
        "tesoreria general",
        "tesoreria gral",
        "social security",
        "generalitat",
        "generalidad",
        "transferencia",
        "transferencias",
        "abono transferencia",
        "transferencia recibida",
        "transferencia realizada",
        "anul",
        "anulacion",
        "devolucion",
        "reembolso"
    ];

    private static readonly string[] SeguroExcludedSearchTerms =
    [
        "seguridad social",
        "seguro social",
        "seguros sociales",
        "tgss",
        "tesoreria general",
        "tesorer\u00eda general",
        "tesoreria gral",
        "social security",
        "generalitat",
        "generalidad",
        "transferencia",
        "transferencias",
        "abono transferencia",
        "transferencia recibida",
        "transferencia realizada",
        "anul",
        "anulacion",
        "anulaci\u00f3n",
        "devolucion",
        "devoluci\u00f3n",
        "reembolso"
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
        var query = BuildRevisionBaseQuery(scope, request.PaisId, TipoComision, ComisionSearchTerms);
        // V-02.08: solo comisiones (cargo, negativo). Los movimientos en positivo
        // son devoluciones/bonificaciones y se muestran como columna de
        // emparejamiento, no como filas de la lista.
        query = query.Where(x => x.Monto < -settings.ComisionesImporteMinimo);

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
            .ThenByDescending(x => x.Monto < 0 ? -x.Monto : x.Monto);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var devoluciones = await ResolveDevolucionesAsync(rows, cancellationToken);

        return new PaginatedResponse<RevisionComisionItemResponse>
        {
            Data = rows.Select(row =>
            {
                var item = ToComisionResponse(row);
                if (devoluciones.TryGetValue(row.ExtractoId, out var devolucion))
                {
                    item.DevolucionExtractoId = devolucion.ExtractoId;
                    item.DevolucionFecha = devolucion.Fecha;
                }

                return item;
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<PaginatedResponse<RevisionSeguroItemResponse>> GetSegurosAsync(
        UserAccessScope scope,
        RevisionQueryRequest request,
        CancellationToken cancellationToken)
    {
        var estadoFiltro = NormalizeEstadoFilter(request.Estado);
        var page = NormalizePage(request.Page);
        var pageSize = NormalizePageSize(request.PageSize);
        var query = BuildRevisionBaseQuery(scope, request.PaisId, TipoSeguro, SeguroSearchTerms)
            .Where(x => x.Monto < 0m)
            .Select(x => new RevisionSeguroItemResponse
            {
                ExtractoId = x.ExtractoId,
                CuentaId = x.CuentaId,
                TitularId = x.TitularId,
                PaisId = x.PaisId,
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

        if (!await _userAccessService.CanReviewCuentaAsync(extracto.CuentaId, scope, cancellationToken))
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
        // V-02.08: al salir de DEVUELTA se limpia el emparejamiento para que el
        // abono quede libre y pueda volver a sugerirse para otra comision.
        if (normalizedEstado != EstadoDevuelta)
        {
            current.ExtractoDevolucionId = null;
        }

        current.FechaModificacion = DateTime.UtcNow;
        current.UsuarioModificacionId = scope.UserId == Guid.Empty ? null : scope.UserId;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // V-02.08: verifica la devolucion de una comision emparejandola con su
    // bonificacion (mismo cuenta, importe exacto opuesto, fecha posterior,
    // concepto tipo comision). Regla global: un abono solo puede asignarse a
    // una comision, y gana siempre la comision mas antigua que encaje.
    public async Task<VerificarDevolucionResponse> VerificarDevolucionAsync(UserAccessScope scope, Guid extractoId, CancellationToken cancellationToken)
    {
        var extracto = await _dbContext.Extractos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == extractoId, cancellationToken);

        if (extracto is null)
        {
            throw new InvalidOperationException("Extracto no encontrado.");
        }

        if (!await _userAccessService.CanReviewCuentaAsync(extracto.CuentaId, scope, cancellationToken))
        {
            throw new UnauthorizedAccessException();
        }

        // V-02.08: el extracto objetivo debe ser el mismo tipo de movimiento que
        // lista GetComisionesAsync (cargo negativo con concepto de comision).
        // Sin esto, un cliente podia pasar cualquier extracto permitido y colar
        // un emparejamiento con un abono no relacionado.
        var settings = await GetSettingsAsync(cancellationToken);
        var esComisionElegible = await _dbContext.Extractos.AsNoTracking()
            .Where(x => x.Id == extractoId)
            .Where(BuildConceptPredicate(ComisionSearchTerms, ComisionExcludedSearchTerms))
            .Where(x => x.Monto < -settings.ComisionesImporteMinimo)
            .AnyAsync(cancellationToken);

        if (!esComisionElegible)
        {
            return new VerificarDevolucionResponse
            {
                Encontrada = false,
                Message = "El extracto no es una comision elegible para devolucion."
            };
        }

        var montoAbono = -extracto.Monto;
        var candidato = await BuildAbonoCandidatesQuery(extracto.CuentaId, montoAbono, extracto.Fecha)
            .OrderBy(e => e.Fecha)
            .ThenBy(e => e.FilaNumero)
            .Select(e => new { e.Id, e.Fecha })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidato is null)
        {
            return new VerificarDevolucionResponse
            {
                Encontrada = false,
                Message = "No hay ninguna bonificacion candidata para esta comision."
            };
        }

        // Regla global acordada: el abono pertenece a la comision pendiente mas
        // antigua que encaje. Si existe otra comision anterior que tambien podria
        // reclamarlo (misma cuenta e importe, abono posterior a su fecha), se
        // rechaza la verificacion de esta.
        var compiteOtraMasAntigua = await _dbContext.Extractos.AsNoTracking()
            .Where(z => z.Id != extractoId
                && z.CuentaId == extracto.CuentaId
                && z.Monto == extracto.Monto
                && z.Fecha <= candidato.Fecha
                && (z.Fecha < extracto.Fecha || (z.Fecha == extracto.Fecha && z.FilaNumero < extracto.FilaNumero)))
            .Where(BuildConceptPredicate(ComisionSearchTerms, ComisionExcludedSearchTerms))
            .Where(z => !_dbContext.RevisionExtractoEstados.Any(r => r.ExtractoId == z.Id && r.Tipo == TipoComision && r.Estado != EstadoPendiente))
            .AnyAsync(cancellationToken);

        if (compiteOtraMasAntigua)
        {
            return new VerificarDevolucionResponse
            {
                Encontrada = false,
                Message = "La bonificacion candidata corresponde a una comision mas antigua aun pendiente."
            };
        }

        var current = await _dbContext.RevisionExtractoEstados
            .FirstOrDefaultAsync(x => x.ExtractoId == extractoId && x.Tipo == TipoComision, cancellationToken);

        if (current is null)
        {
            current = new RevisionExtractoEstado
            {
                Id = Guid.NewGuid(),
                ExtractoId = extractoId,
                Tipo = TipoComision
            };
            _dbContext.RevisionExtractoEstados.Add(current);
        }

        current.Estado = EstadoDevuelta;
        current.ExtractoDevolucionId = candidato.Id;
        current.FechaModificacion = DateTime.UtcNow;
        current.UsuarioModificacionId = scope.UserId == Guid.Empty ? null : scope.UserId;
        // Si dos usuarios verifican a la vez el mismo abono, el indice unico
        // parcial sobre extracto_devolucion_id rechaza la doble asignacion
        // (DbUpdateException que el controller traduce a 409).
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new VerificarDevolucionResponse
        {
            Encontrada = true,
            Message = "Devolucion verificada",
            DevolucionExtractoId = candidato.Id,
            DevolucionFecha = candidato.Fecha
        };
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

    public static bool IsCommissionConcept(string? concept) => MatchesAnyIncludedTerm(concept, ComisionTerms, ComisionExcludedTerms);

    public static bool IsInsuranceConcept(string? concept) => MatchesAnyIncludedTerm(concept, SeguroTerms, SeguroExcludedTerms);

    private IQueryable<RevisionRawRow> BuildRevisionBaseQuery(UserAccessScope scope, Guid? paisId, string tipo, IReadOnlyList<string> terms)
    {
        var cuentasQuery = _userAccessService
            .ApplyCuentaScope(_dbContext.Cuentas.AsNoTracking(), scope)
            .ApplyPaisScope(paisId);

        return
            from e in _dbContext.Extractos.AsNoTracking()
                .Where(BuildConceptPredicate(terms, GetExcludedSearchTerms(tipo)))
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
                PaisId = c.PaisId,
                Titular = t.Nombre,
                Cuenta = c.Nombre,
                Divisa = c.Divisa,
                Fecha = e.Fecha,
                Monto = e.Monto,
                FilaNumero = e.FilaNumero,
                Concepto = e.Concepto ?? string.Empty,
                Estado = estado == null ? EstadoPendiente : estado.Estado,
                ExtractoDevolucionId = estado == null ? null : estado.ExtractoDevolucionId
            };
    }

    // V-02.08: resuelve la columna Devolucion de cada comision de la pagina.
    // - Filas ya verificadas: fecha del extracto emparejado persistido (una query).
    // - Pendientes: sugerencia automatica por lotes. Un abono se sugiere como
    //   maximo una vez por pagina; gana siempre la comision mas antigua
    //   (fecha asc, fila_numero asc). La asignacion definitiva la hace
    //   VerificarDevolucionAsync al persistir.
    private async Task<Dictionary<Guid, DevolucionInfo>> ResolveDevolucionesAsync(List<RevisionRawRow> rows, CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<Guid, DevolucionInfo>();
        if (rows.Count == 0)
        {
            return resultado;
        }

        var referenciadas = rows
            .Where(x => x.ExtractoDevolucionId is not null)
            .Select(x => x.ExtractoDevolucionId!.Value)
            .Distinct()
            .ToList();

        if (referenciadas.Count > 0)
        {
            var fechas = await _dbContext.Extractos.AsNoTracking()
                .Where(x => referenciadas.Contains(x.Id))
                .Select(x => new { x.Id, x.Fecha })
                .ToDictionaryAsync(x => x.Id, x => x.Fecha, cancellationToken);

            foreach (var row in rows)
            {
                if (row.ExtractoDevolucionId is not null && fechas.TryGetValue(row.ExtractoDevolucionId.Value, out var fecha))
                {
                    resultado[row.ExtractoId] = new DevolucionInfo(row.ExtractoDevolucionId.Value, fecha);
                }
            }
        }

        var pendientes = rows
            .Where(x => x.Estado == EstadoPendiente && x.ExtractoDevolucionId is null && !resultado.ContainsKey(x.ExtractoId))
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.FilaNumero)
            .ToList();

        if (pendientes.Count == 0)
        {
            return resultado;
        }

        var cuentaIds = pendientes.Select(x => x.CuentaId).Distinct().ToList();
        var importes = pendientes.Select(x => -x.Monto).Distinct().ToList();
        var fechaMinima = pendientes.Min(x => x.Fecha);

        var candidatos = await BuildAbonoCandidatesBatchQuery(cuentaIds, importesAbono: importes, fechaDesde: fechaMinima)
            .Select(e => new AbonoCandidate
            {
                Id = e.Id,
                CuentaId = e.CuentaId,
                Monto = e.Monto,
                Fecha = e.Fecha,
                FilaNumero = e.FilaNumero
            })
            .ToListAsync(cancellationToken);

        var candidatosPorClave = candidatos
            .GroupBy(x => (x.CuentaId, x.Monto))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Fecha).ThenBy(x => x.FilaNumero).ThenBy(x => x.Id).ToList());

        var usados = new HashSet<Guid>();
        foreach (var pendiente in pendientes)
        {
            if (!candidatosPorClave.TryGetValue((pendiente.CuentaId, -pendiente.Monto), out var lista))
            {
                continue;
            }

            // La consulta por lotes arranca en la fecha minima de toda la pagina,
            // asi que aqui hay que exigir tambien candidato.Fecha >= pendiente.Fecha
            // (igual que hace la consulta individual de VerificarDevolucionAsync)
            // para no sugerir un abono anterior a la propia comision.
            var candidato = lista.FirstOrDefault(x => !usados.Contains(x.Id) && x.Fecha >= pendiente.Fecha);
            if (candidato is null)
            {
                continue;
            }

            usados.Add(candidato.Id);
            resultado[pendiente.ExtractoId] = new DevolucionInfo(candidato.Id, candidato.Fecha);
        }

        return resultado;
    }

    // Candidatos de abono para una sola comision (endpoint verificar).
    private IQueryable<Extracto> BuildAbonoCandidatesQuery(Guid cuentaId, decimal montoAbono, DateOnly fechaDesde) =>
        ApplyAbonoEligibility(_dbContext.Extractos.AsNoTracking())
            .Where(e => e.CuentaId == cuentaId && e.Monto == montoAbono && e.Fecha >= fechaDesde);

    // Candidatos de abono por lotes para toda una pagina de comisiones.
    private IQueryable<Extracto> BuildAbonoCandidatesBatchQuery(List<Guid> cuentaIds, List<decimal> importesAbono, DateOnly fechaDesde) =>
        ApplyAbonoEligibility(_dbContext.Extractos.AsNoTracking())
            .Where(e => cuentaIds.Contains(e.CuentaId) && importesAbono.Contains(e.Monto) && e.Fecha >= fechaDesde);

    // Reglas comunes de elegibilidad de un abono: concepto tipo comision/
    // bonificacion, no descartado previamente como comision y sin emparejar
    // todavia con otra revision. El filtro global de soft delete excluye solo
    // los extractos y estados borrados.
    private IQueryable<Extracto> ApplyAbonoEligibility(IQueryable<Extracto> query) =>
        query
            .Where(BuildConceptPredicate(ComisionSearchTerms, ComisionExcludedSearchTerms))
            .Where(e => !_dbContext.RevisionExtractoEstados.Any(r => r.ExtractoDevolucionId == e.Id))
            .Where(e => !_dbContext.RevisionExtractoEstados.Any(r => r.ExtractoId == e.Id && r.Tipo == TipoComision && r.Estado == EstadoDescartada));

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

    private static Expression<Func<Extracto, bool>> BuildConceptPredicate(IReadOnlyList<string> terms, IReadOnlyList<string> excludedTerms)
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
        Expression? excluded = null;
        foreach (var term in excludedTerms.Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            var contains = Expression.Call(lower, containsMethod, Expression.Constant(term));
            excluded = excluded is null ? contains : Expression.OrElse(excluded, contains);
        }

        var match = excluded is null
            ? anyTerm
            : Expression.AndAlso(anyTerm, Expression.Not(excluded));

        return Expression.Lambda<Func<Extracto, bool>>(
            Expression.AndAlso(Expression.AndAlso(notNull, notEmpty), match),
            extracto);
    }

    private static IReadOnlyList<string> GetExcludedSearchTerms(string tipo) =>
        tipo == TipoSeguro ? SeguroExcludedSearchTerms : ComisionExcludedSearchTerms;

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

    private static bool MatchesAnyIncludedTerm(string? concept, IReadOnlyList<string> includedTerms, IReadOnlyList<string> excludedTerms)
    {
        if (string.IsNullOrWhiteSpace(concept))
        {
            return false;
        }

        var normalized = RemoveDiacritics(concept).ToLowerInvariant();
        if (excludedTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return includedTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
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

    private static RevisionComisionItemResponse ToComisionResponse(RevisionRawRow row) => new()
    {
        ExtractoId = row.ExtractoId,
        CuentaId = row.CuentaId,
        TitularId = row.TitularId,
        PaisId = row.PaisId,
        Titular = row.Titular,
        Cuenta = row.Cuenta,
        Divisa = row.Divisa,
        Fecha = row.Fecha,
        Monto = row.Monto,
        Concepto = row.Concepto,
        EstadoDevolucion = row.Estado
    };

    private readonly record struct DevolucionInfo(Guid ExtractoId, DateOnly Fecha);

    private sealed class AbonoCandidate
    {
        public Guid Id { get; init; }
        public Guid CuentaId { get; init; }
        public decimal Monto { get; init; }
        public DateOnly Fecha { get; init; }
        public int FilaNumero { get; init; }
    }

    private sealed class RevisionRawRow
    {
        public Guid ExtractoId { get; init; }
        public Guid CuentaId { get; init; }
        public Guid TitularId { get; init; }
        public Guid? PaisId { get; init; }
        public string Titular { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public string Divisa { get; init; } = string.Empty;
        public DateOnly Fecha { get; init; }
        public decimal Monto { get; init; }
        public int FilaNumero { get; init; }
        public string Concepto { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public Guid? ExtractoDevolucionId { get; init; }
    }
}
