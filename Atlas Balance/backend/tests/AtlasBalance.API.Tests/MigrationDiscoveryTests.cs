using AtlasBalance.API.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void V0205Migrations_Should_Be_Discoverable_By_EfCore()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=migration_discovery;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new AppDbContext(options);

        db.Database.GetMigrations().Should().Contain([
            "20260710090000_RecreateUniqueIndexesWithSoftDeleteFilter",
            "20260710091000_AddConciliacionSoftDeleteAndEstadoCheck",
            "20260710092000_AddSoftDeleteToImportacionFilaColumnaExtraRevision"
        ]);
    }
}
