using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-03.02 (M-9): cierra la deriva entre modelo, snapshot y esquema real.
    //
    // El snapshot de esta migracion (Designer + AppDbContextModelSnapshot) se
    // genero con `dotnet ef migrations add`, asi que refleja el modelo actual.
    // El Up/Down del scaffold NO se usa: intentaba crear columnas e indices que
    // ya existen (creados por migraciones SQL manuscritas). En su lugar, SQL
    // idempotente que lleva cualquier base construida con las migraciones
    // anteriores al estado del modelo:
    //   1. deleted_at de REVISION_EXTRACTO_ESTADOS, IMPORTACION_LOTE_FILAS y
    //      EXTRACTOS_COLUMNAS_EXTRA pasa de `timestamp` a `timestamptz`.
    //      PostgreSQL no deja cambiar el tipo de una columna usada por una
    //      policy, asi que se guardan las policies dependientes (desde el
    //      catalogo, en cualquier tabla), se retiran y se recrean identicas.
    //      Conversion: la escritura era timestamptz -> timestamp con la zona de
    //      la sesion; se invierte con esa misma zona (`current_setting('TimeZone')`),
    //      que es la del servidor, igual que en runtime.
    //   2. FK deleted_by_id -> USUARIOS (RESTRICT) e indice en las tres tablas,
    //      anulando antes referencias huerfanas. Las tablas tienen FORCE ROW
    //      LEVEL SECURITY: se desactiva solo durante esa limpieza.
    //   3. Indices: se renombran los creados con la errata `ix_extratos_*`, se
    //      exige que los unicos sean parciales (`deleted_at IS NULL`) y se
    //      retiran los que el modelo ya no declara.
    //   4. CONCILIACIONES: la FK de deleted_by_id toma el nombre que genera EF.
    //   5. IMPORTACION_LOTES.notas: ya existe desde V0203_Hardening; IF NOT EXISTS.
    // Los atributos [DbContext] y [Migration] estan en el .Designer.cs.
    public partial class CloseM9SnapshotDrift : Migration
    {
        private static readonly string[] SoftDeleteTables =
        {
            "REVISION_EXTRACTO_ESTADOS",
            "IMPORTACION_LOTE_FILAS",
            "EXTRACTOS_COLUMNAS_EXTRA"
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in SoftDeleteTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "deleted_at" timestamp with time zone;
                    ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "deleted_by_id" uuid;
                    """);
            }

            migrationBuilder.Sql(AlterDeletedAtTypeSql("timestamp with time zone"));

            foreach (var table in SoftDeleteTables)
            {
                var prefix = table.ToLowerInvariant();
                migrationBuilder.Sql($"""
                    DO $$
                    DECLARE
                        was_forced boolean;
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_constraint
                            WHERE conname = 'fk_{prefix}_usuarios_deleted_by_id'
                              AND conrelid = '"{table}"'::regclass
                        ) THEN
                            SELECT relforcerowsecurity INTO was_forced
                            FROM pg_class WHERE oid = '"{table}"'::regclass;
                            IF was_forced THEN
                                EXECUTE 'ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY';
                            END IF;

                            UPDATE "{table}" t
                            SET "deleted_by_id" = NULL
                            WHERE t."deleted_by_id" IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM "USUARIOS" u WHERE u."id" = t."deleted_by_id");

                            ALTER TABLE "{table}"
                                ADD CONSTRAINT "fk_{prefix}_usuarios_deleted_by_id"
                                FOREIGN KEY ("deleted_by_id") REFERENCES "USUARIOS" ("id")
                                ON DELETE RESTRICT;

                            IF was_forced THEN
                                EXECUTE 'ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY';
                            END IF;
                        END IF;
                    END $$;

                    CREATE INDEX IF NOT EXISTS "ix_{prefix}_deleted_by_id" ON "{table}" ("deleted_by_id");
                    """);
            }

            // Errata historica: indices creados como ix_extratos_* (sin la "c").
            migrationBuilder.Sql(RenameIndexSql("ix_extratos_columnas_extra_deleted_at", "ix_extractos_columnas_extra_deleted_at"));
            migrationBuilder.Sql(RenameIndexSql("ix_extratos_columnas_extra_extracto_id_nombre_columna", "ix_extractos_columnas_extra_extracto_id_nombre_columna"));

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_revision_extracto_estados_deleted_at" ON "REVISION_EXTRACTO_ESTADOS" ("deleted_at");
                CREATE INDEX IF NOT EXISTS "ix_importacion_lote_filas_deleted_at" ON "IMPORTACION_LOTE_FILAS" ("deleted_at");
                CREATE INDEX IF NOT EXISTS "ix_extractos_columnas_extra_deleted_at" ON "EXTRACTOS_COLUMNAS_EXTRA" ("deleted_at");
                CREATE INDEX IF NOT EXISTS "ix_extractos_columnas_extra_extracto_id" ON "EXTRACTOS_COLUMNAS_EXTRA" ("extracto_id");
                CREATE INDEX IF NOT EXISTS "ix_ia_uso_usuarios_deleted_at" ON "IA_USO_USUARIOS" ("deleted_at");

                DROP INDEX IF EXISTS "ix_extractos_columnas_extra_nombre_columna";
                DROP INDEX IF EXISTS "ix_importacion_lotes_usuario_creador_id";
                """);

            migrationBuilder.Sql(EnsurePartialUniqueIndexSql("ix_revision_extracto_estados_extracto_id_tipo", "REVISION_EXTRACTO_ESTADOS", "\"extracto_id\", \"tipo\""));
            migrationBuilder.Sql(EnsurePartialUniqueIndexSql("ix_importacion_lote_filas_lote_id_indice", "IMPORTACION_LOTE_FILAS", "\"lote_id\", \"indice\""));
            migrationBuilder.Sql(EnsurePartialUniqueIndexSql("ix_extractos_columnas_extra_extracto_id_nombre_columna", "EXTRACTOS_COLUMNAS_EXTRA", "\"extracto_id\", \"nombre_columna\""));

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_conciliaciones_deleted_by_id'
                          AND conrelid = '"CONCILIACIONES"'::regclass
                    ) AND NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_conciliaciones_usuarios_deleted_by_id'
                          AND conrelid = '"CONCILIACIONES"'::regclass
                    ) THEN
                        ALTER TABLE "CONCILIACIONES"
                            RENAME CONSTRAINT "fk_conciliaciones_deleted_by_id" TO "fk_conciliaciones_usuarios_deleted_by_id";
                    END IF;
                END $$;

                ALTER TABLE "IMPORTACION_LOTES" ADD COLUMN IF NOT EXISTS "notas" text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Vuelve al esquema previo a esta migracion. No retira columnas:
            // ya existian antes (migraciones manuscritas) y contienen datos.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_conciliaciones_usuarios_deleted_by_id'
                          AND conrelid = '"CONCILIACIONES"'::regclass
                    ) THEN
                        ALTER TABLE "CONCILIACIONES"
                            RENAME CONSTRAINT "fk_conciliaciones_usuarios_deleted_by_id" TO "fk_conciliaciones_deleted_by_id";
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS "ix_extractos_columnas_extra_nombre_columna" ON "EXTRACTOS_COLUMNAS_EXTRA" ("nombre_columna");
                CREATE INDEX IF NOT EXISTS "ix_importacion_lotes_usuario_creador_id" ON "IMPORTACION_LOTES" ("usuario_creador_id");
                """);

            migrationBuilder.Sql(RenameIndexSql("ix_extractos_columnas_extra_deleted_at", "ix_extratos_columnas_extra_deleted_at"));
            migrationBuilder.Sql(RenameIndexSql("ix_extractos_columnas_extra_extracto_id_nombre_columna", "ix_extratos_columnas_extra_extracto_id_nombre_columna"));

            foreach (var table in SoftDeleteTables)
            {
                var prefix = table.ToLowerInvariant();
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" DROP CONSTRAINT IF EXISTS "fk_{prefix}_usuarios_deleted_by_id";
                    DROP INDEX IF EXISTS "ix_{prefix}_deleted_by_id";
                    """);
            }

            migrationBuilder.Sql(AlterDeletedAtTypeSql("timestamp without time zone"));
        }

        // Cambia el tipo de deleted_at en las tres tablas preservando las
        // policies RLS que dependen de esas columnas (de cualquier tabla).
        private static string AlterDeletedAtTypeSql(string targetType)
        {
            var tables = string.Join(", ", SoftDeleteTables.Select(t => $"'\"{t}\"'::regclass"));
            var conversion = targetType == "timestamp with time zone"
                ? "\"deleted_at\" AT TIME ZONE current_setting(''TimeZone'')"
                : "(\"deleted_at\" AT TIME ZONE current_setting(''TimeZone''))";
            return $"""
                DO $$
                DECLARE
                    saved jsonb;
                    pol record;
                    tbl regclass;
                    role_list text;
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_attribute a
                        WHERE a.attrelid IN ({tables})
                          AND a.attname = 'deleted_at'
                          AND NOT a.attisdropped
                          AND format_type(a.atttypid, a.atttypmod) <> '{targetType}'
                    ) THEN
                        RETURN;
                    END IF;

                    -- Definiciones guardadas en una variable (sin tabla temporal: no
                    -- depende del privilegio TEMP del rol de migracion).
                    SELECT coalesce(jsonb_agg(to_jsonb(x)), '[]'::jsonb) INTO saved
                    FROM (
                        SELECT DISTINCT p.polrelid::regclass::text AS table_name,
                               p.polname::text AS polname,
                               p.polpermissive,
                               p.polcmd::text AS polcmd,
                               p.polroles::oid[]::text AS polroles,
                               pg_get_expr(p.polqual, p.polrelid) AS qual,
                               pg_get_expr(p.polwithcheck, p.polrelid) AS with_check
                        FROM pg_depend d
                        JOIN pg_policy p ON d.classid = 'pg_policy'::regclass AND d.objid = p.oid
                        JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
                        WHERE d.refobjid IN ({tables})
                          AND a.attname = 'deleted_at'
                    ) x;

                    FOR pol IN
                        SELECT * FROM jsonb_to_recordset(saved)
                            AS r(table_name text, polname text, polpermissive boolean, polcmd text,
                                 polroles text, qual text, with_check text)
                    LOOP
                        EXECUTE format('DROP POLICY %I ON %s', pol.polname, pol.table_name);
                    END LOOP;

                    FOREACH tbl IN ARRAY ARRAY[{tables}] LOOP
                        IF EXISTS (
                            SELECT 1 FROM pg_attribute a
                            WHERE a.attrelid = tbl AND a.attname = 'deleted_at' AND NOT a.attisdropped
                              AND format_type(a.atttypid, a.atttypmod) <> '{targetType}'
                        ) THEN
                            EXECUTE format(
                                'ALTER TABLE %s ALTER COLUMN "deleted_at" TYPE {targetType} USING {conversion}',
                                tbl);
                        END IF;
                    END LOOP;

                    FOR pol IN
                        SELECT * FROM jsonb_to_recordset(saved)
                            AS r(table_name text, polname text, polpermissive boolean, polcmd text,
                                 polroles text, qual text, with_check text)
                    LOOP
                        SELECT string_agg(CASE WHEN r.role_oid = 0 THEN 'PUBLIC' ELSE quote_ident(pr.rolname) END, ', ')
                        INTO role_list
                        FROM unnest(pol.polroles::oid[]) AS r(role_oid)
                        LEFT JOIN pg_roles pr ON pr.oid = r.role_oid;

                        EXECUTE format(
                            'CREATE POLICY %I ON %s AS %s FOR %s TO %s%s%s',
                            pol.polname,
                            pol.table_name,
                            CASE WHEN pol.polpermissive THEN 'PERMISSIVE' ELSE 'RESTRICTIVE' END,
                            CASE pol.polcmd WHEN 'r' THEN 'SELECT' WHEN 'a' THEN 'INSERT' WHEN 'w' THEN 'UPDATE' WHEN 'd' THEN 'DELETE' ELSE 'ALL' END,
                            role_list,
                            CASE WHEN pol.qual IS NOT NULL THEN ' USING (' || pol.qual || ')' ELSE '' END,
                            CASE WHEN pol.with_check IS NOT NULL THEN ' WITH CHECK (' || pol.with_check || ')' ELSE '' END);
                    END LOOP;
                END $$;
                """;
        }

        private static string RenameIndexSql(string from, string to) => $"""
            DO $$
            BEGIN
                IF to_regclass('"{from}"') IS NOT NULL THEN
                    IF to_regclass('"{to}"') IS NULL THEN
                        ALTER INDEX "{from}" RENAME TO "{to}";
                    ELSE
                        DROP INDEX "{from}";
                    END IF;
                END IF;
            END $$;
            """;

        // Garantiza un indice unico parcial `WHERE deleted_at IS NULL` con ese
        // nombre: si existe sin el predicado (version antigua), se recrea.
        private static string EnsurePartialUniqueIndexSql(string name, string table, string columns) => $"""
            DO $$
            BEGIN
                IF to_regclass('"{name}"') IS NOT NULL
                   AND pg_get_indexdef(to_regclass('"{name}"')) NOT LIKE '%WHERE (deleted_at IS NULL)%' THEN
                    DROP INDEX "{name}";
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "{name}" ON "{table}" ({columns}) WHERE "deleted_at" IS NULL;
            """;
    }
}
