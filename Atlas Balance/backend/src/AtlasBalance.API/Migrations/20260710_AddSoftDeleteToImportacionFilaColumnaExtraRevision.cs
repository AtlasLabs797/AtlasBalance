using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260710092000_AddSoftDeleteToImportacionFilaColumnaExtraRevision")]
    public partial class AddSoftDeleteToImportacionFilaColumnaExtraRevision : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V-02-05 (MED-22): ISoftDelete explicito en las entidades restantes.
            // El soft delete global ya existe via ApplySoftDeleteQueryFilters
            // pero las columnas fisicas deleted_at / deleted_by_id solo estaban
            // en entidades con la interfaz implementada.

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTE_FILAS"
                ADD COLUMN IF NOT EXISTS "deleted_at" timestamp NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTE_FILAS"
                ADD COLUMN IF NOT EXISTS "deleted_by_id" uuid NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_importacion_lote_filas_deleted_at"
                ON "IMPORTACION_LOTE_FILAS" ("deleted_at");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_importacion_lote_filas_lote_id_indice";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_importacion_lote_filas_lote_id_indice"
                ON "IMPORTACION_LOTE_FILAS" ("lote_id", "indice")
                WHERE "deleted_at" IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA"
                ADD COLUMN IF NOT EXISTS "deleted_at" timestamp NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA"
                ADD COLUMN IF NOT EXISTS "deleted_by_id" uuid NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_extratos_columnas_extra_deleted_at"
                ON "EXTRACTOS_COLUMNAS_EXTRA" ("deleted_at");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_extratos_columnas_extra_extracto_id_nombre_columna";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_extratos_columnas_extra_extracto_id_nombre_columna"
                ON "EXTRACTOS_COLUMNAS_EXTRA" ("extracto_id", "nombre_columna")
                WHERE "deleted_at" IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                ADD COLUMN IF NOT EXISTS "deleted_at" timestamp NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                ADD COLUMN IF NOT EXISTS "deleted_by_id" uuid NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_revision_extracto_estados_deleted_at"
                ON "REVISION_EXTRACTO_ESTADOS" ("deleted_at");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_revision_extracto_estados_extracto_id_tipo";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_revision_extracto_estados_extracto_id_tipo"
                ON "REVISION_EXTRACTO_ESTADOS" ("extracto_id", "tipo")
                WHERE "deleted_at" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_revision_extracto_estados_extracto_id_tipo";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_revision_extracto_estados_extracto_id_tipo"
                ON "REVISION_EXTRACTO_ESTADOS" ("extracto_id", "tipo");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_revision_extracto_estados_deleted_at";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                DROP COLUMN IF EXISTS "deleted_by_id";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS"
                DROP COLUMN IF EXISTS "deleted_at";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_extratos_columnas_extra_extracto_id_nombre_columna";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_extratos_columnas_extra_extracto_id_nombre_columna"
                ON "EXTRACTOS_COLUMNAS_EXTRA" ("extracto_id", "nombre_columna");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_extratos_columnas_extra_deleted_at";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA"
                DROP COLUMN IF EXISTS "deleted_by_id";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA"
                DROP COLUMN IF EXISTS "deleted_at";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_importacion_lote_filas_lote_id_indice";
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_importacion_lote_filas_lote_id_indice"
                ON "IMPORTACION_LOTE_FILAS" ("lote_id", "indice");
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_importacion_lote_filas_deleted_at";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTE_FILAS"
                DROP COLUMN IF EXISTS "deleted_by_id";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTE_FILAS"
                DROP COLUMN IF EXISTS "deleted_at";
                """);
        }
    }
}
