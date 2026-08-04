using AtlasBalance.API.Caching;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtlasBalance.API.Data;

/// <summary>
/// Invalida los caches de dashboard, configuracion, scope de usuario,
/// autenticacion y tokens de integracion despues de un SaveChanges exitoso
/// cuando las entidades tocadas pueden haber cambiado resultados cacheados.
/// No depende de los controllers: cualquier ruta (incluidos jobs de
/// Hangfire) que use <c>SaveChangesAsync</c> del AppDbContext dispara la
/// invalidacion automaticamente.
///
/// Cobertura minima: cambios en <see cref="Extracto"/>,
/// <see cref="Cuenta"/>, <see cref="PlazoFijo"/>, <see cref="Titular"/>,
/// <see cref="Pais"/>, <see cref="PermisoUsuario"/>,
/// <see cref="PreferenciaUsuarioCuenta"/>, <see cref="TipoCambio"/>,
/// <see cref="DivisaActiva"/>, <see cref="Usuario"/>,
/// <see cref="Configuracion"/> o <see cref="IntegrationToken"/>.
/// </summary>
public sealed class DashboardCacheInvalidationInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<Type> EntidadesQueInvalidanMetricas = new()
    {
        typeof(Extracto),
        typeof(ExtractoColumnaExtra),
        typeof(ExtractoDesglose),
        typeof(Cuenta),
        typeof(PlazoFijo),
        typeof(TipoCambio),
        typeof(DivisaActiva),
        typeof(Configuracion),
    };

    private static readonly HashSet<Type> EntidadesQueInvalidanScope = new()
    {
        typeof(PermisoUsuario),
        typeof(PreferenciaUsuarioCuenta),
        typeof(Usuario),
        typeof(Cuenta),
        typeof(Titular),
        typeof(Pais),
    };

    private static readonly HashSet<Type> EntidadesQueInvalidanReferencia = new()
    {
        typeof(DivisaActiva),
        typeof(Configuracion),
    };

    private static readonly HashSet<Type> EntidadesQueInvalidanConfiguracion = new()
    {
        typeof(Configuracion),
    };

    private static readonly HashSet<Type> EntidadesQueInvalidanAuthCurrent = new()
    {
        typeof(Usuario),
        typeof(PermisoUsuario),
        typeof(PreferenciaUsuarioCuenta),
    };

    private static readonly HashSet<Type> EntidadesQueInvalidanIntegrationToken = new()
    {
        typeof(IntegrationToken),
    };

    private static readonly HashSet<string> ClavesConfiguracionQueInvalidanReferencia = new(StringComparer.OrdinalIgnoreCase)
    {
        "divisa_principal_default",
        "dashboard_color_ingresos",
        "dashboard_color_egresos",
        "dashboard_color_saldo",
    };

    private readonly IDashboardCacheInvalidator _invalidator;
    private readonly ICacheService _cacheService;

    public DashboardCacheInvalidationInterceptor(
        IDashboardCacheInvalidator invalidator,
        ICacheService cacheService)
    {
        _invalidator = invalidator;
        _cacheService = cacheService;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext dbContext && result > 0)
        {
            Invalidate(dbContext);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is AppDbContext dbContext && result > 0)
        {
            Invalidate(dbContext);
        }

        return base.SavedChanges(eventData, result);
    }

    private void Invalidate(AppDbContext dbContext)
    {
        bool invalidateMetrics = false;
        bool invalidateScope = false;
        bool invalidateReference = false;
        bool invalidateConfiguracion = false;
        bool invalidateAuthCurrent = false;
        bool invalidateIntegrationToken = false;

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            var state = entry.State;
            if (state is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var type = entry.Entity.GetType();
            if (EntidadesQueInvalidanMetricas.Contains(type))
            {
                invalidateMetrics = true;
            }

            if (EntidadesQueInvalidanScope.Contains(type))
            {
                invalidateScope = true;
            }

            if (EntidadesQueInvalidanReferencia.Contains(type))
            {
                invalidateReference = true;
                if (type == typeof(Configuracion) &&
                    entry.Entity is Configuracion cfg &&
                    !string.IsNullOrEmpty(cfg.Clave) &&
                    !ClavesConfiguracionQueInvalidanReferencia.Contains(cfg.Clave))
                {
                    // Solo algunas claves de Configuracion afectan a la
                    // referencia del dashboard. Evitamos invalidar por
                    // cambios no relacionados (SMTP, secretos, etc.).
                    invalidateReference = false;
                }
            }

            if (EntidadesQueInvalidanConfiguracion.Contains(type))
            {
                // Cualquier cambio en CONFIGURACIONES invalida el cache de
                // todas las claves (ya cubierto por IConfiguracionRepository
                // al escribir, pero esto protege escrituras que pasen por
                // el contexto directamente: jobs, seeds, migraciones).
                invalidateConfiguracion = true;
            }

            if (EntidadesQueInvalidanAuthCurrent.Contains(type))
            {
                invalidateAuthCurrent = true;
            }

            if (EntidadesQueInvalidanIntegrationToken.Contains(type))
            {
                invalidateIntegrationToken = true;
            }
        }

        if (invalidateScope)
        {
            _invalidator.InvalidateDashboardScope();
            _cacheService.Invalidate(new CacheNamespace(UserAccessService.Namespace));
        }

        if (invalidateMetrics)
        {
            _invalidator.InvalidateDashboardMetrics();
        }

        if (invalidateReference)
        {
            _invalidator.InvalidateDashboardReference();
        }

        if (invalidateConfiguracion)
        {
            _cacheService.Invalidate(new CacheNamespace(ConfiguracionRepository.Namespace));
        }

        if (invalidateAuthCurrent)
        {
            _cacheService.Invalidate(new CacheNamespace(AuthService.AuthCurrentNamespace));
        }

        if (invalidateIntegrationToken)
        {
            _cacheService.Invalidate(new CacheNamespace(IntegrationTokenService.Namespace));
        }
    }
}
