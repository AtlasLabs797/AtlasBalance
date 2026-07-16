using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02-06 (BACKUP-01): tests no-Docker de la resolucion owner en BackupService.
// El servicio debe negarse a usar el rol runtime para pg_dump porque FORCE
// RLS filtra filas; sin owner no hay backup completo valido.
public sealed class BackupServiceOwnerResolutionTests
{
    [Fact]
    public void ResolveDumpConnection_WithoutOwnerAndWithoutWatchdog_ShouldReturnNonOwner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=atlas;Username=app_user;Password=app"
            })
            .Build();

        var service = new BackupService(
            dbContext: null!,
            configuration: configuration,
            auditService: null!,
            googleDriveBackupService: null!,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupService>.Instance);

        var result = service.ResolveDumpConnection();

        result.IsOwner.Should().BeFalse();
        result.Source.Should().Be("DefaultConnection");
        result.Connection.User.Should().Be("app_user");
    }

    [Fact]
    public void ResolveDumpConnection_WithMigrationConnection_ShouldReturnOwner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=atlas;Username=app_user;Password=app",
                ["ConnectionStrings:MigrationConnection"] =
                    "Host=localhost;Port=5432;Database=atlas;Username=atlas_balance_owner;Password=owner"
            })
            .Build();

        var service = new BackupService(
            dbContext: null!,
            configuration: configuration,
            auditService: null!,
            googleDriveBackupService: null!,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupService>.Instance);

        var result = service.ResolveDumpConnection();

        result.IsOwner.Should().BeTrue();
        result.Source.Should().Be("MigrationConnection");
        result.Connection.User.Should().Be("atlas_balance_owner");
    }

    [Fact]
    public void ResolveDumpConnection_WithWatchdogOwnerCredentials_ShouldReturnOwner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=remotehost;Port=5432;Database=atlas;Username=app_user;Password=app",
                ["WatchdogSettings:DbOwnerUser"] = "atlas_balance_owner",
                ["WatchdogSettings:DbOwnerPassword"] = "owner"
            })
            .Build();

        var service = new BackupService(
            dbContext: null!,
            configuration: configuration,
            auditService: null!,
            googleDriveBackupService: null!,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupService>.Instance);

        var result = service.ResolveDumpConnection();

        result.IsOwner.Should().BeTrue();
        result.Source.Should().Be("WatchdogSettings.DbOwner*");
        result.Connection.User.Should().Be("atlas_balance_owner");
        result.Connection.Host.Should().Be("remotehost");
    }
}
