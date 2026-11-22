using System;
using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

// S-13: vida maxima de sesion. El ancla se fija al abrir la sesion y se hereda
// en cada rotacion del refresh token. Escrita a mano (no con `dotnet ef
// migrations add`) porque el modelo arrastra una deriva previa con el snapshot
// y el scaffold generaba una migracion con cambios ajenos a este hallazgo.
[DbContext(typeof(AppDbContext))]
[Migration("20261116210000_AddSesionIniEnRefreshToken")]
public partial class AddSesionIniEnRefreshToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "sesion_ini_en",
            table: "REFRESH_TOKENS",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "sesion_ini_en",
            table: "REFRESH_TOKENS");
    }
}
