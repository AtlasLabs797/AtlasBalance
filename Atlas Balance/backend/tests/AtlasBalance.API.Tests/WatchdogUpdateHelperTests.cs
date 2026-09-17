using System.Reflection;
using AtlasBalance.Watchdog.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class WatchdogUpdateHelperTests
{
    [Fact]
    public void OnlineUpdateHelper_Should_Use_Atomic_State_Replacement()
    {
        var method = typeof(WatchdogOperationsService).GetMethod(
            "BuildOnlineUpdateHelperScript",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var script = (string)method!.Invoke(null, null)!;

        script.Should().Contain(".tmp");
        script.Should().Contain("[System.IO.File]::Replace");
        script.Should().Contain("[System.IO.File]::Move");
        script.Should().NotContain("Set-Content -LiteralPath $StateFilePath");
    }

    [Fact]
    public void ElevatedUpdateRunner_Should_Exist_And_Require_Explicit_Mode()
    {
        var runnerType = typeof(WatchdogOperationsService).Assembly.GetType(
            "AtlasBalance.Watchdog.Services.ElevatedUpdateRunner");

        runnerType.Should().NotBeNull();
        var isRequested = runnerType!.GetMethod(
            "IsRequested",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        isRequested.Should().NotBeNull();
        ((bool)isRequested!.Invoke(null, [new[] { "--run-elevated-update" }])!).Should().BeTrue();
        ((bool)isRequested.Invoke(null, [new[] { "--server" }])!).Should().BeFalse();
    }
}
