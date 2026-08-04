using System.Diagnostics;
using AtlasBalance.API.Services;

namespace AtlasBalance.API.Middleware;

/// <summary>
/// V-02.07: mide latencia y codigo de respuesta de cada peticion de la API para
/// que HealthAlertJob pueda detectar picos de error y degradacion de tiempos.
///
/// Va lo mas afuera posible del pipeline para medir el tiempo real que ve el
/// cliente, incluidos rate limiting y autenticacion.
/// </summary>
public sealed class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestMetrics _metrics;

    public RequestMetricsMiddleware(RequestDelegate next, IRequestMetrics metrics)
    {
        _next = next;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Solo la API. Servir estaticos falsearia la latencia a la baja y el
        // volumen al alza, y no es lo que se quiere vigilar.
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var marca = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            // En finally para que una excepcion no observada tambien cuente: si
            // el manejador global la convierte en 500, ese 500 es justo lo que
            // hay que ver en la tasa de error.
            _metrics.Registrar(
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(marca).TotalMilliseconds);
        }
    }
}
