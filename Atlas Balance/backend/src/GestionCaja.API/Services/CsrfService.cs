using System.Security.Cryptography;
using System.Text;

namespace GestionCaja.API.Services;

public interface ICsrfService
{
    string GenerateToken();
    bool IsValid(string? cookieToken, string? headerToken);
}

public sealed class CsrfService : ICsrfService
{
    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public bool IsValid(string? cookieToken, string? headerToken)
    {
        if (string.IsNullOrWhiteSpace(cookieToken) || string.IsNullOrWhiteSpace(headerToken))
        {
            return false;
        }

        var cookieBytes = Encoding.UTF8.GetBytes(cookieToken);
        var headerBytes = Encoding.UTF8.GetBytes(headerToken);
        if (cookieBytes.Length != headerBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(cookieBytes, headerBytes);
    }
}
