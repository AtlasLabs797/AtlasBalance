using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

public partial class SignRowLevelSecurityContext : Migration
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
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION atlas_security.current_user_id()
            RETURNS uuid
            LANGUAGE sql
            STABLE
            AS $$
                SELECT NULLIF(current_setting('atlas.user_id', true), '')::uuid
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.current_integration_token_id()
            RETURNS uuid
            LANGUAGE sql
            STABLE
            AS $$
                SELECT NULLIF(current_setting('atlas.integration_token_id', true), '')::uuid
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_system()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT current_setting('atlas.system', true) = 'true'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_admin()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT current_setting('atlas.is_admin', true) = 'true'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_user_mode()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT current_setting('atlas.auth_mode', true) = 'user'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_integration_mode()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT current_setting('atlas.auth_mode', true) = 'integration'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_auth_flow()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT current_setting('atlas.auth_mode', true) = 'auth'
            $$;

            CREATE OR REPLACE FUNCTION atlas_security.is_dashboard_scope()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            AS $$
                SELECT current_setting('atlas.request_scope', true) = 'dashboard'
            $$;

            DROP FUNCTION IF EXISTS atlas_security.context_is_valid();
            DROP FUNCTION IF EXISTS atlas_security.context_payload();
            DROP TABLE IF EXISTS atlas_security.rls_context_secret;
            """);
    }
}
