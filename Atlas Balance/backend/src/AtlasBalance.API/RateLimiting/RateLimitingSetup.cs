using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AtlasBalance.API.Logging;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.RateLimiting;

/// <summary>
/// Rate limiting global de la API (V-02.07). Sustituye a la idea de decorar 153
/// endpoints uno a uno: el limitador global clasifica por ruta y verbo, asi que
/// cualquier endpoint nuevo queda cubierto por defecto sin que nadie se acuerde
/// de anadir un atributo. Solo las operaciones caras llevan atributo explicito
/// (<see cref="PolicyNames.Expensive"/>), que se SUMA al limite de escritura.
///
/// Particionado: lo autenticado va por <c>userId</c> y no por IP, para que la
/// topologia de red (proxy delante, varios usuarios tras la misma IP) no pueda
/// meter a gente distinta en el mismo cubo. Solo las rutas anonimas particionan
/// por IP, que es lo unico que hay antes de tener sesion.
/// </summary>
internal static class RateLimitingSetup
{
    internal static class PolicyNames
    {
        public const string Expensive = "atlas-expensive";
    }

    private const string IntegrationPathPrefix = "/api/integration/openclaw";
    private const string ApiPathPrefix = "/api";
    private const string HealthPath = "/api/health";
    private const string HealthPathPrefix = "/api/health/";
    // V-02.08: a diferencia de /api/health y /api/health/ready (stateless),
    // /api/health/functional abre una transaccion, publica un contexto RLS
    // elevado, inserta en AUDITORIAS y hace rollback en cada llamada. Eximirlo
    // del limitador permitiria a un cliente anonimo agotar el pool de
    // conexiones de PostgreSQL con sondas paralelas ilimitadas.
    private const string FunctionalHealthPath = "/api/health/functional";

    /// <summary>
    /// Rutas que verifican credenciales. Van a su propio cubo por IP, mas
    /// estrecho que el resto, porque son las unicas atacables sin sesion previa.
    /// <c>cambiar-password</c> entra aqui aunque exija sesion: compara la
    /// contrasena actual, asi que es superficie de fuerza bruta igual.
    /// </summary>
    private static readonly string[] AuthPaths =
    {
        "/api/auth/login",
        "/api/auth/refresh-token",
        "/api/auth/mfa/verify",
        "/api/auth/cambiar-password"
    };

    public static IServiceCollection AddAtlasRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                return ResolvePartition(context, options);
            });

            limiter.AddPolicy(PolicyNames.Expensive, context =>
            {
                var options = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                if (!options.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                return Window(
                    $"expensive:{ResolveIdentityKey(context)}",
                    options.ExpensivePerMinutePerUser,
                    options.Window);
            });

            limiter.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    private static RateLimitPartition<string> ResolvePartition(HttpContext context, RateLimitingOptions options)
    {
        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        var path = context.Request.Path;

        // Los estaticos de la SPA y el healthcheck no consumen presupuesto de API.
        // V-02.08: tambien se eximen los nuevos /api/health/ready y
        // /api/health/functional, que el instalador y el actualizador invocan
        // como sondeos de readiness tras reiniciar servicios.
        // codeql[cs/user-controlled-bypass] — by design: static assets and health
        // endpoints are public, stateless, and rate-limiting them adds no security
        // value. The check uses StartsWithSegments (prefix match on parsed
        // PathString), not raw string comparison.
        if (path.Equals(FunctionalHealthPath, StringComparison.OrdinalIgnoreCase))
        {
            return Window($"health-functional:{ResolveIpKey(context)}", options.AuthPerMinutePerIp, options.Window);
        }

        if (!path.StartsWithSegments(ApiPathPrefix)
            || path.Equals(HealthPath)
            || path.StartsWithSegments(HealthPathPrefix))
        {
            return RateLimitPartition.GetNoLimiter("exento");
        }

        // La integracion OpenClaw ya tiene su limite por token en
        // IntegrationAuthMiddleware (100/min configurable en BD). Meterla aqui
        // la particionaria por IP, que es la clave equivocada: un token es un
        // token venga de donde venga.
        if (path.StartsWithSegments(IntegrationPathPrefix))
        {
            return RateLimitPartition.GetNoLimiter("integracion");
        }

        var ip = ResolveIpKey(context);

        if (IsAuthPath(path))
        {
            return Window($"auth:{ip}", options.AuthPerMinutePerIp, options.Window);
        }

        var userId = ResolveUserId(context);
        if (userId is null)
        {
            return Window($"anon:{ip}", options.AnonymousPerMinutePerIp, options.Window);
        }

        return IsReadMethod(context.Request.Method)
            ? Window($"read:{userId}", options.ReadPerMinutePerUser, options.Window)
            : Window($"write:{userId}", options.WritePerMinutePerUser, options.Window);
    }

    private static RateLimitPartition<string> Window(string key, int permitLimit, TimeSpan window)
    {
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    private static bool IsAuthPath(PathString path)
    {
        foreach (var authPath in AuthPaths)
        {
            if (path.Equals(authPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReadMethod(string method)
    {
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
    }

    private static string? ResolveUserId(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst("sub")?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Clave de la politica de operaciones caras: usuario si lo hay, IP si no.
    /// Todos los endpoints caros exigen sesion, asi que la rama de IP solo
    /// existe para no dejar la clave vacia si alguno deja de exigirla.
    /// </summary>
    private static string ResolveIdentityKey(HttpContext context)
    {
        return ResolveUserId(context) ?? ResolveIpKey(context);
    }

    private static string ResolveIpKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
    }

    /// <summary>
    /// Devuelve 429 con <c>Retry-After</c> y deja constancia en el log. Ese log
    /// es la medicion: si aparecen rechazos de usuarios reales, los limites de
    /// <c>appsettings</c> estan mal calibrados y hay que subirlos con ese dato
    /// delante, no a ojo.
    /// </summary>
    private static ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : options.WindowSeconds;

        if (retryAfterSeconds < 1)
        {
            retryAfterSeconds = 1;
        }

        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AtlasBalance.API.RateLimiting");

        // Scrub obligatorio: Path y Method son tainted para CodeQL (cs/log-forging,
        // CWE-117). Mismo patron que CsrfMiddleware tras el alert #16.
        logger.LogWarning(
            "Rate limit alcanzado: metodo={MethodSafe} path={PathSafe} ip={IpSafe} usuario={UsuarioSafe} retryAfter={RetryAfter}s",
            LogScrubber.Scrub(httpContext.Request.Method),
            LogScrubber.Scrub(httpContext.Request.Path.Value),
            LogScrubber.Scrub(ResolveIpKey(httpContext)),
            LogScrubber.Scrub(ResolveUserId(httpContext)) ?? "anonimo",
            retryAfterSeconds);

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        return new ValueTask(httpContext.Response.WriteAsJsonAsync(
            new { error = "Demasiadas peticiones. Espera unos segundos y vuelve a intentarlo." },
            cancellationToken));
    }
}
