using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

public partial class HardenReleaseSecurityPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION atlas_security.is_write_scope()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.request_scope', true) IN ('write', 'revision')
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_export_scope()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.request_scope', true) = 'export'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_revision_scope()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.request_scope', true) = 'revision'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_read_cuenta(target_cuenta_id uuid, target_titular_id uuid)
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
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
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
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                        )
                    )
            $$;

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
                                  SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id
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
                                  SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id
                              ))
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                        )
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_export_cuenta(target_cuenta_id uuid, target_titular_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR (
                        atlas_security.is_user_mode()
                        AND atlas_security.is_export_scope()
                        AND atlas_security.current_user_id() IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM "PERMISOS_USUARIO" p
                            WHERE p.usuario_id = atlas_security.current_user_id()
                              AND p.puede_ver_cuentas
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                        )
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
                        WHERE c.id = target_cuenta_id
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
                            JOIN "PERMISOS_USUARIO" p ON p.usuario_id = atlas_security.current_user_id()
                            WHERE e.id = target_extracto_id
                              AND p.puede_editar_lineas
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = c.id)
                              AND (p.titular_id IS NULL OR p.titular_id = c.titular_id)
                        )
                    )
            $$;

            DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
            CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                FOR ALL USING (atlas_security.can_export_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_export_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS";
            CREATE POLICY revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS"
                FOR ALL USING (atlas_security.can_review_extracto(extracto_id))
                WITH CHECK (atlas_security.can_review_extracto(extracto_id));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
            CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS";
            CREATE POLICY revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS"
                FOR ALL USING (atlas_security.can_write_extracto(extracto_id))
                WITH CHECK (atlas_security.can_write_extracto(extracto_id));

            DROP FUNCTION IF EXISTS atlas_security.can_review_extracto(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_export_cuenta_by_id(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_export_cuenta(uuid, uuid);
            """);
    }
}
