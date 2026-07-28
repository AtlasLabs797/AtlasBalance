using System.Security.Cryptography;
using System.Text;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    internal const string Namespace = "integration_token";

    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICacheService _cacheService;
    private readonly CachingOptions _cachingOptions;

    public IntegrationTokenService(
        AppDbContext dbContext,
        ICacheService cacheService,
        IOptions<CachingOptions> cachingOptions,
        IClock? clock = null)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _cachingOptions = cachingOptions.Value;
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

        return await _cacheService.GetOrLoadAsync(
            new CacheNamespace(Namespace),
            tokenHash,
            ct => LoadActiveTokenAsync(tokenHash, ct),
            _cachingOptions.IntegrationTokenTtl,
            cancellationToken);
    }

    private async Task<IntegrationToken?> LoadActiveTokenAsync(string tokenHash, CancellationToken cancellationToken)
    {
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

        // Tras revocar, los tokens cacheados por hash deben invalidarse para
        // que la siguiente validacion no devuelva un token que ya no es valido.
        // Usamos la generacion por namespace porque no siempre tenemos acceso
        // al hash desde el ID.
        _cacheService.Invalidate(new CacheNamespace(Namespace));

        return true;
    }
}
