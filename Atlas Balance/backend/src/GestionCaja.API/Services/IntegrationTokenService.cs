using System.Security.Cryptography;
using System.Text;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

public interface IIntegrationTokenService
{
    string GeneratePlainToken();
    string ComputeSha256(string value);
    Task<IntegrationToken?> ValidateActiveTokenAsync(string? plainToken, CancellationToken cancellationToken);
    Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken);
}

public sealed class IntegrationTokenService : IIntegrationTokenService
{
    private readonly AppDbContext _dbContext;

    public IntegrationTokenService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string GeneratePlainToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var base64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"sk_gestion_caja_{base64}";
    }

    public string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<IntegrationToken?> ValidateActiveTokenAsync(string? plainToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plainToken))
        {
            return null;
        }

        var tokenHash = ComputeSha256(plainToken.Trim());

        return await _dbContext.IntegrationTokens
            .FirstOrDefaultAsync(x =>
                x.TokenHash == tokenHash &&
                x.Estado == EstadoTokenIntegracion.Activo &&
                x.DeletedAt == null,
                cancellationToken);
    }

    public async Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await _dbContext.IntegrationTokens.FirstOrDefaultAsync(x => x.Id == tokenId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.Estado = EstadoTokenIntegracion.Revocado;
        token.FechaRevocacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
