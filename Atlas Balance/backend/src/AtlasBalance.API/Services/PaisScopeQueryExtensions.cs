using AtlasBalance.API.Models;

namespace AtlasBalance.API.Services;

public static class PaisScopeQueryExtensions
{
    public static IQueryable<Cuenta> ApplyPaisScope(this IQueryable<Cuenta> query, Guid? paisId)
    {
        return paisId.HasValue
            ? query.Where(c => c.PaisId == paisId.Value)
            : query;
    }
}
