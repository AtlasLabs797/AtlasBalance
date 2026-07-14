using System.Security.Cryptography;
using System.Text;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IIntegrationTokenService
{
    string GeneratePlainToken();
    string ComputeSha256(string value);
    Task<IntegrationToken?> ValidateActiveTokenAsync(string? plainToken, CancellationToken cancellationToken);
    DateTime? ResolveExpiration(
        DateTime? requestedExpiration,
        bool noExpirationConfirmed,
        string? noExpirationConfirmationText = null);
    Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken);
}

public sealed class IntegrationTokenService : IIntegrationTokenService
{
    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;

    public IntegrationTokenService(AppDbContext dbContext, IClock? clock = null)
    {
        _dbContext = dbContext;
        _clock = clock ?? new SystemClock();
    }

    public string GeneratePlainToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var base64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"sk_atlas_balance_{base64}";
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
                (x.FechaExpiracion == null || x.FechaExpiracion > _clock.UtcNow) &&
                x.DeletedAt == null,
                cancellationToken);
    }

    public const string NoExpirationConfirmationPhrase = "NO_EXPIRAR";

    public DateTime? ResolveExpiration(
        DateTime? requestedExpiration,
        bool noExpirationConfirmed,
        string? noExpirationConfirmationText = null)
    {
        if (requestedExpiration.HasValue)
        {
            return requestedExpiration.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(requestedExpiration.Value, DateTimeKind.Utc)
                : requestedExpiration.Value.ToUniversalTime();
        }

        if (noExpirationConfirmed)
        {
            // SECURITY (C-NEW-2, V-02-03): un token sin expiracion es un riesgo
            // enorme si se filtra. Exigimos que el caller escriba el texto magico
            // "NO_EXPIRAR" para confirmar que es una decision consciente y no un
            // checkbox olvidado en la UI.
            if (string.IsNullOrWhiteSpace(noExpirationConfirmationText) ||
                !string.Equals(noExpirationConfirmationText.Trim(), NoExpirationConfirmationPhrase, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Para crear un token sin expiracion, confirma escribiendo exactamente '{NoExpirationConfirmationPhrase}' en el campo SinExpiracionTextoConfirmacion.",
                    nameof(noExpirationConfirmationText));
            }
            return null;
        }

        return _clock.UtcNow.AddDays(90);
    }

    public async Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await _dbContext.IntegrationTokens.FirstOrDefaultAsync(x => x.Id == tokenId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.Estado = EstadoTokenIntegracion.Revocado;
        token.FechaRevocacion = _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
