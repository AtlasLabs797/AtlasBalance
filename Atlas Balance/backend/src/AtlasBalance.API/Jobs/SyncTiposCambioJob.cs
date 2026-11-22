using AtlasBalance.API.Services;
using Hangfire;

namespace AtlasBalance.API.Jobs;

// V-03.02 (J-1): cron cada 12 h. Una sola sincronizacion a la vez y reintento
// ante un fallo transitorio de BD (los fallos de la API los absorbe el servicio y
// dejan aviso al admin, sin lanzar excepcion).
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class SyncTiposCambioJob
{
    private readonly ITiposCambioService _tiposCambioService;
    private readonly ILogger<SyncTiposCambioJob> _logger;

    public SyncTiposCambioJob(ITiposCambioService tiposCambioService, ILogger<SyncTiposCambioJob> logger)
    {
        _tiposCambioService = tiposCambioService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var result = await _tiposCambioService.SincronizarTiposCambioAsync(CancellationToken.None);
        if (!result.Success)
        {
            _logger.LogWarning("SyncTiposCambioJob finalizó con error: {Error}", result.ErrorMessage);
            return;
        }

        _logger.LogInformation("SyncTiposCambioJob actualizado: {UpdatedCount} tasas", result.UpdatedCount);
    }
}
