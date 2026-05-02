using System.Security.Cryptography;
using System.Text;

namespace GestionCaja.API.Data;

public static class RlsContextSigner
{
    public static string BuildPayload(
        string authMode,
        string userId,
        string integrationTokenId,
        string isAdmin,
        string system,
        string requestScope) =>
        string.Join(
            "|",
            authMode,
            userId,
            integrationTokenId,
            isAdmin,
            system,
            requestScope);

    public static string Sign(
        string secret,
        string authMode,
        string userId,
        string integrationTokenId,
        string isAdmin,
        string system,
        string requestScope)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        var payload = BuildPayload(authMode, userId, integrationTokenId, isAdmin, system, requestScope);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
