using AtlasBalance.API.Models;

namespace AtlasBalance.API.Middleware;

public sealed class PrimerLoginMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/refresh-token",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/auth/cambiar-password",
        "/api/health"
    };

    public PrimerLoginMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresPrimerLoginCheck(context))
        {
            await _next(context);
            return;
        }

        if (context.Items[HttpContextItemKeys.CurrentUsuario] is Usuario usuario && usuario.PrimerLogin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Debes cambiar la contrasena antes de continuar"
            });
            return;
        }

        await _next(context);
    }

    private static bool RequiresPrimerLoginCheck(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !AllowedPaths.Contains(context.Request.Path.Value ?? string.Empty);
    }
}
