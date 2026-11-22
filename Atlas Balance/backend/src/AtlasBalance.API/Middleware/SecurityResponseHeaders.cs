namespace AtlasBalance.API.Middleware;

/// <summary>
/// S-18: las respuestas de la API llevan datos de negocio (saldos, extractos)
/// y no pueden quedarse en la cache de un proxy o del navegador. Sin
/// Cache-Control, una respuesta de la API es cacheable por defecto.
///
/// Los estaticos de wwwroot no se tocan: siguen con la politica de cache
/// actual (el html nunca se cachea, los assets con hash si).
///
/// Vive en una clase aparte porque el bloque que la aplica esta dentro de
/// Program.cs (middleware en linea) y asi la regla se puede testear sin
/// levantar la aplicacion.
/// </summary>
public static class SecurityResponseHeaders
{
    public static void ApplyCacheControl(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Si el controlador ya fijo su propia Cache-Control, no se pisa.
        if (context.Response.Headers.ContainsKey("Cache-Control"))
        {
            return;
        }

        context.Response.Headers["Cache-Control"] = "no-store";
    }
}
