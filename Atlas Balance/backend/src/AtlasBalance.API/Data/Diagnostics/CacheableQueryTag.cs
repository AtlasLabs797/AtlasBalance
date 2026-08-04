namespace AtlasBalance.API.Data.Diagnostics;

/// <summary>
/// Etiquetas candidatas para que el codigo de aplicacion anote
/// consultas EF Core con <c>dbContext.TagWith(...)</c> cuando se
/// quiera telemetria de volumen SQL por caso de uso. No hay todavia
/// un interceptor que las consuma: se mantienen como constantes
/// centralizadas para futuras iteraciones de observabilidad.
/// </summary>
public static class CacheableQueryTag
{
    public const string DashboardScope = "atlas:dashboard:scope";
    public const string DashboardCuentas = "atlas:dashboard:cuentas";
    public const string DashboardMetricsLatest = "atlas:dashboard:metrics:latest";
    public const string DashboardMetricsMonth = "atlas:dashboard:metrics:month";
    public const string DashboardMetricsPlazos = "atlas:dashboard:metrics:plazos";
    public const string DashboardReferenceDivisa = "atlas:dashboard:reference:divisa";
    public const string DashboardReferenceColores = "atlas:dashboard:reference:colores";
    public const string TiposCambioRates = "atlas:fx:rates";
}
