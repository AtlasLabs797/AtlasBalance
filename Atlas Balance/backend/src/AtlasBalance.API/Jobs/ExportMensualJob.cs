using AtlasBalance.API.Services;

namespace AtlasBalance.API.Jobs;

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
