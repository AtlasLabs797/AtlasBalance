using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260626180000_ReduceUserRolesToThreeTypes")]
public partial class ReduceUserRolesToThreeTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "USUARIOS"
            SET "rol" = 2
            WHERE "rol" IN (3, 4);

            DROP TYPE IF EXISTS rol_usuario;
            CREATE TYPE rol_usuario AS ENUM ('admin', 'gerente', 'empleado');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TYPE IF EXISTS rol_usuario;
            CREATE TYPE rol_usuario AS ENUM ('admin', 'gerente', 'empleado_ultra', 'empleado_plus', 'empleado');
            """);
    }
}
