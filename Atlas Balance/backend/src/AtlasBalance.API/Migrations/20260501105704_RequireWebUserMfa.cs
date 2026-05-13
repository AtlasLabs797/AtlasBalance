using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class RequireWebUserMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mfa_enabled",
                table: "USUARIOS",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_enabled_at",
                table: "USUARIOS",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "mfa_last_accepted_step",
                table: "USUARIOS",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_secret",
                table: "USUARIOS",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_mfa_enabled",
                table: "USUARIOS",
                column: "mfa_enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_mfa_enabled",
                table: "USUARIOS");

            migrationBuilder.DropColumn(
                name: "mfa_enabled",
                table: "USUARIOS");

            migrationBuilder.DropColumn(
                name: "mfa_enabled_at",
                table: "USUARIOS");

            migrationBuilder.DropColumn(
                name: "mfa_last_accepted_step",
                table: "USUARIOS");

            migrationBuilder.DropColumn(
                name: "mfa_secret",
                table: "USUARIOS");
        }
    }
}
