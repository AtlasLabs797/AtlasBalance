using System.Net;
using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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

    // -----------------------------------------------------------------------
    // V-02.07 (retencion de PII en la nube): DeleteRemoteBackupCopyAsync borra
    // el fichero remoto de Drive por su RemoteFileId. Un 404 significa que el
    // fichero ya no existe alli, y debe tratarse como exito idempotente (no
    // como fallo), para que reintentar la retencion sobre una copia ya borrada
    // no la deje marcada con error indefinidamente.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteRemoteBackupCopyAsync_Should_Treat_404_As_Idempotent_Success()
    {
        await using var db = BuildDbContext();
        var connection = new BackupCloudConnection
        {
            Id = Guid.NewGuid(),
            Provider = GoogleDriveBackupService.ProviderName,
            Estado = "CONNECTED",
            RefreshToken = "refresh-token-plain",
            ConnectedAt = DateTime.UtcNow
        };
        db.BackupCloudConnections.Add(connection);
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "google_drive_oauth_client_id", Valor = "client-id" },
            new Configuracion { Clave = "google_drive_oauth_client_secret", Valor = "client-secret" });
        var copy = new BackupCloudCopy
        {
            Id = Guid.NewGuid(),
            BackupId = Guid.NewGuid(),
            ConnectionId = connection.Id,
            Provider = GoogleDriveBackupService.ProviderName,
            Estado = "SUCCESS",
            RemoteFileId = "drive-file-already-gone",
            FechaCreacion = DateTime.UtcNow,
            ErrorCode = "stale_error_from_previous_attempt",
            ErrorMessage = "stale message"
        };
        db.BackupCloudCopies.Add(copy);
        await db.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"fake-access-token"}""")
                };
            }

            if (request.Method == HttpMethod.Delete &&
                request.RequestUri.AbsolutePath.Contains("drive-file-already-gone", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            throw new InvalidOperationException($"Peticion inesperada en el test: {request.Method} {request.RequestUri}");
        });

        var service = BuildService(db, handler);

        await service.DeleteRemoteBackupCopyAsync(copy, CancellationToken.None);

        copy.DeletedAt.Should().NotBeNull();
        copy.ErrorCode.Should().BeNull();
        copy.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRemoteBackupCopyAsync_Should_Record_Error_Without_Throwing_When_Drive_Not_Linked()
    {
        await using var db = BuildDbContext();
        var copy = new BackupCloudCopy
        {
            Id = Guid.NewGuid(),
            BackupId = Guid.NewGuid(),
            Provider = GoogleDriveBackupService.ProviderName,
            Estado = "SUCCESS",
            RemoteFileId = "drive-file-orphan",
            FechaCreacion = DateTime.UtcNow
        };
        db.BackupCloudCopies.Add(copy);
        await db.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("No deberia llamarse a Drive sin conexion vinculada."));
        var service = BuildService(db, handler);

        await service.DeleteRemoteBackupCopyAsync(copy, CancellationToken.None);

        copy.DeletedAt.Should().BeNull();
        copy.ErrorCode.Should().Be("google_drive_not_linked");
        copy.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static GoogleDriveBackupService BuildService(AppDbContext db, HttpMessageHandler handler)
    {
        return new GoogleDriveBackupService(
            db,
            new NamedHttpClientFactory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            new PlainTextSecretProtector(),
            new NotSupportedBackupEncryptionService(),
            TestAuditService.Create(db),
            NullLogger<GoogleDriveBackupService>.Instance);
    }

    private sealed class NamedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public NamedHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            var baseAddress = name switch
            {
                "google-oauth" => new Uri("https://oauth2.googleapis.com/"),
                "google-apis" => new Uri("https://www.googleapis.com/"),
                _ => throw new InvalidOperationException($"Cliente HTTP inesperado en el test: {name}")
            };

            return new HttpClient(_handler, disposeHandler: false) { BaseAddress = baseAddress };
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class NotSupportedBackupEncryptionService : IBackupEncryptionService
    {
        public Task<EncryptedBackupFile> EncryptAsync(string sourcePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("No se esperaba cifrado en este test.");

        public Task DecryptAsync(string encryptedPath, string destinationPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("No se esperaba descifrado en este test.");
    }
}
