using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.06: cierra la asimetria en exportaciones_write (creada en
    // 20260522103000_HardenRlsSoftDeleteBackstop): el USING exige
    // deleted_at IS NULL para las filas candidatas, pero el WITH CHECK no lo
    // exigia para los valores nuevos de INSERT/UPDATE. Hoy no era explotable
    // (USING ya filtra las filas borradas antes de evaluar WITH CHECK en un
    // UPDATE, e INSERT siempre deja deleted_at en NULL), pero dejaba la
    // policy asimetrica frente al resto de policies FOR ALL de este mismo
    // archivo. Migracion manuscrita-SQL, mismo patron que las anteriores de RLS.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260724090000_FixExportacionesWriteRlsDeletedAtCheck")]
    public partial class FixExportacionesWriteRlsDeletedAtCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
                CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                    FOR ALL USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id)))
                    WITH CHECK (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS exportaciones_write ON "EXPORTACIONES";
                CREATE POLICY exportaciones_write ON "EXPORTACIONES"
                    FOR ALL USING (atlas_security.is_admin_or_system() OR (deleted_at IS NULL AND atlas_security.can_export_cuenta_by_id(cuenta_id)))
                    WITH CHECK (atlas_security.can_export_cuenta_by_id(cuenta_id));
                """);
        }
    }
}
