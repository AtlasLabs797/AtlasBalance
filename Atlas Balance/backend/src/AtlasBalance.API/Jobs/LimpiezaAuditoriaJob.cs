using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// Excepcion intencional a la regla de soft delete universal de CLAUDE.md: las tablas
// de auditoria existen para trazar acciones pasadas, no para ser restauradas, y
// conservarlas indefinidamente via soft delete las haria crecer sin limite. Se purgan
// con hard delete tras RetentionDays dias como politica de retencion deliberada.
public sealed class LimpiezaAuditoriaJob
{
    public const int RetentionDays = 28;

    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<LimpiezaAuditoriaJob> _logger;

    public LimpiezaAuditoriaJob(AppDbContext dbContext, IClock clock, ILogger<LimpiezaAuditoriaJob> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var cutoffUtc = _clock.UtcNow.AddDays(-RetentionDays);

        var auditoriasDeleted = await _dbContext.Auditorias
            .Where(x => x.Timestamp < cutoffUtc)
            .ExecuteDeleteAsync();

        var integrationAuditsDeleted = await _dbContext.AuditoriaIntegraciones
            .Where(x => x.Timestamp < cutoffUtc)
            .ExecuteDeleteAsync();

        _logger.LogInformation(
            "LimpiezaAuditoriaJob elimino {AuditoriasDeleted} auditorias y {IntegrationAuditsDeleted} auditorias de integracion anteriores a {CutoffUtc}",
            auditoriasDeleted,
            integrationAuditsDeleted,
            cutoffUtc);
    }
}
