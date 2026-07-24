using FluentAssertions;
using AtlasBalance.Watchdog.Models;
using AtlasBalance.Watchdog.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class WatchdogOperationsServiceTests
{
    [Fact]
    public async Task StartUpdateAsync_Should_Replace_Target_And_Remove_Stale_Files()
    {
        var root = CreateTempDirectory();
        var updateRoot = Path.Combine(root, "updates");
        var packagePath = Path.Combine(updateRoot, "V-99.00-win-x64");
        var zipPath = Path.Combine(updateRoot, "V-99.00-win-x64.zip");
        var installPath = Path.Combine(root, "install");
        CreateReleasePackage(packagePath, "V-99.00");
        var publicKeyPem = CreateSignedZip(zipPath);
        Directory.CreateDirectory(installPath);

        var staleFile = Path.Combine(installPath, "api", "old.dll");
        var preservedConfig = Path.Combine(installPath, "api", "appsettings.Production.json");
        var preservedLog = Path.Combine(installPath, "api", "logs", "historic.log");
        Directory.CreateDirectory(Path.GetDirectoryName(preservedLog)!);
        await File.WriteAllTextAsync(staleFile, "stale");
        await File.WriteAllTextAsync(preservedConfig, "{ \"existing\": true }");
        await File.WriteAllTextAsync(preservedLog, "keep");
        await File.WriteAllTextAsync(Path.Combine(installPath, "VERSION"), "V-01.09");
        await File.WriteAllTextAsync(Path.Combine(installPath, "atlas-balance.runtime.json"), """{"Version":"V-01.09"}""");

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateServiceWithKey(stateStore, updateRoot, installPath, publicKeyPem);

        var accepted = await service.StartUpdateAsync(packagePath, installPath, zipPath, CancellationToken.None);
        var finalState = await stateStore.WaitForCompletionAsync();
        var updatedApi = await File.ReadAllTextAsync(Path.Combine(installPath, "api", "AtlasBalance.API.exe"));
        var updatedWatchdog = await File.ReadAllTextAsync(Path.Combine(installPath, "watchdog", "AtlasBalance.Watchdog.exe"));
        var updatedVersion = await File.ReadAllTextAsync(Path.Combine(installPath, "VERSION"));
        var updatedRuntime = await File.ReadAllTextAsync(Path.Combine(installPath, "atlas-balance.runtime.json"));

        accepted.Should().BeTrue();
        finalState.Estado.Should().Be("SUCCESS");
        updatedApi.Should().Be("api-new");
        updatedWatchdog.Should().Be("watchdog-new");
        updatedVersion.Trim().Should().Be("V-99.00");
        updatedRuntime.Should().Contain("\"Version\": \"V-99.00\"");
        updatedRuntime.Should().Contain("\"PreviousVersion\": \"V-01.09\"");
        File.Exists(Path.Combine(installPath, "scripts", "Actualizar-AtlasBalance.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(installPath, "update.cmd")).Should().BeTrue();
        Directory.Exists(packagePath).Should().BeTrue();
        File.Exists(staleFile).Should().BeFalse();
        File.Exists(preservedConfig).Should().BeTrue();
        File.Exists(preservedLog).Should().BeTrue();

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Equal_Source_And_Target()
    {
        var root = CreateTempDirectory();
        var sourcePath = Path.Combine(root, "install");
        Directory.CreateDirectory(sourcePath);

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateService(stateStore, root, sourcePath);

        var accepted = await service.StartUpdateAsync(sourcePath, sourcePath, null, CancellationToken.None);

        accepted.Should().BeFalse();

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Source_Outside_Configured_Update_Root()
    {
        var root = CreateTempDirectory();
        var allowedRoot = Path.Combine(root, "allowed");
        var sourcePath = Path.Combine(root, "outside-source");
        var targetPath = Path.Combine(root, "target");
        Directory.CreateDirectory(allowedRoot);
        CreateReleasePackage(sourcePath, "V-99.00");

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateService(stateStore, allowedRoot, targetPath);

        var accepted = await service.StartUpdateAsync(sourcePath, targetPath, null, CancellationToken.None);

        accepted.Should().BeFalse();

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task StartRestoreAsync_Should_Reject_Relative_Backup_Path()
    {
        var root = CreateTempDirectory();
        var backupFile = Path.Combine(root, "backup.dump");
        await File.WriteAllTextAsync(backupFile, "not-a-real-dump");

        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(root);
            var stateStore = new FakeWatchdogStateStore();
            var service = CreateService(stateStore, backupPathRoot: root);

            var accepted = await service.StartRestoreAsync("backup.dump", CancellationToken.None);

            accepted.Should().BeFalse();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(root, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // V-02.06 (CRIT-3): VerifyPackageZipIntegrity rechaza paquetes cuya firma
    // RSA no valida contra la clave publica configurada. Los tests
    // negativos no requieren clave privada: ZIP sin firma, ZIP firmado
    // con bytes basura y ZIP fuera de UpdateSourceRoot.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Zip_Without_Signature()
    {
        var root = CreateTempDirectory();
        var updateRoot = Path.Combine(root, "updates");
        var packagePath = Path.Combine(updateRoot, "V-99.00-win-x64.zip");
        var installPath = Path.Combine(root, "install");
        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(installPath);
        File.WriteAllBytes(packagePath, BuildMinimalZip());

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateServiceWithKey(stateStore, updateRoot, installPath,
            publicKeyPem: "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEARlNmK7MXkYqVXhPiCm5l+9HbSxo=\n-----END PUBLIC KEY-----");

        // VerifyPackageZipIntegrity rechaza antes de iniciar la operacion;
        // por lo tanto StartUpdateAsync retorna false sin tocar el state
        // store. No esperamos a WaitForCompletionAsync (no llegara).
        var accepted = await service.StartUpdateAsync(null, installPath, packagePath, CancellationToken.None);

        accepted.Should().BeFalse();

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Zip_With_Invalid_Signature()
    {
        var root = CreateTempDirectory();
        var updateRoot = Path.Combine(root, "updates");
        var packagePath = Path.Combine(updateRoot, "V-99.00-win-x64.zip");
        var signaturePath = packagePath + ".sig";
        var installPath = Path.Combine(root, "install");
        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(installPath);
        File.WriteAllBytes(packagePath, BuildMinimalZip());
        File.WriteAllBytes(signaturePath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateServiceWithKey(stateStore, updateRoot, installPath,
            publicKeyPem: "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEARlNmK7MXkYqVXhPiCm5l+9HbSxo=\n-----END PUBLIC KEY-----");

        var accepted = await service.StartUpdateAsync(null, installPath, packagePath, CancellationToken.None);

        accepted.Should().BeFalse();

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Zip_Outside_UpdateSourceRoot()
    {
        var root = CreateTempDirectory();
        var allowedRoot = Path.Combine(root, "allowed");
        var outsidePath = Path.Combine(root, "outside", "evil.zip");
        var installPath = Path.Combine(root, "install");
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        Directory.CreateDirectory(installPath);
        File.WriteAllBytes(outsidePath, BuildMinimalZip());

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateServiceWithKey(stateStore, allowedRoot, installPath,
            publicKeyPem: "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEARlNmK7MXkYqVXhPiCm5l+9HbSxo=\n-----END PUBLIC KEY-----");

        var accepted = await service.StartUpdateAsync(null, installPath, outsidePath, CancellationToken.None);

        accepted.Should().BeFalse();

        Directory.Delete(root, recursive: true);
    }

    private static WatchdogOperationsService CreateServiceWithKey(
        FakeWatchdogStateStore stateStore,
        string? updateSourceRoot,
        string? updateTargetPath,
        string publicKeyPem)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WatchdogSettings:ApiServiceName"] = $"FakeService-{Guid.NewGuid():N}",
                ["WatchdogSettings:UpdateSourceRoot"] = updateSourceRoot,
                ["WatchdogSettings:UpdateTargetPath"] = updateTargetPath,
                ["WatchdogSettings:RequireDatabaseBackupBeforeUpdate"] = "false",
                ["UpdateSecurity:ReleaseSigningPublicKeyPem"] = publicKeyPem
            })
            .Build();

        return new WatchdogOperationsService(
            configuration,
            stateStore,
            NullLogger<WatchdogOperationsService>.Instance);
    }

    private static byte[] BuildMinimalZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("VERSION");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("V-99.00");
        }
        return ms.ToArray();
    }

    private static string CreateSignedZip(string zipPath)
    {
        var bytes = BuildMinimalZip();
        File.WriteAllBytes(zipPath, bytes);
        using var rsa = RSA.Create(2048);
        File.WriteAllBytes($"{zipPath}.sig", rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return rsa.ExportSubjectPublicKeyInfoPem();
    }

    private static WatchdogOperationsService CreateService(
        FakeWatchdogStateStore stateStore,
        string? updateSourceRoot = null,
        string? updateTargetPath = null,
        string? backupPathRoot = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WatchdogSettings:ApiServiceName"] = $"FakeService-{Guid.NewGuid():N}",
                ["WatchdogSettings:UpdateSourceRoot"] = updateSourceRoot,
                ["WatchdogSettings:UpdateTargetPath"] = updateTargetPath,
                ["WatchdogSettings:BackupPath"] = backupPathRoot,
                ["WatchdogSettings:RequireDatabaseBackupBeforeUpdate"] = "false"
            })
            .Build();

        return new WatchdogOperationsService(
            configuration,
            stateStore,
            NullLogger<WatchdogOperationsService>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atlas-balance-watchdog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateReleasePackage(string packagePath, string version)
    {
        Directory.CreateDirectory(Path.Combine(packagePath, "api"));
        Directory.CreateDirectory(Path.Combine(packagePath, "watchdog"));
        Directory.CreateDirectory(Path.Combine(packagePath, "scripts"));
        File.WriteAllText(Path.Combine(packagePath, "VERSION"), version);
        File.WriteAllText(Path.Combine(packagePath, "api", "AtlasBalance.API.exe"), "api-new");
        File.WriteAllText(Path.Combine(packagePath, "watchdog", "AtlasBalance.Watchdog.exe"), "watchdog-new");
        File.WriteAllText(Path.Combine(packagePath, "scripts", "Actualizar-AtlasBalance.ps1"), "script-new");
        File.WriteAllText(Path.Combine(packagePath, "update.cmd"), "update-cmd");
    }

    private sealed class FakeWatchdogStateStore : IWatchdogStateStore
    {
        private readonly TaskCompletionSource<WatchdogState> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private WatchdogState _current = new();

        public Task<WatchdogState> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_current);
        }

        public Task SetAsync(WatchdogState state, CancellationToken cancellationToken)
        {
            _current = state;
            if (!string.Equals(state.Estado, "RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                _completion.TrySetResult(state);
            }

            return Task.CompletedTask;
        }

        public async Task<WatchdogState> WaitForCompletionAsync()
        {
            // V-02.06 (CRIT-3): las nuevas verificaciones RSA pueden tardar
            // unos segundos cuando la clave publica es la de produccion;
            // 30s holgura es suficiente sin ralentizar los tests rapidos.
            var completed = await Task.WhenAny(_completion.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != _completion.Task)
            {
                throw new TimeoutException("Watchdog operation did not complete in time.");
            }

            return await _completion.Task;
        }
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Missing_Package_Zip_Path()
    {
        var root = CreateTempDirectory();
        try
        {
            var updateRoot = Path.Combine(root, "updates");
            var packageRoot = Path.Combine(updateRoot, "V-99.00-win-x64");
            var installPath = Path.Combine(root, "install");
            CreateReleasePackage(packageRoot, "V-99.00");
            Directory.CreateDirectory(installPath);

            var service = CreateServiceWithKey(
                new FakeWatchdogStateStore(),
                updateRoot,
                installPath,
                publicKeyPem: "not-used-when-path-is-missing");

            var accepted = await service.StartUpdateAsync(packageRoot, installPath, null, CancellationToken.None);

            accepted.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Missing_Public_Key()
    {
        var root = CreateTempDirectory();
        try
        {
            var updateRoot = Path.Combine(root, "updates");
            var packageRoot = Path.Combine(updateRoot, "V-99.00-win-x64");
            var zipPath = Path.Combine(updateRoot, "V-99.00-win-x64.zip");
            var installPath = Path.Combine(root, "install");
            CreateReleasePackage(packageRoot, "V-99.00");
            Directory.CreateDirectory(installPath);
            File.WriteAllBytes(zipPath, BuildMinimalZip());
            File.WriteAllBytes($"{zipPath}.sig", [1, 2, 3]);

            var service = CreateService(new FakeWatchdogStateStore(), updateRoot, installPath);

            var accepted = await service.StartUpdateAsync(packageRoot, installPath, zipPath, CancellationToken.None);

            accepted.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
