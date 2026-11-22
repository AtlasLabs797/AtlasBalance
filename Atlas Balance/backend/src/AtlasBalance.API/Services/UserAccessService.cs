using System.Linq.Expressions;
using System.Security.Claims;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Services;

public interface IUserAccessService
{
    Task<UserAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>
    /// S-9/M-3: mismo scope que la variante con principal, pero construido a
    /// partir de (usuarioId, rol). La necesitan los servicios que reciben esas
    /// dos piezas en vez de un ClaimsPrincipal (ConciliacionService,
    /// ImportacionService).
    /// </summary>
    Task<UserAccessScope> GetScopeAsync(Guid usuarioId, string rol, CancellationToken cancellationToken);
    IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope);
    IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope);
    Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// S-9/M-3: chequeo booleano de una accion sobre una cuenta, con el
    /// predicado de <see cref="UserAccessAction"/>.
    /// </summary>
    Task<bool> CanCuentaAsync(UserAccessAction accion, Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// S-9/M-3: conjunto de cuentas accesibles para una accion, resuelto en UNA
    /// query (no un chequeo por cuenta). Usa el mismo predicado que el chequeo
    /// booleano, asi que las dos rutas comparten la definicion de la regla.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetCuentasAccesiblesAsync(UserAccessAction accion, Guid usuarioId, CancellationToken cancellationToken);
}

public sealed class UserAccessScope
{
    public Guid UserId { get; init; }
    public bool IsAdmin { get; init; }
    public bool HasPermissions { get; init; }
    public bool HasGlobalAccess { get; init; }
    public IReadOnlyList<Guid> PaisIds { get; init; } = [];
    public IReadOnlyList<Guid> TitularIds { get; init; } = [];
    public IReadOnlyList<Guid> CuentaIds { get; init; } = [];
}

/// <summary>
/// S-9/M-3 (LOTE 6): acciones con permiso sobre una cuenta. Cada accion tiene
/// UN predicado, definido en <see cref="UserAccessService"/>. Los servicios
/// (ConciliacionService, ImportacionService) consultan esa definicion en lugar
/// de reimplementar la regla.
/// </summary>
public enum UserAccessAction
{
    /// <summary>Conciliar: estricto, `puede_conciliar`.</summary>
    Conciliar,

    /// <summary>Cerrar conciliacion: estricto, `puede_cerrar_conciliacion`.</summary>
    CerrarConciliacion,

    /// <summary>
    /// Alcance general de conciliacion (lectura de movimientos y
    /// conciliaciones): `puede_conciliar` o `puede_cerrar_conciliacion`.
    /// </summary>
    AlcanceConciliacion,

    /// <summary>Importar: `puede_importar`.</summary>
    Importar,

    /// <summary>Ver una cuenta en el ambito de importacion.</summary>
    Ver,

    /// <summary>Revisar lineas: `puede_revisar_lineas` o `puede_editar_lineas`.</summary>
    Revisar,

    /// <summary>Aprobar una importacion: `puede_aprobar_importaciones` o `puede_importar`.</summary>
    AprobarImportacion,

    /// <summary>Eliminar lineas (borrado masivo, p. ej. revertir un lote): `puede_eliminar_lineas`.</summary>
    EliminarLineas
}

public sealed class UserAccessService : IUserAccessService
{
    internal const string Namespace = "user_access_scope";

    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly CachingOptions _cachingOptions;

