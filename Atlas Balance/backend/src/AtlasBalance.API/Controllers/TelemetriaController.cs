using AtlasBalance.API.DTOs;
using AtlasBalance.API.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AtlasBalance.API.Controllers;

// V-02.07: receptor de errores no controlados del frontend. Antes el error boundary
// volcaba el stack a la consola del navegador y hacia sendBeacon contra esta ruta,
// que no existia: el detalle acababa en el cliente y no quedaba nada en el servidor.
// Aqui se invierte: el navegador no imprime nada y el detalle vive en el log.
[ApiController]
[AllowAnonymous]
[Route("api/telemetria")]
public sealed class TelemetriaController : ControllerBase
{
    // Un fallo de render en bucle puede disparar cientos de reportes por segundo.
    // El frontend ya limita por carga de pagina, pero eso es cliente y no se confia.
    private const int MaxReportesPorVentana = 20;
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    // Topes de longitud: el cuerpo lo controla el cliente, asi que se recorta antes
    // de escribir nada en el log.
    private const int MaxMensaje = 500;
    private const int MaxStack = 4000;
    private const int MaxPath = 200;

    private readonly ILogger<TelemetriaController> _logger;
    private readonly IMemoryCache _cache;

    public TelemetriaController(ILogger<TelemetriaController> logger, IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    // S-25: el cuerpo se acota en 32 KB (no hay tope propio para este endpoint anonimo,
    // solo el de 10 MB global de Kestrel). 32 KB y no 16 KB: el UTF-8 usa hasta 3 bytes
    // por caracter y el tope de campos ronda los 8,7K caracteres.
    [HttpPost("errores")]
    [RequestSizeLimit(32 * 1024)]
    public IActionResult RegistrarError([FromBody] ErrorClienteRequest? request)
    {
        // 204 siempre que el cuerpo se acepta: los campos largos se recortan (S-25) y
        // las peticiones sobre cuota o sin cuerpo tambien responden 204. Los rechazos
        // del framework (413 por tamano, 400 por JSON mal formado) ocurren antes de
        // llegar aqui. sendBeacon ignora la respuesta y devolver detalle daria a un
        // cliente hostil una via para sondear el estado.
        if (request is null || !TryConsumirCuota())
        {
            return NoContent();
        }

        // S-25: Warning, no Error. Un fallo de render del frontend no es un
        // error del servidor: a nivel Error satura el log de errores con ruido
        // que no indica ningun problema de la API.
        _logger.LogWarning(
            "Error no controlado en el frontend: mensaje={MensajeSafe} path={PathSafe} timestamp={TimestampSafe} ip={IpSafe} stack={StackSafe} componentStack={ComponentStackSafe}",
            Recortar(request.Mensaje, MaxMensaje),
            Recortar(request.Path, MaxPath),
            Recortar(request.Timestamp, 40),
            LogScrubber.Scrub(HttpContext.Connection.RemoteIpAddress?.ToString()),
            Recortar(request.Stack, MaxStack),
            Recortar(request.ComponentStack, MaxStack));

        return NoContent();
    }

    // Ventana fija: el contador se crea con su expiracion y NO se vuelve a tocar la
    // entrada al incrementar. Si se hiciera Set en cada peticion, la expiracion se
    // renovaria sola y una IP que reporte sin parar quedaria bloqueada para siempre.
    private sealed class Contador
    {
        public int Usados;
    }

    private bool TryConsumirCuota()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var contador = _cache.GetOrCreate($"telemetria_errores:{ip}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ventana;
            return new Contador();
        })!;

        return Interlocked.Increment(ref contador.Usados) <= MaxReportesPorVentana;
    }

    // Recorta y sanea. LogScrubber quita CR/LF (log forging, CWE-117) y corta a 256,
    // asi que para stacks se aplica primero el recorte propio y luego el saneo por
    // lineas para no perder toda la traza.
    private static string Recortar(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var plano = value.Replace("\r", " ").Replace("\n", " | ").Replace("\t", " ");
        return plano.Length <= maxLength ? plano : plano[..maxLength];
    }
}
