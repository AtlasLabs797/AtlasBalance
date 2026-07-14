using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260703120000_AddExtractoDesgloses")]
    public partial class AddExtractoDesgloses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EXTRACTOS_DESGLOSES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extracto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    tercero_nombre = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    usuario_creacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usuario_modificacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extractos_desgloses", x => x.id);
                    table.ForeignKey(
                        name: "fk_extractos_desgloses_extractos_extracto_id",
                        column: x => x.extracto_id,
                        principalTable: "EXTRACTOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_desgloses_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_desgloses_usuarios_usuario_creacion_id",
                        column: x => x.usuario_creacion_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_desgloses_usuarios_usuario_modificacion_id",
                        column: x => x.usuario_modificacion_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extractos_desgloses_deleted_at",
                table: "EXTRACTOS_DESGLOSES",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_desgloses_deleted_by_id",
                table: "EXTRACTOS_DESGLOSES",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_desgloses_extracto_id",
                table: "EXTRACTOS_DESGLOSES",
                column: "extracto_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_desgloses_extracto_id_orden",
                table: "EXTRACTOS_DESGLOSES",
                columns: new[] { "extracto_id", "orden" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_desgloses_usuario_creacion_id",
                table: "EXTRACTOS_DESGLOSES",
                column: "usuario_creacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_desgloses_usuario_modificacion_id",
                table: "EXTRACTOS_DESGLOSES",
                column: "usuario_modificacion_id");

            migrationBuilder.Sql("""
                ALTER TABLE "EXTRACTOS_DESGLOSES" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "EXTRACTOS_DESGLOSES" FORCE ROW LEVEL SECURITY;

                CREATE POLICY extractos_desgloses_select ON "EXTRACTOS_DESGLOSES"
                    FOR SELECT USING (deleted_at IS NULL AND atlas_security.can_read_extracto(extracto_id));

                CREATE POLICY extractos_desgloses_write ON "EXTRACTOS_DESGLOSES"
                    FOR ALL USING (atlas_security.can_write_extracto(extracto_id))
                    WITH CHECK (atlas_security.can_write_extracto(extracto_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS extractos_desgloses_select ON "EXTRACTOS_DESGLOSES";
                DROP POLICY IF EXISTS extractos_desgloses_write ON "EXTRACTOS_DESGLOSES";
                ALTER TABLE "EXTRACTOS_DESGLOSES" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "EXTRACTOS_DESGLOSES" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "EXTRACTOS_DESGLOSES");
        }
    }
}
