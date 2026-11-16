using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261116090000_AddOrdenColumnasPreferencias")]
public partial class AddOrdenColumnasPreferencias : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "orden_columnas", table: "PREFERENCIAS_USUARIO_CUENTA", type: "jsonb", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "orden_columnas", table: "PREFERENCIAS_USUARIO_CUENTA");
    }
}
