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

    [Fact]
    public async Task ActualizarApp_Should_Not_Return_Package_Path_When_Zip_Is_Missing()
    {
        var operations = new FakeWatchdogOperationsService();
        var controller = new WatchdogController(operations, new FakeWatchdogStateStore());
        var missingZipPath = Path.Combine(Path.GetTempPath(), "watchdog-sensitive-release.zip");

        var result = await controller.ActualizarApp(
            new ActualizarAppRequest
            {
                SourcePath = Path.GetTempPath(),
                TargetPath = Path.Combine(Path.GetTempPath(), "atlas-balance-target"),
                PackageZipPath = missingZipPath
            },
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        response.Should().NotContain(missingZipPath);
        response.Should().NotContain("package_zip_path");
        operations.UpdateCalled.Should().BeFalse();
    }

    [Fact]
    public void ActualizarAppRequest_Should_Reject_An_Oversized_Or_NonZip_Package_Path()
    {
        var request = new ActualizarAppRequest
        {
            SourcePath = "C:\\AtlasBalance\\updates\\V-02.07",
            TargetPath = "C:\\AtlasBalance\\api",
            PackageZipPath = new string('a', 1025) + ".txt"
        };
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request,
            new System.ComponentModel.DataAnnotations.ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        valid.Should().BeFalse();
        validationResults.Should().NotBeEmpty();
    }

    [Fact]
    public void Sensitive_Watchdog_Operations_Should_Use_The_Narrow_Rate_Limit_Policy()
    {
        var restoreMethod = typeof(WatchdogController).GetMethod(nameof(WatchdogController.RestaurarBackup))!;
        var updateMethod = typeof(WatchdogController).GetMethod(nameof(WatchdogController.ActualizarApp))!;

        restoreMethod.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .Should().ContainSingle(attribute => ((Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute)attribute).PolicyName == AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.SensitiveOperationsPolicy);
        updateMethod.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .Should().ContainSingle(attribute => ((Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute)attribute).PolicyName == AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.SensitiveOperationsPolicy);
        AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.MaxRequestBodySize.Should().Be(16 * 1024);
        AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.SensitivePermitLimit.Should().BeLessThan(AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.GlobalPermitLimit);
    }

    [Fact]
    public void Health_Exemption_Should_Not_Disable_The_Global_Limiter_For_The_Same_Ip()
    {
        var healthContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        healthContext.Request.Path = "/watchdog/health";
        healthContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var statusContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        statusContext.Request.Path = "/watchdog/estado";
        statusContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        var health = AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.CreateGlobalPartition(healthContext);
        var status = AtlasBalance.Watchdog.RateLimiting.WatchdogRateLimiting.CreateGlobalPartition(statusContext);

        health.PartitionKey.Should().NotBe(status.PartitionKey,
            "el cache del PartitionedRateLimiter se indexa por clave y una llamada health no debe dejar sin limite al resto");
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
