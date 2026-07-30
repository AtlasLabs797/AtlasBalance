using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Serilog;
using Serilog.Filters;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    // V-02-05 (LOW-BE-1): no enviar el header "Server: Kestrel" en respuestas.
    options.AddServerHeader = false;
    // V-02-07: el default de Kestrel (30 MB) queda muy por encima de lo que la
    // API realmente necesita. El unico payload grande es la importacion, que
    // limita RawData a 5 MB (ver ImportacionService.MaxRawDataLength), pero el
    // JSON completo (RawData escapado como string + resto de campos) puede
    // crecer bastante sobre esos 5 MB crudos si el contenido tiene comillas,
    // barras invertidas o saltos de linea. 10 MB da el doble de holgura sobre
    // el limite real sin dejar los 30 MB por defecto abiertos.
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});
AddExternalDevelopmentSecrets(builder.Configuration, builder.Environment, "AtlasBalance.API.Development.json");

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, config) =>
{
    // V-02-05 (MED-24): ruta absoluta configurable para evitar que el log acabe
    // en C:\Windows\System32\logs cuando AtlasBalance corre como Windows Service
    // (el cwd por defecto del servicio es System32). Default razonable para
    // on-premise: %ProgramData%\AtlasBalance\logs.
    var defaultLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AtlasBalance",
        "logs");
    var logPath = context.Configuration["Serilog:FilePath"]
        ?? Path.Combine(defaultLogDir, "atlas-balance-.log");

    // V-02.07: los eventos de seguridad van ADEMAS a su propio fichero, en una
    // carpeta aparte. Dos motivos:
    //  1. Retencion propia y mas larga que el log de aplicacion (30 dias no
    //     sirven para investigar un incidente que se descubre dos meses tarde).
    //  2. La carpeta puede llevar una ACL de solo-anexar para la cuenta del
    //     servicio, cosa que no se puede hacer con el log general porque
    //     Serilog necesita rotar y borrar ficheros ahi.
    // Ver SecurityEventLog: emite con la categoria AtlasBalance.Security.
    var securityLogPath = context.Configuration["Serilog:SecurityFilePath"]
        ?? Path.Combine(defaultLogDir, "security", "atlas-security-.log");

    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            logPath,
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 50L * 1024L * 1024L,
            retainedFileCountLimit: 30)
        .WriteTo.Logger(securityLogger => securityLogger
            .Filter.ByIncludingOnly(Matching.FromSource("AtlasBalance.Security"))
            .WriteTo.File(
                securityLogPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 50L * 1024L * 1024L,
                // Por encima de la retencion de AUDITORIAS (365 dias) a
                // proposito: si alguien purga la tabla, el fichero sigue.
                retainedFileCountLimit: 400));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SmtpTestRateLimit>();
// V-02.06 (PR F1): RlsContextSecret es internal y RlsDbCommandInterceptor tiene
// ctor internal. La DI por defecto solo invoca constructores publicos, asi que
// registramos una factory explicita que conserva la encapsulacion del secreto.
builder.Services.AddScoped<RlsDbCommandInterceptor>(serviceProvider =>
    new RlsDbCommandInterceptor(
        serviceProvider.GetRequiredService<IHttpContextAccessor>(),
        serviceProvider.GetRequiredService<RlsContextSecret>()));
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<DashboardCacheInvalidationInterceptor>();
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(
            serviceProvider.GetRequiredService<RlsDbCommandInterceptor>(),
            serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<DashboardCacheInvalidationInterceptor>()));

