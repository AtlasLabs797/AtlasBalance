using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260921090000_OptimizeExtractoRlsPermissionChecks")]
public partial class OptimizeExtractoRlsPermissionChecks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Uncorrelated subqueries let PostgreSQL calculate the authorized set once
        // per statement, instead of repeating signed-context/permission queries
        // for every bank movement. Keep the existing helpers, FORCE RLS and CHECKs.
        // FOR ALL write policies also participate in SELECT, so optimize both.
        migrationBuilder.Sql(
            """
            ALTER POLICY extractos_select ON "EXTRACTOS" USING (
                (SELECT atlas_security.is_admin_or_system())
                OR (deleted_at IS NULL AND cuenta_id IN (
                    SELECT c.id FROM "CUENTAS" c
                    WHERE atlas_security.can_read_cuenta_by_id(c.id)
                ))
            );
            ALTER POLICY extractos_write ON "EXTRACTOS" USING (
                (SELECT atlas_security.is_admin_or_system())
                OR cuenta_id IN (
                    SELECT c.id FROM "CUENTAS" c
                    WHERE atlas_security.can_write_cuenta_by_id(c.id)
                )
            );
            ALTER POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA" USING (
                deleted_at IS NULL AND (
                    (SELECT atlas_security.is_admin_or_system())
                    OR extracto_id IN (
                        WITH readable_accounts AS MATERIALIZED (
                            SELECT c.id FROM "CUENTAS" c
                            WHERE atlas_security.can_read_cuenta_by_id(c.id)
                        )
                        SELECT e.id FROM "EXTRACTOS" e
                        WHERE e.deleted_at IS NULL AND e.cuenta_id IN (
                            SELECT id FROM readable_accounts
                        )
                    )
                )
            );
            ALTER POLICY extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA" USING (
                deleted_at IS NULL AND (
                    (SELECT atlas_security.is_admin_or_system())
                    OR extracto_id IN (
                        WITH writable_accounts AS MATERIALIZED (
                            SELECT c.id FROM "CUENTAS" c
                            WHERE atlas_security.can_write_cuenta_by_id(c.id)
                        )
                        SELECT e.id FROM "EXTRACTOS" e
                        WHERE e.deleted_at IS NULL AND e.cuenta_id IN (
                            SELECT id FROM writable_accounts
                        )
                    )
                )
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER POLICY extractos_select ON "EXTRACTOS" USING (
                atlas_security.is_admin_or_system()
                OR (deleted_at IS NULL AND atlas_security.can_read_cuenta_by_id(cuenta_id))
            );
            ALTER POLICY extractos_write ON "EXTRACTOS" USING (
                atlas_security.can_write_cuenta_by_id(cuenta_id)
            );
            ALTER POLICY extractos_columnas_extra_select ON "EXTRACTOS_COLUMNAS_EXTRA" USING (
                deleted_at IS NULL AND atlas_security.can_read_extracto(extracto_id)
            );
            ALTER POLICY extractos_columnas_extra_write ON "EXTRACTOS_COLUMNAS_EXTRA" USING (
                deleted_at IS NULL AND atlas_security.can_write_extracto(extracto_id)
            );
            """);
    }
}
