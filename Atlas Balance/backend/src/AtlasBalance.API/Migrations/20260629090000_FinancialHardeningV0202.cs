using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260629090000_FinancialHardeningV0202")]
public partial class FinancialHardeningV0202 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "puede_revisar_lineas",
            table: "PERMISOS_USUARIO",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "puede_aprobar_importaciones",
            table: "PERMISOS_USUARIO",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "puede_conciliar",
            table: "PERMISOS_USUARIO",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "puede_cerrar_conciliacion",
            table: "PERMISOS_USUARIO",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql(
            """
            UPDATE "PERMISOS_USUARIO"
            SET
                puede_revisar_lineas = puede_editar_lineas,
                puede_aprobar_importaciones = puede_importar,
                puede_conciliar = (puede_editar_lineas OR puede_importar),
                puede_cerrar_conciliacion = puede_eliminar_lineas;
            """);

        migrationBuilder.AddColumn<DateTime>(
            name: "fecha_expiracion",
            table: "INTEGRATION_TOKENS",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "rotated_from_token_id",
            table: "INTEGRATION_TOKENS",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "endpoint_scopes_json",
            table: "INTEGRATION_TOKENS",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.AddColumn<string>(
            name: "last_used_ip_address",
            table: "INTEGRATION_TOKENS",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "IMPORTACION_LOTES",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                usuario_creador_id = table.Column<Guid>(type: "uuid", nullable: false),
                tipo_origen = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                nombre_archivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                tamanio_bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                separador = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                mapeo_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                resumen_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                contenido_original = table.Column<string>(type: "text", nullable: false),
                lote_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                filas_total = table.Column<int>(type: "integer", nullable: false),
                filas_validas = table.Column<int>(type: "integer", nullable: false),
                filas_error = table.Column<int>(type: "integer", nullable: false),
                filas_advertencia = table.Column<int>(type: "integer", nullable: false),
                advertencias_aceptadas = table.Column<bool>(type: "boolean", nullable: false),
                fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                fecha_confirmacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                confirmado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                fecha_reversion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revertido_por_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_importacion_lotes", x => x.id);
                table.ForeignKey("fk_importacion_lotes_cuentas_cuenta_id", x => x.cuenta_id, "CUENTAS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_importacion_lotes_usuarios_confirmado_por_id", x => x.confirmado_por_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_importacion_lotes_usuarios_revertido_por_id", x => x.revertido_por_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_importacion_lotes_usuarios_usuario_creador_id", x => x.usuario_creador_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IMPORTACION_LOTE_FILAS",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                indice = table.Column<int>(type: "integer", nullable: false),
                valida = table.Column<bool>(type: "boolean", nullable: false),
                seleccionada_default = table.Column<bool>(type: "boolean", nullable: false),
                estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                datos_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                errores_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                advertencias_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_importacion_lote_filas", x => x.id);
                table.ForeignKey("fk_importacion_lote_filas_importacion_lotes_lote_id", x => x.lote_id, "IMPORTACION_LOTES", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<Guid>(
            name: "importacion_lote_id",
            table: "EXTRACTOS",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MOVIMIENTOS_ESPERADOS",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                fecha_esperada = table.Column<DateOnly>(type: "date", nullable: false),
                monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                divisa = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                referencia = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                concepto = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                origen = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                usuario_creacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                usuario_modificacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_movimientos_esperados", x => x.id);
                table.ForeignKey("fk_movimientos_esperados_cuentas_cuenta_id", x => x.cuenta_id, "CUENTAS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_movimientos_esperados_usuarios_deleted_by_id", x => x.deleted_by_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_movimientos_esperados_usuarios_usuario_creacion_id", x => x.usuario_creacion_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_movimientos_esperados_usuarios_usuario_modificacion_id", x => x.usuario_modificacion_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CONCILIACIONES",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                movimiento_esperado_id = table.Column<Guid>(type: "uuid", nullable: false),
                extracto_id = table.Column<Guid>(type: "uuid", nullable: true),
                estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                score = table.Column<int>(type: "integer", nullable: false),
                regla = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                diferencia_dias = table.Column<int>(type: "integer", nullable: false),
                referencia_normalizada = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                concepto_normalizado = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                usuario_sugerencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                fecha_sugerencia = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                usuario_confirmacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                fecha_confirmacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                usuario_resolucion_id = table.Column<Guid>(type: "uuid", nullable: true),
                fecha_resolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                observacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_conciliaciones", x => x.id);
                table.ForeignKey("fk_conciliaciones_cuentas_cuenta_id", x => x.cuenta_id, "CUENTAS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_conciliaciones_extractos_extracto_id", x => x.extracto_id, "EXTRACTOS", "id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("fk_conciliaciones_movimientos_esperados_movimiento_esperado_id", x => x.movimiento_esperado_id, "MOVIMIENTOS_ESPERADOS", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("fk_conciliaciones_usuarios_usuario_confirmacion_id", x => x.usuario_confirmacion_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_conciliaciones_usuarios_usuario_resolucion_id", x => x.usuario_resolucion_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_conciliaciones_usuarios_usuario_sugerencia_id", x => x.usuario_sugerencia_id, "USUARIOS", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("ix_integration_tokens_fecha_expiracion", "INTEGRATION_TOKENS", "fecha_expiracion");
        migrationBuilder.CreateIndex("ix_integration_tokens_rotated_from_token_id", "INTEGRATION_TOKENS", "rotated_from_token_id");
        migrationBuilder.CreateIndex("ix_importacion_lotes_cuenta_id", "IMPORTACION_LOTES", "cuenta_id");
        migrationBuilder.CreateIndex("ix_importacion_lotes_estado", "IMPORTACION_LOTES", "estado");
        migrationBuilder.CreateIndex("ix_importacion_lotes_fecha_creacion", "IMPORTACION_LOTES", "fecha_creacion");
        migrationBuilder.CreateIndex("ix_importacion_lotes_lote_hash", "IMPORTACION_LOTES", "lote_hash");
        migrationBuilder.CreateIndex("ix_importacion_lotes_sha256", "IMPORTACION_LOTES", "sha256");
        migrationBuilder.CreateIndex("ix_importacion_lote_filas_fingerprint", "IMPORTACION_LOTE_FILAS", "fingerprint");
        migrationBuilder.CreateIndex("ix_importacion_lote_filas_lote_id_indice", "IMPORTACION_LOTE_FILAS", new[] { "lote_id", "indice" }, unique: true);
        migrationBuilder.CreateIndex("ix_extractos_importacion_lote_id", "EXTRACTOS", "importacion_lote_id");
        migrationBuilder.CreateIndex("ix_movimientos_esperados_cuenta_id_estado", "MOVIMIENTOS_ESPERADOS", new[] { "cuenta_id", "estado" });
        migrationBuilder.CreateIndex("ix_movimientos_esperados_cuenta_id_fecha_esperada_monto", "MOVIMIENTOS_ESPERADOS", new[] { "cuenta_id", "fecha_esperada", "monto" });
        migrationBuilder.CreateIndex("ix_movimientos_esperados_deleted_at", "MOVIMIENTOS_ESPERADOS", "deleted_at");
        migrationBuilder.CreateIndex("ix_movimientos_esperados_referencia", "MOVIMIENTOS_ESPERADOS", "referencia");
        migrationBuilder.CreateIndex("ix_conciliaciones_cuenta_id_estado", "CONCILIACIONES", new[] { "cuenta_id", "estado" });
        migrationBuilder.CreateIndex("ix_conciliaciones_extracto_id", "CONCILIACIONES", "extracto_id");
        migrationBuilder.CreateIndex("ix_conciliaciones_movimiento_esperado_id", "CONCILIACIONES", "movimiento_esperado_id");
        migrationBuilder.CreateIndex("ix_conciliaciones_movimiento_esperado_id_extracto_id", "CONCILIACIONES", new[] { "movimiento_esperado_id", "extracto_id" }, unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_integration_tokens_integration_tokens_rotated_from_token_id",
            table: "INTEGRATION_TOKENS",
            column: "rotated_from_token_id",
            principalTable: "INTEGRATION_TOKENS",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_extractos_importacion_lotes_importacion_lote_id",
            table: "EXTRACTOS",
            column: "importacion_lote_id",
            principalTable: "IMPORTACION_LOTES",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.Sql(
            """
            ALTER TABLE "IMPORTACION_LOTES" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "IMPORTACION_LOTE_FILAS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "MOVIMIENTOS_ESPERADOS" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "CONCILIACIONES" ENABLE ROW LEVEL SECURITY;

            CREATE POLICY importacion_lotes_select ON "IMPORTACION_LOTES"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
            CREATE POLICY importacion_lotes_write ON "IMPORTACION_LOTES"
                FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

            CREATE POLICY importacion_lote_filas_select ON "IMPORTACION_LOTE_FILAS"
                FOR SELECT USING (
                    EXISTS (
                        SELECT 1 FROM "IMPORTACION_LOTES" l
                        WHERE l.id = lote_id AND atlas_security.can_read_cuenta_by_id(l.cuenta_id)
                    )
                );
            CREATE POLICY importacion_lote_filas_write ON "IMPORTACION_LOTE_FILAS"
                FOR ALL USING (
                    EXISTS (
                        SELECT 1 FROM "IMPORTACION_LOTES" l
                        WHERE l.id = lote_id AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                    )
                )
                WITH CHECK (
                    EXISTS (
                        SELECT 1 FROM "IMPORTACION_LOTES" l
                        WHERE l.id = lote_id AND atlas_security.can_write_cuenta_by_id(l.cuenta_id)
                    )
                );

            CREATE POLICY movimientos_esperados_select ON "MOVIMIENTOS_ESPERADOS"
                FOR SELECT USING (deleted_at IS NULL AND atlas_security.can_read_cuenta_by_id(cuenta_id));
            CREATE POLICY movimientos_esperados_write ON "MOVIMIENTOS_ESPERADOS"
                FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));

            CREATE POLICY conciliaciones_select ON "CONCILIACIONES"
                FOR SELECT USING (atlas_security.can_read_cuenta_by_id(cuenta_id));
            CREATE POLICY conciliaciones_write ON "CONCILIACIONES"
                FOR ALL USING (atlas_security.can_write_cuenta_by_id(cuenta_id))
                WITH CHECK (atlas_security.can_write_cuenta_by_id(cuenta_id));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("fk_extractos_importacion_lotes_importacion_lote_id", "EXTRACTOS");
        migrationBuilder.DropForeignKey("fk_integration_tokens_integration_tokens_rotated_from_token_id", "INTEGRATION_TOKENS");

        migrationBuilder.DropTable("CONCILIACIONES");
        migrationBuilder.DropTable("IMPORTACION_LOTE_FILAS");
        migrationBuilder.DropTable("MOVIMIENTOS_ESPERADOS");
        migrationBuilder.DropTable("IMPORTACION_LOTES");

        migrationBuilder.DropIndex("ix_extractos_importacion_lote_id", "EXTRACTOS");
        migrationBuilder.DropColumn("importacion_lote_id", "EXTRACTOS");

        migrationBuilder.DropColumn("puede_revisar_lineas", "PERMISOS_USUARIO");
        migrationBuilder.DropColumn("puede_aprobar_importaciones", "PERMISOS_USUARIO");
        migrationBuilder.DropColumn("puede_conciliar", "PERMISOS_USUARIO");
        migrationBuilder.DropColumn("puede_cerrar_conciliacion", "PERMISOS_USUARIO");

        migrationBuilder.DropIndex("ix_integration_tokens_fecha_expiracion", "INTEGRATION_TOKENS");
        migrationBuilder.DropIndex("ix_integration_tokens_rotated_from_token_id", "INTEGRATION_TOKENS");
        migrationBuilder.DropColumn("fecha_expiracion", "INTEGRATION_TOKENS");
        migrationBuilder.DropColumn("rotated_from_token_id", "INTEGRATION_TOKENS");
        migrationBuilder.DropColumn("endpoint_scopes_json", "INTEGRATION_TOKENS");
        migrationBuilder.DropColumn("last_used_ip_address", "INTEGRATION_TOKENS");
    }
}
