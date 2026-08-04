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
    IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope);
    IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope);
    Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanApproveImportacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
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

public sealed class UserAccessService : IUserAccessService
{
    internal const string Namespace = "user_access_scope";

    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly CachingOptions _cachingOptions;

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

        if (user.IsInRole(nameof(RolUsuario.ADMIN)))
        {
            // Admin bypass: no tocamos el cache porque el resultado es trivial
            // (no requiere queries) y no queremos que el TTL oculte cambios de
            // rol puntuales (un admin degradado a gerente). La rotacion de
            // SecurityStamp ya invalida el JWT en el middleware.
            return new UserAccessScope
            {
                UserId = userId,
                IsAdmin = true,
                HasPermissions = true,
                HasGlobalAccess = true
            };
        }

        return await _cacheService.GetOrLoadAsync(
            new CacheNamespace(Namespace),
            userId.ToString("N"),
            ct => LoadScopeFromDatabaseAsync(userId, ct),
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
            permiso => permiso.PuedeRevisarLineas || permiso.PuedeEditarLineas,
            cancellationToken);
    }

    public Task<bool> CanApproveImportacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return HasCuentaPermissionAsync(
            cuentaId,
            scope,
            permiso => permiso.PuedeAprobarImportaciones || permiso.PuedeImportar,
            cancellationToken);
    }

    public Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return HasCuentaPermissionAsync(
            cuentaId,
            scope,
            permiso => permiso.PuedeConciliar || permiso.PuedeEditarLineas || permiso.PuedeImportar,
            cancellationToken);
    }

    public Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        return HasCuentaPermissionAsync(
            cuentaId,
            scope,
            permiso => permiso.PuedeCerrarConciliacion || permiso.PuedeConciliar,
            cancellationToken);
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
