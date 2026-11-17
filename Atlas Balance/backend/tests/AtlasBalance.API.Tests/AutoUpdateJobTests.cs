using System.Globalization;
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
            // J-6: la clave se conserva en la BD, pero ya no gobierna el job.
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
                Instalable = true,
                Mensaje = "Actualizacion disponible"
            },
            StartAccepted = true
        };
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(2, 0))),
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
            new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(2, 0))),
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
            // J-6: el dia se compara en hora LOCAL, no en UTC.
            CreateConfig(
                "app_update_auto_last_checked_utc",
                UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(1, 30))
                    .ToString("O", CultureInfo.InvariantCulture)));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService();
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(2, 0))),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(0);
        service.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Do_Anything_When_Local_Time_Is_Outside_The_Window()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            CreateConfig("app_update_auto_enabled", "true"),
            CreateConfig("app_update_auto_last_checked_utc", ""));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService();
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(11, 0))),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(0);
        service.StartCalls.Should().Be(0);
        db.Configuraciones.Single(x => x.Clave == "app_update_auto_last_checked_utc").Valor
            .Should().BeEmpty();
        db.Configuraciones.Should().NotContain(x => x.Clave == "app_update_auto_last_result");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Check_Version_When_Local_Time_Is_Inside_The_Window()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(CreateConfig("app_update_auto_enabled", "true"));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService();
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(2, 0))),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Use_The_Configured_Local_Window()
    {
        // 02:30 local: fuera de la ventana configurada 03:00-04:00.
        await using (var db = BuildDbContext())
        {
            db.Configuraciones.AddRange(
                CreateConfig("app_update_auto_enabled", "true"),
                CreateConfig("app_update_auto_window_start_local", "03:00"),
                CreateConfig("app_update_auto_window_end_local", "04:00"));
            await db.SaveChangesAsync();

            var service = new FakeActualizacionService();
            var job = new AutoUpdateJob(
                db,
                service,
                new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(2, 30))),
                NullLogger<AutoUpdateJob>.Instance);

            await job.ExecuteAsync();

            service.CheckCalls.Should().Be(0);
        }

        // 03:30 local: dentro de la ventana configurada 03:00-04:00.
        await using (var db = BuildDbContext())
        {
            db.Configuraciones.AddRange(
                CreateConfig("app_update_auto_enabled", "true"),
                CreateConfig("app_update_auto_window_start_local", "03:00"),
                CreateConfig("app_update_auto_window_end_local", "04:00"));
            await db.SaveChangesAsync();

            var service = new FakeActualizacionService();
            var job = new AutoUpdateJob(
                db,
                service,
                new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(3, 30))),
                NullLogger<AutoUpdateJob>.Instance);

            await job.ExecuteAsync();

            service.CheckCalls.Should().Be(1);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_Use_The_Default_Window_When_Configuration_Is_Missing_Or_Invalid()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            CreateConfig("app_update_auto_enabled", "true"),
            CreateConfig("app_update_auto_window_start_local", "no-es-una-hora"),
            CreateConfig("app_update_auto_window_end_local", ""));
        await db.SaveChangesAsync();

        var service = new FakeActualizacionService();
        var job = new AutoUpdateJob(
            db,
            service,
            new FakeClock(UtcAtLocalTime(new DateTime(2026, 5, 17), new TimeOnly(2, 0))),
            NullLogger<AutoUpdateJob>.Instance);

        await job.ExecuteAsync();

        service.CheckCalls.Should().Be(1);
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

    // Instante UTC cuya hora local es la pedida, para no depender de la zona horaria del equipo.
    private static DateTime UtcAtLocalTime(DateTime localDate, TimeOnly localTime)
    {
        var local = DateTime.SpecifyKind(
            localDate.Date + localTime.ToTimeSpan(),
            DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

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
