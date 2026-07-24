using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260716123000_AddIaUsoUsuarioSoftDelete")]
    public partial class AddIaUsoUsuarioSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V-02.06 (MED-22): IaUsoUsuario pasa a implementar ISoftDelete.
            // El query filter global ApplySoftDeleteQueryFilters ya estaba
            // vigente; faltaban las columnas fisicas y el indice. Si la
            // columna ya existe (entornos donde se aplico parcialmente),
            // ADD COLUMN IF NOT EXISTS la vuelve idempotente.

            migrationBuilder.Sql("""
                ALTER TABLE "IA_USO_USUARIOS"
                ADD COLUMN IF NOT EXISTS "deleted_at" timestamp NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IA_USO_USUARIOS"
                ADD COLUMN IF NOT EXISTS "deleted_by_id" uuid NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_ia_uso_usuarios_deleted_at"
                ON "IA_USO_USUARIOS" ("deleted_at");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_ia_uso_usuarios_deleted_at";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IA_USO_USUARIOS"
                DROP COLUMN IF EXISTS "deleted_by_id";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IA_USO_USUARIOS"
                DROP COLUMN IF EXISTS "deleted_at";
                """);
        }
    }
}
