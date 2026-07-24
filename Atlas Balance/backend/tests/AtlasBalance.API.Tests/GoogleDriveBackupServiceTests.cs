using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class GoogleDriveBackupServiceTests
{
    // -----------------------------------------------------------------------
    // V-02.06 (HIGH-2): el helper ComputeSha256Async es la pieza que
    // compara el SHA-256 del dump descifrado contra el registrado en
    // BackupCloudCopy. Su logica es trivial pero tiene que ser estable
    // porque un cambio de encoding (mayusculas vs minusculas, hex con
    // o sin padding) haria pasar la validacion contra hashes viejos.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComputeSha256Async_Should_Return_Lowercase_Hex_Hash()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"atlas-sha256-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempFile, "hello world"u8.ToArray());

        try
        {
            // SHA-256("hello world") en hex minusculas canonico.
            const string expected = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";
            var actual = await GoogleDriveBackupService.ComputeSha256Async(tempFile, CancellationToken.None);

            actual.Should().Be(expected);
            actual.Should().Match(s => s == s.ToLowerInvariant());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeSha256Async_Should_Differ_When_File_Content_Changes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"atlas-sha256-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempFile, "payload-original"u8.ToArray());

        try
        {
            var originalHash = await GoogleDriveBackupService.ComputeSha256Async(tempFile, CancellationToken.None);

            await File.WriteAllBytesAsync(tempFile, "payload-corrupted"u8.ToArray());
            var corruptedHash = await GoogleDriveBackupService.ComputeSha256Async(tempFile, CancellationToken.None);

            originalHash.Should().NotBe(corruptedHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
