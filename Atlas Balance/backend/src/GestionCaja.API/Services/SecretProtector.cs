using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace GestionCaja.API.Services;

public interface ISecretProtector
{
    string ProtectForStorage(string? value);
    string? UnprotectFromStorage(string? storedValue);
    bool IsProtected(string? storedValue);
}

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionSecretProtector> _logger;

    public DataProtectionSecretProtector(IDataProtectionProvider provider, ILogger<DataProtectionSecretProtector> logger)
    {
        _protector = provider.CreateProtector("AtlasBalance.ConfigurationSecrets.v1");
        _logger = logger;
    }

    public string ProtectForStorage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return IsProtected(trimmed)
            ? trimmed
            : $"{Prefix}{_protector.Protect(trimmed)}";
    }

    public string? UnprotectFromStorage(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return storedValue;
        }

        var trimmed = storedValue.Trim();
        if (!IsProtected(trimmed))
        {
            return trimmed;
        }

        try
        {
            return _protector.Unprotect(trimmed[Prefix.Length..]);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "No se pudo descifrar un secreto de configuracion.");
            throw new InvalidOperationException("No se pudo descifrar un secreto de configuracion. Revise las claves de Data Protection.", ex);
        }
    }

    public bool IsProtected(string? storedValue) =>
        !string.IsNullOrWhiteSpace(storedValue) &&
        storedValue.Trim().StartsWith(Prefix, StringComparison.Ordinal);
}
