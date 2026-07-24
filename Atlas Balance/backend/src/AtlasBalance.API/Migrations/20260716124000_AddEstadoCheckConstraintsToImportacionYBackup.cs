using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260716124000_AddEstadoCheckConstraintsToImportacionYBackup")]
    public partial class AddEstadoCheckConstraintsToImportacionYBackup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V-02.06 (MED-21): extender la familia de CHECK constraints sobre
            // la columna estado a las dos tablas que quedaron fuera en V-02-05
            // (IMPORTACION_LOTES y BACKUP_CLOUD_CONNECTIONS). Los valores
            // admitidos aqui coinciden al 100% con los que el codigo escribe
            // de verdad en runtime; cualquier valor fuera de la lista deberia
            // ser 500 silencioso, no un row corrupto que sobreviva al backup.

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTES"
                DROP CONSTRAINT IF EXISTS "ck_importacion_lotes_estado";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTES"
                ADD CONSTRAINT "ck_importacion_lotes_estado"
                CHECK ("estado" IN ('validado','validado_con_errores','confirmado','revertido','error'));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "BACKUP_CLOUD_CONNECTIONS"
                DROP CONSTRAINT IF EXISTS "ck_backup_cloud_connections_estado";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "BACKUP_CLOUD_CONNECTIONS"
                ADD CONSTRAINT "ck_backup_cloud_connections_estado"
                CHECK ("estado" IN ('CONNECTED','PENDING','DISCONNECTED','REPLACED'));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "BACKUP_CLOUD_CONNECTIONS"
                DROP CONSTRAINT IF EXISTS "ck_backup_cloud_connections_estado";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "IMPORTACION_LOTES"
                DROP CONSTRAINT IF EXISTS "ck_importacion_lotes_estado";
                """);
        }
    }
}
