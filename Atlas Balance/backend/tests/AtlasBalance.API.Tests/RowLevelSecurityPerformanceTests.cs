using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed partial class RowLevelSecurityTests
{
    [Theory(Timeout = 120000)]
    [InlineData(RolUsuario.GERENTE)]
    [InlineData(RolUsuario.EMPLEADO)]
    public async Task FinancialReads_Should_Complete_Within_RequestBudget_And_Keep_UserScope(RolUsuario role)
    {
        var (migrationConnection, runtimeConnection) = await CreateRoleConnectionStringsAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }
        await ConfigureRlsRuntimeAsync(migrationConnection, runtimeConnection);

        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var allowedAccount = Guid.NewGuid();
        var deniedAccount = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(runtimeConnection);
        await connection.OpenAsync();
        await SetRlsContextAsync(connection, "system", null, null, true, true, "system");
        await using var transaction = await connection.BeginTransactionAsync();
        var seedOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(seedOptions))
        {
            await db.Database.UseTransactionAsync(transaction);
            db.Usuarios.Add(new Usuario { Id = userId, Email = $"{userId:N}@test.invalid", NombreCompleto = "Performance test", PasswordHash = "unused", Rol = role, Activo = true });
            db.Titulares.Add(new Titular { Id = titularId, Nombre = "Performance test", Tipo = TipoTitular.EMPRESA });
            db.Cuentas.AddRange(
                new Cuenta { Id = allowedAccount, TitularId = titularId, Nombre = "Allowed", Divisa = "EUR", Activa = true },
                new Cuenta { Id = deniedAccount, TitularId = titularId, Nombre = "Denied", Divisa = "EUR", Activa = true });
            db.PermisosUsuario.Add(new PermisoUsuario { Id = Guid.NewGuid(), UsuarioId = userId, CuentaId = allowedAccount, PuedeVerCuentas = true, PuedeVerDashboard = true });
            await db.SaveChangesAsync();
        }
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandTimeout = 60;
            seed.CommandText = """
                INSERT INTO "EXTRACTOS" (id, cuenta_id, fecha, concepto, monto, saldo, fila_numero, checked, flagged, fecha_creacion)
                SELECT gen_random_uuid(), c, DATE '2026-09-01', 'Synthetic', 1, n, n, false, false, now()
                FROM unnest(ARRAY[@allowed, @denied]) c CROSS JOIN generate_series(1, 10000) n;
                INSERT INTO "EXTRACTOS_COLUMNAS_EXTRA" (id, extracto_id, nombre_columna, valor)
                SELECT gen_random_uuid(), id, 'Synthetic', 'test' FROM "EXTRACTOS"
                WHERE cuenta_id IN (@allowed, @denied);
                """;
            seed.Parameters.AddWithValue("allowed", allowedAccount);
            seed.Parameters.AddWithValue("denied", deniedAccount);
            await seed.ExecuteNonQueryAsync();
        }

        foreach (var scope in new[] { "data", "dashboard" })
        {
            await SetRlsContextAsync(connection, "user", userId, null, false, false, scope);
            foreach (var table in new[] { "EXTRACTOS", "EXTRACTOS_COLUMNAS_EXTRA" })
            {
                await using var query = connection.CreateCommand();
                // Below the browser's 15s deadline, with room for the other request work.
                query.CommandTimeout = 8;
                query.CommandText = $"SELECT count(*) FROM \"{table}\"";
                var timer = System.Diagnostics.Stopwatch.StartNew();
                ((long)(await query.ExecuteScalarAsync())!).Should().Be(10000, "only the authorized account is visible");
                System.Console.WriteLine($"RLS {role}/{scope}/{table}: {timer.ElapsedMilliseconds} ms");
            }

            await using var latestBalances = connection.CreateCommand();
            latestBalances.CommandTimeout = 8;
            latestBalances.CommandText = """
                SELECT sum(saldo) FROM (
                    SELECT saldo, row_number() OVER (PARTITION BY cuenta_id ORDER BY fila_numero DESC, fecha DESC) AS row
                    FROM "EXTRACTOS" WHERE deleted_at IS NULL
                ) latest WHERE row = 1
                """;
            ((decimal)(await latestBalances.ExecuteScalarAsync())!).Should().Be(10000m);
        }

        await SetUnsignedRlsContextAsync(connection, "user", userId, null, true, false, "data");
        (await ExecuteScalarAsync<long>(connection, "SELECT count(*) FROM \"EXTRACTOS\"")).Should().Be(0);
        (await ExecuteScalarAsync<long>(connection, "SELECT count(*) FROM \"EXTRACTOS_COLUMNAS_EXTRA\"")).Should().Be(0);
        await transaction.RollbackAsync();
    }
}
