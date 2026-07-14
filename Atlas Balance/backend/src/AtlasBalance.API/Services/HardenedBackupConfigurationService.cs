using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public sealed class HardenedBackupConfigurationService : IBackupConfigurationService
{
    private static readonly string[] SecretKeys =
    [
        "google_drive_oauth_client_secret",
        "backup_cloud_encryption_key"
    ];

    private readonly BackupConfigurationService _inner;
    private readonly AppDbContext _dbContext;

    public HardenedBackupConfigurationService(BackupConfigurationService inner, AppDbContext dbContext)
    {
        _inner = inner;
        _dbContext = dbContext;
    }

    public Task<BackupConfigResponse> GetAsync(CancellationToken cancellationToken) =>
        _inner.GetAsync(cancellationToken);

    public async Task<(bool Success, string? Error)> UpdateAsync(UpdateBackupConfigRequest request, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var result = await _inner.UpdateAsync(request, userId, httpContext, cancellationToken);
        if (!result.Success)
        {
            return result;
        }

        var rows = await _dbContext.Configuraciones
            .Where(x => SecretKeys.Contains(x.Clave))
            .ToListAsync(cancellationToken);
        var changed = false;
        foreach (var row in rows)
        {
            if (!row.EsSecreto)
            {
                row.EsSecreto = true;
                changed = true;
            }
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
