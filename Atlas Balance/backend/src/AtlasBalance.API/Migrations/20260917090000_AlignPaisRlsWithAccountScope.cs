using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260917090000_AlignPaisRlsWithAccountScope")]
public partial class AlignPaisRlsWithAccountScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS paises_select ON "PAISES";
            CREATE POLICY paises_select ON "PAISES"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (
                        deleted_at IS NULL
                        AND activo = true
                        AND atlas_security.is_user_mode()
                        AND EXISTS (
                            SELECT 1
                            FROM "CUENTAS" c
                            JOIN "TITULARES" t ON t.id = c.titular_id
                            WHERE c.pais_id = "PAISES".id
                              AND c.deleted_at IS NULL
                              AND t.deleted_at IS NULL
                              AND atlas_security.can_read_cuenta(c.id, c.titular_id, c.pais_id)
                        )
                    )
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS paises_select ON "PAISES";
            CREATE POLICY paises_select ON "PAISES"
                FOR SELECT USING (
                    atlas_security.is_admin_or_system()
                    OR (deleted_at IS NULL AND activo = true AND atlas_security.is_user_mode())
                );
            """);
    }
}
