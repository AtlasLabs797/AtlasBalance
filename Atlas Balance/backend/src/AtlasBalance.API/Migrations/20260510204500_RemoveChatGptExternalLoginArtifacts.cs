using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260510204500_RemoveChatGptExternalLoginArtifacts")]
public partial class RemoveChatGptExternalLoginArtifacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "AUDITORIA_INTEGRACIONES"
            WHERE "token_id" IN (
                SELECT "id"
                FROM "INTEGRATION_TOKENS"
                WHERE "tipo" = 'chatgpt'
            );

            DELETE FROM "INTEGRATION_TOKENS"
            WHERE "tipo" = 'chatgpt';

            DROP TABLE IF EXISTS "CHATGPT_OAUTH_CODES";

            ALTER TABLE "INTEGRATION_TOKENS"
            DROP COLUMN IF EXISTS "expira_en";

            DELETE FROM "CONFIGURACION"
            WHERE "clave" IN (
                'chatgpt_oauth_enabled',
                'chatgpt_oauth_client_id',
                'chatgpt_oauth_client_secret',
                'chatgpt_oauth_redirect_uris',
                'chatgpt_oauth_token_template_id',
                'chatgpt_oauth_access_token_minutes',
                'chatgpt_oauth_scope',
                'chatgpt_oauth_app_base_url'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
