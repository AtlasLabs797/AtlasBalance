using GestionCaja.API.Services;

namespace GestionCaja.API.Jobs;

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
