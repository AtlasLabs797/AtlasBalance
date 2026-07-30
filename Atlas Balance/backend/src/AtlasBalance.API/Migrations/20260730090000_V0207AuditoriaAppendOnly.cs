using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    // V-02.07 (observabilidad de seguridad): convierte AUDITORIAS en un registro
    // append-only, firmado y con contexto de peticion completo.
    //
    // Que anade y por que:
    //
    // - secuencia (identity): monotonica y generada por Postgres. Un hueco en la
    //   secuencia significa que alguien borro filas. Es la unica forma de
    //   detectar borrados en una tabla donde las filas no se referencian entre si.
    // - firma: HMAC-SHA256 del contenido de la fila (ver AuditSigner). Detecta
    //   modificacion e insercion por quien tenga la BD pero no la clave.
    // - user_agent / session_id / origen: el contexto que pedia el requisito de
    //   auditoria (quien, cuando, desde donde, por que canal).
    // - detalles_json pasa de jsonb a json: jsonb NORMALIZA el texto (reordena
    //   claves, quita espacios), asi que la cadena releida no coincidiria con la
    //   firmada y toda la auditoria pareceria manipulada. Nada en el codigo usa
    //   operadores jsonb sobre esta columna.
    // - Trigger append-only + funcion de purga con suelo de retencion.
    //
    // Punto de partida, para no repetir un diagnostico equivocado: AUDITORIAS ya
    // era de facto append-only desde 20260501120000_EnableRowLevelSecurity, que
    // le puso FORCE ROW LEVEL SECURITY y solo politicas de SELECT e INSERT. En
    // PostgreSQL eso hace que UPDATE y DELETE no vean ninguna fila.
    //
    // El problema es que lo hacian EN SILENCIO, y de ahi salen dos cosas:
    //   - Nada avisaba de un intento de manipulacion: el UPDATE devolvia "0
    //     filas afectadas" igual que si no hubiera nada que actualizar.
    //   - LimpiezaAuditoriaJob llevaba desde entonces borrando cero filas cada
    //     noche y registrandolo como exito. La tabla crecia sin limite.
    //
    // Modelo de amenaza: el atacante compromete la aplicacion y con ella el
    // connection string de runtime. La defensa se apoya en que ese rol
    // (atlas_balance_app) NO es el propietario de las tablas: el propietario es
    // atlas_balance_owner, que solo se usa para migraciones. Cuatro capas:
    //
    //   1. Privilegios: Program.GrantRuntimeDatabasePrivileges revoca UPDATE,
    //      DELETE y TRUNCATE sobre AUDITORIAS al rol de runtime. Como no es
    //      propietario, no puede volver a concederselos, ni quitar el trigger,
    //      ni alterar la tabla. Falla ruidosamente, con error de privilegios.
    //   2. Trigger: bloquea TODO UPDATE sin excepcion, y el DELETE de cualquier
    //      fila de menos de MinRetentionDays dias incluso por la via sancionada.
    //      Convierte los no-op silenciosos de RLS en errores visibles.
    //   3. Purga: unica via de borrado. Politica auditorias_delete + funcion
    //      SECURITY DEFINER con el suelo de retencion validado dentro.
    //   4. Deteccion: firma HMAC por fila, continuidad de la secuencia y espejo
    //      a Windows Event Log, para lo que la prevencion no alcance.
    //
    // Donde NO alcanza: quien tenga las credenciales de atlas_balance_owner (o
    // superusuario de PostgreSQL) puede hacer lo que quiera. Contra eso solo
    // queda la deteccion de la capa 4, y por eso existe. En Development, donde
    // suele haber un unico rol que ademas es propietario, la capa 1 no aporta y
    // quedan RLS, trigger y deteccion: aceptable porque ahi no hay datos reales.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260730090000_V0207AuditoriaAppendOnly")]
    public partial class V0207AuditoriaAppendOnly : Migration
    {
        /// <summary>
        /// Suelo de retencion grabado en la BD. Ninguna via puede borrar filas de
        /// auditoria mas recientes que esto. Debe ser menor o igual que
        /// LimpiezaAuditoriaJob.RetentionDays.
        /// </summary>
        private const int MinRetentionDays = 90;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                -- 1. Contexto de peticion -------------------------------------
                ALTER TABLE "AUDITORIAS"
                    ADD COLUMN IF NOT EXISTS user_agent character varying(256),
                    ADD COLUMN IF NOT EXISTS session_id character varying(64),
                    ADD COLUMN IF NOT EXISTS origen character varying(16) NOT NULL DEFAULT 'DESCONOCIDO',
                    ADD COLUMN IF NOT EXISTS firma character varying(64);

                -- El default solo existia para poder rellenar las filas
                -- historicas. A partir de ahora lo escribe la aplicacion siempre.
                ALTER TABLE "AUDITORIAS" ALTER COLUMN origen DROP DEFAULT;

                -- 2. Secuencia monotonica -------------------------------------
                -- Postgres rellena las filas existentes al crear la identidad.
                -- El orden que les asigne es arbitrario, pero esas filas son
                -- pre-V-02.07 y no llevan firma: no entran en la verificacion.
                ALTER TABLE "AUDITORIAS"
                    ADD COLUMN IF NOT EXISTS secuencia bigint GENERATED BY DEFAULT AS IDENTITY;

                ALTER TABLE "AUDITORIAS"
                    DROP CONSTRAINT IF EXISTS ak_auditorias_secuencia;
                ALTER TABLE "AUDITORIAS"
                    ADD CONSTRAINT ak_auditorias_secuencia UNIQUE (secuencia);

                -- 3. detalles_json: jsonb -> json (ver cabecera) ---------------
                ALTER TABLE "AUDITORIAS"
                    ALTER COLUMN detalles_json TYPE json USING detalles_json::text::json;

                -- 4. Indices para las reglas de alerta ------------------------
                CREATE INDEX IF NOT EXISTS ix_auditorias_ip_address_timestamp
                    ON "AUDITORIAS" (ip_address, "timestamp");
                CREATE INDEX IF NOT EXISTS ix_auditorias_tipo_accion_timestamp
                    ON "AUDITORIAS" (tipo_accion, "timestamp");

                -- 5. Id de sesion en REFRESH_TOKENS ---------------------------
                ALTER TABLE "REFRESH_TOKENS"
                    ADD COLUMN IF NOT EXISTS session_id character varying(64);
                CREATE INDEX IF NOT EXISTS ix_refresh_tokens_session_id
                    ON "REFRESH_TOKENS" (session_id);
                """);

            migrationBuilder.Sql(
                $"""
                -- 6. Append-only ----------------------------------------------
                CREATE SCHEMA IF NOT EXISTS atlas_security;

                CREATE OR REPLACE FUNCTION atlas_security.auditorias_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $fn$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION
                            'AUDITORIAS es append-only: UPDATE no permitido (fila %)', OLD.id
                            USING ERRCODE = '42501';
                    END IF;

                    -- Suelo duro: la evidencia reciente no se borra por ninguna
                    -- via. Se comprueba ANTES de la marca de purga a proposito,
                    -- para que falsificar la marca no sirva de nada.
                    IF OLD."timestamp" > now() - interval '{MinRetentionDays} days' THEN
                        RAISE EXCEPTION
                            'AUDITORIAS: no se puede borrar una fila de menos de {MinRetentionDays} dias (fila %, %)', OLD.id, OLD."timestamp"
                            USING ERRCODE = '42501';
                    END IF;

                    IF current_setting('atlas.audit_purge', true) IS DISTINCT FROM 'on' THEN
                        RAISE EXCEPTION
                            'AUDITORIAS es append-only: el DELETE solo se admite via atlas_security.purgar_auditorias()'
                            USING ERRCODE = '42501';
                    END IF;

                    RETURN OLD;
                END;
                $fn$;

                DROP TRIGGER IF EXISTS trg_auditorias_append_only ON "AUDITORIAS";
                CREATE TRIGGER trg_auditorias_append_only
                    BEFORE UPDATE OR DELETE ON "AUDITORIAS"
                    FOR EACH ROW EXECUTE FUNCTION atlas_security.auditorias_append_only();

                -- Politica de DELETE. Hace falta de verdad, no es decorativa:
                -- AUDITORIAS tiene FORCE ROW LEVEL SECURITY desde
                -- 20260501120000_EnableRowLevelSecurity y solo declaraba
                -- politicas de SELECT e INSERT. En PostgreSQL, un DELETE sobre
                -- una tabla con RLS y sin politica de DELETE no falla: no ve
                -- ninguna fila y borra cero en silencio.
                --
                -- Efecto lateral que esto arregla: LimpiezaAuditoriaJob llevaba
                -- desde entonces ejecutando su ExecuteDelete y registrando
                -- "elimino 0 auditorias" cada noche, con la tabla creciendo sin
                -- limite y sin que nada lo delatara.
                --
                -- La politica reproduce el mismo criterio que el trigger para
                -- que la unica via de borrado sea la purga y nunca alcance a la
                -- evidencia reciente.
                DROP POLICY IF EXISTS auditorias_delete ON "AUDITORIAS";
                CREATE POLICY auditorias_delete ON "AUDITORIAS"
                    FOR DELETE USING (
                        current_setting('atlas.audit_purge', true) = 'on'
                        AND "timestamp" < now() - interval '{MinRetentionDays} days'
                    );

                -- 7. Unica via de purga ---------------------------------------
                -- SECURITY DEFINER: corre con los privilegios del propietario,
                -- que es lo que permite revocarle el DELETE al rol de runtime y
                -- que la purga por retencion siga funcionando.
                -- search_path fijo: obligatorio en SECURITY DEFINER, si no un
                -- rol con CREATE en otro esquema podria secuestrar las
                -- referencias no cualificadas de dentro de la funcion.
                CREATE OR REPLACE FUNCTION atlas_security.purgar_auditorias(retencion_dias integer)
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
                            'La retencion de auditoria no puede bajar de % dias (se pidio %)',
                            {MinRetentionDays}, retencion_dias
                            USING ERRCODE = '23514';
                    END IF;

                    corte := now() - make_interval(days => retencion_dias);

                    -- true = LOCAL a la transaccion: la marca desaparece al
                    -- terminar, no queda pegada a la conexion del pool.
                    PERFORM set_config('atlas.audit_purge', 'on', true);
                    DELETE FROM "AUDITORIAS" WHERE "timestamp" < corte;
                    GET DIAGNOSTICS borradas = ROW_COUNT;
                    PERFORM set_config('atlas.audit_purge', 'off', true);

                    RETURN borradas;
                END;
                $fn$;

                -- Una SECURITY DEFINER ejecutable por PUBLIC seria un agujero:
                -- cualquier rol con acceso a la base podria purgar. Solo el
                -- propietario, y Program.GrantRuntimeDatabasePrivileges concede
                -- EXECUTE al rol de runtime para que el job de retencion corra.
                REVOKE ALL ON FUNCTION atlas_security.purgar_auditorias(integer) FROM PUBLIC;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS auditorias_delete ON "AUDITORIAS";
                DROP TRIGGER IF EXISTS trg_auditorias_append_only ON "AUDITORIAS";
                DROP FUNCTION IF EXISTS atlas_security.auditorias_append_only();
                DROP FUNCTION IF EXISTS atlas_security.purgar_auditorias(integer);

                DROP INDEX IF EXISTS ix_refresh_tokens_session_id;
                ALTER TABLE "REFRESH_TOKENS" DROP COLUMN IF EXISTS session_id;

                DROP INDEX IF EXISTS ix_auditorias_tipo_accion_timestamp;
                DROP INDEX IF EXISTS ix_auditorias_ip_address_timestamp;

                ALTER TABLE "AUDITORIAS"
                    ALTER COLUMN detalles_json TYPE jsonb USING detalles_json::text::jsonb;

                ALTER TABLE "AUDITORIAS" DROP CONSTRAINT IF EXISTS ak_auditorias_secuencia;
                ALTER TABLE "AUDITORIAS"
                    DROP COLUMN IF EXISTS secuencia,
                    DROP COLUMN IF EXISTS firma,
                    DROP COLUMN IF EXISTS origen,
                    DROP COLUMN IF EXISTS session_id,
                    DROP COLUMN IF EXISTS user_agent;
                """);
        }
    }
}
