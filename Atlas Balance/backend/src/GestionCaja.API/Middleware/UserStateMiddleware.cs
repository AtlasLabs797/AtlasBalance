using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Middleware;

public static class HttpContextItemKeys
{
    public const string CurrentUsuario = "current_usuario";
}

public sealed class UserStateMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/refresh-token",
        "/api/auth/logout",
        "/api/health"
    };

    public UserStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        if (!RequiresValidation(context))
        {
            await _next(context);
            return;
        }

        if (!TryGetUserId(context.User, out var userId))
        {
            await RejectAsync(context, "Token de usuario invalido");
            return;
        }

        var usuario = await dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, context.RequestAborted);

        if (usuario is null || !usuario.Activo || usuario.DeletedAt.HasValue)
        {
            await RejectAsync(context, "La sesion ya no es valida");
            return;
        }

        if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > DateTime.UtcNow)
        {
            await RejectAsync(context, "Usuario bloqueado temporalmente por intentos fallidos");
            return;
        }

        if (!HasValidSecurityStamp(context.User, usuario))
        {
            await RejectAsync(context, "La sesion ya no es valida");
            return;
        }

        context.Items[HttpContextItemKeys.CurrentUsuario] = usuario;
        context.User = BuildPrincipal(usuario, context.User.Identity?.AuthenticationType);

        await _next(context);
    }

    private static bool RequiresValidation(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ExcludedPaths.Contains(context.Request.Path.Value ?? string.Empty);
    }

    private static ClaimsPrincipal BuildPrincipal(Usuario usuario, string? authenticationType)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", usuario.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Email, usuario.Email),
            new Claim(ClaimTypes.Name, usuario.NombreCompleto),
            new Claim(ClaimTypes.Role, usuario.Rol.ToString()),
            new Claim(AuthClaimNames.SecurityStamp, usuario.SecurityStamp)
        }, authenticationType ?? "JwtCookie");

        return new ClaimsPrincipal(identity);
    }

    private static bool HasValidSecurityStamp(ClaimsPrincipal principal, Usuario usuario)
    {
        var tokenStamp = principal.FindFirstValue(AuthClaimNames.SecurityStamp);
        if (string.IsNullOrWhiteSpace(tokenStamp) || string.IsNullOrWhiteSpace(usuario.SecurityStamp))
        {
            return false;
        }

        var tokenBytes = Encoding.UTF8.GetBytes(tokenStamp);
        var userBytes = Encoding.UTF8.GetBytes(usuario.SecurityStamp);
        return tokenBytes.Length == userBytes.Length &&
               CryptographicOperations.FixedTimeEquals(tokenBytes, userBytes);
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private static async Task RejectAsync(HttpContext context, string error)
    {
        context.Response.Cookies.Delete("access_token");
        context.Response.Cookies.Delete("refresh_token");
        context.Response.Cookies.Delete("csrf_token");
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error });
    }
}