var jwtSecret = ResolveJwtSecret(builder.Configuration, builder.Environment);
var rlsContextSecret = ResolveRlsContextSecret(builder.Configuration, builder.Environment, jwtSecret);
// V-02-06 (RLS-SEC-01): unificar resolucion y validacion del secreto RLS.
// Ya no se permite que Program.cs y el interceptor lean IConfiguration por
// separado: el secreto se valida una unica vez y se inyecta por DI. Si el
// operador no define Security:RlsContextSecret, mantenemos el fallback al
// secreto JWT solo en Development; en Produccion exigimos clave propia para
// que comprometer JWT no permita forjar contextos RLS.
builder.Services.AddSingleton(new AtlasBalance.API.Data.RlsContextSecret(rlsContextSecret));
// V-02.07: clave de firma de AUDITORIAS. Misma politica fail-closed que RLS y
// por el mismo motivo: si se reutiliza el secreto JWT, comprometerlo permitiria
// forjar filas de auditoria con firma valida y el rastro dejaria de valer nada.
builder.Services.AddSingleton(new AtlasBalance.API.Services.AuditSigningKey(
    ResolveAuditSigningKey(builder.Configuration, builder.Environment, jwtSecret)));
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
    // V-02-07: SslMode=Disable o Prefer contra un host remoto deja el trafico
    // con PostgreSQL (PII financiera incluida) sin cifrar y sin nada que lo
    // detecte. No abortamos el arranque (romperia instalaciones existentes
    // con la BD en la misma maquina), pero avisamos claro en el log.
    WarnIfConnectionStringSslModeUnsafe(
        "ConnectionStrings:DefaultConnection",
        builder.Configuration.GetConnectionString("DefaultConnection"));
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // V-02-07: el default del framework es true y filtra en el header
        // WWW-Authenticate el motivo exacto del fallo (firma invalida, timestamp
        // de expiracion, etc). Solo lo dejamos activo en Development.
        options.IncludeErrorDetails = builder.Environment.IsDevelopment();
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

                context.Token = context.Request.Cookies["__Host-atlas-access-token"]
                    ?? context.Request.Cookies["access_token"];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
builder.Services.Configure<CachingOptions>(builder.Configuration.GetSection(CachingOptions.SectionName));
builder.Services.AddSingleton<ICacheService>(sp =>
    new CacheService(
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<CacheService>(),
        new CacheServiceOptions
        {
            SizeLimit = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CachingOptions>>().Value.SizeLimitEntries
        }));
