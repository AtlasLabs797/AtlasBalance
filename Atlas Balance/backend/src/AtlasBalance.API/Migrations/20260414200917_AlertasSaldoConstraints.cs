using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class AlertasSaldoConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_alertas_saldo_cuenta_id",
                table: "ALERTAS_SALDO");

            migrationBuilder.DropIndex(
                name: "ix_alerta_destinatarios_alerta_id_usuario_id",
                table: "ALERTA_DESTINATARIOS");

            migrationBuilder.CreateIndex(
                name: "ix_alertas_saldo_cuenta_id",
                table: "ALERTAS_SALDO",
                column: "cuenta_id",
                unique: true,
                filter: "\"cuenta_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_destinatarios_alerta_id_usuario_id",
                table: "ALERTA_DESTINATARIOS",
                columns: new[] { "alerta_id", "usuario_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS ix_alertas_saldo_global_unica
                ON "ALERTAS_SALDO" ((1))
                WHERE cuenta_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_alertas_saldo_cuenta_id",
                table: "ALERTAS_SALDO");

            migrationBuilder.DropIndex(
                name: "ix_alerta_destinatarios_alerta_id_usuario_id",
                table: "ALERTA_DESTINATARIOS");

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_alertas_saldo_global_unica;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_alertas_saldo_cuenta_id",
                table: "ALERTAS_SALDO",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_destinatarios_alerta_id_usuario_id",
                table: "ALERTA_DESTINATARIOS",
                columns: new[] { "alerta_id", "usuario_id" },
                unique: true);
        }
    }
}
