using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCaja.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPlazoFijoAutonomosAlertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_alertas_saldo_cuenta_id",
                table: "ALERTAS_SALDO",
                newName: "ix_alertas_saldo_cuenta_id_unique");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:estado_plazo_fijo", "activo,proximo_vencer,vencido,renovado,cancelado")
                .Annotation("Npgsql:Enum:estado_proceso", "pending,success,failed")
                .Annotation("Npgsql:Enum:estado_token_integracion", "activo,revocado")
                .Annotation("Npgsql:Enum:fuente_tipo_cambio", "api,manual")
                .Annotation("Npgsql:Enum:rol_usuario", "admin,gerente,empleado_ultra,empleado_plus,empleado")
                .Annotation("Npgsql:Enum:tipo_cuenta", "normal,efectivo,plazo_fijo")
                .Annotation("Npgsql:Enum:tipo_proceso", "auto,manual")
                .Annotation("Npgsql:Enum:tipo_titular", "empresa,particular,autonomo")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .OldAnnotation("Npgsql:Enum:estado_proceso", "pending,success,failed")
                .OldAnnotation("Npgsql:Enum:estado_token_integracion", "activo,revocado")
                .OldAnnotation("Npgsql:Enum:fuente_tipo_cambio", "api,manual")
                .OldAnnotation("Npgsql:Enum:rol_usuario", "admin,gerente,empleado_ultra,empleado_plus,empleado")
                .OldAnnotation("Npgsql:Enum:tipo_proceso", "auto,manual")
                .OldAnnotation("Npgsql:Enum:tipo_titular", "empresa,particular")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.AddColumn<int>(
                name: "tipo_cuenta",
                table: "CUENTAS",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "CUENTAS"
                SET "tipo_cuenta" = 1
                WHERE "es_efectivo" = TRUE;
                """);

            migrationBuilder.AddColumn<int>(
                name: "tipo_titular",
                table: "ALERTAS_SALDO",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PLAZOS_FIJOS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_referencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    interes_previsto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    renovable = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    fecha_ultima_notificacion = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_renovacion = table.Column<DateOnly>(type: "date", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plazos_fijos", x => x.id);
                    table.ForeignKey(
                        name: "fk_plazos_fijos_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plazos_fijos_cuentas_cuenta_referencia_id",
                        column: x => x.cuenta_referencia_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plazos_fijos_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_tipo_cuenta",
                table: "CUENTAS",
                column: "tipo_cuenta");

            migrationBuilder.CreateIndex(
                name: "ix_alertas_saldo_tipo_titular_unique",
                table: "ALERTAS_SALDO",
                column: "tipo_titular",
                unique: true,
                filter: "\"cuenta_id\" IS NULL AND \"tipo_titular\" IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_alertas_saldo_global_unique
                ON "ALERTAS_SALDO" ((1))
                WHERE "cuenta_id" IS NULL AND "tipo_titular" IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "PLAZOS_FIJOS"
                ADD CONSTRAINT ck_plazos_fijos_fechas
                CHECK ("fecha_vencimiento" >= "fecha_inicio");
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "PLAZOS_FIJOS"
                ADD CONSTRAINT ck_plazos_fijos_interes_no_negativo
                CHECK ("interes_previsto" IS NULL OR "interes_previsto" >= 0);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_plazos_fijos_cuenta_id",
                table: "PLAZOS_FIJOS",
                column: "cuenta_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plazos_fijos_cuenta_referencia_id",
                table: "PLAZOS_FIJOS",
                column: "cuenta_referencia_id");

            migrationBuilder.CreateIndex(
                name: "ix_plazos_fijos_deleted_at",
                table: "PLAZOS_FIJOS",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_plazos_fijos_deleted_by_id",
                table: "PLAZOS_FIJOS",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_plazos_fijos_estado",
                table: "PLAZOS_FIJOS",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_plazos_fijos_fecha_vencimiento",
                table: "PLAZOS_FIJOS",
                column: "fecha_vencimiento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PLAZOS_FIJOS");

            migrationBuilder.DropIndex(
                name: "ix_cuentas_tipo_cuenta",
                table: "CUENTAS");

            migrationBuilder.DropIndex(
                name: "ix_alertas_saldo_tipo_titular_unique",
                table: "ALERTAS_SALDO");

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_alertas_saldo_global_unique;
                """);

            migrationBuilder.DropColumn(
                name: "tipo_cuenta",
                table: "CUENTAS");

            migrationBuilder.DropColumn(
                name: "tipo_titular",
                table: "ALERTAS_SALDO");

            migrationBuilder.RenameIndex(
                name: "ix_alertas_saldo_cuenta_id_unique",
                table: "ALERTAS_SALDO",
                newName: "ix_alertas_saldo_cuenta_id");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:estado_proceso", "pending,success,failed")
                .Annotation("Npgsql:Enum:estado_token_integracion", "activo,revocado")
                .Annotation("Npgsql:Enum:fuente_tipo_cambio", "api,manual")
                .Annotation("Npgsql:Enum:rol_usuario", "admin,gerente,empleado_ultra,empleado_plus,empleado")
                .Annotation("Npgsql:Enum:tipo_proceso", "auto,manual")
                .Annotation("Npgsql:Enum:tipo_titular", "empresa,particular")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .OldAnnotation("Npgsql:Enum:estado_plazo_fijo", "activo,proximo_vencer,vencido,renovado,cancelado")
                .OldAnnotation("Npgsql:Enum:estado_proceso", "pending,success,failed")
                .OldAnnotation("Npgsql:Enum:estado_token_integracion", "activo,revocado")
                .OldAnnotation("Npgsql:Enum:fuente_tipo_cambio", "api,manual")
                .OldAnnotation("Npgsql:Enum:rol_usuario", "admin,gerente,empleado_ultra,empleado_plus,empleado")
                .OldAnnotation("Npgsql:Enum:tipo_cuenta", "normal,efectivo,plazo_fijo")
                .OldAnnotation("Npgsql:Enum:tipo_proceso", "auto,manual")
                .OldAnnotation("Npgsql:Enum:tipo_titular", "empresa,particular,autonomo")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");
        }
    }
}