builder.Services.AddSingleton<IDashboardCacheInvalidator, DashboardCacheInvalidator>();
builder.Services.AddAtlasRateLimiting(builder.Configuration);
// V-02.07: sin AddHsts, UseHsts emite max-age=2592000 (30 dias) y sin
// includeSubDomains. Se sube a un ano e incluye subdominios.
// preload queda fuera A PROPOSITO: la lista de preload de los navegadores
// exige un dominio publico registrable y es practicamente irreversible; esta
// app es on-premise y se instala con hostnames de intranet.
// ExcludedHosts se deja con el default del framework (localhost, 127.0.0.1,
// [::1]) para no fijar HSTS en la maquina del propio servidor.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = false;
});
ConfigureForwardedHeaders(builder.Services, builder.Configuration);
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
    client.BaseAddress = ResolveWatchdogBaseUri(config["WatchdogSettings:BaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("google-oauth", client =>
{
    client.BaseAddress = new Uri("https://oauth2.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("google-apis", client =>
{
    client.BaseAddress = new Uri("https://www.googleapis.com/");
    client.Timeout = TimeSpan.FromMinutes(30);
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
builder.Services.AddHttpClient("minimax", client =>
{
    client.BaseAddress = new Uri("https://api.minimax.io/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
})
    .ConfigurePrimaryHttpMessageHandler(() => CreateAiHttpHandler(primaryAiUsesProxy, aiProxyUrl));
builder.Services.AddHttpClient("minimax-fallback", client =>
{
    client.BaseAddress = new Uri("https://api.minimax.io/v1/");
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
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = builder.Configuration.GetValue("Database:HangfireWorkerCount", 2);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    })
    // V-02-07: el default de [ApiController] devuelve ValidationProblemDetails,
    // que filtra traceId, la url type de rfc7231, tipos .NET y nombres de
    // propiedad C# en PascalCase. Sustituimos por un cuerpo generico y
    // logueamos el detalle real del ModelState en el servidor.
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AtlasBalance.API.ModelValidation");
            var invalidFields = string.Join(", ", context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .Select(entry => LogScrubber.Scrub(entry.Key)));
            logger.LogWarning("ModelState invalido en {PathSafe} para los campos: {InvalidFields}",
                LogScrubber.Scrub(context.HttpContext.Request.Path.Value),
                invalidFields);

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { error = "Los datos enviados no son validos. Revisa el formulario e intentalo de nuevo." });
        };
    });

// V-02.07: se retira el wiring de FluentValidation que V-02.06 anadio por
// MED-23. Nunca llego a existir ni un solo AbstractValidator<T> en el backend,
// asi que registraba un escaneo de assembly que no activaba nada. MED-23 sigue
// cerrado: la validacion real de los DTOs son los DataAnnotations
// ([Required], [MaxLength], [Range]), que es la otra via que el propio hallazgo
// daba por valida. Si algun dia hace falta FluentValidation, se vuelve a anadir
// junto con los validadores, no antes.

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
// V-02.07 (observabilidad de seguridad): firma de auditoria y espejo externo.
// Singleton: no tienen estado por peticion y el firmador solo guarda la clave.
builder.Services.AddSingleton<IAuditSigner, AuditSigner>();
builder.Services.AddSingleton<ISecurityEventLog, SecurityEventLog>();
builder.Services.AddScoped<IAuditIntegrityService, AuditIntegrityService>();
builder.Services.Configure<SecurityAlertOptions>(builder.Configuration.GetSection(SecurityAlertOptions.SectionName));
builder.Services.AddScoped<ISecurityAlertService, SecurityAlertService>();
builder.Services.AddScoped<IAlertDispatcher, AlertDispatcher>();
builder.Services.AddScoped<IAppHealthService, AppHealthService>();
// Singleton: los contadores tienen que sobrevivir a la peticion, que es el
// sentido de medir una tasa.
builder.Services.AddSingleton<IRequestMetrics, RequestMetrics>();
builder.Services.AddSingleton<ISlackAlertNotifier, SlackAlertNotifier>();
// Timeout corto: una alerta que no sale en 10s no vale la pena reintentarla en
// caliente; ya quedo registrada en AUDITORIAS y en la notificacion in-app.
builder.Services.AddHttpClient(SlackAlertNotifier.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<ITiposCambioService, TiposCambioService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IImportacionService, ImportacionService>();
builder.Services.AddScoped<ConciliacionService>();
builder.Services.AddScoped<IConciliacionService, HardenedConciliacionService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAlertaService, AlertaService>();
builder.Services.AddScoped<IPlazoFijoService, PlazoFijoService>();
builder.Services.AddScoped<IRevisionService, RevisionService>();
builder.Services.AddScoped<IAtlasAiService, AtlasAiService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<BackupConfigurationService>();
builder.Services.AddScoped<IBackupConfigurationService, HardenedBackupConfigurationService>();
builder.Services.AddScoped<IBackupEncryptionService, BackupEncryptionService>();
builder.Services.AddScoped<GoogleDriveBackupService>();
builder.Services.AddScoped<IGoogleDriveBackupService, HardenedGoogleDriveBackupService>();
builder.Services.AddScoped<IConfiguracionRepository, ConfiguracionRepository>();
builder.Services.AddScoped<IExportacionService, ExportacionService>();
builder.Services.AddScoped<IWatchdogClientService, WatchdogClientService>();
builder.Services.AddScoped<IActualizacionService, ActualizacionService>();
builder.Services.AddScoped<IIntegrationTokenService, IntegrationTokenService>();
builder.Services.AddScoped<IIntegrationAuthorizationService, IntegrationAuthorizationService>();
builder.Services.AddSingleton<IIntegrationRateLimitCleaner, IntegrationRateLimitCleaner>();
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<SyncTiposCambioJob>();
builder.Services.AddScoped<LimpiezaRefreshTokensJob>();
builder.Services.AddScoped<LimpiezaAuditoriaJob>();
builder.Services.AddScoped<LimpiezaExportacionesJob>();
builder.Services.AddScoped<BackupWeeklyJob>();
builder.Services.AddScoped<BackupSchedulerJob>();
builder.Services.AddScoped<ExportMensualJob>();
builder.Services.AddScoped<PlazoFijoVencimientoJob>();
builder.Services.AddScoped<AutoUpdateJob>();
builder.Services.AddScoped<BackupOperationJob>();

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

    recurringJobManager.AddOrUpdate<LimpiezaExportacionesJob>(
        "limpieza-exportaciones",
        job => job.ExecuteAsync(),
        Cron.Daily());

    recurringJobManager.RemoveIfExists("backup-weekly");
    recurringJobManager.AddOrUpdate<BackupSchedulerJob>(
        "backup-scheduler",
        job => job.ExecuteAsync(),
        "*/15 * * * *");

    recurringJobManager.AddOrUpdate<ExportMensualJob>(
        "export-mensual",
        job => job.ExecuteAsync(),
        "0 1 1 * *");

    recurringJobManager.AddOrUpdate<PlazoFijoVencimientoJob>(
        "plazo-fijo-vencimientos",
        job => job.ExecuteAsync(),
        Cron.Daily());

    recurringJobManager.AddOrUpdate<AutoUpdateJob>(
        "auto-update-github-release",
        job => job.ExecuteAsync(),
        "17 * * * *");

    // V-02.07: la cadencia se deriva de la ventana de las reglas para que las
    // ventanas se encadenen sin huecos. Si el operador cambia VentanaMinutos, el
    // cron se ajusta solo en el siguiente arranque.
    var ventanaAlertas = Math.Clamp(
        app.Configuration.GetValue($"{SecurityAlertOptions.SectionName}:VentanaMinutos", 5),
        1,
        59);
    recurringJobManager.AddOrUpdate<SecurityAlertJob>(
        "alertas-seguridad",
        job => job.ExecuteAsync(),
        $"*/{ventanaAlertas} * * * *");

    // A las 4:05, despues de la purga de auditoria de las 3:15: si la purga
    // dejara huecos indebidos, se detectan en la misma madrugada.
    recurringJobManager.AddOrUpdate<VerificacionIntegridadAuditoriaJob>(
        "verificacion-integridad-auditoria",
        job => job.ExecuteAsync(),
        "5 4 * * *");

    // Cada 5 minutos, la misma ventana que compara.
    recurringJobManager.AddOrUpdate<HealthAlertJob>(
        "alertas-salud",
        job => job.ExecuteAsync(),
        "*/5 * * * *");
}

app.UseForwardedHeaders();

// V-02.07: lo mas afuera posible del pipeline, para medir el tiempo real que ve
// el cliente y para que los 500 que produce UseExceptionHandler cuenten como
// errores en la tasa.
app.UseMiddleware<RequestMetricsMiddleware>();

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
            logger.LogError(feature.Error, "Unhandled API exception on {PathSafe}", LogScrubber.Scrub(context.Request.Path.Value));
        }

        if (feature?.Error is TipoCambioMissingException missingRate)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = missingRate.Message });
            return;
        }

        // Conflicto de concurrencia optimista (token xmin): otro usuario modifico
        // el registro entre la lectura y el guardado. Se devuelve 409 para que el
        // frontend recargue el dato en vez de reintentar a ciegas (evita lost updates).
        if (feature?.Error is DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "El registro fue modificado por otro usuario. Recarga los datos y vuelve a intentarlo.",
                code = "concurrency_conflict"
            });
            return;
        }

        // V-02-07: BadHttpRequestException (p.ej. body demasiado grande) traia su
        // propio StatusCode y caia en el 500 generico de abajo, lo que es
        // enganoso. Message no se expone porque puede llevar detalle tecnico
        // del limite y del parseo.
        if (feature?.Error is Microsoft.AspNetCore.Http.BadHttpRequestException badRequest)
        {
            context.Response.StatusCode = badRequest.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = "La solicitud no pudo ser procesada." });
            return;
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
        // V-02-05 (LOW-BE-3): Cross-Origin-Resource-Policy same-origin para limitar
        // quien puede embeber recursos de la API.
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        // V-02-05 (LOW-BE-1): quitar Server header que Kestrel envia por defecto.
        headers.Remove("Server");

        var connectSrc = app.Environment.IsDevelopment()
            ? "'self' http://localhost:5173 https://localhost:5000 http://localhost:5000"
            : "'self'";

        // V-02-05 (LOW-BE-2): upgrade-insecure-requests en produccion para forzar HTTPS.
        // V-02.07: se retira block-all-mixed-content. Quedo fuera de CSP nivel 3, los
        // navegadores actuales lo ignoran y Chrome lo reporta como directiva obsoleta
        // en consola. upgrade-insecure-requests ya cubre el caso.
        var cspUpgrade = app.Environment.IsDevelopment() ? string.Empty : "upgrade-insecure-requests; ";

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
            "style-src 'self' 'unsafe-inline'; " +
            cspUpgrade;

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

    // BUG-COLUMNAS (V-02-04): sin cabeceras de cache el navegador podia
    // reutilizar un index.html viejo tras un rebuild y seguir cargando
    // bundles antiguos. El html nunca se cachea; los assets con hash si.
    if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    }
    else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
    {
        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }
};
app.UseStaticFiles(staticFileOptions);
app.UseMiddleware<IntegrationAuthMiddleware>();

