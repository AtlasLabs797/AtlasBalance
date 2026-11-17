using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// V-03.02 (retencion de datos personales): MFA_TRUSTED_DEVICES guarda resumen de
// user-agent e IP por dispositivo de confianza y nunca se purgaba. Se borran
// (DELETE fisico, como los refresh tokens) los dispositivos caducados y los
// revocados hace mas de 1 dia. Los vigentes no se tocan. Se ignora el filtro
// global (tambien limpia los soft-deleted caducados); la policy de DELETE solo
// admite admin/system, de ahi el SystemContextScope.
//
// V-03.02 (J-1): bloqueo concurrente y reintento automatico.
[DisableConcurrentExecution(timeoutInSeconds: 900)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class LimpiezaMfaTrustedDevicesJob
{
    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<LimpiezaMfaTrustedDevicesJob> _logger;

    public LimpiezaMfaTrustedDevicesJob(AppDbContext dbContext, IClock clock, ILogger<LimpiezaMfaTrustedDevicesJob> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var now = _clock.UtcNow;
        var revokedCutoff = now.AddDays(-1);

        var rows = await _dbContext.MfaTrustedDevices
            .IgnoreQueryFilters()
            .Where(x => x.ExpiresAt <= now || (x.RevokedAt.HasValue && x.RevokedAt.Value <= revokedCutoff))
            .ExecuteDeleteAsync();

        _logger.LogInformation("LimpiezaMfaTrustedDevicesJob elimino {Count} dispositivos MFA de confianza", rows);
    }
}
