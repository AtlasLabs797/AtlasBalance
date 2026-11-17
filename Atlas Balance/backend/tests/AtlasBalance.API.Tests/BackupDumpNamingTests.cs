using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

// A-04: los backups automaticos del mismo dia escribian todos en
// `backup_yyyy-MM-dd.dump`. Con frecuencia horaria caben varios el mismo dia
// y el segundo volcado a medias destruia el primero. El nombre lleva siempre
// la hora, tanto en automatico como en manual (un unico patron).
public sealed class BackupDumpNamingTests
{
    [Fact]
    public void BuildDumpFileName_Should_Include_The_Hour_So_Two_Auto_Backups_The_Same_Day_Differ()
    {
        var morning = new DateTime(2026, 11, 16, 2, 15, 0, DateTimeKind.Utc);
        var evening = new DateTime(2026, 11, 16, 17, 45, 0, DateTimeKind.Utc);

        var morningName = BackupService.BuildDumpFileName(morning);
        var eveningName = BackupService.BuildDumpFileName(evening);

        morningName.Should().NotBe(eveningName);
        morningName.Should().Be("backup_2026-11-16_021500.dump");
        eveningName.Should().Be("backup_2026-11-16_174500.dump");
    }

    [Fact]
    public void BuildDumpFileName_Should_Keep_The_Hour_At_Every_Hour_Of_The_Day()
    {
        BackupService.BuildDumpFileName(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be("backup_2026-11-16_000000.dump");
        BackupService.BuildDumpFileName(new DateTime(2026, 11, 16, 23, 59, 59, DateTimeKind.Utc))
            .Should().Be("backup_2026-11-16_235959.dump");
    }
}
