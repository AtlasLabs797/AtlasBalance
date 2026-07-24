using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720120000_RedactHistoricalConfigurationAudits")]
public sealed class RedactHistoricalConfigurationAudits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "AUDITORIAS"
            SET "detalles_json" =
                CASE
                    WHEN "detalles_json" IS NULL THEN NULL
                    ELSE jsonb_set(
                        jsonb_set("detalles_json", '{old,Valor}', '"[REDACTED]"'::jsonb, false),
                        '{new,Valor}', '"[REDACTED]"'::jsonb, false)
                END,
                "valor_anterior" = CASE WHEN "valor_anterior" IS NULL THEN NULL ELSE '[REDACTED]' END,
                "valor_nuevo" = CASE WHEN "valor_nuevo" IS NULL THEN NULL ELSE '[REDACTED]' END
            WHERE lower(COALESCE("entidad_tipo", '')) = 'configuracion'
              AND (
                    lower(COALESCE(
                        "detalles_json" #>> '{old,Clave}',
                        "detalles_json" #>> '{new,Clave}',
                        "detalles_json" #>> '{old,clave}',
                        "detalles_json" #>> '{new,clave}',
                        "detalles_json" #>> '{clave}',
                        '')) IN (
                            'smtp_password',
                            'exchange_rate_api_key',
                            'openrouter_api_key',
                            'openai_api_key',
                            'minimax_api_key',
                            'google_drive_oauth_client_secret',
                            'backup_cloud_encryption_key',
                            'github_update_token',
                            'jwt_signing_key',
                            'rls_context_secret',
                            'watchdog_shared_secret'
                        )
                    OR lower(COALESCE("detalles_json" #>> '{old,EsSecreto}', 'false')) = 'true'
                    OR lower(COALESCE("detalles_json" #>> '{new,EsSecreto}', 'false')) = 'true'
                    OR lower(COALESCE("detalles_json" #>> '{old,es_secreto}', 'false')) = 'true'
                    OR lower(COALESCE("detalles_json" #>> '{new,es_secreto}', 'false')) = 'true'
                  );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // La redaccion de secretos es deliberadamente irreversible.
    }
}
