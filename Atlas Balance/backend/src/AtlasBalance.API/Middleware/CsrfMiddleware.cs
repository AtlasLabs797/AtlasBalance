using AtlasBalance.API.Logging;
using AtlasBalance.API.Services;

namespace AtlasBalance.API.Middleware;

public sealed class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfMiddleware> _logger;

    // login/mfa/refresh: el cliente aun no posee csrf_token; refresh-token se protege via SameSite=Strict.
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/mfa/verify",
        "/api/auth/refresh-token",
        "/api/health"
    };

    public CsrfMiddleware(RequestDelegate next, ILogger<CsrfMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICsrfService csrfService)
    {
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

        return !ExcludedPaths.Contains(request.Path.Value ?? string.Empty);
    }
}
