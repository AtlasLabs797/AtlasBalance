using System.Security.Claims;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

public interface IUserAccessService
{
    Task<UserAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
    IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope);
    IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope);
    Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken);
    Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken);
}

public sealed class UserAccessScope
{
    public Guid UserId { get; init; }
    public bool IsAdmin { get; init; }
    public bool HasPermissions { get; init; }
    public bool HasGlobalAccess { get; init; }
    public IReadOnlyList<Guid> TitularIds { get; init; } = [];
    public IReadOnlyList<Guid> CuentaIds { get; init; } = [];
}

public sealed class UserAccessService : IUserAccessService
{
    private readonly AppDbContext _dbContext;

    public UserAccessService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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
            return new UserAccessScope
            {
                UserId = userId,
                IsAdmin = true,
                HasPermissions = true,
                HasGlobalAccess = true
            };
        }

        var permisos = await _dbContext.PermisosUsuario
            .Where(p => p.UsuarioId == userId)
            .Select(p => new
            {
                p.TitularId,
                p.CuentaId,
                p.PuedeVerCuentas,
                p.PuedeAgregarLineas,
                p.PuedeEditarLineas,
                p.PuedeEliminarLineas,
                p.PuedeImportar,
                p.PuedeVerDashboard
            })
            .ToListAsync(cancellationToken);

        var titularIds = permisos
            .Where(p => p.TitularId.HasValue)
            .Select(p => p.TitularId!.Value)
            .Distinct()
            .ToList();

        var cuentaIds = permisos
            .Where(p => p.CuentaId.HasValue)
            .Select(p => p.CuentaId!.Value)
            .Distinct()
            .ToList();

        var hasGlobalAccess = permisos.Any(p =>
            p.TitularId is null && p.CuentaId is null &&
            GrantsAccountAccess(p.PuedeVerCuentas, p.PuedeAgregarLineas, p.PuedeEditarLineas, p.PuedeEliminarLineas, p.PuedeImportar));

        return new UserAccessScope
        {
            UserId = userId,
            IsAdmin = false,
            HasPermissions = permisos.Count > 0,
            HasGlobalAccess = hasGlobalAccess,
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

        if (!scope.HasPermissions)
        {
            return query.Where(_ => false);
        }

        if (scope.HasGlobalAccess)
        {
            return query;
        }

        return query.Where(t =>
            scope.TitularIds.Contains(t.Id) ||
            _dbContext.Cuentas.Any(c => c.TitularId == t.Id && scope.CuentaIds.Contains(c.Id)));
    }

    public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope)
    {
        if (scope.IsAdmin)
        {
            return query;
        }

        if (!scope.HasPermissions)
        {
            return query.Where(_ => false);
        }

        if (scope.HasGlobalAccess)
        {
            return query;
        }

        return query.Where(c => scope.CuentaIds.Contains(c.Id) || scope.TitularIds.Contains(c.TitularId));
    }

    public Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return Task.FromResult(true);
        }

        if (!scope.HasPermissions)
        {
            return Task.FromResult(false);
        }

        if (scope.HasGlobalAccess || scope.TitularIds.Contains(titularId))
        {
            return Task.FromResult(true);
        }

        return _dbContext.Cuentas.AnyAsync(
            c => c.TitularId == titularId && scope.CuentaIds.Contains(c.Id),
            cancellationToken);
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

        if (scope.HasGlobalAccess || scope.CuentaIds.Contains(cuentaId))
        {
            return true;
        }

        return await _dbContext.Cuentas
            .AnyAsync(c => c.Id == cuentaId && scope.TitularIds.Contains(c.TitularId), cancellationToken);
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private static bool GrantsAccountAccess(bool canViewAccounts, bool canAdd, bool canEdit, bool canDelete, bool canImport) =>
        canViewAccounts || canAdd || canEdit || canDelete || canImport;
}
