using FluentAssertions;
using AtlasBalance.Watchdog.Models;
using AtlasBalance.Watchdog.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        var installPath = Path.Combine(root, "install");
        CreateReleasePackage(packagePath, "V-99.00");
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
        var service = CreateService(stateStore, updateRoot, installPath);

        var accepted = await service.StartUpdateAsync(packagePath, installPath, null, CancellationToken.None);
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
            var completed = await Task.WhenAny(_completion.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != _completion.Task)
            {
                throw new TimeoutException("Watchdog operation did not complete in time.");
            }

            return await _completion.Task;
        }
    }
}
