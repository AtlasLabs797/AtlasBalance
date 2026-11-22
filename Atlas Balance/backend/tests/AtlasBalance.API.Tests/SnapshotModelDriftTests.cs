using AtlasBalance.API.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// M-9: el modelo de EF y el snapshot de migraciones deben coincidir.
///
/// La comprobacion es la misma que hace EF dentro de <c>Migrate()</c> al
/// emitir <c>RelationalEventId.PendingModelChangesWarning</c>: comparar el
/// modelo del snapshot con el modelo actual mediante
/// <see cref="IMigrationsModelDiffer"/>. El modelo del snapshot se devuelve
/// finalizado pero sin el inicializador de runtime aplicado (por eso no tiene
/// modelo relacional): se inicializa aqui igual que hace EF.
///
/// La deriva historica se cerro en V-03.02 con la migracion
/// 20261122223119_CloseM9SnapshotDrift, y <c>AppDbContext</c> ya no ignora el
/// warning. Si este test falla, el modelo cambio sin migracion (o el snapshot
/// se edito a mano mal): Migrate() fallaria en todas las instalaciones.
/// </summary>
public sealed class SnapshotModelDriftTests
{
    [Fact]
    public void Modelo_Y_Snapshot_No_Deben_Tener_Diferencias()
    {
        var detalladas = CalcularDiferencias().Select(Describir).ToList();

        detalladas.Should().BeEmpty(
            "el snapshot debe reflejar el modelo; diferencias ({0}): {1}",
            detalladas.Count,
            string.Join(Environment.NewLine, detalladas));
    }

    private static List<MigrationOperation> CalcularDiferencias()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=drift_check;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new AppDbContext(options);

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        snapshot.Should().NotBeNull("el proyecto debe tener AppDbContextModelSnapshot");

        var modeloSnapshot = snapshot!.Model;
        var modeloActual = db.GetService<IDesignTimeModel>().Model;
        var differ = db.GetService<IMigrationsModelDiffer>();

        var inicializado = db.GetService<IModelRuntimeInitializer>()
            .Initialize(modeloSnapshot, designTime: true);

        return differ
            .GetDifferences(inicializado.GetRelationalModel(), modeloActual.GetRelationalModel())
            .ToList();
    }

    private static string Describir(MigrationOperation operacion) => operacion switch
    {
        AddColumnOperation o => $"AddColumn {o.Table}.{o.Name} ({o.ColumnType})",
        DropColumnOperation o => $"DropColumn {o.Table}.{o.Name}",
        CreateIndexOperation o =>
            $"CreateIndex {o.Name} on {o.Table} ({string.Join(", ", o.Columns)}) unique={o.IsUnique} filter={o.Filter}",
        DropIndexOperation o => $"DropIndex {o.Name} on {o.Table}",
        AddForeignKeyOperation o => $"AddForeignKey {o.Name} on {o.Table} -> {o.PrincipalTable}",
        DropForeignKeyOperation o => $"DropForeignKey {o.Name} on {o.Table}",
        _ => operacion.GetType().Name
    };
}
