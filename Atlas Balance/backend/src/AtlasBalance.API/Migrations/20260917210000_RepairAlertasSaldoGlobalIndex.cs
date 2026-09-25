using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260917210000_RepairAlertasSaldoGlobalIndex")]
public partial class RepairAlertasSaldoGlobalIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- La migración 20260414200917 creó este índice solo con
            -- cuenta_id IS NULL, haciendo imposible combinar una alerta
            -- global con una alerta por tipo de titular.
            DROP INDEX IF EXISTS ix_alertas_saldo_global_unica;
            CREATE UNIQUE INDEX IF NOT EXISTS ix_alertas_saldo_global_unica
            ON "ALERTAS_SALDO" ((1))
            WHERE cuenta_id IS NULL AND tipo_titular IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS ix_alertas_saldo_global_unica;
            CREATE UNIQUE INDEX IF NOT EXISTS ix_alertas_saldo_global_unica
            ON "ALERTAS_SALDO" ((1))
            WHERE cuenta_id IS NULL;
            """);
    }
}
