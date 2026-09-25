using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-03.01 (#4): RlsDbCommandInterceptor.BuildContext() ya no falla abierto
// (System/admin=true) cuando no hay HttpContext. Estos tests corren contra un
// PostgreSQL real (Testcontainers) porque lo que se comprueba es la policy RLS
// en si, no solo el contexto que el interceptor construye (eso ya lo cubre
// RlsDbCommandInterceptorContextTests, sin PostgreSQL).
public sealed partial class RowLevelSecurityTests
{
    [Fact]
    public async Task WriteWithoutHttpContext_AndWithoutSystemScope_ShouldBeDeniedByRls()
    {
        var (migrationConnection, runtimeConnection) = await CreateRoleConnectionStringsAsync();
        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(migrationOptions))
        {
            await db.Database.MigrateAsync();
        }
        await ConfigureRlsRuntimeAsync(migrationConnection, runtimeConnection);

        var cuentaId = await SeedCuentaAsync(migrationConnection);

        var accessor = new HttpContextAccessor { HttpContext = null };
        var interceptor = new RlsDbCommandInterceptor(accessor, new RlsContextSecret(RlsContextSecret));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(runtimeConnection).UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;

        // Sin HttpContext (worker perdido) y sin SystemContextScope activo: el
        // interceptor publica el contexto "anonymous", que la policy
        // exportaciones_write rechaza para un INSERT (no es admin/system ni
        // tiene permiso de exportacion sobre la cuenta).
        await using var db2 = new AppDbContext(options);
        db2.Exportaciones.Add(new Exportacion
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            FechaExportacion = DateTime.UtcNow,
            Estado = EstadoProceso.SUCCESS,
            Tipo = TipoProceso.MANUAL
        });

        var action = async () => await db2.SaveChangesAsync();
        var thrown = await action.Should().ThrowAsync<DbUpdateException>();
        var pgException = thrown.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        pgException.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task WriteWithoutHttpContext_ButWithSystemScope_ShouldBeAllowedByRls()
    {
        var (migrationConnection, runtimeConnection) = await CreateRoleConnectionStringsAsync();
        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(migrationOptions))
        {
            await db.Database.MigrateAsync();
        }
        await ConfigureRlsRuntimeAsync(migrationConnection, runtimeConnection);

        var cuentaId = await SeedCuentaAsync(migrationConnection);

        var accessor = new HttpContextAccessor { HttpContext = null };
        var interceptor = new RlsDbCommandInterceptor(accessor, new RlsContextSecret(RlsContextSecret));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(runtimeConnection).UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;

        var exportacionId = Guid.NewGuid();
        await using var db2 = new AppDbContext(options);
        using (RlsDbCommandInterceptor.SystemContextScope.Enter())
        {
            db2.Exportaciones.Add(new Exportacion
            {
                Id = exportacionId,
                CuentaId = cuentaId,
                FechaExportacion = DateTime.UtcNow,
                Estado = EstadoProceso.SUCCESS,
                Tipo = TipoProceso.MANUAL
            });

            await db2.SaveChangesAsync();
        }

        await using var verifyConnection = new NpgsqlConnection(migrationConnection);
        await verifyConnection.OpenAsync();
        await SetRlsContextAsync(verifyConnection, "system", null, null, true, true, "system");
        (await CountByIdsAsync(verifyConnection, "EXPORTACIONES", exportacionId)).Should().Be(1);
    }

    // El caso real que motivo 20260731090000_FixExportacionesPurgaRlsWithCheck:
    // LimpiezaExportacionesJob hace un soft-delete (UPDATE ... SET deleted_at)
    // sobre EXPORTACIONES, que exportaciones_write solo permite bajo
    // is_admin_or_system(). El job corre en un worker de Hangfire, sin
    // HttpContext: si no abriera su propio SystemContextScope (Jobs/LimpiezaExportacionesJob.cs),
    // el UPDATE fallaria con "new row violates row-level security policy" en
    // cada ejecucion, exactamente como paso antes de ese fix.
    [Fact]
    public async Task LimpiezaExportacionesJob_ShouldSoftDeleteExpiredExportaciones_ViaItsOwnSystemScope()
    {
        var (migrationConnection, runtimeConnection) = await CreateRoleConnectionStringsAsync();
        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(migrationOptions))
        {
            await db.Database.MigrateAsync();
        }
        await ConfigureRlsRuntimeAsync(migrationConnection, runtimeConnection);

        var cuentaId = await SeedCuentaAsync(migrationConnection);
        var exportacionId = Guid.NewGuid();
        await using (var systemConnection = new NpgsqlConnection(migrationConnection))
        {
            await systemConnection.OpenAsync();
            await SetRlsContextAsync(systemConnection, "system", null, null, true, true, "system");
            await using var seed = systemConnection.CreateCommand();
            seed.CommandText = """
                INSERT INTO "EXPORTACIONES" (id, cuenta_id, fecha_exportacion, estado, tipo)
                VALUES (@id, @cuenta, now() - interval '200 days', 1, 1)
                """;
            seed.Parameters.AddWithValue("id", exportacionId);
            seed.Parameters.AddWithValue("cuenta", cuentaId);
            await seed.ExecuteNonQueryAsync();
        }

        // El job se instancia igual que Program.cs lo resuelve via DI: un
        // AppDbContext con el interceptor real y SIN HttpContext (es exactamente
        // el estado de un worker de Hangfire). No se abre ningun
        // SystemContextScope desde el test: si el job no lo abriera por su
        // cuenta, esto fallaria con 42501.
        var accessor = new HttpContextAccessor { HttpContext = null };
        var interceptor = new RlsDbCommandInterceptor(accessor, new RlsContextSecret(RlsContextSecret));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(runtimeConnection).UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;

        await using (var jobDb = new AppDbContext(options))
        {
            var job = new LimpiezaExportacionesJob(jobDb, new SystemClock(), NullLogger<LimpiezaExportacionesJob>.Instance);

            var action = async () => await job.ExecuteAsync();
            await action.Should().NotThrowAsync(
                "LimpiezaExportacionesJob debe abrir su propio SystemContextScope para pasar el WITH CHECK de exportaciones_write");
        }

        await using var verifyConnection = new NpgsqlConnection(migrationConnection);
        await verifyConnection.OpenAsync();
        await SetRlsContextAsync(verifyConnection, "system", null, null, true, true, "system");
        var wasSoftDeleted = await ExecuteScalarAsync<bool>(
            verifyConnection,
            $"SELECT deleted_at IS NOT NULL FROM \"EXPORTACIONES\" WHERE id = '{exportacionId}'");
        wasSoftDeleted.Should().BeTrue("el job debe haber marcado la exportacion vencida como borrada logicamente");
    }

    // Se usa EF (en vez de SQL crudo) para no tener que replicar a mano todas
    // las columnas NOT NULL/valores por defecto de TITULARES y CUENTAS; el
    // contexto "system" hace falta igualmente porque ambas tablas tienen
    // FORCE ROW LEVEL SECURITY (aplica incluso al rol propietario).
    private static async Task<Guid> SeedCuentaAsync(string migrationConnectionString)
    {
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(migrationConnectionString);
        await connection.OpenAsync();
        await SetRlsContextAsync(connection, "system", null, null, true, true, "system");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(options))
        {
            db.Titulares.Add(new Titular { Id = titularId, Nombre = "Test RLS scope", Tipo = TipoTitular.EMPRESA });
            db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Test RLS scope", Divisa = "EUR", Activa = true });
            await db.SaveChangesAsync();
        }

        return cuentaId;
    }
}
