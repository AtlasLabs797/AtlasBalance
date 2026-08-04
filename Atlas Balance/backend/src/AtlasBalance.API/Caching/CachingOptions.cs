namespace AtlasBalance.API.Caching;

/// <summary>
/// Opciones de la capa de cache en proceso. Se enlazan desde la seccion
/// <c>AtlasBalance:Caching</c> de <c>appsettings.json</c> para que el TTL de
/// cada namespace sea ajustable por entorno sin redeploy.
/// Documentado en V-02.07 (cache de lecturas repetidas).
/// </summary>
public sealed class CachingOptions
{
    public const string SectionName = "AtlasBalance:Caching";

    public int ConfigurationTtlSeconds { get; set; } = 120;
    public int UserScopeTtlSeconds { get; set; } = 45;
    public int IntegrationTokenTtlSeconds { get; set; } = 20;
    public int DashboardScopeTtlSeconds { get; set; } = 30;
    public int DashboardReferenceTtlSeconds { get; set; } = 300;
    public int DashboardMetricsTtlSeconds { get; set; } = 15;
    public int AuthCurrentTtlSeconds { get; set; } = 60;
    public int TiposCambioTtlSeconds { get; set; } = 300;
    public int SizeLimitEntries { get; set; } = 4096;

    public TimeSpan ConfigurationTtl => TimeSpan.FromSeconds(ConfigurationTtlSeconds);
    public TimeSpan UserScopeTtl => TimeSpan.FromSeconds(UserScopeTtlSeconds);
    public TimeSpan IntegrationTokenTtl => TimeSpan.FromSeconds(IntegrationTokenTtlSeconds);
    public TimeSpan DashboardScopeTtl => TimeSpan.FromSeconds(DashboardScopeTtlSeconds);
    public TimeSpan DashboardReferenceTtl => TimeSpan.FromSeconds(DashboardReferenceTtlSeconds);
    public TimeSpan DashboardMetricsTtl => TimeSpan.FromSeconds(DashboardMetricsTtlSeconds);
    public TimeSpan AuthCurrentTtl => TimeSpan.FromSeconds(AuthCurrentTtlSeconds);
    public TimeSpan TiposCambioTtl => TimeSpan.FromSeconds(TiposCambioTtlSeconds);
}