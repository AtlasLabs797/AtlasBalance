using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.06 (PR F2): migracion correctiva que cierra los cuatro bloqueos
    //   * los CHECK constraints de MOVIMIENTOS_ESPERADOS.estado y
    //     CONCILIACIONES.estado no admiten los estados que el servicio
    //     escribe en runtime (`sugerida`/`conciliada`/`excepcion`/`resuelta`);
    //     cualquier sugerir/confirmar/excepcion/resolver lanzado contra
    //     PostgreSQL produce `CheckViolation`.
    //   * `CONCILIACIONES.deleted_at` se creo como `timestamp without time zone`
    //     (migracion V-02.05 manuscrita), pero el resto del modelo espera
    //     `timestamp with time zone`; ademas `deleted_by_id` no tiene FK ni
    //     indice a `USUARIOS.id`.
    //   * las policies RLS de conciliacion siguen el predicado
    //     `can_write_cuenta_by_id`, que solo reconoce los flags operativos
    //     (`agregar`/`editar`/`eliminar`/`importar`); un usuario con
    //     `PuedeConciliar` o `PuedeCerrarConciliacion` pasaba el chequeo del
    //     servicio y se encuentra con un `InsufficientPrivilege` (RLS) al
    //     intentar `SaveChanges`. Se introduce un predicado dedicado
    //     `atlas_security.can_reconcile_cuenta_by_id` que respeta
    //     pais/titular/cuenta y exige `puede_conciliar` o
    //     `puede_cerrar_conciliacion`.
    //   * `CONCILIACIONES` no aparece en el snapshot con su unique index
    //     parcial por `deleted_at IS NULL` ni con `DeletedAt`/`DeletedById`;
    //     queda corregido en este mismo pase.
    //
    // La migracion es manuscrita-SQL (mismo patron que las V-02.05) porque
    // el snapshot EF sigue desalineado y un scaffold recrearia
    // columnas/indices ya presentes.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260720090000_AlignConciliacionEstadosAndSnapshot")]
    public partial class AlignConciliacionEstadosAndSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Normalizar deleted_at a timestamp with time zone.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'CONCILIACIONES'
                          AND column_name = 'deleted_at'
                          AND data_type = 'timestamp without time zone'
                    ) THEN
                        ALTER TABLE "CONCILIACIONES"
                            ALTER COLUMN "deleted_at" TYPE timestamp with time zone
                            USING "deleted_at" AT TIME ZONE 'UTC';
                    END IF;
                END $$;
                """);

            // 2) CHECK constraints alineados con el codigo del servicio.
            migrationBuilder.Sql(
                """
                UPDATE "CONCILIACIONES"
                SET "estado" = CASE "estado"
                    WHEN 'descartada' THEN 'excepcion'
                    WHEN 'cerrada' THEN 'resuelta'
                    ELSE "estado"
                END
                WHERE "estado" IN ('descartada', 'cerrada');

                UPDATE "MOVIMIENTOS_ESPERADOS"
                SET "estado" = CASE "estado"
                    WHEN 'satisfecho' THEN 'conciliada'
                    WHEN 'vencido' THEN 'excepcion'
                    WHEN 'cancelado' THEN 'resuelta'
                    ELSE "estado"
                END
                WHERE "estado" IN ('satisfecho', 'vencido', 'cancelado');

                ALTER TABLE "CONCILIACIONES"
                    DROP CONSTRAINT IF EXISTS "ck_conciliaciones_estado";
                ALTER TABLE "CONCILIACIONES"
                    ADD CONSTRAINT "ck_conciliaciones_estado"
                    CHECK ("estado" IN ('sugerida','conciliada','excepcion','resuelta'));

                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                    DROP CONSTRAINT IF EXISTS "ck_movimientos_esperados_estado";
                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                    ADD CONSTRAINT "ck_movimientos_esperados_estado"
                    CHECK ("estado" IN ('pendiente','sugerida','conciliada','excepcion','resuelta'));
                """);

            // 3) FK y indice para deleted_by_id.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_conciliaciones_deleted_by_id'
                          AND conrelid = '"CONCILIACIONES"'::regclass
                    ) THEN
                        UPDATE "CONCILIACIONES" c
                        SET "deleted_by_id" = NULL
                        WHERE c."deleted_by_id" IS NOT NULL
                          AND NOT EXISTS (SELECT 1 FROM "USUARIOS" u WHERE u."id" = c."deleted_by_id");
                        ALTER TABLE "CONCILIACIONES"
                            ADD CONSTRAINT "fk_conciliaciones_deleted_by_id"
                            FOREIGN KEY ("deleted_by_id") REFERENCES "USUARIOS" ("id")
                            ON DELETE SET NULL;
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS "ix_conciliaciones_deleted_by_id"
                ON "CONCILIACIONES" ("deleted_by_id");
                """);

            // 4) Predicado RLS para conciliacion. Sigue el patron de
            // `can_write_cuenta` (mismas reglas de scope
            // pais/titular/cuenta), pero los flags operativos sustituidos
            // por `puede_conciliar` y `puede_cerrar_conciliacion`.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION atlas_security.can_reconcile_cuenta(target_cuenta_id uuid, target_titular_id uuid, target_pais_id uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT atlas_security.is_admin_or_system()
                        OR (
                            atlas_security.is_user_mode()
                            AND current_setting('atlas.request_scope', true) = 'reconcile'
                            AND atlas_security.current_user_id() IS NOT NULL
                            AND EXISTS (
                                SELECT 1
                                FROM "PERMISOS_USUARIO" p
                                WHERE p.usuario_id = atlas_security.current_user_id()
                                  AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
                                  AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                                  AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                                   AND p.puede_conciliar
                            )
                        )
                $$;

                CREATE OR REPLACE FUNCTION atlas_security.can_close_reconciliation(target_cuenta_id uuid, target_titular_id uuid, target_pais_id uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT atlas_security.is_admin_or_system()
                        OR (
                            atlas_security.is_user_mode()
                            AND current_setting('atlas.request_scope', true) = 'reconcile-close'
                            AND atlas_security.current_user_id() IS NOT NULL
                            AND EXISTS (
                                SELECT 1 FROM "PERMISOS_USUARIO" p
                                WHERE p.usuario_id = atlas_security.current_user_id()
                                  AND (p.pais_id IS NULL OR p.pais_id = target_pais_id)
                                  AND (p.cuenta_id IS NULL OR p.cuenta_id = target_cuenta_id)
                                  AND (p.titular_id IS NULL OR p.titular_id = target_titular_id)
                                  AND p.puede_cerrar_conciliacion
                            )
                        )
                $$;

                CREATE OR REPLACE FUNCTION atlas_security.can_reconcile_cuenta(target_cuenta_id uuid, target_titular_id uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT atlas_security.is_admin_or_system()
                        OR EXISTS (
                            SELECT 1
                            FROM "CUENTAS" c
                            WHERE c.id = target_cuenta_id
                              AND atlas_security.can_reconcile_cuenta(c.id, c.titular_id, c.pais_id)
                        )
                $$;

                CREATE OR REPLACE FUNCTION atlas_security.can_reconcile_cuenta_by_id(target_cuenta_id uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT atlas_security.is_admin_or_system()
                        OR EXISTS (
                            SELECT 1
                            FROM "CUENTAS" c
                            WHERE c.id = target_cuenta_id
                              AND atlas_security.can_reconcile_cuenta(c.id, c.titular_id)
                        )
                $$;

                CREATE OR REPLACE FUNCTION atlas_security.can_close_reconciliation_by_id(target_cuenta_id uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT atlas_security.is_admin_or_system()
                        OR EXISTS (
                            SELECT 1 FROM "CUENTAS" c
                            WHERE c.id = target_cuenta_id
                              AND atlas_security.can_close_reconciliation(c.id, c.titular_id, c.pais_id)
                        )
                $$;

                -- Se conserva el EXECUTE predeterminado para PUBLIC, igual que
                -- en los predicados RLS existentes. Las funciones no conceden
                -- acceso por si mismas: validan el contexto firmado y permisos.
                -- Evita acoplar la migracion a un nombre de rol de despliegue.

                DROP POLICY IF EXISTS cuentas_select ON "CUENTAS";
                CREATE POLICY cuentas_select ON "CUENTAS"
                    FOR SELECT USING (
                        atlas_security.is_admin_or_system()
                        OR (
                            deleted_at IS NULL
                            AND (
                                atlas_security.can_read_cuenta(id, titular_id, pais_id)
                                OR atlas_security.can_reconcile_cuenta(id, titular_id, pais_id)
                                OR atlas_security.can_close_reconciliation(id, titular_id, pais_id)
                            )
                        )
                    );
                """);

            // 5) Reescribir policies de CONCILIACIONES y MOVIMIENTOS_ESPERADOS
            // para que INSERT/UPDATE/DELETE usen el nuevo predicado. SELECT
            // sigue exigiendo permiso de lectura de cuentas, manteniendo el
            // denegio-por-defecto del modelo.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS conciliaciones_select ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_insert ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_update ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_delete ON "CONCILIACIONES";

                CREATE POLICY conciliaciones_select ON "CONCILIACIONES"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND (atlas_security.can_read_cuenta_by_id(cuenta_id)
                             OR atlas_security.can_reconcile_cuenta_by_id(cuenta_id)
                             OR atlas_security.can_close_reconciliation_by_id(cuenta_id))
                    );
                CREATE POLICY conciliaciones_insert ON "CONCILIACIONES"
                    FOR INSERT WITH CHECK (
                        atlas_security.can_reconcile_cuenta_by_id(cuenta_id)
                    );
                CREATE POLICY conciliaciones_update ON "CONCILIACIONES"
                    FOR UPDATE USING (
                        deleted_at IS NULL
                        AND (
                            (atlas_security.can_reconcile_cuenta_by_id(cuenta_id) AND estado <> 'resuelta')
                            OR atlas_security.can_close_reconciliation_by_id(cuenta_id)
                        )
                    )
                    WITH CHECK (
                        (atlas_security.can_reconcile_cuenta_by_id(cuenta_id) AND estado <> 'resuelta')
                        OR (atlas_security.can_close_reconciliation_by_id(cuenta_id) AND estado = 'resuelta')
                    );
                CREATE POLICY conciliaciones_delete ON "CONCILIACIONES"
                    FOR DELETE USING (
                        atlas_security.can_reconcile_cuenta_by_id(cuenta_id)
                    );
                """);

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_insert ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_update ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_delete ON "MOVIMIENTOS_ESPERADOS";

                CREATE POLICY movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND (atlas_security.can_read_cuenta_by_id(cuenta_id)
                             OR atlas_security.can_reconcile_cuenta_by_id(cuenta_id)
                             OR atlas_security.can_close_reconciliation_by_id(cuenta_id))
                    );
                CREATE POLICY movimientos_esperados_insert ON "MOVIMIENTOS_ESPERADOS"
                    FOR INSERT WITH CHECK (
                        atlas_security.can_reconcile_cuenta_by_id(cuenta_id)
                    );
                CREATE POLICY movimientos_esperados_update ON "MOVIMIENTOS_ESPERADOS"
                    FOR UPDATE USING (
                        deleted_at IS NULL
                        AND (
                            (atlas_security.can_reconcile_cuenta_by_id(cuenta_id) AND estado <> 'resuelta')
                            OR atlas_security.can_close_reconciliation_by_id(cuenta_id)
                        )
                    )
                    WITH CHECK (
                        (atlas_security.can_reconcile_cuenta_by_id(cuenta_id) AND estado <> 'resuelta')
                        OR (atlas_security.can_close_reconciliation_by_id(cuenta_id) AND estado = 'resuelta')
                    );
                CREATE POLICY movimientos_esperados_delete ON "MOVIMIENTOS_ESPERADOS"
                    FOR DELETE USING (
                        atlas_security.can_reconcile_cuenta_by_id(cuenta_id)
                    );
                """);

            // 6) Alineamos el indice unique parcial con el modelo
            // (deleted_at IS NULL) por si quedo sin el predicado.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "ix_conciliaciones_movimiento_esperado_id_extracto_id";
                CREATE UNIQUE INDEX "ix_conciliaciones_movimiento_esperado_id_extracto_id"
                ON "CONCILIACIONES" ("movimiento_esperado_id", "extracto_id")
                WHERE "deleted_at" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaurar los constraints originales (V-02.05) sobre las dos
            // tablas y devolver las policies SELECT/INSERT/UPDATE/DELETE al
            // predicado `can_write_cuenta_by_id`. La funcion
            // `can_reconcile_cuenta*` se conserva porque no
            // existe una migracion previa que la haya creado y borrarla
            // dejaria jobs futuros inconsistentes con esta politica.

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_insert ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_update ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_delete ON "MOVIMIENTOS_ESPERADOS";

                CREATE POLICY movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND atlas_security.can_read_cuenta_by_id(cuenta_id)
                    );
                CREATE POLICY movimientos_esperados_insert ON "MOVIMIENTOS_ESPERADOS"
                    FOR INSERT WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                CREATE POLICY movimientos_esperados_update ON "MOVIMIENTOS_ESPERADOS"
                    FOR UPDATE USING (
                        deleted_at IS NULL
                        AND atlas_security.can_write_cuenta_by_id(cuenta_id)
                    )
                    WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                CREATE POLICY movimientos_esperados_delete ON "MOVIMIENTOS_ESPERADOS"
                    FOR DELETE USING (atlas_security.can_write_cuenta_by_id(cuenta_id));
                """);

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS conciliaciones_select ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_insert ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_update ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_delete ON "CONCILIACIONES";

                CREATE POLICY conciliaciones_select ON "CONCILIACIONES"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND atlas_security.can_read_cuenta_by_id(cuenta_id)
                    );
                CREATE POLICY conciliaciones_insert ON "CONCILIACIONES"
                    FOR INSERT WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                CREATE POLICY conciliaciones_update ON "CONCILIACIONES"
                    FOR UPDATE USING (
                        deleted_at IS NULL
                        AND atlas_security.can_write_cuenta_by_id(cuenta_id)
                    )
                    WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                CREATE POLICY conciliaciones_delete ON "CONCILIACIONES"
                    FOR DELETE USING (atlas_security.can_write_cuenta_by_id(cuenta_id));
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CONCILIACIONES"
                    DROP CONSTRAINT IF EXISTS "ck_conciliaciones_estado";
                ALTER TABLE "CONCILIACIONES"
                    ADD CONSTRAINT "ck_conciliaciones_estado"
                    CHECK ("estado" IN ('sugerida','conciliada','descartada','cerrada'));

                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                    DROP CONSTRAINT IF EXISTS "ck_movimientos_esperados_estado";
                ALTER TABLE "MOVIMIENTOS_ESPERADOS"
                    ADD CONSTRAINT "ck_movimientos_esperados_estado"
                    CHECK ("estado" IN ('pendiente','satisfecho','vencido','cancelado'));
                """);

            // No revertimos el tipo de deleted_at: los datos existentes ya
            // se convirtieron a timestamptz al confirmar la migracion, y
            // dejarlos en timestamp sin zona haria perder informacion de
            // zona en futuras migraciones que comparen con timestamp with
            // time zone.
        }
    }
}
