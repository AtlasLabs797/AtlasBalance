using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.07: completa en las tres tablas hijas de EXTRACTOS el mismo backstop
    // de soft-delete que 20260716120000_HardenFinancialV0202Rls aplico a
    // IMPORTACION_LOTE_FILAS, MOVIMIENTOS_ESPERADOS y CONCILIACIONES.
    //
    // Causa raiz: las policies de EXTRACTOS_COLUMNAS_EXTRA (2026-05-01) y
    // REVISION_EXTRACTO_ESTADOS (2026-06-01) se escribieron antes de que
    // 20260710_AddSoftDeleteToImportacionFilaColumnaExtraRevision anadiera la
    // columna deleted_at a esas tablas, asi que nunca la filtraron. Hoy el
    // unico filtro de soft-delete en esas dos tablas es el query filter global
    // de EF Core; RLS no hace de backstop y cualquier consulta con
    // IgnoreQueryFilters, o un UPDATE por id, alcanza filas ya borradas.
    //
    // EXTRACTOS_DESGLOSES si filtra deleted_at en su SELECT, pero no en su
    // policy de escritura.
    //
    // El filtro va solo en USING, nunca en WITH CHECK: el WITH CHECK evalua la
    // fila nueva, y exigirle deleted_at IS NULL bloquearia el propio borrado
    // logico. Ese es exactamente el fallo que
    // 20260731090000_FixExportacionesPurgaRlsWithCheck corrige en EXPORTACIONES.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260731091000_HardenSoftDeleteBackstopHijosExtracto")]
    public partial class HardenSoftDeleteBackstopHijosExtracto : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA";
                CREATE POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA"
                    FOR SELECT USING (deleted_at IS NULL AND atlas_security.can_read_extracto(extracto_id));

                DROP POLICY IF EXISTS extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA";
                CREATE POLICY extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA"
                    FOR ALL USING (deleted_at IS NULL AND atlas_security.can_write_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_write_extracto(extracto_id));

                DROP POLICY IF EXISTS revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS";
                CREATE POLICY revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS"
                    FOR SELECT USING (deleted_at IS NULL AND atlas_security.can_read_extracto(extracto_id));

                DROP POLICY IF EXISTS revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS";
                CREATE POLICY revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS"
                    FOR ALL USING (deleted_at IS NULL AND atlas_security.can_review_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_review_extracto(extracto_id));

                DROP POLICY IF EXISTS extractos_desgloses_write ON "EXTRACTOS_DESGLOSES";
                CREATE POLICY extractos_desgloses_write ON "EXTRACTOS_DESGLOSES"
                    FOR ALL USING (deleted_at IS NULL AND atlas_security.can_write_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_write_extracto(extracto_id));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA";
                CREATE POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA"
                    FOR SELECT USING (atlas_security.can_read_extracto(extracto_id));

                DROP POLICY IF EXISTS extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA";
                CREATE POLICY extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA"
                    FOR ALL USING (atlas_security.can_write_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_write_extracto(extracto_id));

                DROP POLICY IF EXISTS revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS";
                CREATE POLICY revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS"
                    FOR SELECT USING (atlas_security.can_read_extracto(extracto_id));

                DROP POLICY IF EXISTS revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS";
                CREATE POLICY revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS"
                    FOR ALL USING (atlas_security.can_review_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_review_extracto(extracto_id));

                DROP POLICY IF EXISTS extractos_desgloses_write ON "EXTRACTOS_DESGLOSES";
                CREATE POLICY extractos_desgloses_write ON "EXTRACTOS_DESGLOSES"
                    FOR ALL USING (atlas_security.can_write_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_write_extracto(extracto_id));
                """);
        }
    }
}
