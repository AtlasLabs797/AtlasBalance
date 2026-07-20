using AtlasBalance.API.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void V0205AndV0206Migrations_Should_Be_Discoverable_By_EfCore()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=migration_discovery;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new AppDbContext(options);

        // V-02-06: incluimos la migracion de RLS hardening financiero y las
        // de soft delete / CHECK constraints de IaUsoUsuario e importacion
        // para garantizar que se descubren al ejecutar db.Database.Migrate()
        // desde una base vacia.
        db.Database.GetMigrations().Should().Contain([
            "20260710090000_RecreateUniqueIndexesWithSoftDeleteFilter",
            "20260710091000_AddConciliacionSoftDeleteAndEstadoCheck",
            "20260710092000_AddSoftDeleteToImportacionFilaColumnaExtraRevision",
            "20260716120000_HardenFinancialV0202Rls",
            "20260716123000_AddIaUsoUsuarioSoftDelete",
            "20260716124000_AddEstadoCheckConstraintsToImportacionYBackup",
            "20260720090000_AlignConciliacionEstadosAndSnapshot",
            "20260720120000_RedactHistoricalConfigurationAudits",
            "20260720130000_AddBackupOperations",
            "20260720140000_AddImportacionIdempotency"
        ]);
    }
}