    // S-9/M-3: definicion unica de las reglas de permiso por accion. El
    // predicado se evalua contra PERMISOS_USUARIO y se acota despues por
    // pais/titular/cuenta. Tanto el chequeo booleano por cuenta como el
    // conjunto de cuentas accesibles salen de aqui.
    private static readonly IReadOnlyDictionary<UserAccessAction, Expression<Func<PermisoUsuario, bool>>> PermissionPredicates =
        new Dictionary<UserAccessAction, Expression<Func<PermisoUsuario, bool>>>
        {
            [UserAccessAction.Conciliar] = p => p.PuedeConciliar,
            [UserAccessAction.CerrarConciliacion] = p => p.PuedeCerrarConciliacion,
            [UserAccessAction.AlcanceConciliacion] = p => p.PuedeConciliar || p.PuedeCerrarConciliacion,
            [UserAccessAction.Importar] = p => p.PuedeImportar,
            [UserAccessAction.Ver] = p => p.PuedeVerCuentas ||
                                         p.PuedeImportar ||
                                         p.PuedeRevisarLineas ||
                                         p.PuedeAprobarImportaciones,
            [UserAccessAction.Revisar] = p => p.PuedeRevisarLineas || p.PuedeEditarLineas,
            [UserAccessAction.AprobarImportacion] = p => p.PuedeAprobarImportaciones || p.PuedeImportar,
            [UserAccessAction.EliminarLineas] = p => p.PuedeEliminarLineas
        };

