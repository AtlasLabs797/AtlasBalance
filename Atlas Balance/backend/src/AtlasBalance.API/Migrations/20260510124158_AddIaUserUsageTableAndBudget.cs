using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddIaUserUsageTableAndBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IA_USO_USUARIOS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month_key = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    requests = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    coste_estimado_eur = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    fecha_ultimo_uso_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ia_uso_usuarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_ia_uso_usuarios_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ia_uso_usuarios_fecha_modificacion",
                table: "IA_USO_USUARIOS",
                column: "fecha_modificacion");

            migrationBuilder.CreateIndex(
                name: "ix_ia_uso_usuarios_usuario_id_month_key",
                table: "IA_USO_USUARIOS",
                columns: new[] { "usuario_id", "month_key" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "CONFIGURACION" ("clave", "valor", "tipo", "descripcion", "fecha_modificacion", "usuario_modificacion_id")
                VALUES ('ai_user_monthly_budget_eur', '0', 'decimal', 'Presupuesto mensual estimado de IA por usuario en EUR; 0 desactiva bloqueo por coste individual', NOW(), NULL)
                ON CONFLICT ("clave") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CONFIGURACION"
                WHERE "clave" = 'ai_user_monthly_budget_eur';
                """);

            migrationBuilder.DropTable(
                name: "IA_USO_USUARIOS");
        }
    }
}
