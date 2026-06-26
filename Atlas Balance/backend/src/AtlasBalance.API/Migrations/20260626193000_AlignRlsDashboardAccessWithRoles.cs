using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260626193000_AlignRlsDashboardAccessWithRoles")]
public partial class AlignRlsDashboardAccessWithRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION atlas_security.current_user_is_manager()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT EXISTS (
                    SELECT 1
                    FROM "USUARIOS" u
                    WHERE u.id = atlas_security.current_user_id()
                      AND u.rol = 1
                      AND u.activo
                      AND u.deleted_at IS NULL
                )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_read_cuenta(target_cuenta_id uuid, target_titular_id uuid, target_pais_id uuid)
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
                              AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  (
                                      NOT atlas_security.is_dashboard_scope()
                                      AND p.puede_ver_cuentas
                                  )
                                  OR (
                                      atlas_security.is_dashboard_scope()
                                      AND (
                                          p.puede_ver_dashboard
                                          OR atlas_security.current_user_is_manager()
                                      )
                                      AND (
                                          p.puede_ver_cuentas
                                          OR p.puede_agregar_lineas
                                          OR p.puede_editar_lineas
                                          OR p.puede_eliminar_lineas
                                          OR p.puede_importar
                                      )
                                  )
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
                              AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
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
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  p.cuenta_id IS NULL
                                  OR p.cuenta_id IN (
                                      SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                                  )
                              )
                              AND (
                                  p.pais_id IS NULL
                                  OR EXISTS (
                                      SELECT 1 FROM "CUENTAS" c
                                      WHERE c.titular_id = target_titular_id
                                        AND c.deleted_at IS NULL
                                        AND c.pais_id = p.pais_id
                                  )
                              )
                              AND (
                                  (
                                      NOT atlas_security.is_dashboard_scope()
                                      AND p.puede_ver_cuentas
                                  )
                                  OR (
                                      atlas_security.is_dashboard_scope()
                                      AND (
                                          p.puede_ver_dashboard
                                          OR atlas_security.current_user_is_manager()
                                      )
                                      AND (
                                          p.puede_ver_cuentas
                                          OR p.puede_agregar_lineas
                                          OR p.puede_editar_lineas
                                          OR p.puede_eliminar_lineas
                                          OR p.puede_importar
                                      )
                                  )
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
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  p.cuenta_id IS NULL
                                  OR p.cuenta_id IN (
                                      SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                                  )
                              )
                              AND (
                                  p.pais_id IS NULL
                                  OR EXISTS (
                                      SELECT 1 FROM "CUENTAS" c
                                      WHERE c.titular_id = target_titular_id
                                        AND c.deleted_at IS NULL
                                        AND c.pais_id = p.pais_id
                                  )
                              )
                        )
                    )
            $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION atlas_security.can_read_cuenta(target_cuenta_id uuid, target_titular_id uuid, target_pais_id uuid)
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
                              AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  p.puede_ver_cuentas
                                  OR (
                                      atlas_security.is_dashboard_scope()
                                      AND p.puede_ver_dashboard
                                      AND (
                                          p.puede_ver_cuentas
                                          OR p.puede_agregar_lineas
                                          OR p.puede_editar_lineas
                                          OR p.puede_eliminar_lineas
                                          OR p.puede_importar
                                      )
                                  )
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
                              AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
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
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  p.cuenta_id IS NULL
                                  OR p.cuenta_id IN (
                                      SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                                  )
                              )
                              AND (
                                  p.pais_id IS NULL
                                  OR EXISTS (
                                      SELECT 1 FROM "CUENTAS" c
                                      WHERE c.titular_id = target_titular_id
                                        AND c.deleted_at IS NULL
                                        AND c.pais_id = p.pais_id
                                  )
                              )
                              AND (
                                  p.puede_ver_cuentas
                                  OR (
                                      atlas_security.is_dashboard_scope()
                                      AND p.puede_ver_dashboard
                                      AND (
                                          p.puede_ver_cuentas
                                          OR p.puede_agregar_lineas
                                          OR p.puede_editar_lineas
                                          OR p.puede_eliminar_lineas
                                          OR p.puede_importar
                                      )
                                  )
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
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                              AND (
                                  p.cuenta_id IS NULL
                                  OR p.cuenta_id IN (
                                      SELECT c.id FROM "CUENTAS" c WHERE c.titular_id = target_titular_id AND c.deleted_at IS NULL
                                  )
                              )
                              AND (
                                  p.pais_id IS NULL
                                  OR EXISTS (
                                      SELECT 1 FROM "CUENTAS" c
                                      WHERE c.titular_id = target_titular_id
                                        AND c.deleted_at IS NULL
                                        AND c.pais_id = p.pais_id
                                  )
                              )
                        )
                    )
            $$;

            DROP FUNCTION IF EXISTS atlas_security.current_user_is_manager();
            """);
    }
}
