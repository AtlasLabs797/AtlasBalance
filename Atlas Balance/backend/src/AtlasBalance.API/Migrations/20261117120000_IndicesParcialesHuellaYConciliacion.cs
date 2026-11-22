using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations;

// LOTE 5 (B-3 y B-6, parte de base de datos).
// Escrita a mano (no con `dotnet ef migrations add`): el snapshot arrastra una
// deriva previa y un scaffold generaba una migracion con cambios ajenos a
// estos indices (ver REGISTRO_BUGS.md, "Snapshot de EF desalineado con el
// modelo"). Todo el SQL es idempotente y se puede reaplicar sobre una base
// ya migrada.
[DbContext(typeof(AppDbContext))]
[Migration("20261117120000_IndicesParcialesHuellaYConciliacion")]
public partial class IndicesParcialesHuellaYConciliacion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // B-3: EXTRACTOS.(cuenta_id, importacion_fingerprint) unico PARCIAL.
        // El indice anterior no filtraba deleted_at, de modo que tras revertir
        // un lote (soft delete de sus extractos) no se podia reimportar la
        // misma huella. El predicado nuevo es MAS restrictivo que el viejo
        // (ademas de deleted_at IS NULL exige importacion_fingerprint NOT
        // NULL), asi que no puede haber duplicados que purgar: el indice
        // antiguo ya los impedia.
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "ix_extractos_cuenta_id_importacion_fingerprint";
            CREATE UNIQUE INDEX "ix_extractos_cuenta_id_importacion_fingerprint"
            ON "EXTRACTOS" ("cuenta_id", "importacion_fingerprint")
            WHERE "deleted_at" IS NULL AND "importacion_fingerprint" IS NOT NULL;
            """);

        // B-6: CONCILIACIONES.(extracto_id) unico PARCIAL para las
        // conciliaciones en estado 'conciliada' y no borradas logicamente.
        //
        // Antes de crearlo hay que purgar los duplicados que ya existieran en
        // una base real: sin este paso el CREATE UNIQUE INDEX falla con 23505
        // y la migracion no se puede aplicar.
        //
        // Purga minima y no destructiva: de cada extracto se conserva la
        // conciliacion mas antigua y las demas se marcan como borradas
        // logicamente (deleted_at). No se borra ninguna fila ni se reescribe
        // su estado, asi que el historial sigue disponible para auditoria.
        //
        // RLS: "CONCILIACIONES" tiene FORCE ROW LEVEL SECURITY, asi que un
        // UPDATE sin contexto firmado no ve filas y la purga no seria un
        // no-op silencioso (el CREATE UNIQUE INDEX fallaria despues con
        // 23505). Si quien migra es el propietario de la tabla se levanta el
        // FORCE para la operacion y se restaura al final. Todo ocurre dentro
        // de la transaccion de la migracion: si algo falla se revierte todo.
        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                v_es_propietario boolean := false;
                v_forzado boolean := false;
            BEGIN
                SELECT (r.rolname = current_user), c.relforcerowsecurity
                  INTO v_es_propietario, v_forzado
                  FROM pg_class c
                  JOIN pg_roles r ON r.oid = c.relowner
                 WHERE c.oid = '"CONCILIACIONES"'::regclass;

                IF v_es_propietario AND v_forzado THEN
                    EXECUTE 'ALTER TABLE "CONCILIACIONES" NO FORCE ROW LEVEL SECURITY';
                END IF;

                WITH duplicados AS (
                    SELECT "id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "extracto_id"
                               ORDER BY "fecha_creacion" ASC, "id" ASC
                           ) AS rn
                      FROM "CONCILIACIONES"
                     WHERE "estado" = 'conciliada'
                       AND "extracto_id" IS NOT NULL
                       AND "deleted_at" IS NULL
                )
                UPDATE "CONCILIACIONES" c
                   SET "deleted_at" = now(),
                       "deleted_by_id" = NULL
                  FROM duplicados d
                 WHERE c."id" = d."id"
                   AND d.rn > 1;

                IF v_es_propietario AND v_forzado THEN
                    EXECUTE 'ALTER TABLE "CONCILIACIONES" FORCE ROW LEVEL SECURITY';
                END IF;
            END $$;
            """);

        // El indice simple sobre extracto_id lo sustituye el unico parcial:
        // las consultas por extracto que interestan son las de las
        // conciliaciones en estado 'conciliada'.
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "ix_conciliaciones_extracto_id";
            DROP INDEX IF EXISTS "ix_conciliaciones_extracto_id_conciliada";
            CREATE UNIQUE INDEX "ix_conciliaciones_extracto_id_conciliada"
            ON "CONCILIACIONES" ("extracto_id")
            WHERE "estado" = 'conciliada'
              AND "extracto_id" IS NOT NULL
              AND "deleted_at" IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nota: el soft delete de las conciliaciones duplicadas no se
        // revierte aqui (habria que recalcular a mano que conciliacion
        // valida de cada extracto). Lo que se revierte son los indices.
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "ix_conciliaciones_extracto_id_conciliada";
            CREATE INDEX "ix_conciliaciones_extracto_id"
            ON "CONCILIACIONES" ("extracto_id");

            DROP INDEX IF EXISTS "ix_extractos_cuenta_id_importacion_fingerprint";
            CREATE UNIQUE INDEX "ix_extractos_cuenta_id_importacion_fingerprint"
            ON "EXTRACTOS" ("cuenta_id", "importacion_fingerprint")
            WHERE "importacion_fingerprint" IS NOT NULL;
            """);
    }
}
