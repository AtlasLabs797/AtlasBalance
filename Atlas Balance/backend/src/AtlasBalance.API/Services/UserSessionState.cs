using System.Security.Cryptography;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.Services;

public static class UserSessionState
{
    public static string CreateSecurityStamp()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    public static void EnsureSecurityStamp(Usuario usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario.SecurityStamp))
        {
            usuario.SecurityStamp = CreateSecurityStamp();
        }
    }

    public static void RotateAfterPasswordChange(Usuario usuario, DateTime changedAt)
    {
        usuario.SecurityStamp = CreateSecurityStamp();
        usuario.PasswordChangedAt = changedAt;
    }

    public static void RotateSecurityStamp(Usuario usuario)
    {
        usuario.SecurityStamp = CreateSecurityStamp();
    }
}
