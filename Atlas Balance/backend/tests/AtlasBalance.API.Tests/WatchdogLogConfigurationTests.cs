using AtlasBalance.Watchdog.Logging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class WatchdogLogConfigurationTests
{
    [Fact]
    public void DefaultLogDirectory_Should_Be_Absolute_And_Independent_Of_WorkingDirectory()
    {
        // Raiz absoluta en cualquier SO: C:\ProgramData solo es absoluta en Windows.
        var programData = Path.Combine(Path.GetTempPath(), "ProgramData");
        var result = WatchdogLogConfiguration.ResolveLogDirectory(null, programData);

        result.Should().Be(Path.GetFullPath(Path.Combine(programData, "AtlasBalance", "logs")));
        Path.IsPathRooted(result).Should().BeTrue();
    }

    [Fact]
    public void RelativeLogDirectory_Should_Be_Rejected()
    {
        var action = () => WatchdogLogConfiguration.ResolveLogDirectory("logs", @"C:\ProgramData");

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("ruta absoluta");
    }

    [Fact]
    public void AbsoluteConfiguredLogDirectory_Should_Be_Used()
    {
        var configured = Path.Combine(Path.GetTempPath(), "AtlasBalance", "logs");
        var result = WatchdogLogConfiguration.ResolveLogDirectory(configured);

        result.Should().Be(Path.GetFullPath(configured));
    }

    [Fact]
    public void RelativeSerilogFilePath_Should_Be_Rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:FilePath"] = @"logs\watchdog.log"
            })
            .Build();

        var action = () => WatchdogLogConfiguration.ResolveLogFilePath(configuration);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StateFile_Default_Should_Be_Absolute()
    {
        var result = WatchdogLogConfiguration.ResolveStateFilePath(new ConfigurationBuilder().Build());

        Path.IsPathRooted(result).Should().BeTrue();
        result.EndsWith(Path.Combine("AtlasBalance", "watchdog-state.json"), StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue();
    }

    [Fact]
    public void EnsureLogDirectory_Should_Fail_Closed_When_Destination_Is_A_File()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"watchdog-log-file-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(filePath, "test");

        try
        {
            var action = () => WatchdogLogConfiguration.EnsureLogDirectory(Path.Combine(filePath, "watchdog-.log"));

            action.Should().Throw<IOException>();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ExistingAclVerification_Should_Validate_The_Owner_Before_Continuing()
    {
        var source = File.ReadAllText(TestSourceLocator.Find(
            "AtlasBalance.Watchdog", "Logging", "WatchdogLogConfiguration.cs"));

        source.Should().Contain("security.GetOwner(typeof(SecurityIdentifier))");
        source.Should().Contain("El propietario de '{path}' no pertenece a la allowlist protegida.");
    }
}
