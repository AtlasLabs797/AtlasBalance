using FluentAssertions;
using GestionCaja.Watchdog.Models;
using GestionCaja.Watchdog.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCaja.API.Tests;

public sealed class WatchdogOperationsServiceTests
{
    [Fact]
    public async Task StartUpdateAsync_Should_Replace_Target_And_Remove_Stale_Files()
    {
        var root = CreateTempDirectory();
        var sourcePath = Path.Combine(root, "source");
        var targetPath = Path.Combine(root, "target");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);

        var sourceFile = Path.Combine(sourcePath, "app.dll");
        var nestedSourceDirectory = Path.Combine(sourcePath, "wwwroot");
        Directory.CreateDirectory(nestedSourceDirectory);
        await File.WriteAllTextAsync(sourceFile, "new-binary");
        await File.WriteAllTextAsync(Path.Combine(nestedSourceDirectory, "index.html"), "<html>fresh</html>");

        var staleFile = Path.Combine(targetPath, "old.dll");
        var preservedConfig = Path.Combine(targetPath, "appsettings.Production.json");
        var preservedLog = Path.Combine(targetPath, "logs", "historic.log");
        Directory.CreateDirectory(Path.GetDirectoryName(preservedLog)!);
        await File.WriteAllTextAsync(staleFile, "stale");
        await File.WriteAllTextAsync(preservedConfig, "{ \"existing\": true }");
        await File.WriteAllTextAsync(preservedLog, "keep");

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateService(stateStore, root, targetPath);

        var accepted = await service.StartUpdateAsync(sourcePath, targetPath, CancellationToken.None);
        var finalState = await stateStore.WaitForCompletionAsync();
        var updatedBinary = await File.ReadAllTextAsync(Path.Combine(targetPath, "app.dll"));

        accepted.Should().BeTrue();
        finalState.Estado.Should().Be("SUCCESS");
        File.Exists(sourceFile).Should().BeTrue();
        updatedBinary.Should().Be("new-binary");
        File.Exists(Path.Combine(targetPath, "wwwroot", "index.html")).Should().BeTrue();
        File.Exists(staleFile).Should().BeFalse();
        File.Exists(preservedConfig).Should().BeTrue();
        File.Exists(preservedLog).Should().BeTrue();

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task StartUpdateAsync_Should_Reject_Overlapping_Source_And_Target()
    {
        var root = CreateTempDirectory();
        var sourcePath = Path.Combine(root, "source");
        var targetPath = Path.Combine(sourcePath, "nested-target");
        Directory.CreateDirectory(sourcePath);

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateService(stateStore, root, targetPath);

        var accepted = await service.StartUpdateAsync(sourcePath, targetPath, CancellationToken.None);

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
        Directory.CreateDirectory(sourcePath);

        var stateStore = new FakeWatchdogStateStore();
        var service = CreateService(stateStore, allowedRoot, targetPath);

        var accepted = await service.StartUpdateAsync(sourcePath, targetPath, CancellationToken.None);

        accepted.Should().BeFalse();

        Directory.Delete(root, recursive: true);
    }

    private static WatchdogOperationsService CreateService(
        FakeWatchdogStateStore stateStore,
        string? updateSourceRoot = null,
        string? updateTargetPath = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WatchdogSettings:ApiServiceName"] = $"FakeService-{Guid.NewGuid():N}",
                ["WatchdogSettings:UpdateSourceRoot"] = updateSourceRoot,
                ["WatchdogSettings:UpdateTargetPath"] = updateTargetPath
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
