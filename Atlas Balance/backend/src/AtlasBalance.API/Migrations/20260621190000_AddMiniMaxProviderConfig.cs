using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260621190000_AddMiniMaxProviderConfig")]
    public partial class AddMiniMaxProviderConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "CONFIGURACION" ("clave", "valor", "tipo", "descripcion", "fecha_modificacion", "usuario_modificacion_id")
                VALUES ('minimax_api_key', '', 'string', 'API key de MiniMax protegida', NOW(), NULL)
                ON CONFLICT ("clave") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CONFIGURACION"
                WHERE "clave" = 'minimax_api_key';
                """);
        }
    }
}