app.UseAuthentication();
// V-02.07: despues de UseAuthentication para tener context.User resuelto, y
// envolviendo todo lo que viene despues (autorizacion, CSRF y los controladores)
// para capturar cualquier 401/403 venga de donde venga.
app.UseMiddleware<SecurityAuditMiddleware>();
// Despues de UseAuthentication a proposito: las politicas de lectura/escritura
// particionan por userId y necesitan los claims ya resueltos. Las rutas anonimas
// (login, telemetria) siguen pasando por aqui y particionan por IP.
app.UseRateLimiter();
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
app.MapFallbackToFile("index.html", staticFileOptions);

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

// V-02-06 (RLS-SEC-01): resolucion unica del secreto RLS, antes separada
// entre Program.cs y el interceptor. Ahora el secreto saneado se inyecta al
// interceptor via DI para evitar la inconsistencia que permitia arrancar
// con un secreto en blanco o solo espacios y obtener firmas vacias.
static string ResolveRlsContextSecret(IConfiguration configuration, IHostEnvironment environment, string jwtSecret)
{
    var configured = configuration["Security:RlsContextSecret"];
    var trimmed = configured?.Trim();

    if (!string.IsNullOrEmpty(trimmed))
    {
        if (environment.IsDevelopment())
        {
            return trimmed;
        }

        // Reutilizamos la misma politica fail-closed que Production ya exige
        // para JWT y Watchdog: longitud minima 32 y rechazo de placeholders.
        RejectUnsafeProductionSecret(
            "Security:RlsContextSecret",
            trimmed,
            32);
        if (string.Equals(trimmed, jwtSecret, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Security:RlsContextSecret debe ser distinto de JwtSettings:Secret fuera de Development. " +
                "Comprometer el secreto JWT no debe permitir forjar contextos RLS.");
        }
        return trimmed;
    }

    if (environment.IsDevelopment())
    {
        // Mantenemos el fallback al JWT solo en dev para no romper `dotnet
        // run` cuando el operador aun no ha generado su appsettings propio.
        return jwtSecret;
    }

    throw new InvalidOperationException(
        "Security:RlsContextSecret debe estar configurado fuera de Development. " +
        "Genera una clave aleatoria de al menos 32 caracteres y distinala de JwtSettings:Secret. " +
        "Si necesitas migrar una instalacion existente, Actualizar-AtlasBalance.ps1 puede generarla.");
}

