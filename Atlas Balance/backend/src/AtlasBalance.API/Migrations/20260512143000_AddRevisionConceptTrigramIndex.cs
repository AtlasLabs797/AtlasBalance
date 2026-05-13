using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

public partial class AddRevisionConceptTrigramIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE INDEX IF NOT EXISTS ix_extractos_concepto_trgm
            ON "EXTRACTOS"
            USING gin (lower(concepto) gin_trgm_ops)
            WHERE concepto IS NOT NULL AND deleted_at IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS ix_extractos_concepto_trgm;
            """);
    }
}
