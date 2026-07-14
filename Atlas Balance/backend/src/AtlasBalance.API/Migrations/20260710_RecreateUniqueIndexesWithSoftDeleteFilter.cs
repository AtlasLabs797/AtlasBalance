using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260710090000_RecreateUniqueIndexesWithSoftDeleteFilter")]
    public partial class RecreateUniqueIndexesWithSoftDeleteFilter : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V-02-05 (HIGH-5): los indices UNIQUE sobre (cuenta_id) en PLAZOS_FIJOS
            // y (cuenta_id, fila_numero) en EXTRACTOS impedian reutilizar la misma
            // cuenta/fila tras un soft-delete. Recrearlos como indices UNIQUE
            // parciales con WHERE deleted_at IS NULL resuelve el problema sin
            // perder la garantia de unicidad entre filas activas.

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_plazos_fijos_cuenta_id";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_plazos_fijos_cuenta_id"
                ON "PLAZOS_FIJOS" ("cuenta_id")
                WHERE "deleted_at" IS NULL;
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_extractos_cuenta_id_fila_numero";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_extractos_cuenta_id_fila_numero"
                ON "EXTRACTOS" ("cuenta_id", "fila_numero")
                WHERE "deleted_at" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_plazos_fijos_cuenta_id";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_plazos_fijos_cuenta_id"
                ON "PLAZOS_FIJOS" ("cuenta_id");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_extractos_cuenta_id_fila_numero";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_extractos_cuenta_id_fila_numero"
                ON "EXTRACTOS" ("cuenta_id", "fila_numero");
                """);
        }
    }
}
