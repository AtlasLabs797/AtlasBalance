using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Serilog;
using System.Net;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/atlas-balance-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RlsDbCommandInterceptor>();
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(serviceProvider.GetRequiredService<RlsDbCommandInterceptor>()));

var jwtSecret = ResolveJwtSecret(builder.Configuration, builder.Environment);
var rlsContextSecret = ResolveRlsContextSecret(builder.Configuration, jwtSecret);
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
var useAiSystemProxy = builder.Configuration.GetValue("Ia:UseSystemProxy", false);
var aiProxyUrl = builder.Configuration["Ia:ProxyUrl"];
var hasExplicitAiProxy = !string.IsNullOrWhiteSpace(aiProxyUrl);
var primaryAiUsesProxy = useAiSystemProxy || hasExplicitAiProxy;
builder.Services.AddHttpClient("openrouter", client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
})
    .ConfigurePrimaryHttpMessageHandler(() => CreateAiHttpHandler(primaryAiUsesProxy, aiProxyUrl));
builder.Services.AddHttpClient("openrouter-fallback", client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
})
    .ConfigurePrimaryHttpMessageHandler(() => CreateAiHttpHandler(useProxy: false, proxyUrl: null));
builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
})
    .ConfigurePrimaryHttpMessageHandler(() => CreateAiHttpHandler(primaryAiUsesProxy, aiProxyUrl));
builder.Services.AddHttpClient("openai-fallback", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
})
    .ConfigurePrimaryHttpMessageHandler(() => CreateAiHttpHandler(useProxy: false, proxyUrl: null));

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")),
        CreateHangfireStorageOptions()));
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
builder.Services.AddScoped<IPlazoFijoService, PlazoFijoService>();
builder.Services.AddScoped<IRevisionService, RevisionService>();
builder.Services.AddScoped<IAtlasAiService, AtlasAiService>();
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
builder.Services.AddScoped<PlazoFijoVencimientoJob>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var runtimeConnectionString = app.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
    var effectiveMigrationConnectionString = ResolveMigrationConnectionString(
        app.Configuration,
        app.Environment,
        runtimeConnectionString);

    var migrationOptions = CreateDbContextOptions(effectiveMigrationConnectionString);
    using (var migrationDb = new AppDbContext(migrationOptions))
    {
        migrationDb.Database.Migrate();
    }

    EnsureRlsContextSecret(effectiveMigrationConnectionString, rlsContextSecret);
    EnsureHangfireStorage(effectiveMigrationConnectionString);
    GrantRuntimeDatabasePrivileges(effectiveMigrationConnectionString, runtimeConnectionString);
    NpgsqlConnection.ClearAllPools();

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

    recurringJobManager.AddOrUpdate<PlazoFijoVencimientoJob>(
        "plazo-fijo-vencimientos",
        job => job.ExecuteAsync(),
        Cron.Daily());
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        if (feature?.Error is not null)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AtlasBalance.API.UnhandledException");
            logger.LogError(feature.Error, "Unhandled API exception on {Path}", context.Request.Path.Value);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { error = "Error interno del servidor." });
    });
});

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
        headers["X-Frame-Options"] = "SAMEORIGIN";
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
            "frame-ancestors 'self'; " +
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

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
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

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
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

static string ResolveRlsContextSecret(IConfiguration configuration, string jwtSecret)
{
    var configured = configuration["Security:RlsContextSecret"];
    return string.IsNullOrWhiteSpace(configured) ? jwtSecret : configured;
}

static SocketsHttpHandler CreateAiHttpHandler(bool useProxy, string? proxyUrl)
{
    var handler = new SocketsHttpHandler
    {
        UseProxy = useProxy,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    };

    if (!useProxy)
    {
        return handler;
    }

    handler.Proxy = CreateAiProxy(proxyUrl);
    handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
    return handler;
}

static IWebProxy CreateAiProxy(string? proxyUrl)
{
    if (string.IsNullOrWhiteSpace(proxyUrl))
    {
        return WebRequest.GetSystemWebProxy();
    }

    if (!Uri.TryCreate(proxyUrl.Trim(), UriKind.Absolute, out var proxyUri))
    {
        throw new InvalidOperationException("Ia:ProxyUrl debe ser una URL absoluta de proxy.");
    }

    return new WebProxy(proxyUri, true)
    {
        Credentials = CredentialCache.DefaultCredentials
    };
}

static DbContextOptions<AppDbContext> CreateDbContextOptions(string connectionString) =>
    new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention()
        .Options;

static PostgreSqlStorageOptions CreateHangfireStorageOptions() =>
    new()
    {
        PrepareSchemaIfNecessary = true,
        StartupConnectionMaxRetries = 3
    };

static void EnsureHangfireStorage(string connectionString)
{
    var storage = new PostgreSqlStorage(
        connectionString,
        connectionSetup: null,
        options: CreateHangfireStorageOptions());
    using var connection = storage.GetConnection();
}

