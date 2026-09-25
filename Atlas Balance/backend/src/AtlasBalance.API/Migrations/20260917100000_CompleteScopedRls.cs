using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260917100000_CompleteScopedRls")]
public partial class CompleteScopedRls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- V-03.01 (RLS-F): el contexto puede leer una alerta solo cuando
            -- aplica a una cuenta que realmente puede leer. Las alertas por
            -- tipo/global no llevan cuenta_id, por lo que se resuelve el scope
            -- a traves de una cuenta activa y un titular activo accesibles.
            CREATE OR REPLACE FUNCTION atlas_security.can_read_alerta_saldo(
                target_cuenta_id uuid,
                target_tipo_titular integer)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR (
                        (atlas_security.is_user_mode() OR atlas_security.is_integration_mode())
                        AND (
                            (
                                target_cuenta_id IS NOT NULL
                                AND atlas_security.can_read_cuenta_by_id(target_cuenta_id)
                            )
                            OR (
                                target_cuenta_id IS NULL
                                AND EXISTS (
                                    SELECT 1
                                    FROM "CUENTAS" c
                                    JOIN "TITULARES" t ON t.id = c.titular_id
                                    WHERE c.deleted_at IS NULL
                                      AND c.activa
                                      AND t.deleted_at IS NULL
                                      AND (
                                          target_tipo_titular IS NULL
                                          OR t.tipo = target_tipo_titular
                                      )
                                      AND atlas_security.can_read_cuenta_by_id(c.id)
                                )
                            )
                        )
                    )
            $$;

            ALTER TABLE "ALERTAS_SALDO" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "ALERTAS_SALDO" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS alertas_saldo_select ON "ALERTAS_SALDO";
            DROP POLICY IF EXISTS alertas_saldo_write ON "ALERTAS_SALDO";
            CREATE POLICY alertas_saldo_select ON "ALERTAS_SALDO"
                FOR SELECT USING (
                    atlas_security.can_read_alerta_saldo(cuenta_id, tipo_titular)
                );
            CREATE POLICY alertas_saldo_write ON "ALERTAS_SALDO"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            ALTER TABLE "ALERTA_DESTINATARIOS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "ALERTA_DESTINATARIOS" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS alerta_destinatarios_select ON "ALERTA_DESTINATARIOS";
            DROP POLICY IF EXISTS alerta_destinatarios_write ON "ALERTA_DESTINATARIOS";
            CREATE POLICY alerta_destinatarios_select ON "ALERTA_DESTINATARIOS"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "ALERTAS_SALDO" a
                        WHERE a.id = alerta_id
                          AND atlas_security.is_user_mode()
                          AND atlas_security.can_read_alerta_saldo(a.cuenta_id, a.tipo_titular)
                    )
                );
            CREATE POLICY alerta_destinatarios_write ON "ALERTA_DESTINATARIOS"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            ALTER TABLE "IA_USO_USUARIOS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "IA_USO_USUARIOS" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS ia_uso_usuarios_select ON "IA_USO_USUARIOS";
            DROP POLICY IF EXISTS ia_uso_usuarios_insert ON "IA_USO_USUARIOS";
            DROP POLICY IF EXISTS ia_uso_usuarios_update ON "IA_USO_USUARIOS";
            DROP POLICY IF EXISTS ia_uso_usuarios_delete ON "IA_USO_USUARIOS";
            CREATE POLICY ia_uso_usuarios_select ON "IA_USO_USUARIOS"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (
                        deleted_at IS NULL
                        AND atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                );
            CREATE POLICY ia_uso_usuarios_insert ON "IA_USO_USUARIOS"
                FOR INSERT WITH CHECK (
                    atlas_security.is_admin_or_system()
                    OR (
                        atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                );
            CREATE POLICY ia_uso_usuarios_update ON "IA_USO_USUARIOS"
                FOR UPDATE USING (
                    atlas_security.is_admin_or_system()
                    OR (
                        deleted_at IS NULL
                        AND atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                )
                WITH CHECK (
                    atlas_security.is_admin_or_system()
                    OR (
                        atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                );
            CREATE POLICY ia_uso_usuarios_delete ON "IA_USO_USUARIOS"
                FOR DELETE USING (atlas_security.is_admin_or_system());

            ALTER TABLE "BACKUP_OPERATIONS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUP_OPERATIONS" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS backup_operations_select ON "BACKUP_OPERATIONS";
            DROP POLICY IF EXISTS backup_operations_insert ON "BACKUP_OPERATIONS";
            DROP POLICY IF EXISTS backup_operations_update ON "BACKUP_OPERATIONS";
            DROP POLICY IF EXISTS backup_operations_delete ON "BACKUP_OPERATIONS";
            CREATE POLICY backup_operations_select ON "BACKUP_OPERATIONS"
                FOR SELECT USING (atlas_security.is_admin_or_system());
            CREATE POLICY backup_operations_insert ON "BACKUP_OPERATIONS"
                FOR INSERT WITH CHECK (atlas_security.is_admin_or_system());
            CREATE POLICY backup_operations_update ON "BACKUP_OPERATIONS"
                FOR UPDATE USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());
            CREATE POLICY backup_operations_delete ON "BACKUP_OPERATIONS"
                FOR DELETE USING (atlas_security.is_admin_or_system());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS alertas_saldo_select ON "ALERTAS_SALDO";
            DROP POLICY IF EXISTS alertas_saldo_write ON "ALERTAS_SALDO";
            ALTER TABLE "ALERTAS_SALDO" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "ALERTAS_SALDO" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS alerta_destinatarios_select ON "ALERTA_DESTINATARIOS";
            DROP POLICY IF EXISTS alerta_destinatarios_write ON "ALERTA_DESTINATARIOS";
            ALTER TABLE "ALERTA_DESTINATARIOS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "ALERTA_DESTINATARIOS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS ia_uso_usuarios_select ON "IA_USO_USUARIOS";
            DROP POLICY IF EXISTS ia_uso_usuarios_insert ON "IA_USO_USUARIOS";
            DROP POLICY IF EXISTS ia_uso_usuarios_update ON "IA_USO_USUARIOS";
            DROP POLICY IF EXISTS ia_uso_usuarios_delete ON "IA_USO_USUARIOS";
            ALTER TABLE "IA_USO_USUARIOS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "IA_USO_USUARIOS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS backup_operations_select ON "BACKUP_OPERATIONS";
            DROP POLICY IF EXISTS backup_operations_insert ON "BACKUP_OPERATIONS";
            DROP POLICY IF EXISTS backup_operations_update ON "BACKUP_OPERATIONS";
            DROP POLICY IF EXISTS backup_operations_delete ON "BACKUP_OPERATIONS";
            ALTER TABLE "BACKUP_OPERATIONS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUP_OPERATIONS" DISABLE ROW LEVEL SECURITY;

            DROP FUNCTION IF EXISTS atlas_security.can_read_alerta_saldo(uuid, integer);
            """);
    }
}
