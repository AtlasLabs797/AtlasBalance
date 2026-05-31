using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260601090000_AddRefreshTokenSecurityStamp")]
public partial class AddRefreshTokenSecurityStamp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "security_stamp",
            table: "REFRESH_TOKENS",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql("""
            UPDATE "REFRESH_TOKENS" AS rt
            SET "security_stamp" = COALESCE(NULLIF(u."security_stamp", ''), 'legacy-missing-security-stamp')
            FROM "USUARIOS" AS u
            WHERE rt."usuario_id" = u."id";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "security_stamp",
            table: "REFRESH_TOKENS");
    }
}