static string ResolveMigrationConnectionString(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    string runtimeConnectionString)
{
    var configuredMigrationConnection = configuration.GetConnectionString("MigrationConnection");
    if (!string.IsNullOrWhiteSpace(configuredMigrationConnection))
    {
        return configuredMigrationConnection;
    }

    if (!environment.IsDevelopment())
    {
        return runtimeConnectionString;
    }

    var runtimeBuilder = new NpgsqlConnectionStringBuilder(runtimeConnectionString);
    if (!IsKnownRuntimeDatabaseUser(runtimeBuilder.Username))
    {
        return runtimeConnectionString;
    }

    var ownerPassword =
        FirstNonWhiteSpace(
            Environment.GetEnvironmentVariable("ATLAS_BALANCE_POSTGRES_OWNER_PASSWORD"),
            ReadDevelopmentEnvValue(environment.ContentRootPath, "ATLAS_BALANCE_POSTGRES_OWNER_PASSWORD"),
            Environment.GetEnvironmentVariable("ATLAS_BALANCE_POSTGRES_PASSWORD"),
            ReadDevelopmentEnvValue(environment.ContentRootPath, "ATLAS_BALANCE_POSTGRES_PASSWORD"),
            runtimeBuilder.Password)
        ?? runtimeBuilder.Password;

    var ownerBuilder = new NpgsqlConnectionStringBuilder(runtimeConnectionString)
    {
        Username = "atlas_owner",
        Password = ownerPassword
    };

    return ownerBuilder.ConnectionString;
}

static bool IsKnownRuntimeDatabaseUser(string? username) =>
    string.Equals(username, "app_user", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(username, "atlas_balance_app", StringComparison.OrdinalIgnoreCase);

static string? FirstNonWhiteSpace(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

static string? ReadDevelopmentEnvValue(string contentRootPath, string key)
{
    var directory = new DirectoryInfo(contentRootPath);
    for (var depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
    {
        var envPath = Path.Combine(directory.FullName, ".env");
        if (!File.Exists(envPath))
        {
            continue;
        }

        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var currentKey = line[..equalsIndex].Trim();
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(equalsIndex + 1)..].Trim().Trim('"');
        }
    }

    return null;
}

static void EnsureRlsContextSecret(string connectionString, string secret)
{
    using var connection = new NpgsqlConnection(connectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE SCHEMA IF NOT EXISTS atlas_security;
        CREATE EXTENSION IF NOT EXISTS pgcrypto;
        CREATE TABLE IF NOT EXISTS atlas_security.rls_context_secret (
            id boolean PRIMARY KEY DEFAULT true CHECK (id),
            secret text NOT NULL,
            updated_at timestamp with time zone NOT NULL DEFAULT now()
        );
        INSERT INTO atlas_security.rls_context_secret (id, secret, updated_at)
        VALUES (true, @secret, now())
        ON CONFLICT (id) DO UPDATE
        SET secret = EXCLUDED.secret,
            updated_at = now();
        REVOKE ALL ON TABLE atlas_security.rls_context_secret FROM PUBLIC;
        """;
    command.Parameters.AddWithValue("secret", secret);
    command.ExecuteNonQuery();
}

static void GrantRuntimeDatabasePrivileges(string migrationConnectionString, string runtimeConnectionString)
{
    var migrationBuilder = new NpgsqlConnectionStringBuilder(migrationConnectionString);
    var runtimeBuilder = new NpgsqlConnectionStringBuilder(runtimeConnectionString);
    if (string.Equals(migrationBuilder.Username, runtimeBuilder.Username, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(runtimeBuilder.Username))
    {
        throw new InvalidOperationException("Runtime database username is required for RLS grants.");
    }

    var databaseName = string.IsNullOrWhiteSpace(migrationBuilder.Database)
        ? runtimeBuilder.Database
        : migrationBuilder.Database;
    if (string.IsNullOrWhiteSpace(databaseName))
    {
        throw new InvalidOperationException("Database name is required for RLS grants.");
    }

    var runtimeRole = QuotePostgresIdentifier(runtimeBuilder.Username);
    using var connection = new NpgsqlConnection(migrationConnectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = $$"""
        GRANT CONNECT ON DATABASE {{QuotePostgresIdentifier(databaseName)}} TO {{runtimeRole}};
        GRANT USAGE ON SCHEMA public TO {{runtimeRole}};
        GRANT USAGE ON SCHEMA atlas_security TO {{runtimeRole}};
        GRANT USAGE ON SCHEMA hangfire TO {{runtimeRole}};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {{runtimeRole}};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO {{runtimeRole}};
        GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {{runtimeRole}};
        GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA hangfire TO {{runtimeRole}};
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA atlas_security TO {{runtimeRole}};
        REVOKE ALL ON TABLE atlas_security.rls_context_secret FROM {{runtimeRole}};
        """;
    command.ExecuteNonQuery();
}

static string QuotePostgresIdentifier(string value) =>
    "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

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
        "exchange_rate_api_key",
        "openrouter_api_key",
        "openai_api_key"
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
