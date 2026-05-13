using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class SplitPermisosPreferencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PREFERENCIAS_USUARIO_CUENTA",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    columnas_visibles = table.Column<string>(type: "jsonb", nullable: true),
                    columnas_editables = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferencias_usuario_cuenta", x => x.id);
                    table.ForeignKey(
                        name: "fk_preferencias_usuario_cuenta_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_preferencias_usuario_cuenta_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_usuario_cuenta_cuenta_id",
                table: "PREFERENCIAS_USUARIO_CUENTA",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_usuario_cuenta_usuario_id",
                table: "PREFERENCIAS_USUARIO_CUENTA",
                column: "usuario_id",
                unique: true,
                filter: "\"cuenta_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_preferencias_usuario_cuenta_usuario_id_cuenta_id",
                table: "PREFERENCIAS_USUARIO_CUENTA",
                columns: new[] { "usuario_id", "cuenta_id" },
                unique: true,
                filter: "\"cuenta_id\" IS NOT NULL");

            migrationBuilder.Sql(
                """
                INSERT INTO "PREFERENCIAS_USUARIO_CUENTA" (
                    id,
                    usuario_id,
                    cuenta_id,
                    columnas_visibles,
                    columnas_editables,
                    created_at,
                    updated_at
                )
                SELECT
                    gen_random_uuid(),
                    p.usuario_id,
                    p.cuenta_id,
                    p.columnas_visibles,
                    p.columnas_editables,
                    NOW(),
                    NOW()
                FROM (
                    SELECT DISTINCT ON (usuario_id, cuenta_id)
                        usuario_id,
                        cuenta_id,
                        columnas_visibles,
                        columnas_editables,
                        id
                    FROM "PERMISOS_USUARIO"
                    WHERE columnas_visibles IS NOT NULL OR columnas_editables IS NOT NULL
                    ORDER BY usuario_id, cuenta_id, id DESC
                ) p;
                """);

            migrationBuilder.DropColumn(
                name: "columnas_editables",
                table: "PERMISOS_USUARIO");

            migrationBuilder.DropColumn(
                name: "columnas_visibles",
                table: "PERMISOS_USUARIO");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PREFERENCIAS_USUARIO_CUENTA");

            migrationBuilder.AddColumn<string>(
                name: "columnas_editables",
                table: "PERMISOS_USUARIO",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "columnas_visibles",
                table: "PERMISOS_USUARIO",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "PERMISOS_USUARIO" p
                SET
                    columnas_visibles = pref.columnas_visibles,
                    columnas_editables = pref.columnas_editables
                FROM "PREFERENCIAS_USUARIO_CUENTA" pref
                WHERE pref.usuario_id = p.usuario_id
                  AND (
                    (pref.cuenta_id IS NULL AND p.cuenta_id IS NULL)
                    OR pref.cuenta_id = p.cuenta_id
                  );
                """);
        }
    }
}
