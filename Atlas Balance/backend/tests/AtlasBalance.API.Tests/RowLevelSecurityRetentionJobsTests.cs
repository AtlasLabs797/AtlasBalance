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

// V-03.02 (retencion de datos personales): LimpiezaMfaTrustedDevicesJob y
// LimpiezaImportacionLotesJob contra PostgreSQL real con RLS FORCE y el rol de
// runtime sin HttpContext. Con EF InMemory ExecuteDelete/ExecuteUpdate ni
// siquiera se soportan, y un 0-filas por RLS pasaria desapercibido: aqui se
// comprueba que las filas desaparecen / se vacian de verdad.
public sealed partial class RowLevelSecurityTests
{
    private sealed class RetentionClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    [Fact]
    public async Task LimpiezaMfaTrustedDevicesJob_ShouldDeleteExpiredAndOldRevoked_KeepingActiveOnes_UnderForcedRls()
    {
        var (migrationConnection, runtimeConnection) = await PrepareRetentionDatabaseAsync();
        var now = DateTime.UtcNow;
        var usuarioId = await SeedUsuarioAsync(migrationConnection);

        var expired = NewDevice(usuarioId, now.AddDays(-40), expiresAt: now.AddMinutes(-5));
        var revokedLongAgo = NewDevice(usuarioId, now.AddDays(-10), expiresAt: now.AddDays(20), revokedAt: now.AddDays(-2));
        var revokedRecently = NewDevice(usuarioId, now.AddDays(-10), expiresAt: now.AddDays(20), revokedAt: now.AddHours(-1));
        var active = NewDevice(usuarioId, now.AddDays(-1), expiresAt: now.AddDays(29));
        await SeedAsSystemAsync(migrationConnection, db => db.MfaTrustedDevices.AddRange(expired, revokedLongAgo, revokedRecently, active));

        await using (var jobDb = CreateRuntimeDbContext(runtimeConnection))
        {
            var job = new LimpiezaMfaTrustedDevicesJob(jobDb, new RetentionClock(now), NullLogger<LimpiezaMfaTrustedDevicesJob>.Instance);
            await job.ExecuteAsync();
        }

        await using var verify = await OpenSystemConnectionAsync(migrationConnection);
        (await CountByIdsAsync(verify, "MFA_TRUSTED_DEVICES", expired.Id)).Should().Be(0, "caducado");
        (await CountByIdsAsync(verify, "MFA_TRUSTED_DEVICES", revokedLongAgo.Id)).Should().Be(0, "revocado hace mas de 1 dia");
        (await CountByIdsAsync(verify, "MFA_TRUSTED_DEVICES", revokedRecently.Id)).Should().Be(1, "revocado hace menos de 1 dia");
        (await CountByIdsAsync(verify, "MFA_TRUSTED_DEVICES", active.Id)).Should().Be(1, "vigente");
    }

    [Fact]
    public async Task LimpiezaImportacionLotesJob_ShouldEmptyDatosJsonOfOldClosedLotes_KeepingRowsAndRecentOrOpenLotes_UnderForcedRls()
    {
        var (migrationConnection, runtimeConnection) = await PrepareRetentionDatabaseAsync();
        var now = DateTime.UtcNow;
        var cuentaId = await SeedCuentaAsync(migrationConnection);
        var usuarioId = await SeedUsuarioAsync(migrationConnection);

        var oldClosed = NewLote(cuentaId, usuarioId, now.AddDays(-200), "confirmado");
        var oldReverted = NewLote(cuentaId, usuarioId, now.AddDays(-200), "revertido");
        var oldOpen = NewLote(cuentaId, usuarioId, now.AddDays(-200), "validado");
        var recentClosed = NewLote(cuentaId, usuarioId, now.AddDays(-10), "confirmado");
        var filaOldClosed = NewFila(oldClosed.Id, 0);
        var filaOldClosed2 = NewFila(oldClosed.Id, 1);
        var filaOldReverted = NewFila(oldReverted.Id, 0);
        var filaOldOpen = NewFila(oldOpen.Id, 0);
        var filaRecent = NewFila(recentClosed.Id, 0);
        await SeedAsSystemAsync(migrationConnection, db =>
        {
            db.ImportacionLotes.AddRange(oldClosed, oldReverted, oldOpen, recentClosed);
            db.ImportacionLoteFilas.AddRange(filaOldClosed, filaOldClosed2, filaOldReverted, filaOldOpen, filaRecent);
        });

        await using (var jobDb = CreateRuntimeDbContext(runtimeConnection))
        {
            var job = new LimpiezaImportacionLotesJob(jobDb, new RetentionClock(now), NullLogger<LimpiezaImportacionLotesJob>.Instance);
            await job.ExecuteAsync();
        }

        await using var verify = await OpenSystemConnectionAsync(migrationConnection);
        (await CountByIdsAsync(verify, "IMPORTACION_LOTE_FILAS", filaOldClosed.Id, filaOldClosed2.Id, filaOldReverted.Id, filaOldOpen.Id, filaRecent.Id))
            .Should().Be(5, "no se borran filas, solo se vacia datos_json");
        (await DatosJsonAsync(verify, filaOldClosed.Id)).Should().Be("{}");
        (await DatosJsonAsync(verify, filaOldClosed2.Id)).Should().Be("{}");
        (await DatosJsonAsync(verify, filaOldReverted.Id)).Should().Be("{}");
        (await DatosJsonAsync(verify, filaOldOpen.Id)).Should().Contain("concepto", "los lotes abiertos se confirman desde datos_json");
        (await DatosJsonAsync(verify, filaRecent.Id)).Should().Contain("concepto", "lote reciente dentro de la retencion");

        var estadoFila = await ExecuteScalarAsync<string>(verify, $"SELECT estado FROM \"IMPORTACION_LOTE_FILAS\" WHERE id = '{filaOldClosed.Id}'");
        estadoFila.Should().Be("confirmada", "el historial funcional de la fila se conserva");
    }

