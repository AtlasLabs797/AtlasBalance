using System;
using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260622120000_AddBackupSchedulingAndGoogleDrive")]
public partial class AddBackupSchedulingAndGoogleDrive : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BACKUP_CLOUD_CONNECTIONS",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                provider = table.Column<string>(type: "text", nullable: false),
                estado = table.Column<string>(type: "text", nullable: false),
                account_email = table.Column<string>(type: "text", nullable: true),
                scope = table.Column<string>(type: "text", nullable: false),
                refresh_token = table.Column<string>(type: "text", nullable: false),
                connected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_validated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "text", nullable: true),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backup_cloud_connections", x => x.id);
                table.ForeignKey(
                    name: "fk_backup_cloud_connections_usuarios_deleted_by_id",
                    column: x => x.deleted_by_id,
                    principalTable: "USUARIOS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "BACKUP_CLOUD_COPIES",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                backup_id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                provider = table.Column<string>(type: "text", nullable: false),
                estado = table.Column<string>(type: "text", nullable: false),
                remote_file_id = table.Column<string>(type: "text", nullable: true),
                remote_file_name = table.Column<string>(type: "text", nullable: true),
                remote_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                checksum_sha256 = table.Column<string>(type: "text", nullable: true),
                fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error_code = table.Column<string>(type: "text", nullable: true),
                error_message = table.Column<string>(type: "text", nullable: true),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backup_cloud_copies", x => x.id);
                table.ForeignKey(
                    name: "fk_backup_cloud_copies_backup_cloud_connections_connection_id",
                    column: x => x.connection_id,
                    principalTable: "BACKUP_CLOUD_CONNECTIONS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_backup_cloud_copies_backups_backup_id",
                    column: x => x.backup_id,
                    principalTable: "BACKUPS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_backup_cloud_copies_usuarios_deleted_by_id",
                    column: x => x.deleted_by_id,
                    principalTable: "USUARIOS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_backup_cloud_connections_deleted_by_id",
            table: "BACKUP_CLOUD_CONNECTIONS",
            column: "deleted_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_backup_cloud_connections_provider_deleted_at",
            table: "BACKUP_CLOUD_CONNECTIONS",
            columns: new[] { "provider", "deleted_at" });

        migrationBuilder.CreateIndex(
            name: "ix_backup_cloud_copies_backup_id",
            table: "BACKUP_CLOUD_COPIES",
            column: "backup_id");

        migrationBuilder.CreateIndex(
            name: "ix_backup_cloud_copies_connection_id",
            table: "BACKUP_CLOUD_COPIES",
            column: "connection_id");

        migrationBuilder.CreateIndex(
            name: "ix_backup_cloud_copies_deleted_by_id",
            table: "BACKUP_CLOUD_COPIES",
            column: "deleted_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_backup_cloud_copies_provider_estado",
            table: "BACKUP_CLOUD_COPIES",
            columns: new[] { "provider", "estado" });

        migrationBuilder.Sql(
            """
            INSERT INTO "CONFIGURACION" ("clave", "valor", "tipo", "descripcion", "fecha_modificacion", "usuario_modificacion_id")
            VALUES
                ('backup_auto_enabled', 'true', 'bool', 'Activa copias de seguridad automaticas', NOW(), NULL),
                ('backup_auto_frequency', 'WEEKLY', 'string', 'Frecuencia de copias automaticas: HOURLY, DAILY, WEEKLY o MONTHLY', NOW(), NULL),
                ('backup_auto_time_utc', '02:00', 'string', 'Hora UTC para copias diarias, semanales o mensuales', NOW(), NULL),
                ('backup_auto_day_of_week', '0', 'int', 'Dia semanal UTC: 0 domingo, 6 sabado', NOW(), NULL),
                ('backup_auto_day_of_month', '1', 'int', 'Dia mensual UTC para copias automaticas', NOW(), NULL),
                ('backup_auto_interval_hours', '24', 'int', 'Intervalo en horas para copias automaticas por horas', NOW(), NULL),
                ('backup_auto_last_started_utc', '', 'string', 'Ultima copia automatica iniciada en UTC', NOW(), NULL),
                ('backup_auto_last_result', '', 'string', 'Ultimo resultado de copia automatica', NOW(), NULL),
                ('backup_destination', 'LOCAL', 'string', 'Destino de copia: LOCAL o LOCAL_Y_GOOGLE_DRIVE', NOW(), NULL),
                ('google_drive_oauth_client_id', '', 'string', 'OAuth Client ID para copias de seguridad en Google Drive', NOW(), NULL),
                ('google_drive_oauth_client_secret', '', 'string', 'OAuth Client Secret protegido para Google Drive', NOW(), NULL),
                ('google_drive_folder_id', '', 'string', 'Carpeta destino de Google Drive; vacio crea carpeta Atlas Balance Backups', NOW(), NULL),
                ('backup_cloud_encryption_key', '', 'string', 'Clave simetrica protegida para cifrar copias subidas a la nube', NOW(), NULL)
            ON CONFLICT ("clave") DO NOTHING;

            ALTER TABLE "BACKUP_CLOUD_CONNECTIONS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUP_CLOUD_CONNECTIONS" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS backup_cloud_connections_admin ON "BACKUP_CLOUD_CONNECTIONS";
            CREATE POLICY backup_cloud_connections_admin ON "BACKUP_CLOUD_CONNECTIONS"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            ALTER TABLE "BACKUP_CLOUD_COPIES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUP_CLOUD_COPIES" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS backup_cloud_copies_admin ON "BACKUP_CLOUD_COPIES";
            CREATE POLICY backup_cloud_copies_admin ON "BACKUP_CLOUD_COPIES"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS backup_cloud_copies_admin ON "BACKUP_CLOUD_COPIES";
            ALTER TABLE "BACKUP_CLOUD_COPIES" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUP_CLOUD_COPIES" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS backup_cloud_connections_admin ON "BACKUP_CLOUD_CONNECTIONS";
            ALTER TABLE "BACKUP_CLOUD_CONNECTIONS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUP_CLOUD_CONNECTIONS" DISABLE ROW LEVEL SECURITY;

            DELETE FROM "CONFIGURACION"
            WHERE "clave" IN (
                'backup_auto_enabled',
                'backup_auto_frequency',
                'backup_auto_time_utc',
                'backup_auto_day_of_week',
                'backup_auto_day_of_month',
                'backup_auto_interval_hours',
                'backup_auto_last_started_utc',
                'backup_auto_last_result',
                'backup_destination',
                'google_drive_oauth_client_id',
                'google_drive_oauth_client_secret',
                'google_drive_folder_id',
                'backup_cloud_encryption_key'
            );
            """);

        migrationBuilder.DropTable(name: "BACKUP_CLOUD_COPIES");
        migrationBuilder.DropTable(name: "BACKUP_CLOUD_CONNECTIONS");
    }
}
