using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCuentaNotasExtractoComentarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comentarios",
                table: "EXTRACTOS",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notas",
                table: "CUENTAS",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comentarios",
                table: "EXTRACTOS");

            migrationBuilder.DropColumn(
                name: "notas",
                table: "CUENTAS");
        }
    }
}
