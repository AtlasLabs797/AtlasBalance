using AtlasBalance.API.Services;

namespace AtlasBalance.API.Tests;

internal sealed class PlainTextSecretProtector : ISecretProtector
{
    public string ProtectForStorage(string? value) => value?.Trim() ?? string.Empty;
    public string? UnprotectFromStorage(string? storedValue) => storedValue;
    public bool IsProtected(string? storedValue) => false;
}
