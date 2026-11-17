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
/// M-9: estado de la deriva entre el modelo de EF y el snapshot.
///
/// La comprobacion es la misma que hace EF por dentro al activar
/// <c>RelationalEventId.PendingModelChangesWarning</c>, replicada a mano
/// porque el warning no se puede anular desde las opciones del test:
/// <c>DbContext.OnConfiguring</c> se aplica DESPUES de las
/// <c>DbContextOptions</c> que recibe el contexto, asi que el
/// <c>Ignore(...)</c> de <c>AppDbContext</c> gana siempre. La via
/// alternativa es comparar el modelo con el snapshot mediante
/// <see cref="IMigrationsModelDiffer"/>, que es lo que hace EF.
///
/// El modelo del snapshot se devuelve finalizado pero sin el inicializador
/// de runtime aplicado (por eso no tiene modelo relacional): se inicializa
/// aqui igual que hace EF.
///
/// DERIVA RESTANTE (a fecha de 2026-11-17, 27 operaciones). No es solo de
/// snapshot: la base real difiere del modelo, asi que arreglarlo exige una
/// migracion de esquema, no solo tocar el snapshot:
///   * REVISION_EXTRACTO_ESTADOS, IMPORTACION_LOTE_FILAS y
///     EXTRACTOS_COLUMNAS_EXTRA: el snapshot no tiene deleted_at /
///     deleted_by_id ni sus indices, y la base real tiene deleted_at como
///     <c>timestamp without time zone</c> mientras el modelo pide
///     <c>timestamp with time zone</c> (cambiar el tipo de una columna usada
///     por una policy RLS exige tirar y recrear la policy).
///   * IMPORTACION_LOTES: falta la columna notas en el snapshot; el modelo
///     ya no declara el indice simple ix_importacion_lotes_usuario_creador_id
///     (lo cubre ix_importacion_lotes_usuario_creador_id_idempotency_key).
///   * IA_USO_USUARIOS: el snapshot conserva el indice
///     ix_ia_uso_usuarios_deleted_at, que el modelo ya no declara.
///   * CONCILIACIONES: el nombre de la FK de deleted_by_id en el snapshot
///     (fk_conciliaciones_deleted_by_id) no es el que genera el modelo
///     (fk_conciliaciones_usuarios_deleted_by_id), por eso el differ la
///     ve como FK distinta.
///   * EXTRACTOS_COLUMNAS_EXTRA: el snapshot conserva los indices simples
///     ix_extractos_columnas_extra_extracto_id y
///     ix_extractos_columnas_extra_nombre_columna, que el modelo ya no
///     declara.
/// Mientres exista esa deriva, el Ignore de PendingModelChangesWarning en
/// AppDbContext.OnConfiguring es necesario: si se quita, Migrate() falla.
/// </summary>
public sealed class SnapshotModelDriftTests
{
    [Fact]
    public void Deriva_Restante_No_Debe_Afectar_A_PLAZOS_FIJOS_Ni_A_Los_Indices_Parciales()
    {
        var diferencias = CalcularDiferencias();

        var detalladas = diferencias.Select(Describir).ToList();

        var afectadas = detalladas
            .Where(d =>
                d.Contains("PLAZOS_FIJOS", StringComparison.OrdinalIgnoreCase)
                || d.Contains("ix_extractos_cuenta_id_importacion_fingerprint", StringComparison.OrdinalIgnoreCase)
                || d.Contains("ix_conciliaciones_extracto_id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        afectadas.Should().BeEmpty(
            "no debe haber deriva en PLAZOS_FIJOS (xmin) ni en los indices parciales de EXTRACTOS/CONCILIACIONES. " +
            "Deriva total restante ({0} operaciones): {1}",
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
