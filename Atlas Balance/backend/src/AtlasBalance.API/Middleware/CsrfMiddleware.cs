using AtlasBalance.API.Logging;
using AtlasBalance.API.Services;

namespace AtlasBalance.API.Middleware;

public sealed class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfMiddleware> _logger;
    private readonly bool _isDevelopment;

    // V-02.07: en desarrollo el frontend corre en Vite (5173) y la API en 5000, asi que
    // las peticiones son cross-origin de forma legitima. En produccion el frontend se
    // sirve como estatico desde la propia API, luego siempre es same-origin.
    private static readonly HashSet<string> DevelopmentOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:5173",
        "https://localhost:5173"
    };

    // login/mfa/refresh: el cliente aun no posee csrf_token; refresh-token se protege via SameSite=Strict.
    // V-02.07: telemetria/errores se excluye porque el frontend la envia con
    // navigator.sendBeacon, que no permite cabeceras personalizadas y por tanto no
    // puede mandar X-CSRF-Token. El endpoint no lee ni modifica datos: solo escribe
    // una linea de log acotada, asi que el riesgo de CSRF es despreciable.
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/mfa/verify",
        "/api/auth/refresh-token",
        "/api/health",
        "/api/telemetria/errores"
    };

    public CsrfMiddleware(RequestDelegate next, ILogger<CsrfMiddleware> logger, IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _isDevelopment = environment.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context, ICsrfService csrfService)
    {
        // V-02.07: verificacion de Origin/Referer como capa independiente del token.
        // Se aplica ANTES y sin las exclusiones de ExcludedPaths a proposito: cubre
        // /api/auth/refresh-token, que hasta ahora dependia solo de SameSite=Strict.
        if (RequiresOriginValidation(context.Request) && !IsAllowedOrigin(context.Request))
        {
            _logger.LogWarning(
                "Origen rechazado: path={PathSafe} method={MethodSafe} origin={OriginSafe} ip={IpSafe}",
                LogScrubber.Scrub(context.Request.Path.Value),
                LogScrubber.Scrub(context.Request.Method),
                LogScrubber.Scrub(context.Request.Headers.Origin.FirstOrDefault()),
                LogScrubber.Scrub(context.Connection.RemoteIpAddress?.ToString()));
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Origen no permitido" });
            return;
        }

        if (RequiresCsrfValidation(context.Request))
        {
            // S-NEW-1 (V-02-03): aceptar tanto el prefijo __Host- (produccion)
            // como el nombre legado csrf_token (dev). Asi no rompemos dev
            // local en HTTP plano.
            var csrfCookie = context.Request.Cookies["__Host-atlas-csrf-token"]
                ?? context.Request.Cookies["csrf_token"];
            var csrfHeader = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();

            if (!csrfService.IsValid(csrfCookie, csrfHeader))
            {
                // V-02-05 (MED-9): registrar el intento rechazado para visibilidad.
                // V-02-06 (CodeQL #10/#11): sanear path/ip/ua antes de loguearlos para
                // evitar CWE-117 (log forging) si el cliente envia CRLF en la URL o en
                // cabeceras.
                // V-02.07 (CodeQL #16): HttpRequest.Method es string y CodeQL lo considera
                // tainted aunque Kestrel normalice verbos validos. Mismo Scrub que el resto.
                _logger.LogWarning(
                    "CSRF rechazado: path={PathSafe} method={MethodSafe} ip={IpSafe} ua={UaSafe}",
                    LogScrubber.Scrub(context.Request.Path.Value),
                    LogScrubber.Scrub(context.Request.Method),
                    LogScrubber.Scrub(context.Connection.RemoteIpAddress?.ToString()),
                    LogScrubber.Scrub(context.Request.Headers.UserAgent.ToString()));
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "CSRF token inválido" });
                return;
            }
        }

        await _next(context);
    }

    private static bool RequiresCsrfValidation(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        if (!request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // V-02.08: la integracion OpenClaw se autentica con Bearer token propio
        // (IntegrationAuthMiddleware), no con cookies de sesion, asi que CSRF no
        // aplica. Exigir cookie csrf_token + header a un cliente Bearer-only
        // rompia el contrato (fail-closed, pero obligaba a fabricar una pareja
        // arbitraria). El middleware de integracion ya valida token, scopes y
        // rate limit por su cuenta.
        if (request.Path.StartsWithSegments("/api/integration", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ExcludedPaths.Contains(request.Path.Value ?? string.Empty);
    }

    private static bool RequiresOriginValidation(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        return request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAllowedOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(origin))
        {
            var referer = request.Headers.Referer.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(referer))
            {
                // Sin Origin ni Referer no hay peticion de navegador cross-site: los
                // navegadores mandan Origin en todo POST/PUT/PATCH/DELETE. Se deja pasar
                // para no romper clientes no-navegador; el token CSRF sigue aplicando.
                return true;
            }

            if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                return false;
            }

            origin = refererUri.GetLeftPart(UriPartial.Authority);
        }

        // El frontend se sirve desde la propia API, luego el origen esperado es el de la
        // peticion. Si algun dia se pone un proxy inverso que reescriba el Host, hay que
        // anadir ForwardedHeaders.XForwardedHost en Program.cs o esto rechazara todo.
        var expected = $"{request.Scheme}://{request.Host.Value}";
        if (string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _isDevelopment && DevelopmentOrigins.Contains(origin);
    }
}
