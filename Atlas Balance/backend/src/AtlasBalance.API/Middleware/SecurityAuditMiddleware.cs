using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Services;
using Microsoft.Extensions.Caching.Memory;

namespace AtlasBalance.API.Middleware;

/// <summary>
/// V-02.07: audita lo que hasta ahora no dejaba ningun rastro.
///
/// - 403: alguien autenticado intentando tocar un recurso que no le pertenece.
///   Habia decenas de `Forbid()` repartidos por los controladores y ninguno se
///   registraba, asi que un usuario probando ids de cuentas ajenas era invisible.
/// - 401 sobre endpoints protegidos: token ausente, caducado o manipulado.
/// - Lectura masiva: peticiones que piden mas filas que el umbral.
///
/// Se resuelve en middleware y no tocando los ~40 `Forbid()` a proposito: cubre
/// tambien los 403 que emite el propio pipeline de autorizacion (roles, policies)
/// y los que anadan futuros endpoints sin que nadie tenga que acordarse.
/// </summary>
public sealed class SecurityAuditMiddleware
{
    /// <summary>
    /// Rutas donde un 401 es funcionamiento normal, no una senal. El login ya
    /// audita sus propios fallos (LOGIN_FAILED) con mucho mas contexto, y el
    /// refresh devuelve 401 cada vez que caduca un access token.
    /// </summary>
    private static readonly string[] RutasSinAuditar401 =
    {
        "/api/auth/login",
        "/api/auth/refresh-token",
        "/api/auth/logout",
        "/api/telemetria",
        "/api/health"
    };

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SecurityAuditMiddleware> _logger;
    private readonly int _umbralBulk;
    private readonly TimeSpan _ventanaDeduplicacion;

    public SecurityAuditMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<SecurityAuditMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
        _umbralBulk = configuration.GetValue("Security:Auditoria:UmbralAccesoBulk", 100);
        _ventanaDeduplicacion = TimeSpan.FromSeconds(
            configuration.GetValue("Security:Auditoria:VentanaDeduplicacionSegundos", 60));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // Solo la API. Los estaticos y el fallback a index.html no interesan.
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await AuditarSiProcedeAsync(context);
        }
        catch (Exception ex)
        {
            // La respuesta ya salio. Un fallo auditando no puede romper nada,
            // pero tiene que verse: es un agujero en la observabilidad.
            _logger.LogError(ex, "No se pudo auditar el evento de seguridad de {PathSafe}", LogScrubber.Scrub(context.Request.Path.Value));
        }
    }

    private async Task AuditarSiProcedeAsync(HttpContext context)
    {
        var status = context.Response.StatusCode;
        var esAuthz = status == StatusCodes.Status403Forbidden;
        var esAuthn = status == StatusCodes.Status401Unauthorized && !EsRutaExcluidaDe401(context.Request.Path);
        var filasPedidas = LeerPageSize(context.Request.Query);
        var esBulk = status is >= 200 and < 300 && filasPedidas > _umbralBulk;

        if (!esAuthz && !esAuthn && !esBulk)
        {
            return;
        }

        // Un bucle del frontend o un escaneo pueden generar cientos de 403
        // identicos por segundo. Sin deduplicar, la tabla de auditoria se
        // convierte en el vector de denegacion de servicio en vez de la defensa.
        var usuarioId = ResolverUsuarioId(context.User);
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var tipoAccion = esAuthz
            ? AuditActions.AuthzDenied
            : esAuthn
                ? AuditActions.AuthnDenied
                : AuditActions.AccesoBulk;

        if (!DebeRegistrar(tipoAccion, usuarioId, ip, context.Request.Path))
        {
            return;
        }

        var auditService = context.RequestServices.GetRequiredService<IAuditService>();
        var detalles = JsonSerializer.Serialize(new
        {
            metodo = context.Request.Method,
            // Solo el path. La query puede llevar filtros con datos de negocio y
            // no aporta nada que el path y el usuario no digan ya.
            ruta = LogScrubber.Scrub(context.Request.Path.Value),
            status_code = status,
            rol = context.User?.FindFirstValue(ClaimTypes.Role),
            filas_pedidas = esBulk ? filasPedidas : (int?)null,
            umbral_bulk = esBulk ? _umbralBulk : (int?)null
        });

        await auditService.LogAsync(
            usuarioId,
            tipoAccion,
            entidadTipo: null,
            entidadId: null,
            ipAddress: ip,
            detallesJson: detalles,
            cancellationToken: context.RequestAborted);
    }

    private bool DebeRegistrar(string tipoAccion, Guid? usuarioId, string? ip, PathString path)
    {
        var clave = string.Create(
            CultureInfo.InvariantCulture,
            $"secaudit:{tipoAccion}:{usuarioId}:{ip}:{path.Value}");

        if (_cache.TryGetValue(clave, out _))
        {
            return false;
        }

        // Size explicito: el IMemoryCache de la app tiene SizeLimit configurado
        // y una entrada sin Size lanza al insertarse.
        _cache.Set(clave, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ventanaDeduplicacion,
            Size = 1
        });
        return true;
    }

    private static bool EsRutaExcluidaDe401(PathString path)
    {
        foreach (var ruta in RutasSinAuditar401)
        {
            if (path.StartsWithSegments(ruta, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Filas SOLICITADAS, no devueltas: desde el middleware no se puede contar el
    /// cuerpo sin bufferizarlo entero, y bufferizar cada respuesta para auditar
    /// saldria mas caro que el problema. Como senal de intencion vale: los
    /// endpoints paginados topan pageSize, asi que pedir 5000 es deliberado.
    /// Los exports masivos ya se auditan aparte (EXPORTACION_GENERADA).
    /// </summary>
    private static int LeerPageSize(IQueryCollection query)
    {
        if (!query.TryGetValue("pageSize", out var valores))
        {
            return 0;
        }

        return int.TryParse(valores.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static Guid? ResolverUsuarioId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
