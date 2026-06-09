using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260609120000_AddCountryAuthorizationScopes")]
public partial class AddCountryAuthorizationScopes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "pais_id",
            table: "PERMISOS_USUARIO",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "pais_id",
            table: "INTEGRATION_PERMISSIONS",
            type: "uuid",
            nullable: true);

        migrationBuilder.DropIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id_cuenta_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.AddColumn<Guid>(
            name: "pais_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "titular_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_permisos_usuario_pais_id",
            table: "PERMISOS_USUARIO",
            column: "pais_id");

        migrationBuilder.CreateIndex(
            name: "ix_permisos_usuario_usuario_id_pais_id",
            table: "PERMISOS_USUARIO",
            columns: new[] { "usuario_id", "pais_id" });

        migrationBuilder.CreateIndex(
            name: "ix_integration_permissions_pais_id",
            table: "INTEGRATION_PERMISSIONS",
            column: "pais_id");

        migrationBuilder.CreateIndex(
            name: "ix_preferencias_usuario_cuenta_pais_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            column: "pais_id");

        migrationBuilder.CreateIndex(
            name: "ix_preferencias_usuario_cuenta_titular_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            column: "titular_id");

        migrationBuilder.CreateIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            column: "usuario_id");

        migrationBuilder.CreateIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id_pais_id_titular_id_cuenta_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            columns: new[] { "usuario_id", "pais_id", "titular_id", "cuenta_id" });

        migrationBuilder.AddForeignKey(
            name: "fk_permisos_usuario_paises_pais_id",
            table: "PERMISOS_USUARIO",
            column: "pais_id",
            principalTable: "PAISES",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_integration_permissions_paises_pais_id",
            table: "INTEGRATION_PERMISSIONS",
            column: "pais_id",
            principalTable: "PAISES",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_preferencias_usuario_cuenta_paises_pais_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            column: "pais_id",
            principalTable: "PAISES",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_preferencias_usuario_cuenta_titulares_titular_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            column: "titular_id",
            principalTable: "TITULARES",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

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

            CREATE OR REPLACE FUNCTION atlas_security.can_read_cuenta(target_cuenta_id uuid, target_titular_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        WHERE c.id = target_cuenta_id
                          AND atlas_security.can_read_cuenta(c.id, c.titular_id, c.pais_id)
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_write_cuenta(target_cuenta_id uuid, target_titular_id uuid, target_pais_id uuid)
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
                                  p.puede_agregar_lineas
                                  OR p.puede_editar_lineas
                                  OR p.puede_eliminar_lineas
                                  OR p.puede_importar
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
                              AND p.acceso_tipo = 'escritura'
                              AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                              AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                        )
                    )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.can_write_cuenta(target_cuenta_id uuid, target_titular_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        WHERE c.id = target_cuenta_id
                          AND atlas_security.can_write_cuenta(c.id, c.titular_id, c.pais_id)
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

            CREATE OR REPLACE FUNCTION atlas_security.can_export_cuenta(target_cuenta_id uuid, target_titular_id uuid, target_pais_id uuid)
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
                              AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
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
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        WHERE c.id = target_cuenta_id
                          AND atlas_security.can_export_cuenta(c.id, c.titular_id, c.pais_id)
                    )
            $$;

            DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
            CREATE POLICY cuentas_select ON "CUENTAS"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (deleted_at IS NULL AND atlas_security.can_read_cuenta(id, titular_id, pais_id))
                );

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
                              AND (p.pais_id IS NULL OR p.pais_id = c.pais_id)
                              AND (p.cuenta_id IS NULL OR p.cuenta_id = c.id)
                              AND (p.titular_id IS NULL OR p.titular_id = c.titular_id)
                        )
                    )
            $$;

            ALTER TABLE "PERMISOS_USUARIO" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "PERMISOS_USUARIO" FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS permisos_usuario_select ON "PERMISOS_USUARIO";
            CREATE POLICY permisos_usuario_select ON "PERMISOS_USUARIO"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR atlas_security.is_auth_flow()
                    OR (
                        atlas_security.is_user_mode()
                        AND usuario_id = atlas_security.current_user_id()
                    )
                );

            DROP POLICY IF EXISTS permisos_usuario_write ON "PERMISOS_USUARIO";
            CREATE POLICY permisos_usuario_write ON "PERMISOS_USUARIO"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            ALTER TABLE "INTEGRATION_PERMISSIONS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "INTEGRATION_PERMISSIONS" FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS integration_permissions_select ON "INTEGRATION_PERMISSIONS";
            CREATE POLICY integration_permissions_select ON "INTEGRATION_PERMISSIONS"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (
                        atlas_security.is_integration_mode()
                        AND token_id = atlas_security.current_integration_token_id()
                    )
                );

            DROP POLICY IF EXISTS integration_permissions_write ON "INTEGRATION_PERMISSIONS";
            CREATE POLICY integration_permissions_write ON "INTEGRATION_PERMISSIONS"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS integration_permissions_write ON "INTEGRATION_PERMISSIONS";
            DROP POLICY IF EXISTS integration_permissions_select ON "INTEGRATION_PERMISSIONS";
            ALTER TABLE "INTEGRATION_PERMISSIONS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "INTEGRATION_PERMISSIONS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS permisos_usuario_write ON "PERMISOS_USUARIO";
            DROP POLICY IF EXISTS permisos_usuario_select ON "PERMISOS_USUARIO";
            ALTER TABLE "PERMISOS_USUARIO" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "PERMISOS_USUARIO" DISABLE ROW LEVEL SECURITY;

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

            CREATE OR REPLACE FUNCTION atlas_security.can_write_cuenta(target_cuenta_id uuid, target_titular_id uuid)
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
                                  p.puede_agregar_lineas
                                  OR p.puede_editar_lineas
                                  OR p.puede_eliminar_lineas
                                  OR p.puede_importar
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
                              AND p.acceso_tipo = 'escritura'
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

            DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
            CREATE POLICY cuentas_select ON "CUENTAS"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (deleted_at IS NULL AND atlas_security.can_read_cuenta(id, titular_id))
                );

            DROP FUNCTION IF EXISTS atlas_security.can_read_cuenta(uuid, uuid, uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_write_cuenta(uuid, uuid, uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_export_cuenta(uuid, uuid, uuid);
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_integration_permissions_paises_pais_id",
            table: "INTEGRATION_PERMISSIONS");

        migrationBuilder.DropForeignKey(
            name: "fk_permisos_usuario_paises_pais_id",
            table: "PERMISOS_USUARIO");

        migrationBuilder.DropForeignKey(
            name: "fk_preferencias_usuario_cuenta_paises_pais_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropForeignKey(
            name: "fk_preferencias_usuario_cuenta_titulares_titular_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropIndex(
            name: "ix_integration_permissions_pais_id",
            table: "INTEGRATION_PERMISSIONS");

        migrationBuilder.DropIndex(
            name: "ix_permisos_usuario_pais_id",
            table: "PERMISOS_USUARIO");

        migrationBuilder.DropIndex(
            name: "ix_permisos_usuario_usuario_id_pais_id",
            table: "PERMISOS_USUARIO");

        migrationBuilder.DropIndex(
            name: "ix_preferencias_usuario_cuenta_pais_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropIndex(
            name: "ix_preferencias_usuario_cuenta_titular_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id_pais_id_titular_id_cuenta_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropColumn(
            name: "pais_id",
            table: "INTEGRATION_PERMISSIONS");

        migrationBuilder.DropColumn(
            name: "pais_id",
            table: "PERMISOS_USUARIO");

        migrationBuilder.DropColumn(
            name: "pais_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.DropColumn(
            name: "titular_id",
            table: "PREFERENCIAS_USUARIO_CUENTA");

        migrationBuilder.CreateIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            column: "usuario_id",
            unique: true,
            filter: "\"cuenta_id\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_preferencias_usuario_cuenta_usuario_id_cuenta_id",
            table: "PREFERENCIAS_USUARIO_CUENTA",
            columns: new[] { "usuario_id", "cuenta_id" },
            unique: true,
            filter: "\"cuenta_id\" IS NOT NULL");
    }
}
