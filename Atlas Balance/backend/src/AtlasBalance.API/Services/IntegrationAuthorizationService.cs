using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public sealed class IntegrationAccessScope
{
    public Guid TokenId { get; init; }
    public bool HasPermissions { get; init; }
    public bool HasGlobalAccess { get; init; }
    public IReadOnlyList<Guid> TitularIds { get; init; } = [];
    public IReadOnlyList<Guid> CuentaIds { get; init; } = [];
}

public interface IIntegrationAuthorizationService
{
    Task<IntegrationAccessScope> GetScopeAsync(Guid tokenId, CancellationToken cancellationToken, string? accessType = null);
    IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, IntegrationAccessScope scope);
    IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, IntegrationAccessScope scope);
    IQueryable<Extracto> ApplyExtractoScope(IQueryable<Extracto> query, IntegrationAccessScope scope);
}

public sealed class IntegrationAuthorizationService : IIntegrationAuthorizationService
{
    private readonly AppDbContext _dbContext;

    public IntegrationAuthorizationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IntegrationAccessScope> GetScopeAsync(Guid tokenId, CancellationToken cancellationToken, string? accessType = null)
    {
        var normalizedAccessType = NormalizeAccessType(accessType);
        var permisos = await _dbContext.IntegrationPermissions
            .Where(x => x.TokenId == tokenId)
            .Where(x => normalizedAccessType == null || x.AccesoTipo == normalizedAccessType)
            .Select(x => new { x.TitularId, x.CuentaId })
            .ToListAsync(cancellationToken);

        return new IntegrationAccessScope
        {
            TokenId = tokenId,
            HasPermissions = permisos.Count > 0,
            HasGlobalAccess = permisos.Any(x => x.TitularId is null && x.CuentaId is null),
            TitularIds = permisos.Where(x => x.TitularId.HasValue).Select(x => x.TitularId!.Value).Distinct().ToList(),
            CuentaIds = permisos.Where(x => x.CuentaId.HasValue).Select(x => x.CuentaId!.Value).Distinct().ToList()
        };
    }

    private static string? NormalizeAccessType(string? accessType)
    {
        var normalized = accessType?.Trim().ToLowerInvariant();
        return normalized is "lectura" or "escritura" ? normalized : null;
    }

    public IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, IntegrationAccessScope scope)
    {
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
            scope.TitularIds.Contains(t.Id) ||
            _dbContext.Cuentas.Any(c => c.TitularId == t.Id && c.DeletedAt == null && scope.CuentaIds.Contains(c.Id)));
    }

    public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, IntegrationAccessScope scope)
    {
        query = ApplyActiveTitularCuentaScope(query);

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

    public IQueryable<Extracto> ApplyExtractoScope(IQueryable<Extracto> query, IntegrationAccessScope scope)
    {
        query = query.Where(e =>
            e.DeletedAt == null &&
            ApplyActiveTitularCuentaScope(_dbContext.Cuentas).Any(c => c.Id == e.CuentaId));

        if (!scope.HasPermissions)
        {
            return query.Where(_ => false);
        }

        if (scope.HasGlobalAccess)
        {
            return query;
        }

        return query.Where(e =>
            scope.CuentaIds.Contains(e.CuentaId) ||
            ApplyActiveTitularCuentaScope(_dbContext.Cuentas).Any(c => c.Id == e.CuentaId && scope.TitularIds.Contains(c.TitularId)));
    }

    private IQueryable<Cuenta> ApplyActiveTitularCuentaScope(IQueryable<Cuenta> query)
    {
        return query.Where(c =>
            c.DeletedAt == null &&
            _dbContext.Titulares.Any(t => t.Id == c.TitularId && t.DeletedAt == null));
    }
}
