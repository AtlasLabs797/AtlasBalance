using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace AtlasBalance.API.Middleware;

public static class HttpContextItemKeys
{
    public const string CurrentUsuario = "current_usuario";
}

public sealed class UserStateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserStateMiddleware> _logger;

    // V-02.07: la respuesta al cliente es siempre esta, sea cual sea el motivo del
    // rechazo. Para el usuario legitimo la accion es la misma en todos los casos
    // (volver a iniciar sesion), y distinguirlos solo le dice a quien posee un token
    // robado por que dejo de funcionar. Es la misma politica que ya aplica el login,
    // que enmascara deliberadamente cuenta inexistente, bloqueada y password
    // incorrecta bajo un unico "Credenciales invalidas". El motivo real se registra
    // en el log del servidor.
    private const string RejectionMessage = "La sesion ya no es valida. Vuelve a iniciar sesion.";

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/refresh-token",
        "/api/auth/logout",
        "/api/health"
    };

    public UserStateMiddleware(RequestDelegate next, ILogger<UserStateMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

        // V-02.06: los administradores necesitan garantia MFA en el JWT. Si el
        // token se emitio sin ella (sesion heredada o bypass de la politica
        // anterior), la sesion se rechaza para forzar un re-login con MFA.
        if (usuario.Rol == RolUsuario.ADMIN && !HasMfaAssurance(context.User, usuario))
        {
            await RejectAsync(context, "Se requiere MFA para continuar");
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

    private static bool HasMfaAssurance(ClaimsPrincipal principal, Usuario usuario)
    {
        var verifiedRaw = principal.FindFirstValue(AuthClaimNames.MfaVerifiedAt);
        var mfaStamp = principal.FindFirstValue(AuthClaimNames.MfaSecurityStamp);

        if (string.IsNullOrWhiteSpace(verifiedRaw) || string.IsNullOrWhiteSpace(mfaStamp))
        {
            return false;
        }

        // La garantia MFA debe estar anclada al security stamp actual del
        // usuario. Si el stamp cambia (rotacion por password, revocacion
        // administrativa, etc.) la garantia se considera obsoleta.
        var stampBytes = Encoding.UTF8.GetBytes(mfaStamp);
        var userBytes = Encoding.UTF8.GetBytes(usuario.SecurityStamp);
        if (stampBytes.Length != userBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(stampBytes, userBytes))
        {
            return false;
        }

        if (!long.TryParse(verifiedRaw, out var unix))
        {
            return false;
        }

        var verifiedAt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        return verifiedAt <= DateTime.UtcNow;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private async Task RejectAsync(HttpContext context, string reason)
    {
        // El motivo concreto solo va al log del servidor. path/ip se sanean para
        // evitar log forging (CWE-117), igual que en CsrfMiddleware; reason es
        // siempre un literal interno, no entra input del cliente.
        _logger.LogWarning(
            "Sesion rechazada: motivo={Reason} path={PathSafe} ip={IpSafe}",
            reason,
            LogScrubber.Scrub(context.Request.Path.Value),
            LogScrubber.Scrub(context.Connection.RemoteIpAddress?.ToString()));

        DeleteAuthCookies(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = RejectionMessage });
    }

    // Borra las cookies de sesion usando el nombre real segun entorno. En produccion
    // llevan el prefijo __Host-atlas- (ver AuthController.CookieName); borrar solo los
    // nombres legacy dejaba la cookie real viva hasta caducar. Se borran ambas variantes
    // por si quedaran cookies antiguas de una version anterior.
    private static void DeleteAuthCookies(HttpContext context)
    {
        var env = context.RequestServices?.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
        var isDev = env?.IsDevelopment() ?? false;
        var secure = !isDev;

        void Delete(string name, bool httpOnly)
        {
            context.Response.Cookies.Delete(name, new CookieOptions
            {
                Path = "/",
                HttpOnly = httpOnly,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                IsEssential = true
            });
        }

        (string BaseName, bool HttpOnly)[] cookies =
        [
            ("access_token", true),
            ("refresh_token", true),
            ("csrf_token", false),
            ("mfa_trusted", true)
        ];

        foreach (var (baseName, httpOnly) in cookies)
        {
            var realName = isDev ? baseName : $"__Host-atlas-{baseName.Replace("_", "-")}";
            Delete(realName, httpOnly);
            if (!string.Equals(realName, baseName, StringComparison.Ordinal))
            {
                Delete(baseName, httpOnly);
            }
        }
    }
}
