using System.Security.Claims;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Services;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/sistema")]
public sealed class SistemaController : ControllerBase
{
    private readonly IWatchdogClientService _watchdogClientService;
    private readonly IActualizacionService _actualizacionService;
    private readonly IAuditService _auditService;

    public SistemaController(
        IWatchdogClientService watchdogClientService,
        IActualizacionService actualizacionService,
        IAuditService auditService)
    {
        _watchdogClientService = watchdogClientService;
        _actualizacionService = actualizacionService;
        _auditService = auditService;
    }

    [HttpGet("version-actual")]
    public async Task<IActionResult> VersionActual(CancellationToken cancellationToken)
    {
        var data = await _actualizacionService.GetVersionActualAsync(cancellationToken);
        return Ok(data);
    }

    [HttpGet("version-disponible")]
    public async Task<IActionResult> VersionDisponible(CancellationToken cancellationToken)
    {
        var data = await _actualizacionService.CheckVersionDisponibleAsync(cancellationToken);
        return Ok(data);
    }

    [HttpPost("actualizar")]
    [EnableRateLimiting(RateLimitingSetup.PolicyNames.Expensive)]
    public async Task<IActionResult> Actualizar([FromBody] ActualizacionRequest? request, CancellationToken cancellationToken)
    {
        var available = await _actualizacionService.CheckVersionDisponibleAsync(cancellationToken);
        if (!available.ActualizacionDisponible)
        {
            return BadRequest(new { error = available.Mensaje ?? "No hay actualización disponible." });
        }

        if (!available.Instalable)
        {
            return BadRequest(new
            {
                error = available.Mensaje ?? "La actualizacion no es instalable.",
                bloqueos = available.Bloqueos
            });
        }

        var accepted = await _actualizacionService.IniciarActualizacionAsync(
            request?.SourcePath,
            request?.TargetPath,
            cancellationToken);

        // V-02.07: es la accion de admin con mas alcance de toda la app (sustituye
        // los binarios en produccion) y hasta ahora no dejaba rastro en AUDITORIAS.
        // Se audita el intento, aceptado o rechazado.
        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.SistemaActualizacionIniciada,
            "SISTEMA",
            null,
            HttpContext,
            JsonSerializer.Serialize(new
            {
                aceptada_por_watchdog = accepted,
                version_actual = available.VersionActual,
                version_disponible = available.VersionDisponible,
                // Las rutas las elige un admin autenticado y ActualizacionService
                // ya las valida; se registran porque son parte del "que hizo".
                source_path = request?.SourcePath,
                target_path = request?.TargetPath
            }),
            cancellationToken);

        if (!accepted)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Watchdog rechazó la actualización." });
        }

        return Accepted(new { message = "Actualización iniciada" });
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    [HttpGet("estado")]
    public async Task<IActionResult> Estado(CancellationToken cancellationToken)
    {
        var estado = await _watchdogClientService.GetEstadoAsync(cancellationToken);
        return Ok(estado);
    }

    /// <summary>
    /// V-02.07: salud real (base de datos, disco, pool) mas tasa de error y
    /// latencia. /api/health se queda como sonda de vida anonima; esto es la
    /// comprobacion profunda y va detras de ADMIN porque expone el estado
    /// interno del servidor.
    /// </summary>
    [HttpGet("salud")]
    public async Task<IActionResult> Salud([FromServices] IAppHealthService health, CancellationToken cancellationToken)
    {
        var salud = await health.ComprobarAsync(cancellationToken);

        // 503 cuando no esta sano para que un monitor externo lo detecte por
        // codigo de estado sin tener que interpretar el cuerpo.
        return salud.Estado == EstadoSalud.NoSano
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, salud)
            : Ok(salud);
    }

    [HttpGet("metricas")]
    public IActionResult Metricas([FromServices] IRequestMetrics metrics)
    {
        return Ok(new MetricasResponse
        {
            FechaUtc = DateTime.UtcNow,
            UptimeSegundos = (long)(DateTime.UtcNow - metrics.ArranqueUtc).TotalSeconds,
            Ultimos5Min = Mapear(metrics.Ventana(5)),
            Ultimos60Min = Mapear(metrics.Ventana(60)),
            Anterior5Min = Mapear(metrics.VentanaAnterior(5))
        });
    }

    private static VentanaMetricasResponse Mapear(VentanaMetricas v) => new()
    {
        DesdeUtc = v.DesdeUtc,
        HastaUtc = v.HastaUtc,
        Peticiones = v.Peticiones,
        Errores4xx = v.Errores4xx,
        Errores5xx = v.Errores5xx,
        TasaErrorPorcentaje = Math.Round(v.TasaErrorPorcentaje, 2),
        LatenciaP50Ms = v.LatenciaP50Ms,
        LatenciaP95Ms = v.LatenciaP95Ms,
        LatenciaMaxMs = Math.Round(v.LatenciaMaxMs, 1)
    };
}