// V-02.07: clave HMAC con la que se firma cada fila de AUDITORIAS. Misma
// politica que ResolveRlsContextSecret: en Development se tolera el fallback al
// secreto JWT para no romper `dotnet run`; fuera de Development se exige clave
// propia de 32+ caracteres y distinta del JWT.
//
// Rotarla invalida la verificacion de las filas ya firmadas: /api/auditoria/
// integridad las reportara como no verificables, no como manipuladas.
static string ResolveAuditSigningKey(IConfiguration configuration, IHostEnvironment environment, string jwtSecret)
{
    var trimmed = configuration["Security:AuditSigningKey"]?.Trim();

    if (!string.IsNullOrEmpty(trimmed))
    {
        if (environment.IsDevelopment())
        {
            return trimmed;
        }

        RejectUnsafeProductionSecret(
            "Security:AuditSigningKey",
            trimmed,
            32);
        if (string.Equals(trimmed, jwtSecret, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Security:AuditSigningKey debe ser distinto de JwtSettings:Secret fuera de Development. " +
                "Si coinciden, comprometer el JWT permite forjar filas de auditoria con firma valida.");
        }
        return trimmed;
    }

    if (environment.IsDevelopment())
    {
        return jwtSecret;
    }

    throw new InvalidOperationException(
        "Security:AuditSigningKey debe estar configurado fuera de Development. " +
        "Genera una clave aleatoria de al menos 32 caracteres, distinta de JwtSettings:Secret y de " +
        "Security:RlsContextSecret, y guardala fuera de la base de datos: si vive en la misma BD que " +
        "AUDITORIAS, quien compromete la BD puede refirmar las filas que altere.");
}

static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        foreach (var rawProxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rawProxy))
            {
                continue;
            }

            if (!IPAddress.TryParse(rawProxy.Trim(), out var proxyAddress))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contiene una IP invalida: {rawProxy}");
            }

            options.KnownProxies.Add(proxyAddress);
        }

        foreach (var rawNetwork in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rawNetwork))
            {
                continue;
            }

            var parts = rawNetwork.Trim().Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out var prefix) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefixLength) ||
                !IsValidPrefixLength(prefix, prefixLength))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contiene una red CIDR invalida: {rawNetwork}");
            }

            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
        }
    });
}

