using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720130000_AddBackupOperations")]
public sealed class AddBackupOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "BACKUP_OPERATIONS" (
                "id" uuid PRIMARY KEY,
                "tipo" varchar(32) NOT NULL,
                "estado" varchar(16) NOT NULL,
                "usuario_id" uuid NULL,
                "backup_id" uuid NULL,
                "parametro" text NULL,
                "resultado_json" jsonb NULL,
                "error" text NULL,
                "fecha_creacion" timestamptz NOT NULL,
                "fecha_inicio" timestamptz NULL,
                "fecha_fin" timestamptz NULL,
                "deleted_at" timestamptz NULL,
                "deleted_by_id" uuid NULL,
                CONSTRAINT "ck_backup_operations_estado" CHECK ("estado" IN ('PENDING','RUNNING','SUCCESS','FAILED')),
                CONSTRAINT "ck_backup_operations_tipo" CHECK ("tipo" IN ('MANUAL','DRIVE_IMPORT','RESTORE')),
                CONSTRAINT "fk_backup_operations_usuario_id" FOREIGN KEY ("usuario_id") REFERENCES "USUARIOS"("id") ON DELETE SET NULL,
                CONSTRAINT "fk_backup_operations_backup_id" FOREIGN KEY ("backup_id") REFERENCES "BACKUPS"("id") ON DELETE SET NULL,
                CONSTRAINT "fk_backup_operations_deleted_by_id" FOREIGN KEY ("deleted_by_id") REFERENCES "USUARIOS"("id") ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "ix_backup_operations_estado" ON "BACKUP_OPERATIONS"("estado");
            CREATE INDEX IF NOT EXISTS "ix_backup_operations_backup_id" ON "BACKUP_OPERATIONS"("backup_id");
            CREATE INDEX IF NOT EXISTS "ix_backup_operations_usuario_id_fecha_creacion" ON "BACKUP_OPERATIONS"("usuario_id", "fecha_creacion");
            CREATE INDEX IF NOT EXISTS "ix_backup_operations_deleted_by_id" ON "BACKUP_OPERATIONS"("deleted_by_id");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"BACKUP_OPERATIONS\";");
    }
}
