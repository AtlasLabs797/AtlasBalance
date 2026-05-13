using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractoImportacionFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_importacion",
                table: "EXTRACTOS",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "importacion_fila_origen",
                table: "EXTRACTOS",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "importacion_fingerprint",
                table: "EXTRACTOS",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "importacion_lote_hash",
                table: "EXTRACTOS",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_extractos_importacion_lote_hash",
                table: "EXTRACTOS",
                column: "importacion_lote_hash");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_cuenta_id_importacion_fingerprint",
                table: "EXTRACTOS",
                columns: new[] { "cuenta_id", "importacion_fingerprint" },
                unique: true,
                filter: "\"importacion_fingerprint\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_extractos_importacion_lote_hash",
                table: "EXTRACTOS");

            migrationBuilder.DropIndex(
                name: "ix_extractos_cuenta_id_importacion_fingerprint",
                table: "EXTRACTOS");

            migrationBuilder.DropColumn(
                name: "fecha_importacion",
                table: "EXTRACTOS");

            migrationBuilder.DropColumn(
                name: "importacion_fila_origen",
                table: "EXTRACTOS");

            migrationBuilder.DropColumn(
                name: "importacion_fingerprint",
                table: "EXTRACTOS");

            migrationBuilder.DropColumn(
                name: "importacion_lote_hash",
                table: "EXTRACTOS");
        }
    }
}
