using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260608120000_AddPaisAndMfaTrustedDevices")]
public partial class AddPaisAndMfaTrustedDevices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PAISES",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                nombre = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                codigo_iso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_paises", x => x.id);
                table.ForeignKey(
                    name: "fk_paises_usuarios_deleted_by_id",
                    column: x => x.deleted_by_id,
                    principalTable: "USUARIOS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MFA_TRUSTED_DEVICES",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                security_stamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                user_agent_summary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ip_address_summary = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_mfa_trusted_devices", x => x.id);
                table.ForeignKey(
                    name: "fk_mfa_trusted_devices_usuarios_deleted_by_id",
                    column: x => x.deleted_by_id,
                    principalTable: "USUARIOS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_mfa_trusted_devices_usuarios_usuario_id",
                    column: x => x.usuario_id,
                    principalTable: "USUARIOS",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<Guid>(
            name: "pais_id",
            table: "CUENTAS",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_cuentas_pais_id",
            table: "CUENTAS",
            column: "pais_id");

        migrationBuilder.CreateIndex(
            name: "ix_paises_activo",
            table: "PAISES",
            column: "activo");

        migrationBuilder.CreateIndex(
            name: "ix_paises_codigo_iso2",
            table: "PAISES",
            column: "codigo_iso2",
            unique: true,
            filter: "\"codigo_iso2\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_paises_deleted_at",
            table: "PAISES",
            column: "deleted_at");

        migrationBuilder.CreateIndex(
            name: "ix_paises_deleted_by_id",
            table: "PAISES",
            column: "deleted_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_paises_nombre",
            table: "PAISES",
            column: "nombre",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_mfa_trusted_devices_deleted_at",
            table: "MFA_TRUSTED_DEVICES",
            column: "deleted_at");

        migrationBuilder.CreateIndex(
            name: "ix_mfa_trusted_devices_deleted_by_id",
            table: "MFA_TRUSTED_DEVICES",
            column: "deleted_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_mfa_trusted_devices_expires_at",
            table: "MFA_TRUSTED_DEVICES",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ix_mfa_trusted_devices_revoked_at",
            table: "MFA_TRUSTED_DEVICES",
            column: "revoked_at");

        migrationBuilder.CreateIndex(
            name: "ix_mfa_trusted_devices_token_hash",
            table: "MFA_TRUSTED_DEVICES",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_mfa_trusted_devices_usuario_id",
            table: "MFA_TRUSTED_DEVICES",
            column: "usuario_id");

        migrationBuilder.AddForeignKey(
            name: "fk_cuentas_paises_pais_id",
            table: "CUENTAS",
            column: "pais_id",
            principalTable: "PAISES",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(
            """
            ALTER TABLE "PAISES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "PAISES" FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS paises_select ON "PAISES";
            CREATE POLICY paises_select ON "PAISES"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (deleted_at IS NULL AND activo = true AND atlas_security.is_user_mode())
                );

            DROP POLICY IF EXISTS paises_write ON "PAISES";
            CREATE POLICY paises_write ON "PAISES"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            ALTER TABLE "MFA_TRUSTED_DEVICES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "MFA_TRUSTED_DEVICES" FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS mfa_trusted_devices_access ON "MFA_TRUSTED_DEVICES";
            CREATE POLICY mfa_trusted_devices_access ON "MFA_TRUSTED_DEVICES"
                FOR ALL USING (
                    atlas_security.is_admin_or_system()
                    OR atlas_security.is_auth_flow()
                    OR (
                        deleted_at IS NULL
                        AND atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                )
                WITH CHECK (
                    atlas_security.is_admin_or_system()
                    OR atlas_security.is_auth_flow()
                    OR (
                        deleted_at IS NULL
                        AND atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS mfa_trusted_devices_access ON "MFA_TRUSTED_DEVICES";
            DROP POLICY IF EXISTS paises_write ON "PAISES";
            DROP POLICY IF EXISTS paises_select ON "PAISES";
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_cuentas_paises_pais_id",
            table: "CUENTAS");

        migrationBuilder.DropTable(name: "MFA_TRUSTED_DEVICES");
        migrationBuilder.DropTable(name: "PAISES");

        migrationBuilder.DropIndex(
            name: "ix_cuentas_pais_id",
            table: "CUENTAS");

        migrationBuilder.DropColumn(
            name: "pais_id",
            table: "CUENTAS");
    }
}
