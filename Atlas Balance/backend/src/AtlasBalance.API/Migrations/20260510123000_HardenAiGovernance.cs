using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class HardenAiGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "puede_usar_ia",
                table: "USUARIOS",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_puede_usar_ia",
                table: "USUARIOS",
                column: "puede_usar_ia");

            migrationBuilder.Sql("""
                UPDATE "USUARIOS"
                SET "puede_usar_ia" = TRUE
                WHERE "rol" = 0;

                INSERT INTO "CONFIGURACION" ("clave", "valor", "tipo", "descripcion", "fecha_modificacion", "usuario_modificacion_id")
                VALUES
                    ('ai_enabled', 'false', 'bool', 'Interruptor global de IA financiera', NOW(), NULL),
                    ('ai_requests_per_minute', '6', 'int', 'Consultas maximas de IA por usuario y minuto', NOW(), NULL),
                    ('ai_requests_per_hour', '30', 'int', 'Consultas maximas de IA por usuario y hora', NOW(), NULL),
                    ('ai_requests_per_day', '60', 'int', 'Consultas maximas de IA por usuario y dia', NOW(), NULL),
                    ('ai_global_requests_per_day', '300', 'int', 'Consultas maximas globales de IA por dia', NOW(), NULL),
                    ('ai_monthly_budget_eur', '0', 'decimal', 'Presupuesto mensual estimado de IA en EUR; 0 desactiva bloqueo por coste', NOW(), NULL),
                    ('ai_total_budget_eur', '0', 'decimal', 'Presupuesto total estimado de IA en EUR; 0 desactiva bloqueo por coste', NOW(), NULL),
                    ('ai_budget_warning_percent', '80', 'int', 'Porcentaje de presupuesto para mostrar aviso de IA', NOW(), NULL),
                    ('ai_input_cost_per_1m_tokens_eur', '0', 'decimal', 'Coste estimado de entrada por millon de tokens', NOW(), NULL),
                    ('ai_output_cost_per_1m_tokens_eur', '0', 'decimal', 'Coste estimado de salida por millon de tokens', NOW(), NULL),
                    ('ai_max_input_tokens', '6000', 'int', 'Tokens maximos aproximados de contexto por consulta IA', NOW(), NULL),
                    ('ai_max_output_tokens', '700', 'int', 'Tokens maximos de respuesta por consulta IA', NOW(), NULL),
                    ('ai_max_context_rows', '80', 'int', 'Movimientos relevantes maximos enviados a IA', NOW(), NULL),
                    ('ai_usage_month_key', '', 'string', 'Mes contable actual de uso IA', NOW(), NULL),
                    ('ai_usage_month_cost_eur', '0', 'decimal', 'Coste estimado de IA acumulado en el mes actual', NOW(), NULL),
                    ('ai_usage_total_cost_eur', '0', 'decimal', 'Coste estimado total acumulado de IA', NOW(), NULL),
                    ('ai_usage_total_requests', '0', 'int', 'Consultas totales de IA registradas', NOW(), NULL),
                    ('ai_usage_last_user_id', '', 'string', 'Ultimo usuario que uso IA', NOW(), NULL),
                    ('ai_usage_last_at_utc', '', 'datetime', 'Ultimo uso de IA en UTC', NOW(), NULL)
                ON CONFLICT ("clave") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CONFIGURACION"
                WHERE "clave" IN (
                    'ai_enabled',
                    'ai_requests_per_minute',
                    'ai_requests_per_hour',
                    'ai_requests_per_day',
                    'ai_global_requests_per_day',
                    'ai_monthly_budget_eur',
                    'ai_total_budget_eur',
                    'ai_budget_warning_percent',
                    'ai_input_cost_per_1m_tokens_eur',
                    'ai_output_cost_per_1m_tokens_eur',
                    'ai_max_input_tokens',
                    'ai_max_output_tokens',
                    'ai_max_context_rows',
                    'ai_usage_month_key',
                    'ai_usage_month_cost_eur',
                    'ai_usage_total_cost_eur',
                    'ai_usage_total_requests',
                    'ai_usage_last_user_id',
                    'ai_usage_last_at_utc'
                );
                """);

            migrationBuilder.DropIndex(
                name: "ix_usuarios_puede_usar_ia",
                table: "USUARIOS");

            migrationBuilder.DropColumn(
                name: "puede_usar_ia",
                table: "USUARIOS");
        }
    }
}
