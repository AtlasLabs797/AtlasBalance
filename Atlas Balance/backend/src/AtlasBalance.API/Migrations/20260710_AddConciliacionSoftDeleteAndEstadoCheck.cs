using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    public partial class AddConciliacionSoftDeleteAndEstadoCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V-02-05 (MED-22): soft delete explicito en CONCILIACIONES.
            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                ADD COLUMN IF NOT EXISTS "deleted_at" timestamp NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                ADD COLUMN IF NOT EXISTS "deleted_by_id" uuid NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_conciliaciones_deleted_at"
                ON "CONCILIACIONES" ("deleted_at");
                """);

            // V-02-05 (MED-22): el UNIQUE (movimiento_esperado_id, extracto_id) ahora
            // considera solo conciliaciones activas para evitar re-crear tras soft-delete.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_conciliaciones_movimiento_esperado_id_extracto_id";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_conciliaciones_movimiento_esperado_id_extracto_id"
                ON "CONCILIACIONES" ("movimiento_esperado_id", "extracto_id")
                WHERE "deleted_at" IS NULL;
                """);

            // V-02-05 (MED-21): CHECK constraints.
            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                DROP CONSTRAINT IF EXISTS "ck_conciliaciones_estado";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                ADD CONSTRAINT "ck_conciliaciones_estado"
                CHECK ("estado" IN ('sugerida','conciliada','descartada','cerrada'));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                DROP CONSTRAINT IF EXISTS "ck_movimientos_esperados_estado";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                ADD CONSTRAINT "ck_movimientos_esperados_estado"
                CHECK ("estado" IN ('pendiente','satisfecho','vencido','cancelado'));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                DROP CONSTRAINT IF EXISTS "ck_movimientos_esperados_estado";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                DROP CONSTRAINT IF EXISTS "ck_conciliaciones_estado";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_conciliaciones_movimiento_esperado_id_extracto_id";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_conciliaciones_movimiento_esperado_id_extracto_id"
                ON "CONCILIACIONES" ("movimiento_esperado_id", "extracto_id");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_conciliaciones_deleted_at";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                DROP COLUMN IF EXISTS "deleted_by_id";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CONCILIACIONES"
                DROP COLUMN IF EXISTS "deleted_at";
                """);
        }
    }
}
