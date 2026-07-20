using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720140000_AddImportacionIdempotency")]
public sealed class AddImportacionIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "IMPORTACION_LOTES" ADD COLUMN IF NOT EXISTS "idempotency_key" varchar(128) NULL;
            ALTER TABLE "IMPORTACION_LOTES" ADD COLUMN IF NOT EXISTS "confirmacion_idempotency_key" varchar(128) NULL;
            ALTER TABLE "IMPORTACION_LOTES" ADD COLUMN IF NOT EXISTS "confirmacion_response_json" jsonb NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_importacion_lotes_usuario_creador_id_idempotency_key"
                ON "IMPORTACION_LOTES"("usuario_creador_id", "idempotency_key")
                WHERE "idempotency_key" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "ix_importacion_lotes_usuario_creador_id_idempotency_key";
            ALTER TABLE "IMPORTACION_LOTES" DROP COLUMN IF EXISTS "confirmacion_response_json";
            ALTER TABLE "IMPORTACION_LOTES" DROP COLUMN IF EXISTS "confirmacion_idempotency_key";
            ALTER TABLE "IMPORTACION_LOTES" DROP COLUMN IF EXISTS "idempotency_key";
            """);
    }
}
