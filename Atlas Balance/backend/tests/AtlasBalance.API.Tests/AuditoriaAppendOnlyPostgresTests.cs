using System.Net;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-02.07: lo que hace append-only a AUDITORIAS vive en SQL (privilegios,
// trigger y funcion de purga), asi que solo se puede comprobar contra un
// PostgreSQL de verdad. Con EF InMemory todo esto pasaria sin ejecutarse.
//
// Tambien valida el viaje de ida y vuelta de la firma: es donde se rompen las
// suposiciones sobre precision de timestamp y normalizacion de JSON, y si se
// rompe, TODA la auditoria se reporta como manipulada.
// -----------------------------------------------------------------------
[Collection(PostgresCollection.Name)]
public sealed class AuditoriaAppendOnlyPostgresTests
{
    private const int MinRetentionDays = 90;
    private static readonly string RlsSecret = string.Concat("test-rls-context-", "placeholder-value-32-chars");
    private readonly PostgresFixture _fixture;

    public AuditoriaAppendOnlyPostgresTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Signed_Row_Should_Still_Verify_After_A_Round_Trip_Through_Postgres()
    {
        // El test mas importante de la tanda. Cubre a la vez:
        //  - precision del timestamp (Postgres guarda microsegundos, .NET 100 ns)
        //  - detalles_json como json y no jsonb (jsonb reordena claves y quitaria
        //    espacios, cambiando el texto que se firmo)
        //  - inet y su posible normalizacion de IPv4 mapeada
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);

        var firmador = TestAuditService.Signer();
        var id = Guid.NewGuid();

        await using (var db = await ContextoConRlsAsync(owner))
        {
            var fila = new Auditoria
            {
                Id = id,
                // null: usuario_id tiene FK contra USUARIOS y aqui no interesa
                // montar un usuario. Lo que se prueba es el viaje de ida y
                // vuelta de los campos que la firma cubre.
                UsuarioId = null,
                TipoAccion = AuditActions.Login,
                EntidadTipo = "USUARIOS",
                EntidadId = Guid.NewGuid(),
                // Con ticks sueltos a proposito: es el caso que rompe la firma
                // si no se trunca a microsegundos.
                Timestamp = AuditSigner.TruncarAMicrosegundos(DateTime.UtcNow),
                IpAddress = IPAddress.Parse("10.0.0.5"),
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
                SessionId = "sesion-de-ida-y-vuelta",
                Origen = AuditOrigenes.Ui,
                // Claves en orden no alfabetico y con acentos: si la columna
                // fuese jsonb, volveria reordenado y la firma no validaria.
                DetallesJson = """{"zeta":1,"alfa":"informacion","email":"a@b.com"}"""
            };
            fila.Firma = firmador.Firmar(fila);
            db.Auditorias.Add(fila);
            await db.SaveChangesAsync();
        }

        await using (var db = await ContextoConRlsAsync(owner))
        {
            var releida = await db.Auditorias.AsNoTracking().SingleAsync(a => a.Id == id);

            firmador.Verificar(releida).Should().BeTrue(
                "una fila firmada tiene que seguir validando despues de pasar por PostgreSQL");
            releida.Secuencia.Should().BeGreaterThan(0, "Postgres asigna la secuencia en el INSERT");
        }
    }

    [Fact]
    public async Task Runtime_Role_Should_Not_Be_Able_To_Update_Or_Delete_Audit_Rows()
    {
        // Es la defensa de verdad contra una aplicacion comprometida: el rol de
        // runtime no es propietario, asi que no puede reconcederse el privilegio
        // ni quitar el trigger.
        var (owner, runtime) = await CrearRolesAsync();
        await MigrarAsync(owner);
        await ConcederPrivilegiosRuntimeAsync(owner, runtime);
        var id = await InsertarAuditoriaAsync(owner, DateTime.UtcNow);

        await using var conexion = new NpgsqlConnection(runtime);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        var update = async () => await EjecutarAsync(conexion, $"UPDATE \"AUDITORIAS\" SET tipo_accion = 'FALSEADO' WHERE id = '{id}';");
        var delete = async () => await EjecutarAsync(conexion, $"DELETE FROM \"AUDITORIAS\" WHERE id = '{id}';");

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Runtime_Role_Should_Still_Be_Able_To_Insert_Audit_Rows()
    {
        // El REVOKE no puede llevarse por delante lo unico que la aplicacion
        // necesita hacer con esta tabla.
        var (owner, runtime) = await CrearRolesAsync();
        await MigrarAsync(owner);
        await ConcederPrivilegiosRuntimeAsync(owner, runtime);

        await using var conexion = new NpgsqlConnection(runtime);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        var insert = async () => await EjecutarAsync(
            conexion,
            $"INSERT INTO \"AUDITORIAS\" (id, tipo_accion, \"timestamp\", origen) VALUES ('{Guid.NewGuid()}', 'LOGIN', now(), 'UI');");

        await insert.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Audit_Rows_Should_Not_Be_Modifiable_Even_By_The_Owner()
    {
        // Dos barreras encadenadas: RLS filtra la fila (no hay politica de
        // UPDATE sobre AUDITORIAS) y el trigger la rechazaria si llegase. La que
        // gana es RLS, que actua antes, asi que el UPDATE no lanza: no encuentra
        // ninguna fila que actualizar. Lo que se afirma es el resultado, que es
        // lo que importa: el contenido no cambia.
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);
        var id = await InsertarAuditoriaAsync(owner, DateTime.UtcNow);

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        await EjecutarAsync(conexion, $"UPDATE \"AUDITORIAS\" SET tipo_accion = 'FALSEADO' WHERE id = '{id}';");

        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT tipo_accion FROM \"AUDITORIAS\" WHERE id = '{id}';";
        var tipoAccion = (string?)await cmd.ExecuteScalarAsync();
        tipoAccion.Should().Be("LOGIN", "la fila de auditoria no se puede alterar");
    }

    [Fact]
    public async Task Recent_Rows_Should_Survive_A_Delete_Even_Faking_The_Purge_Flag()
    {
        // El suelo de antiguedad esta en la politica RLS y ademas en el trigger,
        // asi que falsificar la marca de purga no sirve para borrar evidencia
        // reciente.
        //
        // Aqui la fila no llega a desaparecer y ademas NO salta excepcion: la
        // politica RLS filtra la fila antes de que el trigger la vea, y un DELETE
        // que no encuentra filas es un no-op silencioso. Lo que importa es el
        // resultado (la evidencia sigue ahi), y se afirma tal cual en vez de
        // fingir que hay un error donde no lo hay.
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);
        var id = await InsertarAuditoriaAsync(owner, DateTime.UtcNow.AddDays(-1));

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        await EjecutarAsync(
            conexion,
            $"DELETE FROM \"AUDITORIAS\" WHERE id = '{id}';",
            conPurga: true);

        (await ExisteAsync(conexion, id)).Should().BeTrue(
            "una fila de menos de 90 dias no se puede borrar por ninguna via");
    }

    [Fact]
    public async Task Delete_Without_The_Purge_Marker_Should_Not_Remove_Anything()
    {
        // Sin la marca de purga, ni siquiera las filas antiguas se borran: la
        // unica via es atlas_security.purgar_auditorias().
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);
        var id = await InsertarAuditoriaAsync(owner, DateTime.UtcNow.AddDays(-400));

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        await EjecutarAsync(conexion, $"DELETE FROM \"AUDITORIAS\" WHERE id = '{id}';");

        (await ExisteAsync(conexion, id)).Should().BeTrue();
    }

    [Fact]
    public async Task Purge_Should_Delete_Old_Rows_And_Keep_Recent_Ones()
    {
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);
        var vieja = await InsertarAuditoriaAsync(owner, DateTime.UtcNow.AddDays(-400));
        var reciente = await InsertarAuditoriaAsync(owner, DateTime.UtcNow.AddDays(-10));

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "SELECT atlas_security.purgar_auditorias(365);";
            var borradas = (long)(await cmd.ExecuteScalarAsync())!;
            borradas.Should().Be(1);
        }

        (await ExisteAsync(conexion, vieja)).Should().BeFalse();
        (await ExisteAsync(conexion, reciente)).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // Regresion del bug de purga silenciosa. AUDITORIA_INTEGRACIONES tenia el
    // mismo defecto que AUDITORIAS: FORCE ROW LEVEL SECURITY sin politica de
    // DELETE, asi que LimpiezaAuditoriaJob borraba cero filas cada noche y lo
    // registraba como exito. Estos dos tests fallan si vuelve a pasar.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Integration_Audit_Purge_Should_Actually_Delete_Old_Rows()
    {
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);
        var vieja = await InsertarAuditoriaIntegracionAsync(owner, DateTime.UtcNow.AddDays(-60));
        var reciente = await InsertarAuditoriaIntegracionAsync(owner, DateTime.UtcNow.AddDays(-2));

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "SELECT atlas_security.purgar_auditorias_integracion(28);";
            var borradas = (long)(await cmd.ExecuteScalarAsync())!;
            borradas.Should().Be(1, "la purga tiene que borrar de verdad, no devolver cero en silencio");
        }

        (await ExisteEnIntegracionAsync(conexion, vieja)).Should().BeFalse();
        (await ExisteEnIntegracionAsync(conexion, reciente)).Should().BeTrue();
    }

    [Fact]
    public async Task Integration_Audit_Should_Not_Be_Deletable_Outside_The_Purge()
    {
        // El bug se arregla sin abrir la mano: un DELETE suelto sigue sin borrar
        // nada, aunque la fila sea antigua.
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);
        var id = await InsertarAuditoriaIntegracionAsync(owner, DateTime.UtcNow.AddDays(-60));

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        await EjecutarAsync(conexion, $"DELETE FROM \"AUDITORIA_INTEGRACIONES\" WHERE id = '{id}';");

        (await ExisteEnIntegracionAsync(conexion, id)).Should().BeTrue();
    }

    [Fact]
    public async Task Integration_Audit_Purge_Should_Reject_A_Retention_Below_Its_Floor()
    {
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        var purga = async () => await EjecutarAsync(conexion, "SELECT atlas_security.purgar_auditorias_integracion(3);");

        (await purga.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }

    [Fact]
    public async Task Purge_Should_Reject_A_Retention_Below_The_Database_Floor()
    {
        // El suelo vive en la BD y no solo en la configuracion de la app: bajar
        // Auditoria:RetentionDays a 7 no puede convertirse en una via para
        // borrar el rastro del ultimo mes.
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);

        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);

        var purga = async () => await EjecutarAsync(conexion, $"SELECT atlas_security.purgar_auditorias({MinRetentionDays - 1});");

        (await purga.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }

    [Fact]
    public async Task Sequence_Should_Be_Monotonic_So_Deletions_Leave_A_Gap()
    {
        var (owner, _) = await CrearRolesAsync();
        await MigrarAsync(owner);

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await InsertarAuditoriaAsync(owner, DateTime.UtcNow.AddDays(-400 + i)));
        }

        await using var db = await ContextoConRlsAsync(owner);
        var secuencias = await db.Auditorias.AsNoTracking()
            .OrderBy(a => a.Secuencia)
            .Select(a => a.Secuencia)
            .ToListAsync();

        secuencias.Should().HaveCount(3);
        secuencias[1].Should().Be(secuencias[0] + 1);
        secuencias[2].Should().Be(secuencias[1] + 1);

        // Purgar la del medio deja hueco, que es la senal que busca
        // AuditIntegrityService.
        await using var conexion = new NpgsqlConnection(owner);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);
        await EjecutarAsync(conexion, $"DELETE FROM \"AUDITORIAS\" WHERE id = '{ids[1]}';", conPurga: true);

        await using var db2 = await ContextoConRlsAsync(owner);
        var restantes = await db2.Auditorias.AsNoTracking()
            .OrderBy(a => a.Secuencia)
            .Select(a => a.Secuencia)
            .ToListAsync();

        restantes.Should().HaveCount(2);
        (restantes[1] - restantes[0]).Should().Be(2, "el hueco delata la fila borrada");
    }

    // --- helpers -----------------------------------------------------------

    private static DbContextOptions<AppDbContext> Opciones(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    private static AppDbContext Contexto(string connectionString) => new(Opciones(connectionString));

    private static async Task MigrarAsync(string ownerConnectionString)
    {
        await using (var db = Contexto(ownerConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await EscribirSecretoRlsAsync(ownerConnectionString);
    }

    /// <summary>
    /// Abre un contexto EF con el contexto RLS de sistema ya fijado sobre la
    /// conexion. Sin esto, AUDITORIAS no devuelve ninguna fila.
    /// </summary>
    private static async Task<AppDbContext> ContextoConRlsAsync(string connectionString)
    {
        var db = Contexto(connectionString);
        await db.Database.OpenConnectionAsync();
        await FijarContextoSistemaAsync((NpgsqlConnection)db.Database.GetDbConnection());
        return db;
    }

    private static async Task<Guid> InsertarAuditoriaAsync(string connectionString, DateTime timestampUtc)
    {
        var id = Guid.NewGuid();
        await using var conexion = new NpgsqlConnection(connectionString);
        await conexion.OpenAsync();
        await FijarContextoSistemaAsync(conexion);
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "AUDITORIAS" (id, tipo_accion, "timestamp", origen)
            VALUES (@id, 'LOGIN', @ts, 'UI');
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("ts", timestampUtc);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// AUDITORIAS tiene FORCE ROW LEVEL SECURITY: sin contexto firmado no se
    /// puede ni insertar ni leer, tampoco siendo el propietario. Se fija el modo
    /// sistema, que es con el que corren los jobs.
    /// </summary>
    private static async Task FijarContextoSistemaAsync(NpgsqlConnection conexion)
    {
        var firma = RlsContextSigner.Sign(
            RlsSecret,
            authMode: "system",
            userId: string.Empty,
            integrationTokenId: string.Empty,
            isAdmin: "true",
            system: "true",
            requestScope: "system");

        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT
                set_config('atlas.auth_mode', 'system', false),
                set_config('atlas.user_id', '', false),
                set_config('atlas.integration_token_id', '', false),
                set_config('atlas.is_admin', 'true', false),
                set_config('atlas.system', 'true', false),
                set_config('atlas.request_scope', 'system', false),
                set_config('atlas.context_signature', @firma, false)
            """;
        cmd.Parameters.AddWithValue("firma", firma);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Persiste el secreto de contexto RLS igual que hace
    /// Program.EnsureRlsContextSecret al arrancar.
    /// </summary>
    private static async Task EscribirSecretoRlsAsync(string ownerConnectionString)
    {
        await using var conexion = new NpgsqlConnection(ownerConnectionString);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        // Mismo SQL que Program.EnsureRlsContextSecret.
        cmd.CommandText = """
            CREATE SCHEMA IF NOT EXISTS atlas_security;
            CREATE TABLE IF NOT EXISTS atlas_security.rls_context_secret (
                id boolean PRIMARY KEY DEFAULT true CHECK (id),
                secret text NOT NULL,
                updated_at timestamp with time zone NOT NULL DEFAULT now()
            );
            INSERT INTO atlas_security.rls_context_secret (id, secret, updated_at)
            VALUES (true, @secret, now())
            ON CONFLICT (id) DO UPDATE SET secret = EXCLUDED.secret, updated_at = now();
            """;
        cmd.Parameters.AddWithValue("secret", RlsSecret);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, bool conPurga = false)
    {
        await using var tx = await conexion.BeginTransactionAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = conPurga
            ? $"SET LOCAL atlas.audit_purge = 'on'; {sql}"
            : sql;
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// AUDITORIA_INTEGRACIONES exige un token por FK, y el token exige un usuario
    /// creador, asi que se monta la cadena entera.
    /// </summary>
    private static async Task<Guid> InsertarAuditoriaIntegracionAsync(string connectionString, DateTime timestampUtc)
    {
        // Via EF y no SQL crudo: USUARIOS e INTEGRATION_TOKENS tienen bastantes
        // columnas NOT NULL con default en el modelo, y enumerarlas a mano aqui
        // solo garantiza que el test se rompa la proxima vez que alguien anada
        // una.
        var usuarioId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var id = Guid.NewGuid();

        await using var db = await ContextoConRlsAsync(connectionString);
        db.Usuarios.Add(new Usuario
        {
            Id = usuarioId,
            Email = $"{usuarioId:N}@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Creador de token",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = tokenId,
            TokenHash = Guid.NewGuid().ToString("N"),
            Nombre = "token-de-prueba",
            UsuarioCreadorId = usuarioId
        });
        db.AuditoriaIntegraciones.Add(new AuditoriaIntegracion
        {
            Id = id,
            TokenId = tokenId,
            Endpoint = "/api/integration/openclaw/cuentas",
            Metodo = "GET",
            Timestamp = timestampUtc
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<bool> ExisteEnIntegracionAsync(NpgsqlConnection conexion, Guid id)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"AUDITORIA_INTEGRACIONES\" WHERE id = '{id}';";
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private static async Task<bool> ExisteAsync(NpgsqlConnection conexion, Guid id)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"AUDITORIAS\" WHERE id = '{id}';";
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>
    /// Replica los grants que aplica Program.GrantRuntimeDatabasePrivileges,
    /// incluido el REVOKE sobre AUDITORIAS que es lo que se esta probando.
    /// </summary>
    private static async Task ConcederPrivilegiosRuntimeAsync(string ownerConnectionString, string runtimeConnectionString)
    {
        var runtimeRole = new NpgsqlConnectionStringBuilder(runtimeConnectionString).Username!;
        await using var conexion = new NpgsqlConnection(ownerConnectionString);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            GRANT USAGE ON SCHEMA public TO "{runtimeRole}";
            GRANT USAGE ON SCHEMA atlas_security TO "{runtimeRole}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "{runtimeRole}";
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO "{runtimeRole}";
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA atlas_security TO "{runtimeRole}";
            REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "AUDITORIAS" FROM "{runtimeRole}";
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(string Owner, string Runtime)> CrearRolesAsync()
    {
        // Base de datos propia por test: las migraciones y los roles son estado
        // global y compartirlos haria los tests dependientes del orden.
        var plantilla = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString);
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var baseDatos = $"audit_ao_{sufijo}";
        var ownerRole = $"ao_owner_{sufijo}";
        var runtimeRole = $"ao_app_{sufijo}";
        var password = $"test-{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"""
                CREATE ROLE "{ownerRole}" WITH LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
                CREATE ROLE "{runtimeRole}" WITH LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
                """;
            await cmd.ExecuteNonQueryAsync();

            await using var crearBd = admin.CreateCommand();
            crearBd.CommandText = $"CREATE DATABASE \"{baseDatos}\" OWNER \"{ownerRole}\";";
            await crearBd.ExecuteNonQueryAsync();
        }

        var owner = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = baseDatos,
            Username = ownerRole,
            Password = password
        }.ToString();

        var runtime = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = baseDatos,
            Username = runtimeRole,
            Password = password
        }.ToString();

        await using (var comoOwner = new NpgsqlConnection(owner))
        {
            await comoOwner.OpenAsync();
            await using var cmd = comoOwner.CreateCommand();
            cmd.CommandText = $"""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;
                GRANT CONNECT ON DATABASE "{baseDatos}" TO "{runtimeRole}";
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        return (owner, runtime);
    }
}
