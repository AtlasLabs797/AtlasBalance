using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260601091000_HardenRlsIntegrationReadBackstop")]
public partial class HardenRlsIntegrationReadBackstop : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
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
                              AND p.acceso_tipo = 'lectura'
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
                              AND p.acceso_tipo = 'lectura'
                              AND (p.cuenta_id IS NULL OR p.cuenta_id IN (
                                  SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                              ))
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                        )
                    )
            $$;

            ALTER TABLE "REVISION_EXTRACTO_ESTADOS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "REVISION_EXTRACTO_ESTADOS" FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS";
            CREATE POLICY revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS"
                FOR SELECT USING (atlas_security.can_read_extracto(extracto_id));

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
            """);
    }
}
