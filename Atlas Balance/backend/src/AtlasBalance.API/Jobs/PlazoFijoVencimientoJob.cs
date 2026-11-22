using AtlasBalance.API.Services;
using Hangfire;

namespace AtlasBalance.API.Jobs;

// V-03.02 (J-1): cron diario. El reintento no duplica emails: el servicio solo
// avisa si FechaUltimaNotificacion != hoy y la guarda (SaveChanges) justo despues
// de enviar el digest; las notificaciones de admin se deduplican por plazo.
[DisableConcurrentExecution(timeoutInSeconds: 900)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class PlazoFijoVencimientoJob
{
    private readonly IPlazoFijoService _plazoFijoService;
    private readonly ILogger<PlazoFijoVencimientoJob> _logger;

    public PlazoFijoVencimientoJob(IPlazoFijoService plazoFijoService, ILogger<PlazoFijoVencimientoJob> logger)
    {
        _plazoFijoService = plazoFijoService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var cambios = await _plazoFijoService.ProcesarVencimientosAsync(hoy, CancellationToken.None);
        _logger.LogInformation("Job de plazos fijos completado. cambios={Cambios}", cambios);
    }
}
