using System.Buffers.Binary;
using System.Security.Cryptography;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IBackupEncryptionService
{
    Task<EncryptedBackupFile> EncryptAsync(string sourcePath, CancellationToken cancellationToken);
    Task DecryptAsync(string encryptedPath, string destinationPath, CancellationToken cancellationToken);
}

public sealed record EncryptedBackupFile(string Path, long SizeBytes, string Sha256Hex);

public sealed class BackupEncryptionService : IBackupEncryptionService
{
    private static readonly byte[] Magic = "ATLASBKP1"u8.ToArray();
    private const int ChunkSize = 1024 * 1024;
    private const int TagSize = 16;
    private const int NonceSize = 12;

    private readonly AppDbContext _dbContext;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<BackupEncryptionService> _logger;

    public BackupEncryptionService(AppDbContext dbContext, ISecretProtector secretProtector, ILogger<BackupEncryptionService> logger)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<EncryptedBackupFile> EncryptAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("No existe el archivo de backup a cifrar.", sourcePath);
        }

        var destinationPath = $"{sourcePath}.enc";
        var key = await ResolveKeyAsync(cancellationToken);
        var baseNonce = RandomNumberGenerator.GetBytes(NonceSize);

        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true))
        await using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
        {
            await destination.WriteAsync(Magic, cancellationToken);
            await destination.WriteAsync(baseNonce, cancellationToken);

            var plain = new byte[ChunkSize];
            var cipher = new byte[ChunkSize];
            var tag = new byte[TagSize];
            var lengthBytes = new byte[4];
            ulong counter = 0;

            while (true)
            {
                var read = await source.ReadAsync(plain.AsMemory(0, plain.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var nonce = BuildChunkNonce(baseNonce, counter++);
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plain.AsSpan(0, read), cipher.AsSpan(0, read), tag, Magic);

                BinaryPrimitives.WriteInt32BigEndian(lengthBytes, read);
                await destination.WriteAsync(lengthBytes, cancellationToken);
                await destination.WriteAsync(tag, cancellationToken);
                await destination.WriteAsync(cipher.AsMemory(0, read), cancellationToken);
            }
        }

        var info = new FileInfo(destinationPath);
        var hash = await ComputeSha256HexAsync(destinationPath, cancellationToken);
        return new EncryptedBackupFile(destinationPath, info.Length, hash);
    }

    public async Task DecryptAsync(string encryptedPath, string destinationPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(encryptedPath))
        {
            throw new FileNotFoundException("No existe el archivo cifrado de backup.", encryptedPath);
        }

        var key = await ResolveKeyAsync(cancellationToken);
        await using var source = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);

        var header = new byte[Magic.Length];
        await ReadExactAsync(source, header, cancellationToken);
        if (!header.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidOperationException("El backup cifrado no tiene un formato reconocido.");
        }

        var baseNonce = new byte[NonceSize];
        await ReadExactAsync(source, baseNonce, cancellationToken);

        var lengthBytes = new byte[4];
        var tag = new byte[TagSize];
        var plain = new byte[ChunkSize];
        var cipher = new byte[ChunkSize];
        ulong counter = 0;

        while (source.Position < source.Length)
        {
            await ReadExactAsync(source, lengthBytes, cancellationToken);
            var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
            if (length <= 0 || length > ChunkSize)
            {
                throw new InvalidOperationException("El backup cifrado contiene un bloque invalido.");
            }

            await ReadExactAsync(source, tag, cancellationToken);
            await ReadExactAsync(source, cipher.AsMemory(0, length), cancellationToken);

            var nonce = BuildChunkNonce(baseNonce, counter++);
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), Magic);
            await destination.WriteAsync(plain.AsMemory(0, length), cancellationToken);
        }
    }

    private async Task<byte[]> ResolveKeyAsync(CancellationToken cancellationToken)
    {
        var row = await _dbContext.Configuraciones
            .FirstOrDefaultAsync(x => x.Clave == "backup_cloud_encryption_key", cancellationToken);
        if (row is not null && !string.IsNullOrWhiteSpace(row.Valor))
        {
            var unprotected = _secretProtector.UnprotectFromStorage(row.Valor);
            if (!string.IsNullOrWhiteSpace(unprotected))
            {
                try
                {
                    var key = Convert.FromBase64String(unprotected);
                    if (key.Length == 32)
                    {
                        return key;
                    }
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "La clave de cifrado de backups en nube no es Base64 valida.");
                }
            }
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        if (row is null)
        {
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = "backup_cloud_encryption_key",
                Valor = _secretProtector.ProtectForStorage(generated),
                Tipo = "string",
                Descripcion = "Clave de cifrado para copias subidas a nube",
                FechaModificacion = DateTime.UtcNow
            });
        }
        else
        {
            row.Valor = _secretProtector.ProtectForStorage(generated);
            row.FechaModificacion = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Convert.FromBase64String(generated);
    }

    private static byte[] BuildChunkNonce(byte[] baseNonce, ulong counter)
    {
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(baseNonce, 0, nonce, 0, NonceSize);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    private static async Task ReadExactAsync(FileStream stream, byte[] buffer, CancellationToken cancellationToken) =>
        await ReadExactAsync(stream, buffer.AsMemory(), cancellationToken);

    private static async Task ReadExactAsync(FileStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("El backup cifrado esta truncado.");
            }

            offset += read;
        }
    }

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
