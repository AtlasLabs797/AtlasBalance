using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class V0203_Hardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notas",
                table: "IMPORTACION_LOTES",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_secreto",
                table: "CONFIGURACION",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_extractos_cuenta_id_fecha_monto",
                table: "EXTRACTOS",
                columns: new[] { "cuenta_id", "fecha", "monto" });

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_es_secreto",
                table: "CONFIGURACION",
                column: "es_secreto");

            migrationBuilder.DropForeignKey(
                name: "fk_conciliaciones_movimientos_esperados_movimiento_esperado_id",
                table: "CONCILIACIONES");

            migrationBuilder.DropForeignKey(
                name: "fk_extractos_columnas_extra_extractos_extracto_id",
                table: "EXTRACTOS_COLUMNAS_EXTRA");

            migrationBuilder.DropForeignKey(
                name: "fk_importacion_lote_filas_importacion_lotes_lote_id",
                table: "IMPORTACION_LOTE_FILAS");

            migrationBuilder.DropForeignKey(
                name: "fk_revision_extracto_estados_extractos_extracto_id",
                table: "REVISION_EXTRACTO_ESTADOS");

            migrationBuilder.AddForeignKey(
                name: "fk_conciliaciones_movimientos_esperados_movimiento_esperado_id",
                table: "CONCILIACIONES",
                column: "movimiento_esperado_id",
                principalTable: "MOVIMIENTOS_ESPERADOS",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_extractos_columnas_extra_extractos_extracto_id",
                table: "EXTRACTOS_COLUMNAS_EXTRA",
                column: "extracto_id",
                principalTable: "EXTRACTOS",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_importacion_lote_filas_importacion_lotes_lote_id",
                table: "IMPORTACION_LOTE_FILAS",
                column: "lote_id",
                principalTable: "IMPORTACION_LOTES",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_revision_extracto_estados_extractos_extracto_id",
                table: "REVISION_EXTRACTO_ESTADOS",
                column: "extracto_id",
                principalTable: "EXTRACTOS",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conciliaciones_movimientos_esperados_movimiento_esperado_id",
                table: "CONCILIACIONES");

            migrationBuilder.DropForeignKey(
                name: "fk_extractos_columnas_extra_extractos_extracto_id",
                table: "EXTRACTOS_COLUMNAS_EXTRA");

            migrationBuilder.DropForeignKey(
                name: "fk_importacion_lote_filas_importacion_lotes_lote_id",
                table: "IMPORTACION_LOTE_FILAS");

            migrationBuilder.DropForeignKey(
                name: "fk_revision_extracto_estados_extractos_extracto_id",
                table: "REVISION_EXTRACTO_ESTADOS");

            migrationBuilder.DropIndex(
                name: "ix_extractos_cuenta_id_fecha_monto",
                table: "EXTRACTOS");

            migrationBuilder.DropIndex(
                name: "ix_configuracion_es_secreto",
                table: "CONFIGURACION");

            migrationBuilder.DropColumn(
                name: "notas",
                table: "IMPORTACION_LOTES");

            migrationBuilder.DropColumn(
                name: "es_secreto",
                table: "CONFIGURACION");

            migrationBuilder.AddForeignKey(
                name: "fk_conciliaciones_movimientos_esperados_movimiento_esperado_id",
                table: "CONCILIACIONES",
                column: "movimiento_esperado_id",
                principalTable: "MOVIMIENTOS_ESPERADOS",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_extractos_columnas_extra_extractos_extracto_id",
                table: "EXTRACTOS_COLUMNAS_EXTRA",
                column: "extracto_id",
                principalTable: "EXTRACTOS",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_importacion_lote_filas_importacion_lotes_lote_id",
                table: "IMPORTACION_LOTE_FILAS",
                column: "lote_id",
                principalTable: "IMPORTACION_LOTES",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_revision_extracto_estados_extractos_extracto_id",
                table: "REVISION_EXTRACTO_ESTADOS",
                column: "extracto_id",
                principalTable: "EXTRACTOS",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
