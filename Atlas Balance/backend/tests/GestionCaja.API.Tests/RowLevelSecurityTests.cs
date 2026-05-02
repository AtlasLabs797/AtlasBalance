using FluentAssertions;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GestionCaja.API.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RowLevelSecurityTests
{
    private const string RlsContextSecret = "test-rls-context-secret-with-more-than-32-characters";
    private readonly PostgresFixture _fixture;

    public RowLevelSecurityTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CoreFinancialTables_Should_Enforce_Rls_By_User_And_IntegrationScope()
    {
        var (migrationConnectionString, runtimeConnectionString) = await CreateRoleConnectionStringsAsync();
        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(runtimeConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var adminId = Guid.NewGuid();
        var readerId = Guid.NewGuid();
        var writerId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();
        var cuentaPermitidaId = Guid.NewGuid();
        var cuentaBloqueadaId = Guid.NewGuid();
        var extractoPermitidoId = Guid.NewGuid();
        var extractoBloqueadoId = Guid.NewGuid();
        var integrationTokenId = Guid.NewGuid();

        await using (var db = new AppDbContext(migrationOptions))
        {
            await db.Database.MigrateAsync();
        }

        await ConfigureRlsRuntimeAsync(migrationConnectionString, runtimeConnectionString);

        await using (var db = new AppDbContext(options))
        {
            await db.Database.OpenConnectionAsync();
            await SetRlsContextAsync(
                (NpgsqlConnection)db.Database.GetDbConnection(),
                "system",
                null,
                null,
                isAdmin: true,
                isSystem: true,
                "system");

            db.Usuarios.AddRange(
                new Usuario
                {
                    Id = adminId,
                    Email = $"admin-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Admin RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.ADMIN,
                    Activo = true
                },
                new Usuario
                {
                    Id = readerId,
                    Email = $"reader-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Reader RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.GERENTE,
                    Activo = true
                },
                new Usuario
                {
                    Id = writerId,
                    Email = $"writer-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Writer RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.GERENTE,
                    Activo = true
                });

            db.Titulares.AddRange(
                new Titular { Id = titularPermitidoId, Nombre = "Titular permitido", Tipo = TipoTitular.EMPRESA },
                new Titular { Id = titularBloqueadoId, Nombre = "Titular bloqueado", Tipo = TipoTitular.EMPRESA });

            db.Cuentas.AddRange(
                new Cuenta
                {
                    Id = cuentaPermitidaId,
                    TitularId = titularPermitidoId,
                    Nombre = "Cuenta permitida",
                    Divisa = "EUR",
                    Activa = true
                },
                new Cuenta
                {
                    Id = cuentaBloqueadaId,
                    TitularId = titularBloqueadoId,
                    Nombre = "Cuenta bloqueada",
                    Divisa = "EUR",
                    Activa = true
                });

            db.Extractos.AddRange(
                new Extracto
                {
                    Id = extractoPermitidoId,
                    CuentaId = cuentaPermitidaId,
                    Fecha = new DateOnly(2026, 5, 1),
                    Concepto = "Permitido",
                    Monto = 10,
                    Saldo = 10,
                    FilaNumero = 1
                },
                new Extracto
                {
                    Id = extractoBloqueadoId,
                    CuentaId = cuentaBloqueadaId,
                    Fecha = new DateOnly(2026, 5, 1),
                    Concepto = "Bloqueado",
                    Monto = 20,
                    Saldo = 20,
                    FilaNumero = 1
                });

            db.PermisosUsuario.AddRange(
                new PermisoUsuario
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = readerId,
                    CuentaId = cuentaPermitidaId,
                    PuedeVerCuentas = true
                },
                new PermisoUsuario
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = writerId,
                    CuentaId = cuentaPermitidaId,
                    PuedeImportar = true
                });

            db.IntegrationTokens.Add(new IntegrationToken
            {
                Id = integrationTokenId,
                Nombre = "RLS integration",
                TokenHash = Guid.NewGuid().ToString("N"),
                Tipo = "openclaw",
                Estado = EstadoTokenIntegracion.Activo,
                PermisoLectura = true,
                UsuarioCreadorId = adminId
            });
            db.IntegrationPermissions.Add(new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = integrationTokenId,
                CuentaId = cuentaBloqueadaId,
                AccesoTipo = "lectura"
            });

            await db.SaveChangesAsync();
            await db.Database.CloseConnectionAsync();
        }

        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(runtimeConnectionString);
        await connection.OpenAsync();

        var tableFailures = await ExecuteScalarAsync<long>(
            connection,
            """
            SELECT count(*)
            FROM (
                SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity, coalesce(p.policy_count, 0) AS policy_count
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN (
                    SELECT polrelid, count(*) AS policy_count
                    FROM pg_policy
                    GROUP BY polrelid
                ) p ON p.polrelid = c.oid
                WHERE n.nspname = 'public'
                  AND c.relname IN (
                      'TITULARES',
                      'CUENTAS',
                      'PLAZOS_FIJOS',
                      'EXTRACTOS',
                      'EXTRACTOS_COLUMNAS_EXTRA',
                      'EXPORTACIONES',
                      'PREFERENCIAS_USUARIO_CUENTA',
                      'AUDITORIAS',
                      'AUDITORIA_INTEGRACIONES',
                      'BACKUPS',
                      'NOTIFICACIONES_ADMIN'
                  )
            ) r
            WHERE NOT r.relrowsecurity OR NOT r.relforcerowsecurity OR r.policy_count = 0
            """);
        tableFailures.Should().Be(0);

        var roleFlags = await ExecuteScalarAsync<string>(
            connection,
            "SELECT rolsuper::text || '|' || rolbypassrls::text FROM pg_roles WHERE rolname = current_user");
        roleFlags.Should().Be("false|false");

        var runtimeOwnedTables = await ExecuteScalarAsync<long>(
            connection,
            """
            SELECT count(*)
            FROM pg_tables
            WHERE schemaname = 'public'
              AND tableowner = current_user
              AND tablename IN (
                  'TITULARES',
                  'CUENTAS',
                  'PLAZOS_FIJOS',
                  'EXTRACTOS',
                  'EXTRACTOS_COLUMNAS_EXTRA',
                  'EXPORTACIONES',
                  'PREFERENCIAS_USUARIO_CUENTA',
                  'AUDITORIAS',
                  'AUDITORIA_INTEGRACIONES',
                  'BACKUPS',
                  'NOTIFICACIONES_ADMIN'
              )
            """);
        runtimeOwnedTables.Should().Be(0);

        await SetRlsContextAsync(connection, "anonymous", null, null, isAdmin: false, isSystem: false, "anonymous");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(0);

        await SetUnsignedRlsContextAsync(connection, "user", readerId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(0);
        await SetUnsignedRlsContextAsync(connection, "system", null, null, isAdmin: true, isSystem: true, "system");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(0);

        await SetRlsContextAsync(connection, "user", readerId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "TITULARES", titularPermitidoId, titularBloqueadoId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(1);

        var deniedInsert = async () => await InsertExtractoAsync(connection, cuentaPermitidaId);
        await deniedInsert.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        var deniedExport = async () => await InsertExportacionAsync(connection, cuentaPermitidaId);
        await deniedExport.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        await SetRlsContextAsync(connection, "user", writerId, null, isAdmin: false, isSystem: false, "data");
        await InsertExtractoAsync(connection, cuentaPermitidaId);
        await InsertExportacionAsync(connection, cuentaPermitidaId);

        await SetRlsContextAsync(connection, "integration", null, integrationTokenId, isAdmin: false, isSystem: false, "integration");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(1);

        await SetRlsContextAsync(connection, "user", adminId, null, isAdmin: true, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(2);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(2);
    }

    private async Task<(string MigrationConnectionString, string RuntimeConnectionString)> CreateRoleConnectionStringsAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString);
        var ownerRole = $"rls_owner_{Guid.NewGuid():N}"[..26];
        var runtimeRole = $"rls_app_{Guid.NewGuid():N}"[..24];
        var ownerPassword = $"test-{Guid.NewGuid():N}";
        var runtimePassword = $"test-{Guid.NewGuid():N}";
        var escapedOwnerPassword = ownerPassword.Replace("'", "''", StringComparison.Ordinal);
        var escapedPassword = runtimePassword.Replace("'", "''", StringComparison.Ordinal);
        var escapedDatabase = builder.Database?.Replace("\"", "\"\"", StringComparison.Ordinal) ?? "gestion_caja_tests";

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE EXTENSION IF NOT EXISTS pgcrypto;
            CREATE ROLE "{ownerRole}" WITH LOGIN PASSWORD '{escapedOwnerPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
            CREATE ROLE "{runtimeRole}" WITH LOGIN PASSWORD '{escapedPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
            ALTER DATABASE "{escapedDatabase}" OWNER TO "{ownerRole}";
            ALTER SCHEMA public OWNER TO "{ownerRole}";
            GRANT CONNECT ON DATABASE "{escapedDatabase}" TO "{ownerRole}";
            GRANT CONNECT ON DATABASE "{escapedDatabase}" TO "{runtimeRole}";
            GRANT USAGE, CREATE ON SCHEMA public TO "{ownerRole}";
            DO $$
            DECLARE
                ns record;
                obj record;
            BEGIN
                FOR ns IN
                    SELECT n.nspname
                    FROM pg_namespace n
                    WHERE n.nspname IN ('public', 'atlas_security')
                LOOP
                    EXECUTE format('ALTER SCHEMA %I OWNER TO %I', ns.nspname, '{ownerRole}');
                    EXECUTE format('GRANT USAGE, CREATE ON SCHEMA %I TO %I', ns.nspname, '{ownerRole}');
                    FOR obj IN
                        SELECT c.relkind, c.relname
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = ns.nspname
                          AND c.relkind IN ('r','S','v','m','p')
                    LOOP
                        EXECUTE format('ALTER %s %I.%I OWNER TO %I',
                            CASE obj.relkind
                                WHEN 'r' THEN 'TABLE'
                                WHEN 'p' THEN 'TABLE'
                                WHEN 'S' THEN 'SEQUENCE'
                                WHEN 'v' THEN 'VIEW'
                                WHEN 'm' THEN 'MATERIALIZED VIEW'
                            END,
                            ns.nspname, obj.relname, '{ownerRole}');
                    END LOOP;
                    FOR obj IN
                        SELECT p.proname, pg_get_function_identity_arguments(p.oid) AS args
                        FROM pg_proc p
                        JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = ns.nspname
                    LOOP
                        EXECUTE format('ALTER FUNCTION %I.%I(%s) OWNER TO %I',
                            ns.nspname, obj.proname, obj.args, '{ownerRole}');
                    END LOOP;
                END LOOP;
            END
            $$;
            """;
        await command.ExecuteNonQueryAsync();

        var migrationBuilder = new NpgsqlConnectionStringBuilder(builder.ConnectionString)
        {
            Username = ownerRole,
            Password = ownerPassword
        };
        var runtimeBuilder = new NpgsqlConnectionStringBuilder(builder.ConnectionString)
        {
            Username = runtimeRole,
            Password = runtimePassword
        };
        return (migrationBuilder.ConnectionString, runtimeBuilder.ConnectionString);
    }

    private static async Task ConfigureRlsRuntimeAsync(string migrationConnectionString, string runtimeConnectionString)
    {
        var runtimeBuilder = new NpgsqlConnectionStringBuilder(runtimeConnectionString);
        var runtimeUsername = runtimeBuilder.Username
            ?? throw new InvalidOperationException("Runtime username is required for RLS test grants.");
        var runtimeRole = QuoteIdentifier(runtimeUsername);

        await using var connection = new NpgsqlConnection(migrationConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            INSERT INTO atlas_security.rls_context_secret (id, secret, updated_at)
            VALUES (true, @secret, now())
            ON CONFLICT (id) DO UPDATE
            SET secret = EXCLUDED.secret,
                updated_at = now();

            REVOKE ALL ON TABLE atlas_security.rls_context_secret FROM PUBLIC;
            REVOKE ALL ON TABLE atlas_security.rls_context_secret FROM {{runtimeRole}};
            GRANT USAGE ON SCHEMA public TO {{runtimeRole}};
            GRANT USAGE ON SCHEMA atlas_security TO {{runtimeRole}};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {{runtimeRole}};
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {{runtimeRole}};
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA atlas_security TO {{runtimeRole}};
            """;
        command.Parameters.AddWithValue("secret", RlsContextSecret);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetRlsContextAsync(
        NpgsqlConnection connection,
        string authMode,
        Guid? userId,
        Guid? integrationTokenId,
        bool isAdmin,
        bool isSystem,
        string requestScope)
    {
        await using var command = connection.CreateCommand();
        var isAdminText = isAdmin ? "true" : "false";
        var isSystemText = isSystem ? "true" : "false";
        var signature = RlsContextSigner.Sign(
            RlsContextSecret,
            authMode,
            userId?.ToString() ?? string.Empty,
            integrationTokenId?.ToString() ?? string.Empty,
            isAdminText,
            isSystemText,
            requestScope);
        command.CommandText = """
            SELECT
                set_config('atlas.auth_mode', @auth_mode, false),
                set_config('atlas.user_id', @user_id, false),
                set_config('atlas.integration_token_id', @integration_token_id, false),
                set_config('atlas.is_admin', @is_admin, false),
                set_config('atlas.system', @system, false),
                set_config('atlas.request_scope', @request_scope, false),
                set_config('atlas.context_signature', @context_signature, false)
            """;
        command.Parameters.AddWithValue("auth_mode", authMode);
        command.Parameters.AddWithValue("user_id", userId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("integration_token_id", integrationTokenId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("is_admin", isAdminText);
        command.Parameters.AddWithValue("system", isSystemText);
        command.Parameters.AddWithValue("request_scope", requestScope);
        command.Parameters.AddWithValue("context_signature", signature);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetUnsignedRlsContextAsync(
        NpgsqlConnection connection,
        string authMode,
        Guid? userId,
        Guid? integrationTokenId,
        bool isAdmin,
        bool isSystem,
        string requestScope)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                set_config('atlas.auth_mode', @auth_mode, false),
                set_config('atlas.user_id', @user_id, false),
                set_config('atlas.integration_token_id', @integration_token_id, false),
                set_config('atlas.is_admin', @is_admin, false),
                set_config('atlas.system', @system, false),
                set_config('atlas.request_scope', @request_scope, false),
                set_config('atlas.context_signature', 'invalid-signature', false)
            """;
        command.Parameters.AddWithValue("auth_mode", authMode);
        command.Parameters.AddWithValue("user_id", userId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("integration_token_id", integrationTokenId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("is_admin", isAdmin ? "true" : "false");
        command.Parameters.AddWithValue("system", isSystem ? "true" : "false");
        command.Parameters.AddWithValue("request_scope", requestScope);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountByIdsAsync(NpgsqlConnection connection, string table, params Guid[] ids)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT count(*) FROM "{table}" WHERE id = ANY(@ids)""";
        command.Parameters.AddWithValue("ids", ids);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<T> ExecuteScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        result.Should().NotBeNull();
        return (T)result!;
    }

    private static async Task InsertExtractoAsync(NpgsqlConnection connection, Guid cuentaId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "EXTRACTOS"
                (id, cuenta_id, fecha, concepto, monto, saldo, fila_numero, checked, flagged, fecha_creacion)
            VALUES
                (@id, @cuenta_id, DATE '2026-05-02', 'RLS insert', 1, 1, @fila_numero, false, false, now())
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("cuenta_id", cuentaId);
        command.Parameters.AddWithValue("fila_numero", Random.Shared.Next(1000, 1000000));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertExportacionAsync(NpgsqlConnection connection, Guid cuentaId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "EXPORTACIONES"
                (id, cuenta_id, fecha_exportacion, estado, tipo)
            VALUES
                (@id, @cuenta_id, now(), 1, 1)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("cuenta_id", cuentaId);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
