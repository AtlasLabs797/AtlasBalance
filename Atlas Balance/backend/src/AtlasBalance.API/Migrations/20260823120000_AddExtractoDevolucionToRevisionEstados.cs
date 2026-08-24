using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260823120000_AddExtractoDevolucionToRevisionEstados")]
    public partial class AddExtractoDevolucionToRevisionEstados : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V-02.08: devolucion automatica de comisiones. La columna guarda el
            // extracto positivo (bonificacion) emparejado con una comision al
            // verificarla en la seccion Revision.
            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                ADD COLUMN IF NOT EXISTS "extracto_devolucion_id" uuid NULL;
                """);

            // Indice unico parcial: un abono solo puede estar emparejado con una
            // comision activa; es la garantia contra la carrera de dos usuarios
            // verificando el mismo abono a la vez.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "ix_revision_extracto_estados_extracto_devolucion_id"
                ON "REVISION_EXTRACTO_ESTADOS" ("extracto_devolucion_id")
                WHERE "deleted_at" IS NULL AND "extracto_devolucion_id" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_revision_extracto_estados_extractos_extracto_devolucion_id'
                    ) THEN
                        ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                        ADD CONSTRAINT "fk_revision_extracto_estados_extractos_extracto_devolucion_id"
                        FOREIGN KEY ("extracto_devolucion_id")
                        REFERENCES "EXTRACTOS" ("id") ON DELETE RESTRICT;
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                DROP CONSTRAINT IF EXISTS "fk_revision_extracto_estados_extractos_extracto_devolucion_id";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_revision_extracto_estados_extracto_devolucion_id";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                DROP COLUMN IF EXISTS "extracto_devolucion_id";
                """);
        }
    }
}
