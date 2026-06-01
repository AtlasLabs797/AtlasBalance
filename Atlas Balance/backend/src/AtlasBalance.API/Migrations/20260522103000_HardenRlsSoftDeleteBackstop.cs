using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260522103000_HardenRlsSoftDeleteBackstop")]
public partial class HardenRlsSoftDeleteBackstop : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION atlas_security.can_read_titular(target_titular_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR (
                        atlas_security.is_user_mode()
                        AND atlas_security.current_user_id() IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM "PERMISOS_USUARIO" p
                            WHERE p.usuario_id = atlas_security.current_user_id()
                              AND (p.cuenta_id IS NULL OR p.cuenta_id IN (
                                  SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                              ))
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  p.puede_ver_cuentas
                                  OR (atlas_security.is_dashboard_scope() AND p.puede_ver_dashboard)
                                  OR (
                                      atlas_security.is_write_scope()
                                      AND (
                                          p.puede_agregar_lineas
                                          OR p.puede_editar_lineas
                                          OR p.puede_eliminar_lineas
                                          OR p.puede_importar
                                      )
                                  )
                              )
                        )
                    )
                    OR (
                        atlas_security.is_integration_mode()
                        AND atlas_security.current_integration_token_id() IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM "INTEGRATION_PERMISSIONS" p
                            WHERE p.token_id = atlas_security.current_integration_token_id()
                              AND p.acceso_tipo IN ('lectura', 'escritura')
                              AND (p.cuenta_id IS NULL OR p.cuenta_id IN (
                                  SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                              ))
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                        )
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_read_cuenta_by_id(target_cuenta_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        JOIN "TITULARES" t ON t.id = c.titular_id
                        WHERE c.id = target_cuenta_id
                          AND c.deleted_at IS NULL
                          AND t.deleted_at IS NULL
                          AND atlas_security.can_read_cuenta(c.id, c.titular_id)
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_write_cuenta_by_id(target_cuenta_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        JOIN "TITULARES" t ON t.id = c.titular_id
                        WHERE c.id = target_cuenta_id
                          AND c.deleted_at IS NULL
                          AND t.deleted_at IS NULL
                          AND atlas_security.can_write_cuenta(c.id, c.titular_id)
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_read_extracto(target_extracto_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "EXTRACTOS" e
                        WHERE e.id = target_extracto_id
                          AND e.deleted_at IS NULL
                          AND atlas_security.can_read_cuenta_by_id(e.cuenta_id)
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_write_extracto(target_extracto_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "EXTRACTOS" e
                        WHERE e.id = target_extracto_id
                          AND e.deleted_at IS NULL
                          AND atlas_security.can_write_cuenta_by_id(e.cuenta_id)
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_export_cuenta_by_id(target_cuenta_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        JOIN "TITULARES" t ON t.id = c.titular_id
                        WHERE c.id = target_cuenta_id
                          AND c.deleted_at IS NULL
                          AND t.deleted_at IS NULL
                          AND atlas_security.can_export_cuenta(c.id, c.titular_id)
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_review_extracto(target_extracto_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR (
                        atlas_security.is_user_mode()
                        AND atlas_security.is_revision_scope()
                        AND atlas_security.current_user_id() IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM "EXTRACTOS" e
                            JOIN "CUENTAS" c ON c.id = e.cuenta_id
                            JOIN "TITULARES" t ON t.id = c.titular_id
                            JOIN "PERMISOS_USUARIO" p ON p.usuario_id = atlas_security.current_user_id()
                            WHERE e.id = target_extracto_id
                              AND e.deleted_at IS NULL
                              AND c.deleted_at IS NULL
                              AND t.deleted_at IS NULL
                              AND p.puede_editar_lineas
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = c.id)
                              AND (p.titular_id IS NULL OR p.titular_id = c.titular_id)
                        )
                    )
            $$;

            DROP POLICY IF EXISTS titulares_select ON "TITULARES";
            CREATE POLICY titulares_select ON "TITULARES"
                FOR SELECT USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_read_titular(id)));

            DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
            CREATE POLICY cuentas_select ON "CUENTAS"
                FOR SELECT USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_read_cuenta(id, titular_id)));

            DROP POLICY IF EXISTS plazos_fijos_select ON "PLAZOS_FIJOS";
            CREATE POLICY plazos_fijos_select ON "PLAZOS_FIJOS"
                FOR SELECT USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_read_cuenta_by_id(cuenta_id)));

            DROP POLICY IF EXISTS extractos_select ON "EXTRACTOS";
            CREATE POLICY extractos_select ON "EXTRACTOS"
                FOR SELECT USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_read_cuenta_by_id(cuenta_id)));

            DROP POLICY IF EXISTS exportaciones_select ON "EXPORTACIONES";
            CREATE POLICY exportaciones_select ON "EXPORTACIONES"
                FOR SELECT USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_read_cuenta_by_id(cuenta_id)));

            DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
            CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                FOR ALL USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id)))
                WITH CHECK (atlas_security.can_export_cuenta_by_id(cuenta_id));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS titulares_select ON "TITULARES";
            CREATE POLICY titulares_select ON "TITULARES"
                FOR SELECT USING (atlas_security.can_read_titular(id));

            DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
            CREATE POLICY cuentas_select ON "CUENTAS"
                FOR SELECT USING (atlas_security.can_read_cuenta(id, titular_id));

            DROP POLICY IF EXISTS plazos_fijos_select ON "PLAZOS_FIJOS";
            CREATE POLICY plazos_fijos_select ON "PLAZOS_FIJOS"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS extractos_select ON "EXTRACTOS";
            CREATE POLICY extractos_select ON "EXTRACTOS"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS exportaciones_select ON "EXPORTACIONES";
            CREATE POLICY exportaciones_select ON "EXPORTACIONES"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
            CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                FOR ALL USING (atlas_security.can_export_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_export_cuenta_by_id(cuenta_id));
            """);
    }
}
