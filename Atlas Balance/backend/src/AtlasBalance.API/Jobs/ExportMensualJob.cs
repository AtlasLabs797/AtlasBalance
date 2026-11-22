using AtlasBalance.API.Services;
using Hangfire;

namespace AtlasBalance.API.Jobs;

// V-03.02 (J-1): con 2 workers de Hangfire, una exportacion mensual larga
// puede solaparse con si misma o con otro job y retrasar las alertas de
// seguridad. El bloqueo concurrente y el reintento automatico son la red.
[DisableConcurrentExecution(timeoutInSeconds: 1800)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class ExportMensualJob
{
    private readonly IExportacionService _exportacionService;
    private readonly ILogger<ExportMensualJob> _logger;

    public ExportMensualJob(IExportacionService exportacionService, ILogger<ExportMensualJob> logger)
    {
        _exportacionService = exportacionService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        try
        {
            var total = await _exportacionService.ExportarMensualAsync(CancellationToken.None);
            _logger.LogInformation("ExportMensualJob completado. Exportaciones exitosas: {Total}", total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportMensualJob falló");
            throw;
        }
    }
}
