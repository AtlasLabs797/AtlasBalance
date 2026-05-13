using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using AtlasBalance.API;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class ActualizacionServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task CheckVersionDisponible_Should_Use_GitHub_Releases_When_Config_Is_Default_Repo_Url()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = ConfigurationDefaults.UpdateCheckUrl
        });
        await db.SaveChangesAsync();

        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            request.Headers.UserAgent.ToString().Should().Contain("AtlasBalance");
            request.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v99.0.0","name":"Release 99"}""")
            };
        });
        var service = BuildService(db, handler);

        var result = await service.CheckVersionDisponibleAsync(CancellationToken.None);

        requestedUri.Should().Be(new Uri("https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest"));
        result.VersionDisponible.Should().Be("v99.0.0");
        result.ActualizacionDisponible.Should().BeTrue();
        result.Mensaje.Should().Be("Actualización disponible: v99.0.0. Revisa el paquete antes de instalar.");
    }

    [Fact]
    public async Task CheckVersionDisponible_Should_Fallback_To_Default_Repo_Url_When_Config_Is_Blank()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = ""
        });
        await db.SaveChangesAsync();

        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v99.0.0","name":"Release 99"}""")
            };
        });
        var service = BuildService(db, handler);

        var result = await service.CheckVersionDisponibleAsync(CancellationToken.None);

        requestedUri.Should().Be(new Uri("https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest"));
        result.VersionDisponible.Should().Be("v99.0.0");
    }

    [Fact]
    public async Task CheckVersionDisponible_Should_Not_Request_Unsafe_Configured_Url()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = "http://localhost/internal"
        });
        await db.SaveChangesAsync();

        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v99.0.0","name":"Release 99"}""")
            };
        });
        var service = BuildService(db, handler);

        var result = await service.CheckVersionDisponibleAsync(CancellationToken.None);

        requestedUri.Should().Be(new Uri("https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest"));
        result.VersionDisponible.Should().Be("v99.0.0");
    }

    [Fact]
    public async Task CheckVersionDisponible_Should_Send_GitHub_Token_When_Configured()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = ConfigurationDefaults.UpdateCheckUrl
        });
        await db.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(request =>
        {
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("github_pat_test");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v99.0.0","name":"Release 99"}""")
            };
        });
        var service = BuildService(db, handler, "github_pat_test");

        var result = await service.CheckVersionDisponibleAsync(CancellationToken.None);

        result.VersionDisponible.Should().Be("v99.0.0");
    }

    [Fact]
    public async Task IniciarActualizacionAsync_Should_Use_Configured_Target_And_Ignore_Request_Target()
    {
        await using var db = BuildDbContext();
        var root = Path.Combine(Path.GetTempPath(), $"atlas-balance-update-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "packages", "v99");
        var configuredTarget = Path.Combine(root, "app");
        var requestTarget = Path.Combine(root, "wrong-target");
        Directory.CreateDirectory(sourcePath);

        var watchdog = new RecordingWatchdogClientService();
        var service = BuildService(
            db,
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            watchdog: watchdog,
            updateSourceRoot: Path.Combine(root, "packages"),
            updateTargetPath: configuredTarget);

        var accepted = await service.IniciarActualizacionAsync(sourcePath, requestTarget, CancellationToken.None);

        accepted.Should().BeTrue();
        watchdog.SourcePath.Should().Be(sourcePath);
        watchdog.TargetPath.Should().Be(configuredTarget);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task IniciarActualizacionAsync_Should_Reject_Source_Outside_Update_Root()
    {
        await using var db = BuildDbContext();
        var root = Path.Combine(Path.GetTempPath(), $"atlas-balance-update-{Guid.NewGuid():N}");
        var outsideSource = Path.Combine(root, "outside", "v99");
        var configuredTarget = Path.Combine(root, "app");
        Directory.CreateDirectory(outsideSource);

        var watchdog = new RecordingWatchdogClientService();
        var service = BuildService(
            db,
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            watchdog: watchdog,
            updateSourceRoot: Path.Combine(root, "packages"),
            updateTargetPath: configuredTarget);

        var accepted = await service.IniciarActualizacionAsync(outsideSource, null, CancellationToken.None);

        accepted.Should().BeFalse();
        watchdog.Calls.Should().Be(0);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task IniciarActualizacionAsync_Should_Download_GitHub_Release_Asset_When_SourcePath_Is_Not_Provided()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = ConfigurationDefaults.UpdateCheckUrl
        });
        await db.SaveChangesAsync();

        var root = Path.Combine(Path.GetTempPath(), $"atlas-balance-update-{Guid.NewGuid():N}");
        var updateRoot = Path.Combine(root, "updates");
        var configuredTarget = Path.Combine(root, "app");
        Directory.CreateDirectory(updateRoot);

        var zipBytes = CreateReleaseZipBytes("V-99.00");
        var digest = Sha256Digest(zipBytes);
        using var signingKey = RSA.Create(2048);
        var signature = SignZipBytes(zipBytes, signingKey);
        var watchdog = new RecordingWatchdogClientService();
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == new Uri("https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "tag_name": "V-99.00-win-x64",
                      "name": "Release V-99.00",
                      "assets": [
                        {
                          "name": "AtlasBalance-V-99.00-win-x64.zip",
                          "browser_download_url": "https://github.com/AtlasLabs797/AtlasBalance/releases/download/V-99.00-win-x64/AtlasBalance-V-99.00-win-x64.zip",
                          "digest": "__DIGEST__"
                        },
                        {
                          "name": "AtlasBalance-V-99.00-win-x64.zip.sig",
                          "browser_download_url": "https://github.com/AtlasLabs797/AtlasBalance/releases/download/V-99.00-win-x64/AtlasBalance-V-99.00-win-x64.zip.sig"
                        }
                      ]
                    }
                    """.Replace("__DIGEST__", digest, StringComparison.Ordinal))
                };
            }

            if (request.RequestUri == new Uri("https://github.com/AtlasLabs797/AtlasBalance/releases/download/V-99.00-win-x64/AtlasBalance-V-99.00-win-x64.zip.sig"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(signature)
                };
            }

            request.RequestUri.Should().Be(new Uri("https://github.com/AtlasLabs797/AtlasBalance/releases/download/V-99.00-win-x64/AtlasBalance-V-99.00-win-x64.zip"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            };
        });
        var service = BuildService(
            db,
            handler,
            watchdog: watchdog,
            updateSourceRoot: updateRoot,
            updateTargetPath: configuredTarget,
            releaseSigningPublicKeyPem: signingKey.ExportSubjectPublicKeyInfoPem());

        var accepted = await service.IniciarActualizacionAsync(null, null, CancellationToken.None);

        accepted.Should().BeTrue();
        watchdog.Calls.Should().Be(1);
        watchdog.SourcePath.Should().NotBeNull();
        watchdog.SourcePath!.Replace('\\', '/').Should().EndWith("/api");
        watchdog.SourcePath.Should().StartWith(updateRoot);
        File.Exists(Path.Combine(watchdog.SourcePath, "AtlasBalance.API.exe")).Should().BeTrue();
        watchdog.TargetPath.Should().Be(configuredTarget);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task IniciarActualizacionAsync_Should_Reject_Downloaded_Asset_When_Signature_Is_Missing()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = ConfigurationDefaults.UpdateCheckUrl
        });
        await db.SaveChangesAsync();

        var root = Path.Combine(Path.GetTempPath(), $"atlas-balance-update-{Guid.NewGuid():N}");
        var updateRoot = Path.Combine(root, "updates");
        var configuredTarget = Path.Combine(root, "app");
        Directory.CreateDirectory(updateRoot);

        var zipBytes = CreateReleaseZipBytes("V-99.00");
        var digest = Sha256Digest(zipBytes);
        using var signingKey = RSA.Create(2048);
        var watchdog = new RecordingWatchdogClientService();
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == new Uri("https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "tag_name": "V-99.00-win-x64",
                      "assets": [
                        {
                          "name": "AtlasBalance-V-99.00-win-x64.zip",
                          "browser_download_url": "https://github.com/AtlasLabs797/AtlasBalance/releases/download/V-99.00-win-x64/AtlasBalance-V-99.00-win-x64.zip",
                          "digest": "__DIGEST__"
                        }
                      ]
                    }
                    """.Replace("__DIGEST__", digest, StringComparison.Ordinal))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            };
        });
        var service = BuildService(
            db,
            handler,
            watchdog: watchdog,
            updateSourceRoot: updateRoot,
            updateTargetPath: configuredTarget,
            releaseSigningPublicKeyPem: signingKey.ExportSubjectPublicKeyInfoPem());

        var accepted = await service.IniciarActualizacionAsync(null, null, CancellationToken.None);

        accepted.Should().BeFalse();
        watchdog.Calls.Should().Be(0);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task IniciarActualizacionAsync_Should_Reject_Downloaded_Asset_When_Digest_Does_Not_Match()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_update_check_url",
            Valor = ConfigurationDefaults.UpdateCheckUrl
        });
        await db.SaveChangesAsync();

        var root = Path.Combine(Path.GetTempPath(), $"atlas-balance-update-{Guid.NewGuid():N}");
        var updateRoot = Path.Combine(root, "updates");
        var configuredTarget = Path.Combine(root, "app");
        Directory.CreateDirectory(updateRoot);

        var zipBytes = CreateReleaseZipBytes("V-99.00");
        var watchdog = new RecordingWatchdogClientService();
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == new Uri("https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "tag_name": "V-99.00-win-x64",
                      "assets": [
                        {
                          "name": "AtlasBalance-V-99.00-win-x64.zip",
                          "browser_download_url": "https://github.com/AtlasLabs797/AtlasBalance/releases/download/V-99.00-win-x64/AtlasBalance-V-99.00-win-x64.zip",
                          "digest": "sha256:0000000000000000000000000000000000000000000000000000000000000000"
                        }
                      ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            };
        });
        var service = BuildService(
            db,
            handler,
            watchdog: watchdog,
            updateSourceRoot: updateRoot,
            updateTargetPath: configuredTarget);

        var accepted = await service.IniciarActualizacionAsync(null, null, CancellationToken.None);

        accepted.Should().BeFalse();
        watchdog.Calls.Should().Be(0);

        Directory.Delete(root, recursive: true);
    }

    private static ActualizacionService BuildService(
        AppDbContext db,
        HttpMessageHandler handler,
        string? githubUpdateToken = null,
        IWatchdogClientService? watchdog = null,
        string? updateSourceRoot = null,
        string? updateTargetPath = "C:/AtlasBalance/app",
        string? releaseSigningPublicKeyPem = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WatchdogSettings:SharedSecret"] = "test-secret",
                ["WatchdogSettings:UpdateSourceRoot"] = updateSourceRoot ?? "C:/AtlasBalance/updates",
                ["WatchdogSettings:UpdateTargetPath"] = updateTargetPath,
                ["GitHubSettings:UpdateToken"] = githubUpdateToken,
                ["UpdateSecurity:ReleaseSigningPublicKeyPem"] = releaseSigningPublicKeyPem
            })
            .Build();

        return new ActualizacionService(
            db,
            new StaticHttpClientFactory(handler),
            watchdog ?? new FakeWatchdogClientService(),
            NullLogger<ActualizacionService>.Instance,
            configuration);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StaticHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
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
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeWatchdogClientService : IWatchdogClientService
    {
        public Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken)
            => Task.FromResult(new WatchdogStateResponse());
    }

    private sealed class RecordingWatchdogClientService : IWatchdogClientService
    {
        public int Calls { get; private set; }
        public string? SourcePath { get; private set; }
        public string? TargetPath { get; private set; }

        public Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
        {
            Calls++;
            SourcePath = sourcePath;
            TargetPath = targetPath;
            return Task.FromResult(true);
        }

        public Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken)
            => Task.FromResult(new WatchdogStateResponse());
    }

    private static byte[] CreateReleaseZipBytes(string version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "VERSION", version);
            AddZipEntry(archive, "api/AtlasBalance.API.exe", "api");
            AddZipEntry(archive, "watchdog/AtlasBalance.Watchdog.exe", "watchdog");
        }

        return stream.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string Sha256Digest(byte[] bytes)
    {
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    private static byte[] SignZipBytes(byte[] bytes, RSA rsa)
    {
        return rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
