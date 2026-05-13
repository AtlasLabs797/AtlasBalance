using AtlasBalance.API.Services;

namespace AtlasBalance.API.Jobs;

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
        var result = await _tiposCambioService.SincronizarTiposCambioAsync(CancellationToken.None);
        if (!result.Success)
        {
            _logger.LogWarning("SyncTiposCambioJob finalizó con error: {Error}", result.ErrorMessage);
            return;
        }

        _logger.LogInformation("SyncTiposCambioJob actualizado: {UpdatedCount} tasas", result.UpdatedCount);
    }
}
