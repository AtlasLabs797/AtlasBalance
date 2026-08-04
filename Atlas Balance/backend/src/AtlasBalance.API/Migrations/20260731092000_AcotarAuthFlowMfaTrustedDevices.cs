using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.07: acota la rama is_auth_flow() de MFA_TRUSTED_DEVICES.
    //
    // mfa_trusted_devices_access era una unica policy FOR ALL cuya rama
    // is_auth_flow() no filtraba por usuario ni por comando: durante cualquier
    // request no autenticado bajo /api/auth/* concedia SELECT, INSERT, UPDATE y
    // DELETE sobre TODAS las filas de la tabla que protege el bypass de segundo
    // factor. Hoy no era explotable porque las cinco consultas de AuthService
    // sobre esta tabla filtran por UsuarioId en C# (AuthService.cs:478, 678,
    // 706, 728, 752, 1382), pero RLS no aportaba ningun backstop: bastaba con
    // que un refactor olvidara ese Where para exponer token_hash,
    // security_stamp, revoked_at y expires_at de cualquier usuario.
    //
    // No se puede acotar la rama de auth por usuario_id: en modo auth el
    // contexto RLS todavia no publica atlas.user_id (el usuario aun no esta
    // autenticado a nivel HTTP cuando corren esas consultas). Lo que si se
    // puede es reducir la superficie al minimo que el flujo necesita de verdad:
    //
    //   - SELECT  : lo necesita (verificacion de dispositivo confiable).
    //   - INSERT  : lo necesita (alta del dispositivo tras verificar MFA).
    //   - UPDATE  : lo necesita (revocacion y last_used_at).
    //   - DELETE  : NO lo necesita. Ningun camino de codigo borra fisicamente
    //               un dispositivo; la revocacion es logica (RevokedAt).
    //               Queda restringido a admin/system.
    //
    // Se anade ademas deleted_at IS NULL a la rama de auth, que antes solo
    // aplicaba a la rama de usuario: un dispositivo borrado no debe poder
    // usarse para saltarse el segundo factor. El filtro va solo en USING; el
    // WITH CHECK no lo lleva para no bloquear un futuro borrado logico (la
    // leccion de 20260731090000_FixExportacionesPurgaRlsWithCheck).
    [DbContext(typeof(AppDbContext))]
    [Migration("20260731092000_AcotarAuthFlowMfaTrustedDevices")]
    public partial class AcotarAuthFlowMfaTrustedDevices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS mfa_trusted_devices_access ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_select ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_insert ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_update ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_delete ON "MFA_TRUSTED_DEVICES";

                CREATE POLICY mfa_trusted_devices_select ON "MFA_TRUSTED_DEVICES"
                    FOR SELECT USING (
                        atlas_security.is_admin_or_system()
                        OR (deleted_at IS NULL AND atlas_security.is_auth_flow())
                        OR (
                            deleted_at IS NULL
                            AND atlas_security.is_user_mode()
                            AND usuario_id = atlas_security.current_user_id()
                        )
                    );

                CREATE POLICY mfa_trusted_devices_insert ON "MFA_TRUSTED_DEVICES"
                    FOR INSERT WITH CHECK (
                        atlas_security.is_admin_or_system()
                        OR atlas_security.is_auth_flow()
                        OR (
                            atlas_security.is_user_mode()
                            AND usuario_id = atlas_security.current_user_id()
                        )
                    );

                CREATE POLICY mfa_trusted_devices_update ON "MFA_TRUSTED_DEVICES"
                    FOR UPDATE USING (
                        atlas_security.is_admin_or_system()
                        OR (deleted_at IS NULL AND atlas_security.is_auth_flow())
                        OR (
                            deleted_at IS NULL
                            AND atlas_security.is_user_mode()
                            AND usuario_id = atlas_security.current_user_id()
                        )
                    )
                    WITH CHECK (
                        atlas_security.is_admin_or_system()
                        OR atlas_security.is_auth_flow()
                        OR (
                            atlas_security.is_user_mode()
                            AND usuario_id = atlas_security.current_user_id()
                        )
                    );

                CREATE POLICY mfa_trusted_devices_delete ON "MFA_TRUSTED_DEVICES"
                    FOR DELETE USING (atlas_security.is_admin_or_system());
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS mfa_trusted_devices_select ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_insert ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_update ON "MFA_TRUSTED_DEVICES";
                DROP POLICY IF EXISTS mfa_trusted_devices_delete ON "MFA_TRUSTED_DEVICES";

                DROP POLICY IF EXISTS mfa_trusted_devices_access ON "MFA_TRUSTED_DEVICES";
                CREATE POLICY mfa_trusted_devices_access ON "MFA_TRUSTED_DEVICES"
                    FOR ALL USING (
                        atlas_security.is_admin_or_system()
                        OR atlas_security.is_auth_flow()
                        OR (
                            deleted_at IS NULL
                            AND atlas_security.is_user_mode()
                            AND usuario_id = atlas_security.current_user_id()
                        )
                    )
                    WITH CHECK (
                        atlas_security.is_admin_or_system()
                        OR atlas_security.is_auth_flow()
                        OR (
                            deleted_at IS NULL
                            AND atlas_security.is_user_mode()
                            AND usuario_id = atlas_security.current_user_id()
                        )
                    );
                """);
        }
    }
}
