using AtlasBalance.API.Constants;

namespace AtlasBalance.API.Services;

/// <summary>
/// Datos de contexto (User-Agent, sesion, canal de entrada) que acompanan a
/// cada fila de AUDITORIAS. Se resuelven desde el HttpContext en un unico sitio
/// para que AuditService y AuditSaveChangesInterceptor no diverjan.
/// </summary>
public readonly record struct AuditRequestInfo(string? UserAgent, string? SessionId, string Origen)
{
    public static AuditRequestInfo Job { get; } = new(null, null, AuditOrigenes.Job);
}

public static class AuditRequestContext
{
    public const int MaxUserAgentLength = 256;
    public const int MaxSessionIdLength = 64;

    /// <summary>Nombre del claim que transporta el id de sesion en el JWT.</summary>
    public const string SessionClaim = "sid";

    // Mismos nombres que usa JwtBearerEvents.OnMessageReceived en Program.cs.
    // Si cambian alli, hay que cambiarlos aqui: es la senal de que la peticion
    // viene del navegador y no de la integracion.
    private static readonly string[] AccessTokenCookies = { "__Host-atlas-access-token", "access_token" };

    public static AuditRequestInfo Resolver(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return AuditRequestInfo.Job;
        }

        return new AuditRequestInfo(
            Truncar(httpContext.Request.Headers.UserAgent.ToString(), MaxUserAgentLength),
            Truncar(httpContext.User?.FindFirst(SessionClaim)?.Value, MaxSessionIdLength),
            ResolverOrigen(httpContext));
    }

    private static string ResolverOrigen(HttpContext httpContext)
    {
        // La integracion OpenClaw tiene su propia ruta y su propio middleware de
        // bearer token; no pasa por el JwtBearer de la UI.
        if (httpContext.Request.Path.StartsWithSegments("/api/integration", StringComparison.OrdinalIgnoreCase))
        {
            return AuditOrigenes.Api;
        }

        foreach (var cookie in AccessTokenCookies)
        {
            if (!string.IsNullOrEmpty(httpContext.Request.Cookies[cookie]))
            {
                return AuditOrigenes.Ui;
            }
        }

        // Un bearer explicito sin cookie de sesion es acceso directo a la API.
        // La UI nunca manda Authorization: su token viaja en cookie httpOnly.
        if (httpContext.Request.Headers.Authorization.Count > 0)
        {
            return AuditOrigenes.Api;
        }

        // Login, refresh y telemetria son anonimos por definicion, pero llegan
        // del navegador: si traen el header CSRF o un Origin propio, son UI.
        if (!string.IsNullOrEmpty(httpContext.Request.Headers["X-CSRF-Token"].ToString()) ||
            !string.IsNullOrEmpty(httpContext.Request.Headers.Origin.ToString()))
        {
            return AuditOrigenes.Ui;
        }

        return AuditOrigenes.Desconocido;
    }

    private static string? Truncar(string? value, int maxLength)
    {
        // \r y \n fuera: estos valores acaban en logs y en CSV de auditoria.
        var normalized = value?.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
