using AtlasBalance.Watchdog.Models;
using AtlasBalance.Watchdog.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class WatchdogStateStoreTests
{
    [Fact]
    public async Task SetAsync_Should_Write_Valid_State_And_Remove_Temporary_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atlas-watchdog-state-{Guid.NewGuid():N}");
        var statePath = Path.Combine(root, "state.json");
        try
        {
            var store = CreateStore(statePath);
            await store.SetAsync(new WatchdogState { Estado = "OK", Operacion = "TEST" }, CancellationToken.None);

            var loaded = await store.GetAsync(CancellationToken.None);
            loaded.Estado.Should().Be("OK");
            Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_Should_Reject_Corrupt_State_Instead_Of_Silently_Resetting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atlas-watchdog-state-{Guid.NewGuid():N}");
        var statePath = Path.Combine(root, "state.json");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(statePath, "{ not-json");
            var store = CreateStore(statePath);

            var action = () => store.GetAsync(CancellationToken.None);

            await action.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static WatchdogStateStore CreateStore(string statePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WatchdogSettings:StateFilePath"] = statePath,
            })
            .Build();
        return new WatchdogStateStore(configuration, NullLogger<WatchdogStateStore>.Instance);
    }
}
