using System.Security.Cryptography;
using System.Text;
using AtlasBalance.Watchdog.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5001);
});
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/watchdog-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddControllers();
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
