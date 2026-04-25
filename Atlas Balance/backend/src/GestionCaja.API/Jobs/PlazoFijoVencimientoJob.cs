using GestionCaja.API.Services;

namespace GestionCaja.API.Jobs;

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
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var cambios = await _plazoFijoService.ProcesarVencimientosAsync(hoy, CancellationToken.None);
        _logger.LogInformation("Job de plazos fijos completado. cambios={Cambios}", cambios);
    }
}
