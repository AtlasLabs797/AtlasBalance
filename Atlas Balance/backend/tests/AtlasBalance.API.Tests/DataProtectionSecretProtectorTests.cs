using System.Security.Cryptography;
using System.Text;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class DataProtectionSecretProtectorTests
{
    private const string Purpose = "AtlasBalance.ConfigurationSecrets.v1";
    private const string HmacSalt = "atlas-balance-v2-hmac-v1";

    [Fact]
    public void ProtectForStorage_Then_UnprotectFromStorage_Should_RoundTrip()
    {
        var provider = new EphemeralDataProtectionProvider();
        var sut = new DataProtectionSecretProtector(provider, NullLogger<DataProtectionSecretProtector>.Instance);

        var stored = sut.ProtectForStorage("secreto");
        sut.UnprotectFromStorage(stored).Should().Be("secreto");
    }

    [Fact]
    public void UnprotectFromStorage_Should_Throw_When_Mac_Is_Tampered()
    {
        var provider = new EphemeralDataProtectionProvider();
        var sut = new DataProtectionSecretProtector(provider, NullLogger<DataProtectionSecretProtector>.Instance);

        var stored = sut.ProtectForStorage("secreto");
        var tampered = stored[..^1] + (stored[^1] == 'A' ? 'B' : 'A');

        var act = () => sut.UnprotectFromStorage(tampered);
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Regresion V-03.01: la migracion a .NET 10 cambio la version del ensamblado
    /// Microsoft.AspNetCore.DataProtection (8.0.0.0 -> 10.0.0.0). DeriveHmacKey usaba
    /// el AssemblyQualifiedName del tipo del provider, que incluye esa version, asi
    /// que un valor "enc:v2:" escrito por una instalacion en .NET 8 fallaba la
    /// validacion de HMAC al leerse desde .NET 10. Este test simula ese valor
    /// preexistente (HMAC calculado con Version=8.0.0.0, como lo hacia el build de
    /// .NET 8) y comprueba que el protector actual todavia lo acepta.
    /// </summary>
    [Fact]
    public void UnprotectFromStorage_Should_Accept_Value_Written_By_Net8_Build()
    {
        // Directorio unico dentro del workspace (bin del test), no en el temp del SO.
        var testRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "dp-test-keys"));
        var keysDir = new DirectoryInfo(Path.Combine(testRoot, Guid.NewGuid().ToString("N")));
        keysDir.Create();
        try
        {
            // Mismo tipo de provider que en produccion (key ring en disco).
            var provider = DataProtectionProvider.Create(keysDir);
            var cipher = provider.CreateProtector(Purpose).Protect("secreto");

            // AssemblyQualifiedName literal que producia el build de .NET 8.
            const string net8Aqn = "Microsoft.AspNetCore.DataProtection.KeyManagement.KeyRingBasedDataProtectionProvider, Microsoft.AspNetCore.DataProtection, Version=8.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60";

            using var sha = SHA256.Create();
            var hmacKey = sha.ComputeHash(Encoding.UTF8.GetBytes($"{net8Aqn}|{HmacSalt}"));
            using var hmac = new HMACSHA256(hmacKey);
            var mac = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(cipher)));

            var stored = $"enc:v2:{cipher}:{mac}";

            var sut = new DataProtectionSecretProtector(provider, NullLogger<DataProtectionSecretProtector>.Instance);
            sut.UnprotectFromStorage(stored).Should().Be("secreto");
        }
        finally
        {
            var resolved = Path.GetFullPath(keysDir.FullName);
            if (resolved.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                keysDir.Delete(recursive: true);
            }
        }
    }
}
