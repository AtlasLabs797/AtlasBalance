using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public async Task DecryptAsync_Should_Reject_And_Delete_Partial_Output_When_Plaintext_Exceeds_Limit()
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
            await File.WriteAllBytesAsync(sourcePath, new byte[2048], CancellationToken.None);
            var encrypted = await service.EncryptAsync(sourcePath, CancellationToken.None);

            var act = () => service.DecryptAsync(encrypted.Path, restoredPath, 1024, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            File.Exists(restoredPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    // S-19: un .enc truncado en un limite exacto de bloque se descifraba sin error
    // y dejaba un volcado incompleto. El descifrado debe rechazar el fichero.
    [Fact]
    public async Task DecryptAsync_Should_Reject_Truncated_Backup_At_Block_Boundary()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "sample.dump");
        var restoredPath = Path.Combine(workDir, "restored.dump");
        var truncatedPath = Path.Combine(workDir, "truncated.enc");

        try
        {
            // Dos bloques completos de 1 MiB: el recorte deja exactamente el primero.
            await File.WriteAllBytesAsync(sourcePath, new byte[2 * 1024 * 1024], CancellationToken.None);

            var encrypted = await service.EncryptAsync(sourcePath, CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(encrypted.Path, CancellationToken.None);
            var headerSize = GetHeaderSize(bytes);
            // El limite de bloque se lee del propio fichero: longitud (4) + tag (16) + cifrado.
            var firstBlockLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(headerSize, 4));
            var truncatedLength = headerSize + 4 + 16 + firstBlockLength;

            await File.WriteAllBytesAsync(truncatedPath, bytes.AsSpan(0, truncatedLength).ToArray(), CancellationToken.None);

            var act = () => service.DecryptAsync(truncatedPath, restoredPath, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            File.Exists(restoredPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    // S-19: los backups ya existentes en formato V2 deben seguir descifrándose.
    [Fact]
    public async Task DecryptAsync_Should_Still_Open_Legacy_V2_Backups()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var legacyPath = Path.Combine(workDir, "legacy-v2.enc");
        var restoredPath = Path.Combine(workDir, "restored-v2.dump");

        try
        {
            var key = await ResolveTestKeyAsync(db, service, workDir);
            var baseNonce = RandomNumberGenerator.GetBytes(12);
            var blocks = new[] { RandomNumberGenerator.GetBytes(1000), RandomNumberGenerator.GetBytes(2000) };

            await WriteLegacyBackupAsync(legacyPath, Encoding.ASCII.GetBytes("ATLASBKP2"), key, baseNonce, blocks, "BuildChunkNonceV2");

            await service.DecryptAsync(legacyPath, restoredPath, CancellationToken.None);

            var restored = await File.ReadAllBytesAsync(restoredPath, CancellationToken.None);
            restored.Should().Equal(blocks[0].Concat(blocks[1]).ToArray());
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    // S-19: los backups ya existentes en formato V1 deben seguir descifrándose.
    [Fact]
    public async Task DecryptAsync_Should_Still_Open_Legacy_V1_Backups()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var legacyPath = Path.Combine(workDir, "legacy-v1.enc");
        var restoredPath = Path.Combine(workDir, "restored-v1.dump");

        try
        {
            var key = await ResolveTestKeyAsync(db, service, workDir);
            var baseNonce = RandomNumberGenerator.GetBytes(12);
            var blocks = new[] { RandomNumberGenerator.GetBytes(700), RandomNumberGenerator.GetBytes(1500) };

            await WriteLegacyBackupAsync(legacyPath, Encoding.ASCII.GetBytes("ATLASBKP1"), key, baseNonce, blocks, "BuildChunkNonceLegacyV1");

            await service.DecryptAsync(legacyPath, restoredPath, CancellationToken.None);

            var restored = await File.ReadAllBytesAsync(restoredPath, CancellationToken.None);
            restored.Should().Equal(blocks[0].Concat(blocks[1]).ToArray());
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    // S-19: el formato nuevo (V3) ida y vuelta, byte a byte, con varios bloques.
    [Fact]
    public async Task EncryptAndDecrypt_Should_Restore_OriginalContent_ByteByByte()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "sample.dump");
        var restoredPath = Path.Combine(workDir, "restored.dump");

        try
        {
            var payload = RandomNumberGenerator.GetBytes((2 * 1024 * 1024) + 4321);
            await File.WriteAllBytesAsync(sourcePath, payload, CancellationToken.None);

            var encrypted = await service.EncryptAsync(sourcePath, CancellationToken.None);
            await service.DecryptAsync(encrypted.Path, restoredPath, CancellationToken.None);

            var encryptedBytes = await File.ReadAllBytesAsync(encrypted.Path, CancellationToken.None);
            Encoding.ASCII.GetString(encryptedBytes, 0, 9).Should().Be("ATLASBKP3");

            var restored = await File.ReadAllBytesAsync(restoredPath, CancellationToken.None);
            restored.Should().Equal(payload);
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    // S-19: manipular el numero de bloques de la cabecera invalida el descifrado.
    [Fact]
    public async Task DecryptAsync_Should_Reject_Tampered_BlockCount()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "sample.dump");
        var restoredPath = Path.Combine(workDir, "restored.dump");
        var tamperedPath = Path.Combine(workDir, "tampered.enc");

        try
        {
            await File.WriteAllBytesAsync(sourcePath, new byte[2 * 1024 * 1024], CancellationToken.None);
            var encrypted = await service.EncryptAsync(sourcePath, CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(encrypted.Path, CancellationToken.None);
            var headerSize = GetHeaderSize(bytes);
            GetHeaderSize(bytes).Should().Be(25, "el formato V3 anade el numero de bloques a la cabecera");

            // El numero de bloques es el ultimo campo de la cabecera.
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(headerSize - 4), 1);

            await File.WriteAllBytesAsync(tamperedPath, bytes, CancellationToken.None);

            var act = () => service.DecryptAsync(tamperedPath, restoredPath, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            File.Exists(restoredPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    // S-19: un volcado de 0 bytes no es un volcado: EncryptAsync debe fallar.
    [Fact]
    public async Task EncryptAsync_Should_Reject_Empty_Source()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        var workDir = Path.Combine(Path.GetTempPath(), "atlas-backup-encryption-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "empty.dump");

        try
        {
            await File.WriteAllBytesAsync(sourcePath, Array.Empty<byte>(), CancellationToken.None);

            var act = () => service.EncryptAsync(sourcePath, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            File.Exists($"{sourcePath}.enc").Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    private static BackupEncryptionService CreateService(AppDbContext db) =>
        new(db, new PlainTextSecretProtector(), NullLogger<BackupEncryptionService>.Instance);

    // Obliga a que el servicio cree la clave de cifrado y la devuelve en bytes.
    private static async Task<byte[]> ResolveTestKeyAsync(AppDbContext db, BackupEncryptionService service, string workDir)
    {
        var seedPath = Path.Combine(workDir, "seed.dump");
        await File.WriteAllBytesAsync(seedPath, new byte[16], CancellationToken.None);
        await service.EncryptAsync(seedPath, CancellationToken.None);

        var row = await db.Configuraciones.FirstAsync(x => x.Clave == "backup_cloud_encryption_key");
        var unprotected = new PlainTextSecretProtector().UnprotectFromStorage(row.Valor);
        return Convert.FromBase64String(unprotected!);
    }

    // Construye a mano un backup en formato legacy (V1 o V2) para comprobar
    // que el descifrado actual los sigue entendiendo.
    private static async Task WriteLegacyBackupAsync(
        string path,
        byte[] magic,
        byte[] key,
        byte[] baseNonce,
        byte[][] blocks,
        string nonceBuilderName)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(magic);
        await stream.WriteAsync(baseNonce);

        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            var nonce = InvokeNonceBuilder(nonceBuilderName, baseNonce, (ulong)index);
            var tag = new byte[16];
            var cipher = new byte[block.Length];
            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, block, cipher, tag, magic);
            }

            var lengthBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(lengthBytes, block.Length);
            await stream.WriteAsync(lengthBytes);
            await stream.WriteAsync(tag);
            await stream.WriteAsync(cipher);
        }
    }

    // Reutiliza el derivador de nonce real del servicio (es privado) para no
    // reimplementar el formato legacy en el test.
    private static byte[] InvokeNonceBuilder(string methodName, byte[] baseNonce, ulong counter)
    {
        var method = typeof(BackupEncryptionService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"El servicio debe conservar el derivador de nonce {methodName}.");

        return (byte[])method!.Invoke(null, new object[] { baseNonce, counter })!;
    }

    // Tamano de la cabecera segun el formato: magic (9) + baseNonce (12) y, en V3,
    // el numero de bloques (4).
    private static int GetHeaderSize(byte[] fileBytes) =>
        fileBytes.AsSpan(0, 9).SequenceEqual(Encoding.ASCII.GetBytes("ATLASBKP3")) ? 25 : 21;
}
