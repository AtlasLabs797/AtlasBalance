using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02-06 (RLS-FIN-01): endurezco las policies y aplico FORCE RLS en las
    // cuatro tablas del ciclo V-02.02 que quedaron sin FORCE, alineo las
    // policies con el soft-delete ya aplicado en V-02.05 y separo el policy
    // FOR ALL para que un usuario con escritura no vea filas eliminadas en
    // SELECT. La migracion es manuscrita-SQL (mismo patron que las V-02.05)
    // porque el snapshot EF esta desalineado con el modelo tras los cambios
    // de V-02.05; un scaffold EF podria recrear columnas/indices ya presentes.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260716120000_HardenFinancialV0202Rls")]
    public partial class HardenFinancialV0202Rls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- V-02-06: FORCE RLS en las cuatro tablas creadas por
                -- 20260629090000_FinancialHardeningV0202. Sin FORCE, el rol
                -- owner ve TODAS las filas sin filtrar por permisos.
                ALTER TABLE "IMPORTACION_LOTES" FORCE ROW LEVEL SECURITY;
                ALTER TABLE "IMPORTACION_LOTE_FILAS" FORCE ROW LEVEL SECURITY;
                ALTER TABLE "MOVIMIENTOS_ESPERADOS" FORCE ROW LEVEL SECURITY;
                ALTER TABLE "CONCILIACIONES" FORCE ROW LEVEL SECURITY;

                -- V-02-06: IMPORTACION_LOTES
                DROP POLICY IF EXISTS importacion_lotes_select ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_insert ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_update ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_delete ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_write ON "IMPORTACION_LOTES";

                CREATE POLICY importacion_lotes_select ON "IMPORTACION_LOTES"
                    FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
                CREATE POLICY importacion_lotes_insert ON "IMPORTACION_LOTES"
                    FOR INSERT WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                CREATE POLICY importacion_lotes_update ON "IMPORTACION_LOTES"
                    FOR UPDATE USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                    WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                CREATE POLICY importacion_lotes_delete ON "IMPORTACION_LOTES"
                    FOR DELETE USING (atlas_security.can_write_cuenta_by_id(cuenta_id));

                -- V-02-06: IMPORTACION_LOTE_FILAS. El filtro deleted_at cubre
                -- las filas soft-deleted por V-02.05; las policies previas no
                -- lo hacian, asi que un usuario con escritura veia filas
                -- borradas como si estuvieran vivas.
                DROP POLICY IF EXISTS importacion_lote_filas_select ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_insert ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_update ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_delete ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_write ON "IMPORTACION_LOTE_FILAS";

                CREATE POLICY importacion_lote_filas_select ON "IMPORTACION_LOTE_FILAS"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id
                              AND atlas_security.can_read_cuenta_by_id(l.cuenta_id)
                        )
                    );
                CREATE POLICY importacion_lote_filas_insert ON "IMPORTACION_LOTE_FILAS"
                    FOR INSERT WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id
                              AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                        )
                    );
                CREATE POLICY importacion_lote_filas_update ON "IMPORTACION_LOTE_FILAS"
                    FOR UPDATE USING (
                        deleted_at IS NULL
                        AND EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id
                              AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id
                              AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                        )
                    );
                CREATE POLICY importacion_lote_filas_delete ON "IMPORTACION_LOTE_FILAS"
                    FOR DELETE USING (
                        EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id
                              AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                        )
                    );

                -- V-02-06: MOVIMIENTOS_ESPERADOS. Ya tenia deleted_at, ahora
                -- separamos SELECT/INSERT/UPDATE/DELETE para que el policy
                -- FOR ALL no haga visibles filas borradas al usuario con
                -- escritura en el mismo predicado.
                DROP POLICY IF EXISTS movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_insert ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_update ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_delete ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_write ON "MOVIMIENTOS_ESPERADOS";

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

                -- V-02-06: CONCILIACIONES. La columna deleted_at existe desde
                -- V-02.05 pero la policy previa no la filtraba. Separamos
                -- SELECT/INSERT/UPDATE/DELETE.
                DROP POLICY IF EXISTS conciliaciones_select ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_insert ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_update ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_delete ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_write ON "CONCILIACIONES";

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

                -- V-02-06: EXTRACTOS_COLUMNAS_EXTRA y REVISION_EXTRACTO_ESTADOS
                -- recibieron columnas deleted_at en V-02.05 pero su policy
                -- SELECT seguia sin filtrarlas. Solo actualizamos SELECT;
                -- INSERT/UPDATE/DELETE se mantienen igual.
                DROP POLICY IF EXISTS extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA";

                CREATE POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND atlas_security.can_read_extracto(extracto_id)
                    );

                DROP POLICY IF EXISTS revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS";

                CREATE POLICY revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS"
                    FOR SELECT USING (
                        deleted_at IS NULL
                        AND atlas_security.can_read_extracto(extracto_id)
                    );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "IMPORTACION_LOTES" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "IMPORTACION_LOTE_FILAS" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "MOVIMIENTOS_ESPERADOS" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "CONCILIACIONES" NO FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA";
                CREATE POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA"
                    FOR SELECT USING (atlas_security.can_read_extracto(extracto_id));

                DROP POLICY IF EXISTS revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS";
                CREATE POLICY revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS"
                    FOR SELECT USING (atlas_security.can_read_extracto(extracto_id));

                DROP POLICY IF EXISTS importacion_lotes_select ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_insert ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_update ON "IMPORTACION_LOTES";
                DROP POLICY IF EXISTS importacion_lotes_delete ON "IMPORTACION_LOTES";
                CREATE POLICY importacion_lotes_select ON "IMPORTACION_LOTES"
                    FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
                CREATE POLICY importacion_lotes_write ON "IMPORTACION_LOTES"
                    FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                    WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

                DROP POLICY IF EXISTS importacion_lote_filas_select ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_insert ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_update ON "IMPORTACION_LOTE_FILAS";
                DROP POLICY IF EXISTS importacion_lote_filas_delete ON "IMPORTACION_LOTE_FILAS";
                CREATE POLICY importacion_lote_filas_select ON "IMPORTACION_LOTE_FILAS"
                    FOR SELECT USING (
                        EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id AND atlas_security.can_read_cuenta_by_id(l.cuenta_id)
                        )
                    );
                CREATE POLICY importacion_lote_filas_write ON "IMPORTACION_LOTE_FILAS"
                    FOR ALL USING (
                        EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "IMPORTACION_LOTES" l
                            WHERE l.id = lote_id AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                        )
                    );

                DROP POLICY IF EXISTS movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_insert ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_update ON "MOVIMIENTOS_ESPERADOS";
                DROP POLICY IF EXISTS movimientos_esperados_delete ON "MOVIMIENTOS_ESPERADOS";
                CREATE POLICY movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS"
                    FOR SELECT USING (deleted_at IS NULL AND atlas_security.can_read_cuenta_by_id(cuenta_id));
                CREATE POLICY movimientos_esperados_write ON "MOVIMIENTOS_ESPERADOS"
                    FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                    WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

                DROP POLICY IF EXISTS conciliaciones_select ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_insert ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_update ON "CONCILIACIONES";
                DROP POLICY IF EXISTS conciliaciones_delete ON "CONCILIACIONES";
                CREATE POLICY conciliaciones_select ON "CONCILIACIONES"
                    FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
                CREATE POLICY conciliaciones_write ON "CONCILIACIONES"
                    FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                    WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
                """);
        }
    }
}
