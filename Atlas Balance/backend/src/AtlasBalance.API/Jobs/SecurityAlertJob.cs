using AtlasBalance.API.Logging;
using AtlasBalance.API.Services;

namespace AtlasBalance.API.Jobs;

/// <summary>
/// V-02.07: evalua las reglas de deteccion sobre AUDITORIAS. Se programa con la
/// misma cadencia que SecurityAlertOptions.VentanaMinutos para que las ventanas
/// se encadenen sin dejar huecos ni solaparse (ver Program.cs).
/// </summary>
public sealed class SecurityAlertJob
{
    private readonly ISecurityAlertService _alertService;
    private readonly ILogger<SecurityAlertJob> _logger;

    public SecurityAlertJob(ISecurityAlertService alertService, ILogger<SecurityAlertJob> logger)
    {
        _alertService = alertService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var alertas = await _alertService.EvaluarYNotificarAsync(CancellationToken.None);

        if (alertas.Count == 0)
        {
            return;
        }

        _logger.LogWarning(
            "SecurityAlertJob notifico {Count} alertas de seguridad: {Reglas}",
            alertas.Count,
            LogScrubber.Scrub(string.Join(", ", alertas.Select(a => a.Regla).Distinct())));
    }
}
