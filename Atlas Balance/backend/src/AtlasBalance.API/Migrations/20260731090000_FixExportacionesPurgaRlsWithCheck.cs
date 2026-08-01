using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.07: 20260724090000_FixExportacionesWriteRlsDeletedAtCheck anadio
    // deleted_at IS NULL al WITH CHECK de exportaciones_write para cerrar una
    // asimetria, pero lo hizo sin replicar la salida is_admin_or_system() que
    // el USING si tiene. Efecto: LimpiezaExportacionesJob (contexto system)
    // pasa el USING pero su UPDATE deja deleted_at no nulo, asi que el
    // WITH CHECK lo rechaza con "new row violates row-level security policy" y
    // la purga por retencion de ficheros de exportacion con PII deja de
    // funcionar en cada ejecucion.
    //
    // El WITH CHECK recupera la simetria con el USING. Para un usuario normal
    // no cambia nada: sigue exigiendo deleted_at IS NULL y permiso de
    // exportacion sobre la cuenta, asi que no puede borrar logicamente una
    // exportacion (ademas no existe endpoint que lo permita: el unico camino
    // de soft-delete de EXPORTACIONES es el job, que corre como system).
    [DbContext(typeof(AppDbContext))]
    [Migration("20260731090000_FixExportacionesPurgaRlsWithCheck")]
    public partial class FixExportacionesPurgaRlsWithCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
                CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                    FOR ALL USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id)))
                    WITH CHECK (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id)));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
                CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                    FOR ALL USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id)))
                    WITH CHECK (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id));
                """);
        }
    }
}
