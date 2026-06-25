using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public class BackupScheduleTests
{
    [Fact]
    public void WeeklySchedule_Should_Run_When_CurrentOccurrence_Was_Not_Started()
    {
        var schedule = new BackupSchedule(
            AutoEnabled: true,
            Frequency: "WEEKLY",
            TimeUtc: "02:00",
            DayOfWeek: 0,
            DayOfMonth: 1,
            IntervalHours: 24);

        var utcNow = new DateTime(2026, 6, 21, 2, 15, 0, DateTimeKind.Utc);
        var lastStarted = new DateTime(2026, 6, 14, 2, 0, 0, DateTimeKind.Utc);

        schedule.IsDue(utcNow, lastStarted).Should().BeTrue();
    }

    [Fact]
    public void WeeklySchedule_Should_Not_Run_Twice_For_SameOccurrence()
    {
        var schedule = new BackupSchedule(
            AutoEnabled: true,
            Frequency: "WEEKLY",
            TimeUtc: "02:00",
            DayOfWeek: 0,
            DayOfMonth: 1,
            IntervalHours: 24);

        var utcNow = new DateTime(2026, 6, 21, 2, 15, 0, DateTimeKind.Utc);
        var lastStarted = new DateTime(2026, 6, 21, 2, 0, 0, DateTimeKind.Utc);

        schedule.IsDue(utcNow, lastStarted).Should().BeFalse();
    }

    [Fact]
    public void HourlySchedule_Should_Respect_ConfiguredInterval()
    {
        var schedule = new BackupSchedule(
            AutoEnabled: true,
            Frequency: "HOURLY",
            TimeUtc: "02:00",
            DayOfWeek: 0,
            DayOfMonth: 1,
            IntervalHours: 6);

        var utcNow = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        schedule.IsDue(utcNow, utcNow.AddHours(-6)).Should().BeTrue();
        schedule.IsDue(utcNow, utcNow.AddHours(-5).AddMinutes(-59)).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_Should_Reject_Invalid_Frequency()
    {
        var ok = BackupSchedule.TryNormalize(
            enabled: true,
            frequency: "YEARLY",
            timeUtc: "02:00",
            dayOfWeek: 0,
            dayOfMonth: 1,
            intervalHours: 24,
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
