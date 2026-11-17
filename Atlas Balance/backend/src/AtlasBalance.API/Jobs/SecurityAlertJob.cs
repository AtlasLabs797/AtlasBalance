using AtlasBalance.API.Logging;
using AtlasBalance.API.Services;
using Hangfire;

namespace AtlasBalance.API.Jobs;

/// <summary>
/// V-02.07: evalua las reglas de deteccion sobre AUDITORIAS. Se programa con la
/// misma cadencia que SecurityAlertOptions.VentanaMinutos para que las ventanas
/// se encadenen sin dejar huecos ni solaparse (ver Program.cs).
///
/// V-03.02 (J-1): con 2 workers, un backup largo y una exportacion pueden
/// ocupar los dos y retrasar las alertas. El bloqueo concurrente impide que
/// dos pasadas se pisen y el reintento automatico recupera un fallo puntual.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
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
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
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
