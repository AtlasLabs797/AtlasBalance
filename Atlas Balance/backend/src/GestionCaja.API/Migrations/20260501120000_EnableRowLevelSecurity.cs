using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCaja.API.Migrations;

public partial class EnableRowLevelSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE SCHEMA IF NOT EXISTS atlas_security;
            CREATE EXTENSION IF NOT EXISTS pgcrypto;

            CREATE TABLE IF NOT EXISTS atlas_security.rls_context_secret (
                id boolean PRIMARY KEY DEFAULT true CHECK (id),
                secret text NOT NULL,
                updated_at timestamp with time zone NOT NULL DEFAULT now()
            );
            REVOKE ALL ON TABLE atlas_security.rls_context_secret FROM PUBLIC;

            CREATE OR REPLACE FUNCTION atlas_security.context_payload()
            RETURNS text
            LANGUAGE sql
            STABLE
            AS $$
                SELECT concat_ws(
                    '|',
                    coalesce(current_setting('atlas.auth_mode', true), ''),
                    coalesce(current_setting('atlas.user_id', true), ''),
                    coalesce(current_setting('atlas.integration_token_id', true), ''),
                    coalesce(current_setting('atlas.is_admin', true), ''),
                    coalesce(current_setting('atlas.system', true), ''),
                    coalesce(current_setting('atlas.request_scope', true), '')
                )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.context_is_valid()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = pg_catalog, atlas_security
            AS $$
                SELECT EXISTS (
                    SELECT 1
                    FROM atlas_security.rls_context_secret s
                    WHERE lower(coalesce(current_setting('atlas.context_signature', true), '')) =
                          encode(
                              public.hmac(
                                  convert_to(atlas_security.context_payload(), 'UTF8'),
                                  convert_to(s.secret, 'UTF8'),
                                  'sha256'),
                              'hex')
                )
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.current_user_id()
            RETURNS uuid
            LANGUAGE sql
            STABLE
            AS $$
                WITH setting AS (
                    SELECT NULLIF(current_setting('atlas.user_id', true), '') AS value
                )
                SELECT CASE
                    WHEN value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                    THEN value::uuid
                    ELSE NULL
                END
                FROM setting
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.current_integration_token_id()
            RETURNS uuid
            LANGUAGE sql
            STABLE
            AS $$
                WITH setting AS (
                    SELECT NULLIF(current_setting('atlas.integration_token_id', true), '') AS value
                )
                SELECT CASE
                    WHEN value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                    THEN value::uuid
                    ELSE NULL
                END
                FROM setting
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_system()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.system', true) = 'true'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_admin()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.is_admin', true) = 'true'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_admin_or_system()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_system() OR atlas_security.is_admin()
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_user_mode()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.auth_mode', true) = 'user'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_integration_mode()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.auth_mode', true) = 'integration'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_auth_flow()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.auth_mode', true) = 'auth'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_dashboard_scope()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.context_is_valid()
                    AND current_setting('atlas.request_scope', true) = 'dashboard'
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
                                  OR p.puede_agregar_lineas
                                  OR p.puede_editar_lineas
                                  OR p.puede_eliminar_lineas
                                  OR p.puede_importar
                                  OR (atlas_security.is_dashboard_scope() AND p.puede_ver_dashboard)
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

            CREATE OR REPLACE FUNCTION atlas_security.can_read_cuenta_by_id(target_cuenta_id uuid)
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT atlas_security.is_admin_or_system()
                    OR EXISTS (
                        SELECT 1
                        FROM "CUENTAS" c
                        WHERE c.id = target_cuenta_id
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
                        WHERE c.id = target_cuenta_id
                          AND atlas_security.can_write_cuenta(c.id, c.titular_id)
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
                                  OR p.puede_agregar_lineas
                                  OR p.puede_editar_lineas
                                  OR p.puede_eliminar_lineas
                                  OR p.puede_importar
                                  OR (atlas_security.is_dashboard_scope() AND p.puede_ver_dashboard)
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
                          AND atlas_security.can_write_cuenta_by_id(e.cuenta_id)
                    )
            $$;

            DROP POLICY IF EXISTS titulares_select ON "TITULARES";
            DROP POLICY IF EXISTS titulares_write ON "TITULARES";
            ALTER TABLE "TITULARES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "TITULARES" FORCE ROW LEVEL SECURITY;
            CREATE POLICY titulares_select ON "TITULARES"
                FOR SELECT USING (atlas_security.can_read_titular(id));
            CREATE POLICY titulares_write ON "TITULARES"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
            DROP POLICY IF EXISTS cuentas_write ON "CUENTAS";
            ALTER TABLE "CUENTAS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "CUENTAS" FORCE ROW LEVEL SECURITY;
            CREATE POLICY cuentas_select ON "CUENTAS"
                FOR SELECT USING (atlas_security.can_read_cuenta(id, titular_id));
            CREATE POLICY cuentas_write ON "CUENTAS"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            DROP POLICY IF EXISTS plazos_fijos_select ON "PLAZOS_FIJOS";
            DROP POLICY IF EXISTS plazos_fijos_write ON "PLAZOS_FIJOS";
            ALTER TABLE "PLAZOS_FIJOS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "PLAZOS_FIJOS" FORCE ROW LEVEL SECURITY;
            CREATE POLICY plazos_fijos_select ON "PLAZOS_FIJOS"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
            CREATE POLICY plazos_fijos_write ON "PLAZOS_FIJOS"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            DROP POLICY IF EXISTS extractos_select ON "EXTRACTOS";
            DROP POLICY IF EXISTS extractos_write ON "EXTRACTOS";
            ALTER TABLE "EXTRACTOS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "EXTRACTOS" FORCE ROW LEVEL SECURITY;
            CREATE POLICY extractos_select ON "EXTRACTOS"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
            CREATE POLICY extractos_write ON "EXTRACTOS"
                FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA";
            DROP POLICY IF EXISTS extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA";
            ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA" FORCE ROW LEVEL SECURITY;
            CREATE POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA"
                FOR SELECT USING (atlas_security.can_read_extracto(extracto_id));
            CREATE POLICY extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA"
                FOR ALL USING (atlas_security.can_write_extracto(extracto_id))
                WITH CHECK (atlas_security.can_write_extracto(extracto_id));

            DROP POLICY IF EXISTS exportaciones_select ON "EXPORTACIONES";
            DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
            ALTER TABLE "EXPORTACIONES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "EXPORTACIONES" FORCE ROW LEVEL SECURITY;
            CREATE POLICY exportaciones_select ON "EXPORTACIONES"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
            CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

            DROP POLICY IF EXISTS preferencias_usuario_cuenta_select ON "PREFERENCIAS_USUARIO_CUENTA";
            DROP POLICY IF EXISTS preferencias_usuario_cuenta_write ON "PREFERENCIAS_USUARIO_CUENTA";
            ALTER TABLE "PREFERENCIAS_USUARIO_CUENTA" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "PREFERENCIAS_USUARIO_CUENTA" FORCE ROW LEVEL SECURITY;
            CREATE POLICY preferencias_usuario_cuenta_select ON "PREFERENCIAS_USUARIO_CUENTA"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (atlas_security.is_user_mode() AND usuario_id = atlas_security.current_user_id())
                );
            CREATE POLICY preferencias_usuario_cuenta_write ON "PREFERENCIAS_USUARIO_CUENTA"
                FOR ALL USING (
                    atlas_security.is_admin_or_system()
                    OR (atlas_security.is_user_mode() AND usuario_id = atlas_security.current_user_id())
                )
                WITH CHECK (
                    atlas_security.is_admin_or_system()
                    OR (atlas_security.is_user_mode() AND usuario_id = atlas_security.current_user_id())
                );

            DROP POLICY IF EXISTS auditorias_select ON "AUDITORIAS";
            DROP POLICY IF EXISTS auditorias_insert ON "AUDITORIAS";
            ALTER TABLE "AUDITORIAS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "AUDITORIAS" FORCE ROW LEVEL SECURITY;
            CREATE POLICY auditorias_select ON "AUDITORIAS"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (atlas_security.is_user_mode() AND usuario_id = atlas_security.current_user_id())
                    OR (entidad_tipo = 'EXTRACTOS' AND entidad_id IS NOT NULL AND atlas_security.can_read_extracto(entidad_id))
                    OR (entidad_tipo = 'CUENTAS' AND entidad_id IS NOT NULL AND atlas_security.can_read_cuenta_by_id(entidad_id))
                    OR (entidad_tipo = 'TITULARES' AND entidad_id IS NOT NULL AND atlas_security.can_read_titular(entidad_id))
                );
            CREATE POLICY auditorias_insert ON "AUDITORIAS"
                FOR INSERT WITH CHECK (
                    atlas_security.is_admin_or_system()
                    OR atlas_security.is_auth_flow()
                    OR (atlas_security.is_user_mode() AND atlas_security.current_user_id() IS NOT NULL)
                );

            DROP POLICY IF EXISTS auditoria_integraciones_select ON "AUDITORIA_INTEGRACIONES";
            DROP POLICY IF EXISTS auditoria_integraciones_insert ON "AUDITORIA_INTEGRACIONES";
            ALTER TABLE "AUDITORIA_INTEGRACIONES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "AUDITORIA_INTEGRACIONES" FORCE ROW LEVEL SECURITY;
            CREATE POLICY auditoria_integraciones_select ON "AUDITORIA_INTEGRACIONES"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (atlas_security.is_integration_mode() AND token_id = atlas_security.current_integration_token_id())
                );
            CREATE POLICY auditoria_integraciones_insert ON "AUDITORIA_INTEGRACIONES"
                FOR INSERT WITH CHECK (
                    atlas_security.is_admin_or_system()
                    OR (atlas_security.is_integration_mode() AND token_id = atlas_security.current_integration_token_id())
                );

            DROP POLICY IF EXISTS backups_admin ON "BACKUPS";
            ALTER TABLE "BACKUPS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUPS" FORCE ROW LEVEL SECURITY;
            CREATE POLICY backups_admin ON "BACKUPS"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            DROP POLICY IF EXISTS notificaciones_admin_admin ON "NOTIFICACIONES_ADMIN";
            ALTER TABLE "NOTIFICACIONES_ADMIN" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "NOTIFICACIONES_ADMIN" FORCE ROW LEVEL SECURITY;
            CREATE POLICY notificaciones_admin_admin ON "NOTIFICACIONES_ADMIN"
                FOR ALL USING (atlas_security.is_admin_or_system())
                WITH CHECK (atlas_security.is_admin_or_system());

            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS titulares_select ON "TITULARES";
            DROP POLICY IF EXISTS titulares_write ON "TITULARES";
            ALTER TABLE "TITULARES" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "TITULARES" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
            DROP POLICY IF EXISTS cuentas_write ON "CUENTAS";
            ALTER TABLE "CUENTAS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "CUENTAS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS plazos_fijos_select ON "PLAZOS_FIJOS";
            DROP POLICY IF EXISTS plazos_fijos_write ON "PLAZOS_FIJOS";
            ALTER TABLE "PLAZOS_FIJOS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "PLAZOS_FIJOS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS extractos_select ON "EXTRACTOS";
            DROP POLICY IF EXISTS extractos_write ON "EXTRACTOS";
            ALTER TABLE "EXTRACTOS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "EXTRACTOS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA";
            DROP POLICY IF EXISTS extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA";
            ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "EXTRACTOS_COLUMNAS_EXTRA" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS exportaciones_select ON "EXPORTACIONES";
            DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
            ALTER TABLE "EXPORTACIONES" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "EXPORTACIONES" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS preferencias_usuario_cuenta_select ON "PREFERENCIAS_USUARIO_CUENTA";
            DROP POLICY IF EXISTS preferencias_usuario_cuenta_write ON "PREFERENCIAS_USUARIO_CUENTA";
            ALTER TABLE "PREFERENCIAS_USUARIO_CUENTA" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "PREFERENCIAS_USUARIO_CUENTA" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS auditorias_select ON "AUDITORIAS";
            DROP POLICY IF EXISTS auditorias_insert ON "AUDITORIAS";
            ALTER TABLE "AUDITORIAS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "AUDITORIAS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS auditoria_integraciones_select ON "AUDITORIA_INTEGRACIONES";
            DROP POLICY IF EXISTS auditoria_integraciones_insert ON "AUDITORIA_INTEGRACIONES";
            ALTER TABLE "AUDITORIA_INTEGRACIONES" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "AUDITORIA_INTEGRACIONES" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS backups_admin ON "BACKUPS";
            ALTER TABLE "BACKUPS" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "BACKUPS" DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS notificaciones_admin_admin ON "NOTIFICACIONES_ADMIN";
            ALTER TABLE "NOTIFICACIONES_ADMIN" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "NOTIFICACIONES_ADMIN" DISABLE ROW LEVEL SECURITY;

            DROP FUNCTION IF EXISTS atlas_security.can_write_extracto(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_read_extracto(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_write_cuenta_by_id(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_read_cuenta_by_id(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_read_titular(uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_write_cuenta(uuid, uuid);
            DROP FUNCTION IF EXISTS atlas_security.can_read_cuenta(uuid, uuid);
            DROP FUNCTION IF EXISTS atlas_security.is_dashboard_scope();
            DROP FUNCTION IF EXISTS atlas_security.is_auth_flow();
            DROP FUNCTION IF EXISTS atlas_security.is_integration_mode();
            DROP FUNCTION IF EXISTS atlas_security.is_user_mode();
            DROP FUNCTION IF EXISTS atlas_security.is_admin_or_system();
            DROP FUNCTION IF EXISTS atlas_security.is_admin();
            DROP FUNCTION IF EXISTS atlas_security.is_system();
            DROP FUNCTION IF EXISTS atlas_security.current_integration_token_id();
            DROP FUNCTION IF EXISTS atlas_security.current_user_id();
            DROP FUNCTION IF EXISTS atlas_security.context_is_valid();
            DROP FUNCTION IF EXISTS atlas_security.context_payload();
            DROP TABLE IF EXISTS atlas_security.rls_context_secret;
            DROP SCHEMA IF EXISTS atlas_security;
            """);
    }
}