    [Fact]
    public async Task LimpiezaImportacionLotesJob_ShouldHonorConfiguredRetentionDays()
    {
        var (migrationConnection, runtimeConnection) = await PrepareRetentionDatabaseAsync();
        var now = DateTime.UtcNow;
        var cuentaId = await SeedCuentaAsync(migrationConnection);
        var usuarioId = await SeedUsuarioAsync(migrationConnection);

        var lote60 = NewLote(cuentaId, usuarioId, now.AddDays(-60), "confirmado");
        var lote10 = NewLote(cuentaId, usuarioId, now.AddDays(-10), "confirmado");
        var fila60 = NewFila(lote60.Id, 0);
        var fila10 = NewFila(lote10.Id, 0);
        await SeedAsSystemAsync(migrationConnection, db =>
        {
            db.Configuraciones.Add(new Configuracion
            {
                Clave = LimpiezaExportacionesJob.ImportacionContenidoRetentionDaysKey,
                Valor = "30"
            });
            db.ImportacionLotes.AddRange(lote60, lote10);
            db.ImportacionLoteFilas.AddRange(fila60, fila10);
        });

        await using (var jobDb = CreateRuntimeDbContext(runtimeConnection))
        {
            var job = new LimpiezaImportacionLotesJob(jobDb, new RetentionClock(now), NullLogger<LimpiezaImportacionLotesJob>.Instance);
            await job.ExecuteAsync();
        }

        await using var verify = await OpenSystemConnectionAsync(migrationConnection);
        (await DatosJsonAsync(verify, fila60.Id)).Should().Be("{}");
        (await DatosJsonAsync(verify, fila10.Id)).Should().Contain("concepto");
    }

    private async Task<(string Migration, string Runtime)> PrepareRetentionDatabaseAsync()
    {
        var (migrationConnection, runtimeConnection) = await CreateRoleConnectionStringsAsync();
        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnection).UseSnakeCaseNamingConvention().Options;
        await using (var db = new AppDbContext(migrationOptions))
        {
            await db.Database.MigrateAsync();
        }
        await ConfigureRlsRuntimeAsync(migrationConnection, runtimeConnection);
        return (migrationConnection, runtimeConnection);
    }

    // Igual que el AppDbContext de un worker de Hangfire: interceptor RLS real y
    // sin HttpContext. El test NO abre SystemContextScope; lo debe abrir el job.
    private static AppDbContext CreateRuntimeDbContext(string runtimeConnection)
    {
        var interceptor = new RlsDbCommandInterceptor(new HttpContextAccessor { HttpContext = null }, new RlsContextSecret(RlsContextSecret));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(runtimeConnection).UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedAsSystemAsync(string migrationConnectionString, Action<AppDbContext> add)
    {
        await using var connection = await OpenSystemConnectionAsync(migrationConnectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(options);
        add(db);
        await db.SaveChangesAsync();
    }

    private static async Task<NpgsqlConnection> OpenSystemConnectionAsync(string migrationConnectionString)
    {
        var connection = new NpgsqlConnection(migrationConnectionString);
        await connection.OpenAsync();
        await SetRlsContextAsync(connection, "system", null, null, true, true, "system");
        return connection;
    }

    private static async Task<Guid> SeedUsuarioAsync(string migrationConnectionString)
    {
        var usuarioId = Guid.NewGuid();
        await SeedAsSystemAsync(migrationConnectionString, db => db.Usuarios.Add(new Usuario
        {
            Id = usuarioId,
            Email = $"retencion-{Guid.NewGuid():N}@atlas.local",
            NombreCompleto = "Retencion RLS",
            PasswordHash = "test",
            Rol = RolUsuario.ADMIN,
            Activo = true
        }));
        return usuarioId;
    }

    private static MfaTrustedDevice NewDevice(Guid usuarioId, DateTime createdAt, DateTime expiresAt, DateTime? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = usuarioId,
        TokenHash = Guid.NewGuid().ToString("N"),
        SecurityStamp = "stamp",
        CreatedAt = createdAt,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
        UserAgentSummary = "Test UA",
        IpAddressSummary = "10.0.0.x"
    };

    private static ImportacionLote NewLote(Guid cuentaId, Guid usuarioId, DateTime fechaCreacion, string estado) => new()
    {
        Id = Guid.NewGuid(),
        CuentaId = cuentaId,
        UsuarioCreadorId = usuarioId,
        Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        LoteHash = Guid.NewGuid().ToString("N"),
        Estado = estado,
        FechaCreacion = fechaCreacion
    };

    private static ImportacionLoteFila NewFila(Guid loteId, int indice) => new()
    {
        Id = Guid.NewGuid(),
        LoteId = loteId,
        Indice = indice,
        Valida = true,
        Estado = "confirmada",
        DatosJson = """{"concepto":"Cobro cliente","monto":100}"""
    };

    private static async Task<string> DatosJsonAsync(NpgsqlConnection connection, Guid filaId)
        => await ExecuteScalarAsync<string>(connection, $"SELECT datos_json::text FROM \"IMPORTACION_LOTE_FILAS\" WHERE id = '{filaId}'");
}
