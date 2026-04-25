using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCaja.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPuedeVerCuentasPermiso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "puede_ver_cuentas",
                table: "PERMISOS_USUARIO",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "PERMISOS_USUARIO"
                SET "puede_ver_cuentas" = TRUE
                WHERE "cuenta_id" IS NOT NULL
                   OR "titular_id" IS NOT NULL
                   OR "puede_agregar_lineas" = TRUE
                   OR "puede_editar_lineas" = TRUE
                   OR "puede_eliminar_lineas" = TRUE
                   OR "puede_importar" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "puede_ver_cuentas",
                table: "PERMISOS_USUARIO");
        }
    }
}
