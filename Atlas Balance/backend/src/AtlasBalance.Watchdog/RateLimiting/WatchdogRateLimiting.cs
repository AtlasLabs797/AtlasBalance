using System.Threading.RateLimiting;

namespace AtlasBalance.Watchdog.RateLimiting;

public static class WatchdogRateLimiting
{
    public const string SensitiveOperationsPolicy = "watchdog-sensitive-operations";
    public const int MaxRequestBodySize = 16 * 1024;
    public const int GlobalPermitLimit = 120;
    public const int SensitivePermitLimit = 5;

    public static RateLimitPartition<string> CreateGlobalPartition(HttpContext context)
    {
        if (context.Request.Path.Equals("/watchdog/health", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("health-exempt");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"global:{GetClientKey(context)}",
            _ => CreateOptions(GlobalPermitLimit));
    }

    public static RateLimitPartition<string> CreateSensitivePartition(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientKey(context),
            _ => CreateOptions(SensitivePermitLimit));

    private static FixedWindowRateLimiterOptions CreateOptions(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static string GetClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