static bool IsValidPrefixLength(IPAddress prefix, int prefixLength)
{
    return prefix.AddressFamily switch
    {
        AddressFamily.InterNetwork => prefixLength is >= 0 and <= 32,
        AddressFamily.InterNetworkV6 => prefixLength is >= 0 and <= 128,
        _ => false
    };
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

static Uri ResolveWatchdogBaseUri(string? configuredBaseUrl)
{
    var rawBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
        ? "http://localhost:5001"
        : configuredBaseUrl.Trim();

    if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var uri) ||
        uri.Scheme is not ("http" or "https") ||
        !IsLoopbackHost(uri.Host))
    {
        throw new InvalidOperationException("WatchdogSettings:BaseUrl debe apuntar a localhost/loopback.");
    }

    return uri;
}

static bool IsLoopbackHost(string host)
{
    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
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

    // V-02-06 (BACKUP-02): en produccion exigimos una MigrationConnection
    // explicita para que migraciones, semilla del secreto RLS y concesiones
    // de privilegios usen el rol owner. Caer al runtime deja al usuario
    // app_user manejando migraciones, lo que ya ha provocado errores de
    // "permission denied for table __EFMigrationsHistory" en instalaciones
    // legacy. El actualizador es responsable de regenerar esta cadena en
    // upgrades antes de llegar aqui.
    if (!environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "ConnectionStrings:MigrationConnection es obligatorio fuera de Development. " +
            "Configuralo con un usuario owner (atlas_balance_owner) y una password dedicada. " +
            "Si vienes de una version anterior, Actualizar-AtlasBalance.ps1 puede regenerarlo.");
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

        -- V-02.07: AUDITORIAS es append-only tambien a nivel de privilegios.
        -- El GRANT de arriba es un blanket sobre todas las tablas del esquema, y
        -- sin este REVOKE el rol de la aplicacion podria modificar o borrar su
        -- propio rastro. Aqui la separacion de roles si da prevencion real y no
        -- solo deteccion: el rol de runtime no es el propietario de la tabla,
        -- asi que no puede volver a concederse el privilegio, ni quitar el
        -- trigger, ni alterar la tabla.
        --
        -- La purga por retencion sigue funcionando porque va por
        -- atlas_security.purgar_auditorias(), que es SECURITY DEFINER y por
        -- tanto se ejecuta con los privilegios del propietario.
        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "AUDITORIAS" FROM {{runtimeRole}};
        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "AUDITORIA_INTEGRACIONES" FROM {{runtimeRole}};
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

static void WarnIfConnectionStringSslModeUnsafe(string key, string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return;
    }

    NpgsqlConnectionStringBuilder parsed;
    try
    {
        parsed = new NpgsqlConnectionStringBuilder(connectionString);
    }
    catch (ArgumentException)
    {
        // Cadena no parseable: no es este el sitio para validar su formato.
        return;
    }

    var host = parsed.Host?.Trim();
    var isLoopbackHost = string.IsNullOrEmpty(host) ||
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host == "127.0.0.1" ||
        host == "::1";

    if (isLoopbackHost || (parsed.SslMode != SslMode.Disable && parsed.SslMode != SslMode.Prefer))
    {
        return;
    }

    Log.Warning(
        "{Key} apunta a un host remoto ({Host}) con SslMode={SslMode}: el trafico con PostgreSQL " +
        "(incluida PII financiera) viaja sin cifrar y nada lo detecta. Usa SslMode=Require como minimo " +
        "cuando la base de datos no corre en la misma maquina que la API.",
        key,
        host,
        parsed.SslMode);
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

static void ProtectExistingConfigurationSecrets(AppDbContext dbContext, ISecretProtector secretProtector)
{
    var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "smtp_password",
        "exchange_rate_api_key",
        "openrouter_api_key",
        "openai_api_key",
        "minimax_api_key",
        "google_drive_oauth_client_secret",
        "backup_cloud_encryption_key",
        "github_update_token"
    };

    var changed = false;
    foreach (var item in dbContext.Configuraciones.Where(c => secretKeys.Contains(c.Clave)))
    {
        if (!item.EsSecreto)
        {
            item.EsSecreto = true;
            changed = true;
        }

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
