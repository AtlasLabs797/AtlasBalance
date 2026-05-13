using AtlasBalance.API.Services;

namespace AtlasBalance.API.Middleware;

public sealed class CsrfMiddleware
{
    private readonly RequestDelegate _next;

    // login/mfa/refresh: el cliente aun no posee csrf_token; refresh-token se protege via SameSite=Strict.
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/mfa/verify",
        "/api/auth/refresh-token",
        "/api/health"
    };

    public CsrfMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICsrfService csrfService)
    {
        if (RequiresCsrfValidation(context.Request))
        {
            var csrfCookie = context.Request.Cookies["csrf_token"];
            var csrfHeader = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();

            if (!csrfService.IsValid(csrfCookie, csrfHeader))
            {
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
