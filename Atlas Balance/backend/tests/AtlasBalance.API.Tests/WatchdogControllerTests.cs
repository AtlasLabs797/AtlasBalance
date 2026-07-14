using AtlasBalance.Watchdog.Controllers;
using AtlasBalance.Watchdog.Models;
using AtlasBalance.Watchdog.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class WatchdogControllerTests
{
    [Fact]
    public async Task ActualizarApp_Should_Return_BadRequest_For_Invalid_Path()
    {
        var operations = new FakeWatchdogOperationsService();
        var controller = new WatchdogController(operations, new FakeWatchdogStateStore());

        var result = await controller.ActualizarApp(
            new ActualizarAppRequest
            {
                SourcePath = "\0bad",
                TargetPath = "C:\\AtlasBalance\\api"
            },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        operations.UpdateCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RestaurarBackup_Should_Return_BadRequest_For_Missing_Body()
    {
        var operations = new FakeWatchdogOperationsService();
        var controller = new WatchdogController(operations, new FakeWatchdogStateStore());

        var result = await controller.RestaurarBackup(null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        operations.RestoreCalled.Should().BeFalse();
    }

    private sealed class FakeWatchdogOperationsService : IWatchdogOperationsService
    {
        public bool RestoreCalled { get; private set; }
        public bool UpdateCalled { get; private set; }

        public Task<bool> StartRestoreAsync(string backupPath, CancellationToken cancellationToken)
        {
            RestoreCalled = true;
            return Task.FromResult(true);
        }

        public Task<bool> StartUpdateAsync(string? sourcePath, string? targetPath, string? packageZipPath, CancellationToken cancellationToken)
        {
            UpdateCalled = true;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeWatchdogStateStore : IWatchdogStateStore
    {
        public Task<WatchdogState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new WatchdogState());

        public Task SetAsync(WatchdogState state, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
