using GestionCaja.API.Data;
using GestionCaja.API.Jobs;
using GestionCaja.API.Middleware;
using GestionCaja.API.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/atlas-balance-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseSnakeCaseNamingConvention());

var jwtSecret = ResolveJwtSecret(builder.Configuration, builder.Environment);
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "atlas-balance-api";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "atlas-balance-app";

if (!builder.Environment.IsDevelopment())
{
    RejectUnsafeProductionSecret(
        "JwtSettings:Secret",
        jwtSecret,
        32);
    RejectUnsafeProductionSecret(
        "WatchdogSettings:SharedSecret",
        builder.Configuration["WatchdogSettings:SharedSecret"],
        32);
    RejectUnsafeProductionSecret(
        "ConnectionStrings:DefaultConnection",
        builder.Configuration.GetConnectionString("DefaultConnection"),
        1);
    RejectUnsafeAllowedHosts(builder.Configuration["AllowedHosts"]);
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/api/integration/openclaw", StringComparison.OrdinalIgnoreCase))
                {
                    context.NoResult();
                    return Task.CompletedTask;
                }

                context.Token = context.Request.Cookies["access_token"];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("AtlasBalance");
if (!builder.Environment.IsDevelopment())
{
    var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        dataProtectionKeysPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AtlasBalance",
            "keys");
    }

    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
    if (OperatingSystem.IsWindows())
    {
        dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}
builder.Services.AddHttpClient("exchange-rate-api", client =>
{
    client.BaseAddress = new Uri("https://v6.exchangerate-api.com/v6/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("watchdog-client", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["WatchdogSettings:BaseUrl"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICsrfService, CsrfService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITiposCambioService, TiposCambioService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IImportacionService, ImportacionService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAlertaService, AlertaService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IExportacionService, ExportacionService>();
builder.Services.AddScoped<IWatchdogClientService, WatchdogClientService>();
builder.Services.AddScoped<IActualizacionService, ActualizacionService>();
builder.Services.AddScoped<IIntegrationTokenService, IntegrationTokenService>();
builder.Services.AddScoped<IIntegrationAuthorizationService, IntegrationAuthorizationService>();
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<SyncTiposCambioJob>();
builder.Services.AddScoped<LimpiezaRefreshTokensJob>();
builder.Services.AddScoped<LimpiezaAuditoriaJob>();
builder.Services.AddScoped<BackupWeeklyJob>();
builder.Services.AddScoped<ExportMensualJob>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SeedData.Initialize(db, app.Configuration, app.Environment);
    ProtectExistingConfigurationSecrets(
        db,
        scope.ServiceProvider.GetRequiredService<ISecretProtector>());

    // Configure recurring jobs
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<SyncTiposCambioJob>(
        "sync-tipos-cambio",
        job => job.ExecuteAsync(),
        "0 */12 * * *");

    recurringJobManager.AddOrUpdate<LimpiezaRefreshTokensJob>(
        "limpieza-refresh-tokens",
        job => job.ExecuteAsync(),
        Cron.Daily());

    recurringJobManager.AddOrUpdate<LimpiezaAuditoriaJob>(
        "limpieza-auditoria",
        job => job.ExecuteAsync(),
        "15 3 * * *");

    recurringJobManager.AddOrUpdate<BackupWeeklyJob>(
        "backup-weekly",
        job => job.ExecuteAsync(),
        "0 2 * * 0");

    recurringJobManager.AddOrUpdate<ExportMensualJob>(
        "export-mensual",
        job => job.ExecuteAsync(),
        "0 1 1 * *");
}

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        var connectSrc = app.Environment.IsDevelopment()
            ? "'self' http://localhost:5173 https://localhost:5000 http://localhost:5000"
            : "'self'";

        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            $"connect-src {connectSrc}; " +
            "font-src 'self' data:; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "img-src 'self' data: blob:; " +
            "object-src 'none'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'";

        return Task.CompletedTask;
    });

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();

var staticFileOptions = new StaticFileOptions();
staticFileOptions.OnPrepareResponse = ctx =>
{
    if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        ctx.File.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        ctx.File.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Context.Response.ContentType = ctx.Context.Response.ContentType + "; charset=utf-8";
    }
};
app.UseStaticFiles(staticFileOptions);
app.UseMiddleware<IntegrationAuthMiddleware>();

app.UseAuthentication();
app.UseMiddleware<UserStateMiddleware>();
app.UseAuthorization();
app.UseMiddleware<PrimerLoginMiddleware>();
app.UseMiddleware<CsrfMiddleware>();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire");
}

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
app.MapFallback("/api/{**catchAll}", () => Results.NotFound(new { error = "Endpoint no encontrado" }));
app.MapFallbackToFile("index.html");

app.Run();

static void RejectUnsafeProductionSecret(string key, string? value, int minimumLength, params string[] forbiddenValues)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{key} must be configured with a non-default secret outside Development.");
    }

    var trimmed = value.Trim();
    var isKnownDefault = forbiddenValues.Any(forbiddenValue =>
        string.Equals(trimmed, forbiddenValue, StringComparison.Ordinal));

    if (trimmed.Length < minimumLength || isKnownDefault || LooksLikePlaceholder(trimmed))
    {
        throw new InvalidOperationException($"{key} must be configured with a non-default secret outside Development.");
    }
}

static string ResolveJwtSecret(IConfiguration configuration, IHostEnvironment environment)
{
    var configured = configuration["JwtSettings:Secret"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    if (!environment.IsDevelopment())
    {
        throw new InvalidOperationException("JwtSettings:Secret must be configured outside Development.");
    }

    var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    configuration["JwtSettings:Secret"] = generated;
    return generated;
}

static bool LooksLikePlaceholder(string value)
{
    var lower = value.ToLowerInvariant();
    return lower.Contains("dev-", StringComparison.Ordinal) ||
           lower.Contains("dev_", StringComparison.Ordinal) ||
           lower.Contains("change", StringComparison.Ordinal) ||
           lower.Contains("cambiar", StringComparison.Ordinal) ||
           lower.Contains("generar", StringComparison.Ordinal) ||
           lower.Contains("placeholder", StringComparison.Ordinal) ||
           lower.Contains("aqui", StringComparison.Ordinal);
}

static void RejectUnsafeAllowedHosts(string? allowedHosts)
{
    if (string.IsNullOrWhiteSpace(allowedHosts))
    {
        throw new InvalidOperationException("AllowedHosts must be configured outside Development.");
    }

    var hosts = allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (hosts.Length == 0 ||
        hosts.Any(host => host.Contains('*', StringComparison.Ordinal) || LooksLikePlaceholder(host)))
    {
        throw new InvalidOperationException("AllowedHosts must list explicit host names outside Development.");
    }
}

static void ProtectExistingConfigurationSecrets(AppDbContext dbContext, ISecretProtector secretProtector)
{
    var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "smtp_password",
        "exchange_rate_api_key"
    };

    var changed = false;
    foreach (var item in dbContext.Configuraciones.Where(c => secretKeys.Contains(c.Clave)))
    {
        if (string.IsNullOrWhiteSpace(item.Valor) || secretProtector.IsProtected(item.Valor))
        {
            continue;
        }

        item.Valor = secretProtector.ProtectForStorage(item.Valor);
        item.FechaModificacion = DateTime.UtcNow;
        changed = true;
    }

    if (changed)
    {
        dbContext.SaveChanges();
    }
}
