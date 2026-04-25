using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCaja.API.Migrations
{
    /// <inheritdoc />
    public partial class UserSessionHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "password_changed_at",
                table: "USUARIOS",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "USUARIOS",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "replace(gen_random_uuid()::text, '-', '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_changed_at",
                table: "USUARIOS");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "USUARIOS");
        }
    }
}
