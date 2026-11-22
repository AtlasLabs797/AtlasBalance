using System.Buffers.Binary;
using System.Security.Cryptography;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

// Modificaciones V-02-03 — ver Documentacion/DOCUMENTACION_CAMBIOS.md.
// C2: ResolveKeyAsync nunca regenera una clave existente (anti-pérdida de backups cifrados).

public interface IBackupEncryptionService
{
    Task<EncryptedBackupFile> EncryptAsync(string sourcePath, CancellationToken cancellationToken);
    Task DecryptAsync(string encryptedPath, string destinationPath, CancellationToken cancellationToken);
    Task DecryptAsync(string encryptedPath, string destinationPath, long maxPlaintextBytes, CancellationToken cancellationToken);
}

public sealed record EncryptedBackupFile(string Path, long SizeBytes, string Sha256Hex);

public sealed class BackupEncryptionService : IBackupEncryptionService
{
    // V-02.06: BuildChunkNonceLegacyV1 solo dejaba 4 bytes reales de baseNonce (los otros 8
    // los pisaba el contador), desperdiciando entropia del nonce de AES-GCM. Los backups ya
    // cifrados con el formato V1 siguen siendo descifrables: el header identifica la version
    // y selecciona el derivador de nonce correspondiente. Los backups nuevos usan V2.
    private static readonly byte[] MagicV1 = "ATLASBKP1"u8.ToArray();
    private static readonly byte[] MagicV2 = "ATLASBKP2"u8.ToArray();
    // S-19 (V-03.02): V3 anade el numero de bloques a la cabecera y lo autentica
    // mediante el AAD del ultimo bloque. V1 y V2 se conservan intactos para poder
    // descifrar los backups ya existentes (sin autenticar el numero de bloques:
    // es un limite del formato legacy que no se puede corregir sin romperlos).
    private static readonly byte[] MagicV3 = "ATLASBKP3"u8.ToArray();
    private const int ChunkSize = 1024 * 1024;
    private const int TagSize = 16;
    private const int NonceSize = 12;
    internal const long DefaultMaxPlaintextBytes = 10L * 1024 * 1024 * 1024;

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

        // Un volcado de 0 bytes no es un volcado: cifrarlo produciria un .enc que
        // descifra a un fichero vacio sin dar ningun aviso. Se rechaza antes de escribir.
        if (new FileInfo(sourcePath).Length == 0)
        {
            throw new InvalidOperationException("El archivo de backup a cifrar esta vacio (0 bytes): no se puede cifrar un volcado vacio.");
        }

