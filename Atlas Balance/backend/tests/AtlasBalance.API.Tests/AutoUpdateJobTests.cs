using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class AutoUpdateJobTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Start_Update_When_Enabled_And_Daily_Window_Reached()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            CreateConfig("app_update_auto_enabled", "true"),
            CreateConfig("app_update_auto_hour_utc", "3"),
            CreateConfig("app_update_auto_last_checked_utc", "2026-05-16T04:00:00.0000000Z"));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService
        {
            AvailableResponse = new VersionDisponibleResponse
            {
                VersionActual = "V-01.09",
                VersionDisponible = "V-99.00",
                ActualizacionDisponible = true,
                Mensaje = "Actualizacion disponible"
            },
            StartAccepted = true
        };
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(new DateTime(2026, 5, 17, 4, 0, 0, DateTimeKind.Utc)),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(1);
        service.StartCalls.Should().Be(1);
        db.Configuraciones.Single(x => x.Clave == "app_update_auto_last_result").Valor
            .Should().Contain("iniciada");
        db.Configuraciones.Single(x => x.Clave == "app_update_auto_last_started_utc").Valor
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Check_When_Auto_Update_Is_Disabled()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(CreateConfig("app_update_auto_enabled", "false"));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService();
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(new DateTime(2026, 5, 17, 4, 0, 0, DateTimeKind.Utc)),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(0);
        service.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Check_Only_Once_Per_Utc_Day()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            CreateConfig("app_update_auto_enabled", "true"),
            CreateConfig("app_update_auto_hour_utc", "3"),
            CreateConfig("app_update_auto_last_checked_utc", "2026-05-17T03:20:00.0000000Z"));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService();
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(new DateTime(2026, 5, 17, 8, 0, 0, DateTimeKind.Utc)),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(0);
        service.StartCalls.Should().Be(0);
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static Configuracion CreateConfig(string key, string value) =>
        new()
        {
            Clave = key,
            Valor = value
        };

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }

    private sealed class FakeActualizacionService : IActualizacionService
    {
        public int CheckCalls { get; private set; }
        public int StartCalls { get; private set; }
        public bool StartAccepted { get; init; }
        public VersionDisponibleResponse AvailableResponse { get; init; } = new()
        {
            VersionActual = "V-01.09",
            ActualizacionDisponible = false,
            Mensaje = "Sin actualizacion disponible."
        };

        public Task<VersionActualResponse> GetVersionActualAsync(CancellationToken cancellationToken)
            => Task.FromResult(new VersionActualResponse { VersionActual = "V-01.09" });

        public Task<VersionDisponibleResponse> CheckVersionDisponibleAsync(CancellationToken cancellationToken)
        {
            CheckCalls++;
            return Task.FromResult(AvailableResponse);
        }

        public Task<bool> IniciarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.FromResult(StartAccepted);
        }
    }
}
