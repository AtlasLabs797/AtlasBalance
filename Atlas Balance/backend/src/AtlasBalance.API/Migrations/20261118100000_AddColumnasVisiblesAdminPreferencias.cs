using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118100000_AddColumnasVisiblesAdminPreferencias")]
public partial class AddColumnasVisiblesAdminPreferencias : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // S-2: separacion entre la restriccion de columnas que fija el
        // administrador y la preferencia de vista del usuario. Hasta ahora
        // ambas escribian en la misma columna (columnas_visibles) y el PUT del
        // usuario pisaba la regla del administrador.
        migrationBuilder.AddColumn<string>(name: "columnas_visibles_admin", table: "PREFERENCIAS_USUARIO_CUENTA", type: "jsonb", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "columnas_visibles_admin", table: "PREFERENCIAS_USUARIO_CUENTA");
    }
}
