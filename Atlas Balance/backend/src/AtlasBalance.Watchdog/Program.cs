using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using AtlasBalance.Watchdog.RateLimiting;
using AtlasBalance.Watchdog.Services;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
AddExternalDevelopmentSecrets(builder.Configuration, builder.Environment, "AtlasBalance.Watchdog.Development.json");

builder.Host.UseWindowsService();
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5001);
    options.Limits.MaxRequestBodySize = WatchdogRateLimiting.MaxRequestBodySize;
});
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/watchdog-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(WatchdogRateLimiting.CreateGlobalPartition);
    options.AddPolicy(WatchdogRateLimiting.SensitiveOperationsPolicy, WatchdogRateLimiting.CreateSensitivePartition);
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        context.HttpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Demasiadas solicitudes. Intentalo de nuevo mas tarde." },
            cancellationToken);
    };
});
builder.Services.AddSingleton<IWatchdogStateStore, WatchdogStateStore>();
builder.Services.AddSingleton<IWatchdogOperationsService, WatchdogOperationsService>();

var app = builder.Build();

var sharedSecret = builder.Configuration["WatchdogSettings:SharedSecret"];
if (string.IsNullOrWhiteSpace(sharedSecret))
{
    throw new InvalidOperationException("WatchdogSettings:SharedSecret is required");
}
if (!builder.Environment.IsDevelopment())
{
    RejectUnsafeProductionSecret(
        "WatchdogSettings:SharedSecret",
        sharedSecret,
        32);
    RejectUnsafeProductionSecret(
        "WatchdogSettings:DbPassword",
        builder.Configuration["WatchdogSettings:DbPassword"],
        12);
}

const string healthPath = "/watchdog/health";

app.UseRouting();
// Debe ejecutarse antes de validar X-Watchdog-Secret: asi los intentos con
// secreto invalido tambien consumen cuota y no permiten fuerza bruta gratis.
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals(healthPath, StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var secret = context.Request.Headers["X-Watchdog-Secret"].FirstOrDefault();
    if (!SecretMatches(secret, sharedSecret))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid watchdog secret" });
        return;
    }

    await next();
});

app.MapControllers();
app.MapGet(healthPath, () => Results.Ok(new { status = "healthy" }));

app.Run();

static bool SecretMatches(string? supplied, string expected)
{
    if (string.IsNullOrWhiteSpace(supplied))
    {
        return false;
    }

    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    return suppliedBytes.Length == expectedBytes.Length &&
           CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
}

static void RejectUnsafeProductionSecret(string key, string? value, int minLength, params string[] unsafeDefaults)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length < minLength)
    {
        throw new InvalidOperationException($"{key} must be configured with at least {minLength} characters outside Development.");
    }

    if (unsafeDefaults.Any(defaultValue => string.Equals(value, defaultValue, StringComparison.Ordinal)) ||
        LooksLikePlaceholder(value))
    {
        throw new InvalidOperationException($"{key} still contains a development/default placeholder. Configure a real production value.");
    }
}

static bool LooksLikePlaceholder(string value)
{
    var normalized = value.Trim().ToLowerInvariant();
    return normalized.StartsWith("dev-", StringComparison.Ordinal) ||
           normalized.Contains("dev_", StringComparison.Ordinal) ||
           normalized.Contains("change", StringComparison.Ordinal) ||
           normalized.Contains("cambiar", StringComparison.Ordinal) ||
           normalized.Contains("generar", StringComparison.Ordinal) ||
           normalized.Contains("placeholder", StringComparison.Ordinal) ||
           normalized.Contains("aqui", StringComparison.Ordinal);
}

static void AddExternalDevelopmentSecrets(IConfigurationBuilder configuration, IWebHostEnvironment environment, string fileName)
{
    if (!environment.IsDevelopment())
    {
        return;
    }

    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    if (string.IsNullOrWhiteSpace(appData))
    {
        return;
    }

    var path = Path.Combine(appData, "AtlasBalance", "dev-secrets", fileName);
    configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
}