        var destinationPath = $"{sourcePath}.enc";
        var key = await ResolveKeyAsync(cancellationToken);
        var baseNonce = RandomNumberGenerator.GetBytes(NonceSize);
        var destinationCreated = false;

        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true))
            await using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
            {
                destinationCreated = true;
                await destination.WriteAsync(MagicV3, cancellationToken);
                await destination.WriteAsync(baseNonce, cancellationToken);
                // El numero de bloques no se conoce hasta el final del flujo: se deja
                // un marcador y se parchea cuando ya esta cifrado el ultimo bloque.
                await destination.WriteAsync(new byte[4], cancellationToken);

                var plain = new byte[ChunkSize];
                var cipher = new byte[ChunkSize];
                var tag = new byte[TagSize];
                var lengthBytes = new byte[4];
                var pending = new byte[ChunkSize];
                var pendingLength = 0;
                var blockCount = 0;
                ulong counter = 0;

                async Task WriteBlockAsync(byte[] buffer, int length, byte[] aad, ulong chunkCounter)
                {
                    var nonce = BuildChunkNonceV2(baseNonce, chunkCounter);
                    using var aes = new AesGcm(key, TagSize);
                    aes.Encrypt(nonce, buffer.AsSpan(0, length), cipher.AsSpan(0, length), tag, aad);
                    BinaryPrimitives.WriteInt32BigEndian(lengthBytes, length);
                    await destination.WriteAsync(lengthBytes, cancellationToken);
                    await destination.WriteAsync(tag, cancellationToken);
                    await destination.WriteAsync(cipher.AsMemory(0, length), cancellationToken);
                }

                while (true)
                {
                    var read = await source.ReadAsync(plain.AsMemory(0, plain.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    if (pendingLength > 0)
                    {
                        // El bloque retenido ya no es el ultimo: se cifra con el AAD simple.
                        await WriteBlockAsync(pending, pendingLength, MagicV3, counter++);
                        blockCount++;
                    }

                    Buffer.BlockCopy(plain, 0, pending, 0, read);
                    pendingLength = read;
                }

                if (pendingLength == 0)
                {
                    throw new InvalidOperationException("El archivo de backup a cifrar esta vacio (0 bytes): no se puede cifrar un volcado vacio.");
                }

                // El ultimo bloque se cifra con un AAD que liga el numero de bloques:
                // un recorte o una manipulacion de la cabecera invalida su tag.
                var totalBlocks = blockCount + 1;
                var countBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(countBytes, totalBlocks);
                await WriteBlockAsync(pending, pendingLength, BuildBlockCountAad(totalBlocks), counter);

                // Se parchea el numero de bloques real en la cabecera.
                destination.Seek(MagicV3.Length + NonceSize, SeekOrigin.Begin);
                await destination.WriteAsync(countBytes, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
        }
        catch
        {
            // No se deja un .enc a medias: con el numero de bloques sin parchear seria
            // un fichero corrupto que el descifrado no puede validar. Solo se borra si
            // este proceso ha llegado a crear el fichero.
            if (destinationCreated && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw;
        }

        var info = new FileInfo(destinationPath);
        var hash = await ComputeSha256HexAsync(destinationPath, cancellationToken);
        return new EncryptedBackupFile(destinationPath, info.Length, hash);
    }

    public async Task DecryptAsync(string encryptedPath, string destinationPath, CancellationToken cancellationToken)
    {
        await DecryptAsync(encryptedPath, destinationPath, DefaultMaxPlaintextBytes, cancellationToken);
    }

    public async Task DecryptAsync(string encryptedPath, string destinationPath, long maxPlaintextBytes, CancellationToken cancellationToken)
    {
        if (!File.Exists(encryptedPath))
        {
            throw new FileNotFoundException("No existe el archivo cifrado de backup.", encryptedPath);
        }

        if (maxPlaintextBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPlaintextBytes), "El limite de descifrado debe ser positivo.");
        }

        var key = await ResolveKeyAsync(cancellationToken);
        var completed = false;
        try
        {
            await using var source = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);

            var header = new byte[MagicV3.Length];
            await ReadExactAsync(source, header, cancellationToken);

            if (header.AsSpan().SequenceEqual(MagicV3))
            {
                await DecryptV3Async(source, destination, key, maxPlaintextBytes, cancellationToken);
            }
            else
            {
                byte[] aad;
                Func<byte[], ulong, byte[]> buildNonce;
                if (header.AsSpan().SequenceEqual(MagicV2))
                {
                    aad = MagicV2;
                    buildNonce = BuildChunkNonceV2;
                }
                else if (header.AsSpan().SequenceEqual(MagicV1))
                {
                    aad = MagicV1;
                    buildNonce = BuildChunkNonceLegacyV1;
                }
                else
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
                long totalPlaintextBytes = 0;

                while (source.Position < source.Length)
                {
                    await ReadExactAsync(source, lengthBytes, cancellationToken);
                    var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
                    if (length <= 0 || length > ChunkSize)
                    {
                        throw new InvalidOperationException("El backup cifrado contiene un bloque invalido.");
                    }

                    if (length > maxPlaintextBytes - totalPlaintextBytes)
                    {
                        throw new InvalidOperationException("El backup cifrado supera el limite de tamano permitido.");
                    }

                    await ReadExactAsync(source, tag, cancellationToken);
                    await ReadExactAsync(source, cipher.AsMemory(0, length), cancellationToken);

                    var nonce = buildNonce(baseNonce, counter++);
                    using var aes = new AesGcm(key, TagSize);
                    aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), aad);
                    await destination.WriteAsync(plain.AsMemory(0, length), cancellationToken);
                    totalPlaintextBytes += length;
                }
            }

            completed = true;
        }
        finally
        {
            if (!completed && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    // S-19 (V-03.02): descifrado del formato V3. La cabecera declara el numero de
    // bloques y ese numero esta autenticado por el AAD del ultimo bloque: un fichero
    // truncado en un limite de bloque deja la cabecera con un numero que ya no
    // corresponde, y el tag del bloque que pasa a ser el ultimo no valida.
    private static async Task DecryptV3Async(FileStream source, FileStream destination, byte[] key, long maxPlaintextBytes, CancellationToken cancellationToken)
    {
        var baseNonce = new byte[NonceSize];
        await ReadExactAsync(source, baseNonce, cancellationToken);

        var countBytes = new byte[4];
        await ReadExactAsync(source, countBytes, cancellationToken);
        var declaredBlocks = BinaryPrimitives.ReadInt32BigEndian(countBytes);
        if (declaredBlocks <= 0)
        {
            throw new InvalidOperationException("El backup cifrado declara un numero de bloques invalido.");
        }

        var lengthBytes = new byte[4];
        var tag = new byte[TagSize];
        var plain = new byte[ChunkSize];
        var cipher = new byte[ChunkSize];
        long totalPlaintextBytes = 0;

        for (var index = 0; index < declaredBlocks; index++)
        {
            int length;
            try
            {
                await ReadExactAsync(source, lengthBytes, cancellationToken);
                length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
                if (length <= 0 || length > ChunkSize)
                {
                    throw new InvalidOperationException("El backup cifrado contiene un bloque invalido.");
                }

                await ReadExactAsync(source, tag, cancellationToken);
                await ReadExactAsync(source, cipher.AsMemory(0, length), cancellationToken);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidOperationException(
                    $"El backup cifrado esta truncado: la cabecera declara {declaredBlocks} bloques y solo se han podido leer {index}.",
                    ex);
            }

            if (length > maxPlaintextBytes - totalPlaintextBytes)
            {
                throw new InvalidOperationException("El backup cifrado supera el limite de tamano permitido.");
            }

            var aad = index == declaredBlocks - 1 ? BuildBlockCountAad(declaredBlocks) : MagicV3;
            var nonce = BuildChunkNonceV2(baseNonce, (ulong)index);
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), aad);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    $"El backup cifrado ha sido manipulado o esta truncado: no se pudo validar el bloque {index + 1} de {declaredBlocks}.",
                    ex);
            }

            await destination.WriteAsync(plain.AsMemory(0, length), cancellationToken);
            totalPlaintextBytes += length;
        }

        if (source.Position != source.Length)
        {
            throw new InvalidOperationException("El backup cifrado contiene datos extra tras el ultimo bloque.");
        }
    }

    // S-19 (V-03.02): AAD del ultimo bloque = magic V3 + numero de bloques.
    private static byte[] BuildBlockCountAad(int blockCount)
    {
        var aad = new byte[MagicV3.Length + 4];
        Buffer.BlockCopy(MagicV3, 0, aad, 0, MagicV3.Length);
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(MagicV3.Length), blockCount);
        return aad;
    }

    private async Task<byte[]> ResolveKeyAsync(CancellationToken cancellationToken)
    {
        var row = await _dbContext.Configuraciones
            .FirstOrDefaultAsync(x => x.Clave == "backup_cloud_encryption_key", cancellationToken);

        // SECURITY (C2, V-02-03): si existe una clave previa, se usa SIEMPRE,
        // aunque parezca corrupta. Regenerar automaticamente destruiria todos
        // los backups cifrados subidos a la nube con la clave anterior.
        // La rotacion de claves debe ser una operacion manual y consciente.
        if (row is not null && !string.IsNullOrWhiteSpace(row.Valor))
        {
            var unprotected = _secretProtector.UnprotectFromStorage(row.Valor);
            if (!string.IsNullOrWhiteSpace(unprotected))
            {
                byte[] key;
                try
                {
                    key = Convert.FromBase64String(unprotected);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "La clave de cifrado de backups en nube esta corrupta (no es Base64 valido). NO se regenera para no perder backups cifrados.");
                    throw new InvalidOperationException(
                        "La clave de cifrado de backups en nube esta corrupta. NO se regenera automaticamente para no perder backups cifrados. " +
                        "Intervencion manual requerida: contacte con soporte para recuperar la clave o restaurar desde una copia de seguridad.", ex);
                }

                if (key.Length != 32)
                {
                    _logger.LogError("La clave de cifrado de backups en nube tiene una longitud invalida ({Length} bytes; se requieren 32). NO se regenera para no perder backups cifrados.", key.Length);
                    throw new InvalidOperationException(
                        $"La clave de cifrado de backups en nube tiene longitud invalida ({key.Length} bytes; se requieren 32). " +
                        "NO se regenera automaticamente para no perder backups cifrados. Intervencion manual requerida.");
                }

                return key;
            }
        }

        // Solo generamos una clave nueva cuando NO existe ninguna previa.
        // Esto cubre el primer arranque de la aplicacion.
        if (row is null)
        {
            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = "backup_cloud_encryption_key",
                Valor = _secretProtector.ProtectForStorage(generated),
                Tipo = "string",
                Descripcion = "Clave de cifrado para copias subidas a nube (rotacion manual)",
                FechaModificacion = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Convert.FromBase64String(generated);
        }

        // La fila existe pero no tiene valor util: tampoco sobreescribir.
        _logger.LogError("La fila backup_cloud_encryption_key existe pero esta vacia o solo contiene espacios en blanco. NO se regenera para no perder backups cifrados.");
        throw new InvalidOperationException(
            "La clave de cifrado de backups en nube esta vacia. NO se regenera automaticamente para no perder backups cifrados. " +
            "Intervencion manual requerida.");
    }

    // Formato legado (ATLASBKP1): pisaba los 8 bytes centrales del baseNonce de 12 bytes
    // con un contador de 8 bytes, dejando solo 4 bytes (32 bits) de entropia real por
    // archivo. Se conserva sin cambios, solo para poder descifrar backups ya existentes.
    private static byte[] BuildChunkNonceLegacyV1(byte[] baseNonce, ulong counter)
    {
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(baseNonce, 0, nonce, 0, NonceSize);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    // V-02.06: conserva los primeros 8 bytes (64 bits) del baseNonce aleatorio y solo
    // pisa los ultimos 4 bytes con el contador de fragmento. A 1 MiB por fragmento, un
    // contador de 32 bits cubre hasta 4 EiB por backup, muy por encima de cualquier
    // tamano real, y sube la entropia efectiva del nonce de 32 a 64 bits.
    private static byte[] BuildChunkNonceV2(byte[] baseNonce, ulong counter)
    {
        if (counter > uint.MaxValue)
        {
            throw new InvalidOperationException(
                "El backup supera el numero maximo de fragmentos cifrables (4294967295) con el esquema de nonce actual.");
        }

        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(baseNonce, 0, nonce, 0, NonceSize);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NonceSize - 4), (uint)counter);
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
