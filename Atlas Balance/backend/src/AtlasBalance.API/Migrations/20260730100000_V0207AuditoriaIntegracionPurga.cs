using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.07: cierra la segunda mitad del bug de purga silenciosa.
    //
    // 20260730090000_V0207AuditoriaAppendOnly arreglo AUDITORIAS, pero
    // AUDITORIA_INTEGRACIONES arrastra exactamente el mismo defecto desde
    // 20260501120000_EnableRowLevelSecurity: FORCE ROW LEVEL SECURITY con
    // politicas de SELECT e INSERT y ninguna de DELETE. En PostgreSQL eso hace
    // que el DELETE no vea ninguna fila y borre cero sin error, asi que
    // LimpiezaAuditoriaJob tambien llevaba sin purgar esta tabla.
    //
    // Se le da el mismo tratamiento que a AUDITORIAS, con dos diferencias
    // deliberadas:
    //
    //   - El suelo de retencion es de 7 dias y no de 90. AUDITORIA_INTEGRACIONES
    //     es un log de peticiones HTTP de la integracion: volumen alto, valor
    //     forense bajo comparado con AUDITORIAS, y una retencion nominal de 28
    //     dias. Un suelo de 90 haria la purga literalmente imposible.
    //   - No lleva trigger append-only. RLS ya impide el UPDATE (no hay politica)
    //     y aqui no se firma ninguna fila, asi que el trigger solo aportaria
    //     ruido. Queda anotado por si en algun momento esta tabla sube de
    //     categoria.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260730100000_V0207AuditoriaIntegracionPurga")]
    public partial class V0207AuditoriaIntegracionPurga : Migration
    {
        /// <summary>
        /// Suelo de retencion de AUDITORIA_INTEGRACIONES, grabado en la BD.
        /// Debe ser menor o igual que LimpiezaAuditoriaJob.IntegrationRetentionDays.
        /// </summary>
        private const int MinRetentionDays = 7;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                -- Misma marca de sesion que la purga de AUDITORIAS: un DELETE
                -- suelto sigue sin borrar nada.
                DROP POLICY IF EXISTS auditoria_integraciones_delete ON "AUDITORIA_INTEGRACIONES";
                CREATE POLICY auditoria_integraciones_delete ON "AUDITORIA_INTEGRACIONES"
                    FOR DELETE USING (
                        current_setting('atlas.audit_purge', true) = 'on'
                        AND "timestamp" < now() - interval '{MinRetentionDays} days'
                    );

                CREATE OR REPLACE FUNCTION atlas_security.purgar_auditorias_integracion(retencion_dias integer)
                RETURNS bigint
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, public
                AS $fn$
                DECLARE
                    borradas bigint;
                    corte timestamptz;
                BEGIN
                    IF retencion_dias IS NULL OR retencion_dias < {MinRetentionDays} THEN
                        RAISE EXCEPTION
                            'La retencion de auditoria de integracion no puede bajar de % dias (se pidio %)',
                            {MinRetentionDays}, retencion_dias
                            USING ERRCODE = '23514';
                    END IF;

                    corte := now() - make_interval(days => retencion_dias);

                    PERFORM set_config('atlas.audit_purge', 'on', true);
                    DELETE FROM "AUDITORIA_INTEGRACIONES" WHERE "timestamp" < corte;
                    GET DIAGNOSTICS borradas = ROW_COUNT;
                    PERFORM set_config('atlas.audit_purge', 'off', true);

                    RETURN borradas;
                END;
                $fn$;

                REVOKE ALL ON FUNCTION atlas_security.purgar_auditorias_integracion(integer) FROM PUBLIC;

                -- Coherencia con AUDITORIAS: el rol de runtime tampoco tiene por
                -- que poder alterar ni borrar directamente el log de integracion.
                -- Program.GrantRuntimeDatabasePrivileges repite este REVOKE en
                -- cada arranque, porque su GRANT sobre ALL TABLES lo revertiria.
                DO $do$
                DECLARE
                    rol text;
                BEGIN
                    FOR rol IN
                        SELECT DISTINCT grantee
                        FROM information_schema.role_table_grants
                        WHERE table_name = 'AUDITORIA_INTEGRACIONES'
                          AND privilege_type IN ('UPDATE', 'DELETE')
                          AND grantee <> current_user
                          AND grantee <> 'PUBLIC'
                    LOOP
                        EXECUTE format(
                            'REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "AUDITORIA_INTEGRACIONES" FROM %I',
                            rol);
                    END LOOP;
                END;
                $do$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS auditoria_integraciones_delete ON "AUDITORIA_INTEGRACIONES";
                DROP FUNCTION IF EXISTS atlas_security.purgar_auditorias_integracion(integer);
                """);
        }
    }
}
