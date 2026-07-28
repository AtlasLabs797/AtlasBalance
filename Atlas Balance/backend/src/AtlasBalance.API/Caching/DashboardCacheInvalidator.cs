using AtlasBalance.API.Services;

namespace AtlasBalance.API.Caching;

/// <summary>
/// Fachada de invalidacion para los consumidores que no necesitan conocer
/// las claves internas del dashboard y del catalogo de tasas. Centraliza
/// los nombres de namespace y la politica de bump por grupo de datos.
/// </summary>
public interface IDashboardCacheInvalidator
{
    /// <summary>
    /// Invalida el scope autorizado por usuario (DashboardService).
    /// Llamar tras: cambio de permisos, rol o estado del usuario,
    /// creacion/edicion/restauracion de cuentas, cambios de relacion titular-pais.
    /// </summary>
    void InvalidateDashboardScope();

    /// <summary>
    /// Invalida la referencia de divisa base y colores del dashboard.
    /// Llamar tras: crear/actualizar divisa, modificar claves
    /// dashboard_color_* o divisa_principal_default.
    /// </summary>
    void InvalidateDashboardReference();

    /// <summary>
    /// Invalida los snapshots de metricas (saldos por divisa/cuenta/pais,
    /// ingresos/egresos del mes). Llamar tras cualquier escritura que
    /// cambie saldos: extractos, cuentas, plazos fijos, sincronizacion de
    /// tipos de cambio o cambios de divisa.
    /// </summary>
    void InvalidateDashboardMetrics();
}

public sealed class DashboardCacheInvalidator : IDashboardCacheInvalidator
{
    private readonly ICacheService _cacheService;

    public DashboardCacheInvalidator(ICacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public void InvalidateDashboardScope() =>
        _cacheService.Invalidate(new CacheNamespace(DashboardService.ScopeNamespace));

    public void InvalidateDashboardReference() =>
        _cacheService.Invalidate(new CacheNamespace(DashboardService.ReferenceNamespace));

    public void InvalidateDashboardMetrics() =>
        _cacheService.Invalidate(new CacheNamespace(DashboardService.MetricsNamespace));
}