    public UserAccessService(
        AppDbContext dbContext,
        ICacheService cacheService,
        IOptions<CachingOptions> cachingOptions)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _cachingOptions = cachingOptions.Value;
    }

    public async Task<UserAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(user, out var userId))
        {
            return new UserAccessScope
            {
                UserId = Guid.Empty,
                IsAdmin = false,
                HasPermissions = false
            };
        }

        var rol = user.IsInRole(nameof(RolUsuario.ADMIN)) ? RolUsuario.ADMIN.ToString() : string.Empty;
        return await GetScopeAsync(userId, rol, cancellationToken);
    }

    /// <summary>
    /// S-9/M-3: construye el scope a partir de (usuarioId, rol). Es la misma
    /// fuente que la variante con principal: mismo bypass de ADMIN y mismo
    /// cache (namespace + TTL de CachingOptions.UserScopeTtl).
    /// </summary>
    public async Task<UserAccessScope> GetScopeAsync(Guid usuarioId, string rol, CancellationToken cancellationToken)
    {
        if (string.Equals(rol, RolUsuario.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            // Admin bypass: no tocamos el cache porque el resultado es trivial
            // (no requiere queries) y no queremos que el TTL oculte cambios de
            // rol puntuales (un admin degradado a gerente). La rotacion de
            // SecurityStamp ya invalida el JWT en el middleware.
            return new UserAccessScope
            {
                UserId = usuarioId,
                IsAdmin = true,
                HasPermissions = true,
                HasGlobalAccess = true
            };
        }

        if (usuarioId == Guid.Empty)
        {
            return new UserAccessScope
            {
                UserId = Guid.Empty,
                IsAdmin = false,
                HasPermissions = false
            };
        }

        return await _cacheService.GetOrLoadAsync(
            new CacheNamespace(Namespace),
            usuarioId.ToString("N"),
            ct => LoadScopeFromDatabaseAsync(usuarioId, ct),
            _cachingOptions.UserScopeTtl,
            cancellationToken);
    }

    private async Task<UserAccessScope> LoadScopeFromDatabaseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var permisos = await _dbContext.PermisosUsuario
            .Where(p => p.UsuarioId == userId)
            .Select(p => new
            {
                p.TitularId,
                p.CuentaId,
                p.PaisId,
                p.PuedeVerCuentas,
                p.PuedeAgregarLineas,
                p.PuedeEditarLineas,
                p.PuedeEliminarLineas,
                p.PuedeImportar,
                p.PuedeVerDashboard,
                p.PuedeRevisarLineas,
                p.PuedeAprobarImportaciones,
                p.PuedeConciliar,
                p.PuedeCerrarConciliacion
            })
            .ToListAsync(cancellationToken);

        var dataPermissions = permisos
            .Where(p => p.PuedeVerCuentas)
            .ToList();

        var titularIds = dataPermissions
            .Where(p => p.TitularId.HasValue)
            .Select(p => p.TitularId!.Value)
            .Distinct()
            .ToList();

        var paisIds = dataPermissions
            .Where(p => p.PaisId.HasValue)
            .Select(p => p.PaisId!.Value)
            .Distinct()
            .ToList();

        var cuentaIds = dataPermissions
            .Where(p => p.CuentaId.HasValue)
            .Select(p => p.CuentaId!.Value)
            .Distinct()
            .ToList();

        var hasGlobalAccess = dataPermissions.Any(p =>
            p.PaisId is null && p.TitularId is null && p.CuentaId is null &&
            p.PuedeVerCuentas);

        return new UserAccessScope
        {
            UserId = userId,
            IsAdmin = false,
            HasPermissions = permisos.Count > 0,
            HasGlobalAccess = hasGlobalAccess,
            PaisIds = paisIds,
            TitularIds = titularIds,
            CuentaIds = cuentaIds
        };
    }

    public IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope)
    {
        if (scope.IsAdmin)
        {
            return query;
        }

        query = query.Where(t => t.DeletedAt == null);

        if (!scope.HasPermissions)
        {
            return query.Where(_ => false);
        }

        if (scope.HasGlobalAccess)
        {
            return query;
        }

        return query.Where(t =>
            _dbContext.PermisosUsuario.Any(p =>
                p.UsuarioId == scope.UserId &&
                p.PuedeVerCuentas &&
                _dbContext.Cuentas.Any(c =>
                    c.TitularId == t.Id &&
                    c.DeletedAt == null &&
                    (p.PaisId == null || p.PaisId == c.PaisId) &&
                    (p.TitularId == null || p.TitularId == c.TitularId) &&
                    (p.CuentaId == null || p.CuentaId == c.Id))));
    }

    public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope)
    {
        if (scope.IsAdmin)
        {
            return query;
        }

        query = ApplyActiveTitularCuentaScope(query);

        if (!scope.HasPermissions)
        {
            return query.Where(_ => false);
        }

        if (scope.HasGlobalAccess)
        {
            return query;
        }

        return query.Where(c =>
            _dbContext.PermisosUsuario.Any(p =>
                p.UsuarioId == scope.UserId &&
                p.PuedeVerCuentas &&
                (p.PaisId == null || p.PaisId == c.PaisId) &&
                (p.TitularId == null || p.TitularId == c.TitularId) &&
                (p.CuentaId == null || p.CuentaId == c.Id)));
    }

    public async Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return true;
        }

        if (!scope.HasPermissions)
        {
            return false;
        }

        var titularActivo = await _dbContext.Titulares
            .AsNoTracking()
            .AnyAsync(t => t.Id == titularId && t.DeletedAt == null, cancellationToken);
        if (!titularActivo)
        {
            return false;
        }

        if (scope.HasGlobalAccess)
        {
            return true;
        }

        return await ApplyTitularScope(_dbContext.Titulares.AsNoTracking(), scope)
            .AnyAsync(t => t.Id == titularId, cancellationToken);
    }

    public async Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return true;
        }

        if (!scope.HasPermissions)
        {
            return false;
        }

        return await ApplyCuentaScope(_dbContext.Cuentas.AsNoTracking(), scope)
            .AnyAsync(c => c.Id == cuentaId, cancellationToken);
    }

    public async Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return true;
        }

        if (!scope.HasPermissions || scope.UserId == Guid.Empty)
        {
            return false;
        }

        return await (
                from permiso in _dbContext.PermisosUsuario.AsNoTracking()
                join cuenta in ApplyActiveTitularCuentaScope(_dbContext.Cuentas.AsNoTracking()) on cuentaId equals cuenta.Id
                where permiso.UsuarioId == scope.UserId &&
                      (permiso.PuedeAgregarLineas || permiso.PuedeEditarLineas || permiso.PuedeEliminarLineas || permiso.PuedeImportar) &&
                      (permiso.PaisId == null || permiso.PaisId == cuenta.PaisId) &&
                      (permiso.TitularId == null || permiso.TitularId == cuenta.TitularId) &&
                      (permiso.CuentaId == null || permiso.CuentaId == cuenta.Id)
                select permiso.Id)
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return true;
        }

        if (!scope.HasPermissions || scope.UserId == Guid.Empty)
        {
            return false;
        }

        return await (
                from permiso in _dbContext.PermisosUsuario.AsNoTracking()
                join cuenta in ApplyActiveTitularCuentaScope(_dbContext.Cuentas.AsNoTracking()) on cuentaId equals cuenta.Id
                where permiso.UsuarioId == scope.UserId &&
                      permiso.PuedeEditarLineas &&
                      (permiso.PaisId == null || permiso.PaisId == cuenta.PaisId) &&
                      (permiso.TitularId == null || permiso.TitularId == cuenta.TitularId) &&
                      (permiso.CuentaId == null || permiso.CuentaId == cuenta.Id)
                select permiso.Id)
            .AnyAsync(cancellationToken);
    }

    public Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return HasCuentaPermissionAsync(
            cuentaId,
            scope,
            PermissionPredicates[UserAccessAction.Revisar],
            cancellationToken);
    }

    public Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return CanCuentaAsync(UserAccessAction.Conciliar, cuentaId, scope, cancellationToken);
    }

    public Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return CanCuentaAsync(UserAccessAction.CerrarConciliacion, cuentaId, scope, cancellationToken);
    }

    /// <summary>
    /// S-9/M-3: unico chequeo por accion. El predicado sale del mapa
    /// <see cref="PermissionPredicates"/>, asi que no puede divergir del que
    /// usa el conjunto de cuentas accesibles.
    /// </summary>
    public Task<bool> CanCuentaAsync(UserAccessAction accion, Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return HasCuentaPermissionAsync(cuentaId, scope, PermissionPredicates[accion], cancellationToken);
    }

    /// <summary>
    /// S-9/M-3: cuentas accesibles para una accion, resueltas en UNA query
    /// (permisos x cuentas x titulares). Es la version de conjunto de la misma
    /// regla que usa <see cref="CanCuentaAsync"/>: evita el N+1 de preguntar
    /// cuenta por cuenta.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> GetCuentasAccesiblesAsync(UserAccessAction accion, Guid usuarioId, CancellationToken cancellationToken)
    {
        var predicate = PermissionPredicates[accion];

        // V-02.08: las cuentas ya excluyen borradas por el query filter global
        // ISoftDelete, pero faltaba exigir TITULAR activo (mismo criterio que
        // UserAccessService.ApplyActiveTitularCuentaScope); sin ello, un
        // usuario seguia viendo movimientos/conciliaciones de cuentas cuyo
        // titular fue eliminado logicamente.
        var rows = await (
                from permiso in _dbContext.PermisosUsuario.AsNoTracking().Where(predicate)
                where permiso.UsuarioId == usuarioId
                from cuenta in _dbContext.Cuentas.AsNoTracking()
                join titular in _dbContext.Titulares.AsNoTracking() on cuenta.TitularId equals titular.Id
                where (permiso.PaisId == null || permiso.PaisId == cuenta.PaisId)
                   && (permiso.TitularId == null || permiso.TitularId == cuenta.TitularId)
                   && (permiso.CuentaId == null || permiso.CuentaId == cuenta.Id)
                   && titular.DeletedAt == null
                select cuenta.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new HashSet<Guid>(rows);
    }

    private async Task<bool> HasCuentaPermissionAsync(
        Guid cuentaId,
        UserAccessScope scope,
        Expression<Func<PermisoUsuario, bool>> permissionPredicate,
        CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return true;
        }

        if (!scope.HasPermissions || scope.UserId == Guid.Empty)
        {
            return false;
        }

        return await (
                from permiso in _dbContext.PermisosUsuario.AsNoTracking().Where(permissionPredicate)
                join cuenta in ApplyActiveTitularCuentaScope(_dbContext.Cuentas.AsNoTracking()) on cuentaId equals cuenta.Id
                where permiso.UsuarioId == scope.UserId &&
                      (permiso.PaisId == null || permiso.PaisId == cuenta.PaisId) &&
                      (permiso.TitularId == null || permiso.TitularId == cuenta.TitularId) &&
                      (permiso.CuentaId == null || permiso.CuentaId == cuenta.Id)
                select permiso.Id)
            .AnyAsync(cancellationToken);
    }

    private IQueryable<Cuenta> ApplyActiveTitularCuentaScope(IQueryable<Cuenta> query)
    {
        return query.Where(c =>
            c.DeletedAt == null &&
            _dbContext.Titulares.Any(t => t.Id == c.TitularId && t.DeletedAt == null));
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

}
