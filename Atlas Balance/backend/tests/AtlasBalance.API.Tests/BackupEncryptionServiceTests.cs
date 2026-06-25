using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public class BackupEncryptionServiceTests
{
    [Fact]
    public async Task EncryptAndDecrypt_Should_Restore_OriginalContent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = new BackupEncryptionService(
            db,
            new PlainTextSecretProtector(),
            NullLogger<BackupEncryptionService>.Instance);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "sample.dump");
        var restoredPath = Path.Combine(workDir, "restored.dump");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "atlas backup payload", CancellationToken.None);

            var encrypted = await service.EncryptAsync(sourcePath, CancellationToken.None);
            await service.DecryptAsync(encrypted.Path, restoredPath, CancellationToken.None);

            File.Exists(encrypted.Path).Should().BeTrue();
            encrypted.SizeBytes.Should().BeGreaterThan(0);
            encrypted.Sha256Hex.Should().NotBeNullOrWhiteSpace();
            var restored = await File.ReadAllTextAsync(restoredPath, CancellationToken.None);
            restored.Should().Be("atlas backup payload");
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }
}
