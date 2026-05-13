using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

public sealed class LimpiezaRefreshTokensJob
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<LimpiezaRefreshTokensJob> _logger;

    public LimpiezaRefreshTokensJob(AppDbContext dbContext, ILogger<LimpiezaRefreshTokensJob> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var now = DateTime.UtcNow;
        var rows = await _dbContext.RefreshTokens
            .Where(x => x.ExpiraEn <= now || (x.RevocadoEn.HasValue && x.RevocadoEn.Value <= now.AddDays(-1)))
            .ExecuteDeleteAsync();

        _logger.LogInformation("LimpiezaRefreshTokensJob eliminó {Count} refresh tokens", rows);
    }
}
