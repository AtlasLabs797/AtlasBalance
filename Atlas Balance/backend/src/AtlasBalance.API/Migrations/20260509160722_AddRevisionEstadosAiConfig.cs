using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionEstadosAiConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "REVISION_EXTRACTO_ESTADOS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extracto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usuario_modificacion_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_extracto_estados", x => x.id);
                    table.ForeignKey(
                        name: "fk_revision_extracto_estados_extractos_extracto_id",
                        column: x => x.extracto_id,
                        principalTable: "EXTRACTOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_revision_extracto_estados_usuarios_usuario_modificacion_id",
                        column: x => x.usuario_modificacion_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revision_extracto_estados_estado",
                table: "REVISION_EXTRACTO_ESTADOS",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_revision_extracto_estados_extracto_id_tipo",
                table: "REVISION_EXTRACTO_ESTADOS",
                columns: new[] { "extracto_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_revision_extracto_estados_tipo",
                table: "REVISION_EXTRACTO_ESTADOS",
                column: "tipo");

            migrationBuilder.CreateIndex(
                name: "ix_revision_extracto_estados_usuario_modificacion_id",
                table: "REVISION_EXTRACTO_ESTADOS",
                column: "usuario_modificacion_id");

            migrationBuilder.Sql("""
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS" FORCE ROW LEVEL SECURITY;

                CREATE POLICY revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS"
                    FOR SELECT
                    USING (atlas_security.can_read_extracto(extracto_id));

                CREATE POLICY revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS"
                    FOR ALL
                    USING (atlas_security.can_write_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_write_extracto(extracto_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS revision_extracto_estados_select ON "REVISION_EXTRACTO_ESTADOS";
                DROP POLICY IF EXISTS revision_extracto_estados_write ON "REVISION_EXTRACTO_ESTADOS";
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "REVISION_EXTRACTO_ESTADOS" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "REVISION_EXTRACTO_ESTADOS");
        }
    }
}
