using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260510180000_AddOpenAiProviderConfig")]
    public partial class AddOpenAiProviderConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "CONFIGURACION" ("clave", "valor", "tipo", "descripcion", "fecha_modificacion", "usuario_modificacion_id")
                VALUES ('openai_api_key', '', 'string', 'API key de OpenAI protegida', NOW(), NULL)
                ON CONFLICT ("clave") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CONFIGURACION"
                WHERE "clave" = 'openai_api_key';
                """);
        }
    }
}
