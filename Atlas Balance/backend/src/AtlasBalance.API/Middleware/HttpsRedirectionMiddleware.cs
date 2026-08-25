using System.Net;
using System.Text;

namespace AtlasBalance.API.Middleware;

// SEC V-02.09: la redireccion HTTP->HTTPS de esta app depende de la topologia
// (ver DOCUMENTACION_TECNICA, transporte). UseHttpsRedirection del framework es
// un no-op cuando Kestrel no tiene endpoint HTTPS ni ASPNETCORE_HTTPS_PORT, que
// es justo el caso del modo reverse-proxy (Kestrel ligado a http://127.0.0.1:5000).
// Este middleware cubre los dos modos sin crear bucles de redireccion:
//
//  - Modo directo (Kestrel https://0.0.0.0:443): si alguien abre un listener
//    HTTP expuesto, una peticion remota por HTTP se redirige a HTTPS.
//  - Modo reverse-proxy: solo se redirige si el proxy declaro X-Forwarded-Proto,
//    es decir, si sabemos que el cliente externo vino por HTTP. Si el proxy no
//    envia la cabecera, no hay forma segura de distinguir "cliente externo en
//    HTTP" de "el propio proxy en loopback", y redirigir a ciegas provocaria un
//    bucle 308: se mantiene el comportamiento anterior y queda como requisito
//    del proxy exigir TLS externamente (documentado en el runbook).
//
// Exclusiones: /api/health* debe seguir respondiendo por HTTP local porque el
// Watchdog, el instalador y las sondas post-actualizacion lo consultan sobre
// loopback sin TLS.
//
// Se emite 308 (no 301) para conservar el metodo: un POST reenviado con 301
// pasa a GET en muchos clientes y corrompe peticiones de escritura.
public sealed class HttpsRedirectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private readonly int? _httpsPort;

    public HttpsRedirectionMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _next = next;
        _enabled = !environment.IsDevelopment()
            && configuration.GetValue("Security:HttpsRedirect", true);
        _httpsPort = configuration.GetValue<int?>("Security:HttpsPort");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled && ShouldRedirect(context.Request, context.Connection))
        {
            context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            context.Response.Headers.Location = BuildHttpsLocation(context.Request);
            return;
        }

        await _next(context);
    }

    private static bool ShouldRedirect(HttpRequest request, ConnectionInfo connection)
    {
        if (!string.Equals(request.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.Path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Tras UseForwardedHeaders, Scheme ya refleja X-Forwarded-Proto cuando
        // viene de un proxy conocido: llegar aqui con scheme=http y cabecera
        // presente significa "cliente externo vino por HTTP". Sin cabecera, solo
        // redirigimos si la conexion NO es loopback (exposicion directa por HTTP).
        if (request.Headers.ContainsKey("X-Forwarded-Proto"))
        {
            return true;
        }

        return !IsLoopbackRemote(connection.RemoteIpAddress);
    }

    private static bool IsLoopbackRemote(IPAddress? remote)
    {
        if (remote is null)
        {
            return true;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return IPAddress.IsLoopback(remote);
    }

    private string BuildHttpsLocation(HttpRequest request)
    {
        // Host.Host llega sin corchetes tambien para literales IPv6; se
        // reenvuelven para producir una Location valida.
        var host = request.Host.Host;
        if (host.Contains(':', StringComparison.Ordinal))
        {
            host = $"[{host}]";
        }

        var location = new StringBuilder("https://").Append(host);
        if (_httpsPort is int port && port != 443)
        {
            location.Append(':').Append(port);
        }

        location.Append(request.PathBase);
        location.Append(request.Path);
        location.Append(request.QueryString);
        return location.ToString();
    }
}
