using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Security.Claims;
using Xunit;

namespace AtlasBalance.API.Tests;

[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class RowLevelSecurityTests
{
    private static readonly string RlsContextSecret = string.Concat("test-rls-context-", "placeholder-value-32-chars");
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
        var countryScopedUserId = Guid.NewGuid();
        var globalScopedUserId = Guid.NewGuid();
        var employeeNoDashboardId = Guid.NewGuid();
        var employeeDashboardId = Guid.NewGuid();
        var paisPermitidoId = Guid.NewGuid();
        var paisBloqueadoId = Guid.NewGuid();
        var paisSinCuentasId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();
        var cuentaPermitidaId = Guid.NewGuid();
        var cuentaBloqueadaId = Guid.NewGuid();
        var cuentaMismaPaisOtroTitularId = Guid.NewGuid();
        var cuentaMismoTitularOtroPaisId = Guid.NewGuid();
        var cuentaEliminadaId = Guid.NewGuid();
        var extractoPermitidoId = Guid.NewGuid();
        var extractoBloqueadoId = Guid.NewGuid();
        var extractoMismaPaisOtroTitularId = Guid.NewGuid();
        var extractoMismoTitularOtroPaisId = Guid.NewGuid();
        var extractoEliminadoId = Guid.NewGuid();
        var extractoCuentaEliminadaId = Guid.NewGuid();
        var revisionPermitidaId = Guid.NewGuid();
        var revisionBloqueadaId = Guid.NewGuid();
        var readerPermissionId = Guid.NewGuid();
        var employeeNoDashboardPermissionId = Guid.NewGuid();
        var employeeDashboardPermissionId = Guid.NewGuid();
        var countryPermissionId = Guid.NewGuid();
        var integrationTokenId = Guid.NewGuid();
        var writeOnlyIntegrationTokenId = Guid.NewGuid();
        var countryIntegrationTokenId = Guid.NewGuid();
        var countryIntegrationPermissionId = Guid.NewGuid();
        var alertPermitidaId = Guid.NewGuid();
        var alertBloqueadaId = Guid.NewGuid();
        var alertGlobalId = Guid.NewGuid();
        var alertTipoId = Guid.NewGuid();
        var destinatarioPermitidoId = Guid.NewGuid();
        var destinatarioBloqueadoId = Guid.NewGuid();
        var destinatarioGlobalId = Guid.NewGuid();
        var destinatarioTipoId = Guid.NewGuid();
        var iaUsageReaderId = Guid.NewGuid();
        var iaUsageWriterId = Guid.NewGuid();
        var backupOperationId = Guid.NewGuid();

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
                },
                new Usuario
                {
                    Id = countryScopedUserId,
                    Email = $"country-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Country RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.GERENTE,
                    Activo = true
                },
                new Usuario
                {
                    Id = globalScopedUserId,
                    Email = $"global-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Global RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.GERENTE,
                    Activo = true
                },
                new Usuario
                {
                    Id = employeeNoDashboardId,
                    Email = $"employee-no-dashboard-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Employee No Dashboard RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.EMPLEADO,
                    Activo = true
                },
                new Usuario
                {
                    Id = employeeDashboardId,
                    Email = $"employee-dashboard-{Guid.NewGuid():N}@atlas.local",
                    NombreCompleto = "Employee Dashboard RLS",
                    PasswordHash = "test",
                    Rol = RolUsuario.EMPLEADO,
                    Activo = true
                });

            db.Paises.AddRange(
                new Pais { Id = paisPermitidoId, Nombre = "Pais permitido", CodigoIso2 = "AA", Activo = true },
                new Pais { Id = paisBloqueadoId, Nombre = "Pais bloqueado", CodigoIso2 = "BB", Activo = true },
                new Pais { Id = paisSinCuentasId, Nombre = "Pais sin cuentas", CodigoIso2 = "CC", Activo = true });

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
                    PaisId = paisPermitidoId,
                    Activa = true
                },
                new Cuenta
                {
                    Id = cuentaBloqueadaId,
                    TitularId = titularBloqueadoId,
                    Nombre = "Cuenta bloqueada",
                    Divisa = "EUR",
                    PaisId = paisBloqueadoId,
                    Activa = true
                },
                new Cuenta
                {
                    Id = cuentaMismaPaisOtroTitularId,
                    TitularId = titularBloqueadoId,
                    Nombre = "Cuenta misma pais otro titular",
                    Divisa = "EUR",
                    PaisId = paisPermitidoId,
                    Activa = true
                },
                new Cuenta
                {
                    Id = cuentaMismoTitularOtroPaisId,
                    TitularId = titularPermitidoId,
                    Nombre = "Cuenta mismo titular otro pais",
                    Divisa = "EUR",
                    PaisId = paisBloqueadoId,
                    Activa = true
                },
                new Cuenta
                {
                    Id = cuentaEliminadaId,
                    TitularId = titularPermitidoId,
                    Nombre = "Cuenta eliminada",
                    Divisa = "EUR",
                    PaisId = paisPermitidoId,
                    Activa = true,
                    DeletedAt = DateTime.UtcNow
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
                },
                new Extracto
                {
                    Id = extractoMismaPaisOtroTitularId,
                    CuentaId = cuentaMismaPaisOtroTitularId,
                    Fecha = new DateOnly(2026, 5, 1),
                    Concepto = "Misma pais otro titular",
                    Monto = 21,
                    Saldo = 21,
                    FilaNumero = 1
                },
                new Extracto
                {
                    Id = extractoMismoTitularOtroPaisId,
                    CuentaId = cuentaMismoTitularOtroPaisId,
                    Fecha = new DateOnly(2026, 5, 1),
                    Concepto = "Mismo titular otro pais",
                    Monto = 22,
                    Saldo = 22,
                    FilaNumero = 1
                },
                new Extracto
                {
                    Id = extractoEliminadoId,
                    CuentaId = cuentaPermitidaId,
                    Fecha = new DateOnly(2026, 5, 1),
                    Concepto = "Extracto eliminado",
                    Monto = 30,
                    Saldo = 30,
                    FilaNumero = 2,
                    DeletedAt = DateTime.UtcNow
                },
                new Extracto
                {
                    Id = extractoCuentaEliminadaId,
                    CuentaId = cuentaEliminadaId,
                    Fecha = new DateOnly(2026, 5, 1),
                    Concepto = "Extracto cuenta eliminada",
                    Monto = 40,
                    Saldo = 40,
                    FilaNumero = 1
                });

            db.PermisosUsuario.AddRange(
                new PermisoUsuario
                {
                    Id = readerPermissionId,
                    UsuarioId = readerId,
                    CuentaId = cuentaPermitidaId,
                    PuedeVerCuentas = true
                },
                new PermisoUsuario
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = readerId,
                    CuentaId = cuentaEliminadaId,
                    PuedeVerCuentas = true
                },
                new PermisoUsuario
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = writerId,
                    CuentaId = cuentaPermitidaId,
                    PuedeImportar = true
                },
                new PermisoUsuario
                {
                    Id = employeeNoDashboardPermissionId,
                    UsuarioId = employeeNoDashboardId,
                    CuentaId = cuentaPermitidaId,
                    PuedeVerCuentas = true,
                    PuedeVerDashboard = false
                },
                new PermisoUsuario
                {
                    Id = employeeDashboardPermissionId,
                    UsuarioId = employeeDashboardId,
                    CuentaId = cuentaPermitidaId,
                    PuedeVerCuentas = true,
                    PuedeVerDashboard = true
                },
                new PermisoUsuario
                {
                    Id = countryPermissionId,
                    UsuarioId = countryScopedUserId,
                    PaisId = paisPermitidoId,
                    TitularId = null,
                    PuedeVerCuentas = true
                },
                new PermisoUsuario
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = globalScopedUserId,
                    PuedeVerCuentas = true
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
            db.IntegrationTokens.Add(new IntegrationToken
            {
                Id = writeOnlyIntegrationTokenId,
                Nombre = "RLS write-only integration",
                TokenHash = Guid.NewGuid().ToString("N"),
                Tipo = "openclaw",
                Estado = EstadoTokenIntegracion.Activo,
                PermisoEscritura = true,
                UsuarioCreadorId = adminId
            });
            db.IntegrationTokens.Add(new IntegrationToken
            {
                Id = countryIntegrationTokenId,
                Nombre = "RLS country integration",
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
            db.IntegrationPermissions.Add(new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = integrationTokenId,
                CuentaId = cuentaEliminadaId,
                AccesoTipo = "lectura"
            });
            db.IntegrationPermissions.Add(new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = writeOnlyIntegrationTokenId,
                CuentaId = cuentaBloqueadaId,
                AccesoTipo = "escritura"
            });
            db.IntegrationPermissions.Add(new IntegrationPermission
            {
                Id = countryIntegrationPermissionId,
                TokenId = countryIntegrationTokenId,
                PaisId = paisPermitidoId,
                TitularId = titularPermitidoId,
                AccesoTipo = "lectura"
            });
            db.RevisionExtractoEstados.AddRange(
                new RevisionExtractoEstado
                {
                    Id = revisionPermitidaId,
                    ExtractoId = extractoPermitidoId,
                    Tipo = "COMISION",
                    Estado = "PENDIENTE",
                    FechaModificacion = DateTime.UtcNow,
                    UsuarioModificacionId = adminId
                },
                new RevisionExtractoEstado
                {
                    Id = revisionBloqueadaId,
                    ExtractoId = extractoBloqueadoId,
                    Tipo = "COMISION",
                    Estado = "PENDIENTE",
                    FechaModificacion = DateTime.UtcNow,
                    UsuarioModificacionId = adminId
                });

            db.AlertasSaldo.AddRange(
                new AlertaSaldo
                {
                    Id = alertPermitidaId,
                    CuentaId = cuentaPermitidaId,
                    SaldoMinimo = 100
                },
                new AlertaSaldo
                {
                    Id = alertBloqueadaId,
                    CuentaId = cuentaBloqueadaId,
                    SaldoMinimo = 100
                },
                new AlertaSaldo
                {
                    Id = alertGlobalId,
                    SaldoMinimo = 100
                },
                new AlertaSaldo
                {
                    Id = alertTipoId,
                    TipoTitular = TipoTitular.EMPRESA,
                    SaldoMinimo = 100
                });

            db.AlertaDestinatarios.AddRange(
                new AlertaDestinatario
                {
                    Id = destinatarioPermitidoId,
                    AlertaId = alertPermitidaId,
                    UsuarioId = readerId
                },
                new AlertaDestinatario
                {
                    Id = destinatarioBloqueadoId,
                    AlertaId = alertBloqueadaId,
                    UsuarioId = readerId
                },
                new AlertaDestinatario
                {
                    Id = destinatarioGlobalId,
                    AlertaId = alertGlobalId,
                    UsuarioId = readerId
                },
                new AlertaDestinatario
                {
                    Id = destinatarioTipoId,
                    AlertaId = alertTipoId,
                    UsuarioId = readerId
                });

            db.IaUsoUsuarios.AddRange(
                new IaUsoUsuario
                {
                    Id = iaUsageReaderId,
                    UsuarioId = readerId,
                    MonthKey = "2026-09",
                    FechaUltimoUsoUtc = DateTime.UtcNow
                },
                new IaUsoUsuario
                {
                    Id = iaUsageWriterId,
                    UsuarioId = writerId,
                    MonthKey = "2026-09",
                    FechaUltimoUsoUtc = DateTime.UtcNow
                });

            db.BackupOperations.Add(new BackupOperation
            {
                Id = backupOperationId,
                Tipo = "MANUAL",
                Estado = "PENDING",
                FechaCreacion = DateTime.UtcNow
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
                      'EXTRACTOS_DESGLOSES',
                      'REVISION_EXTRACTO_ESTADOS',
                      'EXPORTACIONES',
                      'PREFERENCIAS_USUARIO_CUENTA',
                      'AUDITORIAS',
                      'AUDITORIA_INTEGRACIONES',
                      'BACKUPS',
                      'BACKUP_CLOUD_CONNECTIONS',
                      'BACKUP_CLOUD_COPIES',
                      'PAISES',
                      'MFA_TRUSTED_DEVICES',
                      'PERMISOS_USUARIO',
                      'INTEGRATION_PERMISSIONS',
                      'NOTIFICACIONES_ADMIN',
                      'IMPORTACION_LOTES',
                      'IMPORTACION_LOTE_FILAS',
                      'MOVIMIENTOS_ESPERADOS',
                      'CONCILIACIONES',
                      'ALERTAS_SALDO',
                      'ALERTA_DESTINATARIOS',
                      'IA_USO_USUARIOS',
                      'BACKUP_OPERATIONS'
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
                  'REVISION_EXTRACTO_ESTADOS',
                  'EXPORTACIONES',
                  'PREFERENCIAS_USUARIO_CUENTA',
                  'AUDITORIAS',
                  'AUDITORIA_INTEGRACIONES',
                  'BACKUPS',
                  'PAISES',
                  'MFA_TRUSTED_DEVICES',
                  'PERMISOS_USUARIO',
                  'INTEGRATION_PERMISSIONS',
                  'NOTIFICACIONES_ADMIN',
                  'ALERTAS_SALDO',
                  'ALERTA_DESTINATARIOS',
                  'IA_USO_USUARIOS',
                  'BACKUP_OPERATIONS'
              )
            """);
        runtimeOwnedTables.Should().Be(0);

        await SetRlsContextAsync(connection, "anonymous", null, null, isAdmin: false, isSystem: false, "anonymous");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId, extractoEliminadoId, extractoCuentaEliminadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "ALERTAS_SALDO", alertPermitidaId, alertBloqueadaId, alertGlobalId, alertTipoId)).Should().Be(0);
        (await CountByIdsAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, destinatarioBloqueadoId, destinatarioGlobalId, destinatarioTipoId)).Should().Be(0);
        (await CountByIdsAsync(connection, "IA_USO_USUARIOS", iaUsageReaderId, iaUsageWriterId)).Should().Be(0);
        (await CountByIdsAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(0);

        await SetUnsignedRlsContextAsync(connection, "user", readerId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "ALERTAS_SALDO", alertPermitidaId, alertBloqueadaId, alertGlobalId, alertTipoId)).Should().Be(0);
        (await CountByIdsAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, destinatarioBloqueadoId, destinatarioGlobalId, destinatarioTipoId)).Should().Be(0);
        (await CountByIdsAsync(connection, "IA_USO_USUARIOS", iaUsageReaderId, iaUsageWriterId)).Should().Be(0);
        (await CountByIdsAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(0);
        await SetUnsignedRlsContextAsync(connection, "system", null, null, isAdmin: true, isSystem: true, "system");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(0);

        await SetRlsContextAsync(connection, "user", readerId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "TITULARES", titularPermitidoId, titularBloqueadoId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId, extractoEliminadoId, extractoCuentaEliminadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "REVISION_EXTRACTO_ESTADOS", revisionPermitidaId, revisionBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "PERMISOS_USUARIO", readerPermissionId, countryPermissionId)).Should().Be(1);
        (await CountByIdsAsync(connection, "ALERTAS_SALDO", alertPermitidaId, alertBloqueadaId, alertGlobalId, alertTipoId)).Should().Be(3);
        (await CountByIdsAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, destinatarioBloqueadoId, destinatarioGlobalId, destinatarioTipoId)).Should().Be(3);
        (await CountByIdsAsync(connection, "IA_USO_USUARIOS", iaUsageReaderId, iaUsageWriterId)).Should().Be(1);
        (await CountByIdsAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(0);

        var deniedAlertInsert = () => InsertAlertAsync(connection, Guid.NewGuid(), cuentaBloqueadaId, null);
        await deniedAlertInsert.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        (await UpdateByIdAsync(connection, "ALERTAS_SALDO", alertPermitidaId, "saldo_minimo = saldo_minimo + 1")).Should().Be(0);
        (await DeleteByIdAsync(connection, "ALERTAS_SALDO", alertPermitidaId)).Should().Be(0);
        var deniedRecipientInsert = () => InsertRecipientAsync(connection, Guid.NewGuid(), alertPermitidaId, readerId);
        await deniedRecipientInsert.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        (await UpdateByIdAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, "usuario_id = usuario_id")).Should().Be(0);
        (await DeleteByIdAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId)).Should().Be(0);
        var deniedBackupOperationInsert = () => InsertBackupOperationAsync(connection, Guid.NewGuid());
        await deniedBackupOperationInsert.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        (await UpdateByIdAsync(connection, "BACKUP_OPERATIONS", backupOperationId, "estado = 'RUNNING'")).Should().Be(0);
        (await DeleteByIdAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(0);

        var ownUsageId = Guid.NewGuid();
        await InsertIaUsageAsync(connection, ownUsageId, readerId, "2099-01");
        (await UpdateByIdAsync(connection, "IA_USO_USUARIOS", ownUsageId, "requests = requests + 1")).Should().Be(1);
        var deniedUsageOwnerUpdate = () => UpdateIaUsageOwnerAsync(connection, ownUsageId, writerId);
        await deniedUsageOwnerUpdate.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        (await DeleteByIdAsync(connection, "IA_USO_USUARIOS", ownUsageId)).Should().Be(0);

        await SetRlsContextAsync(connection, "user", readerId, null, isAdmin: false, isSystem: false, "dashboard");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(1);

        await SetRlsContextAsync(connection, "user", employeeNoDashboardId, null, isAdmin: false, isSystem: false, "dashboard");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(0);

        await SetRlsContextAsync(connection, "user", employeeNoDashboardId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(1);

        await SetRlsContextAsync(connection, "user", employeeDashboardId, null, isAdmin: false, isSystem: false, "dashboard");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(1);

        var deniedInsert = async () => await InsertExtractoAsync(connection, cuentaPermitidaId);
        await deniedInsert.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        await SetRlsContextAsync(connection, "user", readerId, null, isAdmin: false, isSystem: false, "export");
        await InsertExportacionAsync(connection, cuentaPermitidaId);

        await SetRlsContextAsync(connection, "user", writerId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId)).Should().Be(0);

        await SetRlsContextAsync(connection, "user", writerId, null, isAdmin: false, isSystem: false, "write");
        await InsertExtractoAsync(connection, cuentaPermitidaId);

        await SetRlsContextAsync(connection, "user", writerId, null, isAdmin: false, isSystem: false, "export");
        var deniedWriterExport = async () => await InsertExportacionAsync(connection, cuentaPermitidaId);
        await deniedWriterExport.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        await SetRlsContextAsync(connection, "user", globalScopedUserId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaMismaPaisOtroTitularId, cuentaMismoTitularOtroPaisId, cuentaEliminadaId)).Should().Be(4);
        (await CountByIdsAsync(connection, "PAISES", paisPermitidoId, paisBloqueadoId, paisSinCuentasId)).Should().Be(2);

        var postgresGlobalAccountIds = await SelectAllIdsAsync(connection, "CUENTAS");
        await using (var appDb = new AppDbContext(options))
        {
            await appDb.Database.OpenConnectionAsync();
            await SetRlsContextAsync(
                (NpgsqlConnection)appDb.Database.GetDbConnection(),
                "user",
                globalScopedUserId,
                null,
                isAdmin: false,
                isSystem: false,
                "data");
            var accessService = new UserAccessService(
                appDb,
                new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
                Options.Create(new CachingOptions()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, globalScopedUserId.ToString()),
                new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
            ], "TestAuth"));
            var scope = await accessService.GetScopeAsync(principal, CancellationToken.None);
            scope.HasGlobalAccess.Should().BeTrue();
            var applicationAccountIds = await accessService
                .ApplyCuentaScope(appDb.Cuentas.AsNoTracking(), scope)
                .Select(c => c.Id)
                .ToListAsync();

            applicationAccountIds.Should().BeEquivalentTo(postgresGlobalAccountIds);
        }

        await SetRlsContextAsync(connection, "user", countryScopedUserId, null, isAdmin: false, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaMismaPaisOtroTitularId, cuentaMismoTitularOtroPaisId, cuentaBloqueadaId)).Should().Be(2);
        (await CountByIdsAsync(connection, "TITULARES", titularPermitidoId, titularBloqueadoId)).Should().Be(2);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoMismaPaisOtroTitularId, extractoMismoTitularOtroPaisId, extractoBloqueadoId)).Should().Be(2);
        (await CountByIdsAsync(connection, "PAISES", paisPermitidoId, paisBloqueadoId, paisSinCuentasId)).Should().Be(1);
        (await CountByIdsAsync(connection, "PERMISOS_USUARIO", readerPermissionId, countryPermissionId)).Should().Be(1);

        var postgresCountryAccountIds = await SelectAllIdsAsync(connection, "CUENTAS");
        await using (var appDb = new AppDbContext(options))
        {
            await appDb.Database.OpenConnectionAsync();
            await SetRlsContextAsync(
                (NpgsqlConnection)appDb.Database.GetDbConnection(),
                "user",
                countryScopedUserId,
                null,
                isAdmin: false,
                isSystem: false,
                "data");
            var accessService = new UserAccessService(
                appDb,
                new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
                Options.Create(new CachingOptions()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, countryScopedUserId.ToString()),
                new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
            ], "TestAuth"));
            var scope = await accessService.GetScopeAsync(principal, CancellationToken.None);
            var applicationAccountIds = await accessService
                .ApplyCuentaScope(appDb.Cuentas.AsNoTracking(), scope)
                .Select(c => c.Id)
                .ToListAsync();

            applicationAccountIds.Should().BeEquivalentTo(postgresCountryAccountIds);
        }

        await SetRlsContextAsync(connection, "system", null, null, isAdmin: true, isSystem: true, "system");
        await InsertExportacionAsync(connection, cuentaPermitidaId);

        // V-02.07: LimpiezaExportacionesJob purga por retencion marcando
        // deleted_at en contexto system. 20260724090000 dejo el WITH CHECK de
        // exportaciones_write sin la salida is_admin_or_system() que si tiene el
        // USING, asi que ese UPDATE moria con "new row violates row-level
        // security policy" y la purga de ficheros con PII no corria.
        // 20260731090000 lo corrige. Este assert cubre el hueco: los tests del
        // job usan UseInMemoryDatabase, que no evalua RLS y da verde igual.
        (await SoftDeleteExportacionesAsync(connection)).Should().BeGreaterThan(0);

        await SetRlsContextAsync(connection, "integration", null, integrationTokenId, isAdmin: false, isSystem: false, "integration");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId, extractoEliminadoId, extractoCuentaEliminadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "INTEGRATION_PERMISSIONS", countryIntegrationPermissionId)).Should().Be(0);
        (await CountByIdsAsync(connection, "ALERTAS_SALDO", alertPermitidaId, alertBloqueadaId, alertGlobalId, alertTipoId)).Should().Be(3);
        (await CountByIdsAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, destinatarioBloqueadoId, destinatarioGlobalId, destinatarioTipoId)).Should().Be(0);
        (await CountByIdsAsync(connection, "IA_USO_USUARIOS", iaUsageReaderId, iaUsageWriterId)).Should().Be(0);
        (await CountByIdsAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(0);

        await SetRlsContextAsync(connection, "integration", null, writeOnlyIntegrationTokenId, isAdmin: false, isSystem: false, "integration");
        (await CountByIdsAsync(connection, "TITULARES", titularPermitidoId, titularBloqueadoId)).Should().Be(0);
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(0);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId, extractoEliminadoId, extractoCuentaEliminadaId)).Should().Be(0);

        await SetRlsContextAsync(connection, "integration", null, countryIntegrationTokenId, isAdmin: false, isSystem: false, "integration");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaMismaPaisOtroTitularId, cuentaMismoTitularOtroPaisId, cuentaBloqueadaId)).Should().Be(1);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoMismaPaisOtroTitularId, extractoMismoTitularOtroPaisId, extractoBloqueadoId)).Should().Be(1);
        (await CountByIdsAsync(connection, "INTEGRATION_PERMISSIONS", countryIntegrationPermissionId)).Should().Be(1);

        await SetRlsContextAsync(connection, "user", adminId, null, isAdmin: true, isSystem: false, "data");
        (await CountByIdsAsync(connection, "CUENTAS", cuentaPermitidaId, cuentaBloqueadaId, cuentaEliminadaId)).Should().Be(3);
        (await CountByIdsAsync(connection, "EXTRACTOS", extractoPermitidoId, extractoBloqueadoId, extractoEliminadoId, extractoCuentaEliminadaId)).Should().Be(4);
        (await CountByIdsAsync(connection, "PAISES", paisPermitidoId, paisBloqueadoId, paisSinCuentasId)).Should().Be(3);
        (await CountByIdsAsync(connection, "ALERTAS_SALDO", alertPermitidaId, alertBloqueadaId, alertGlobalId, alertTipoId)).Should().Be(4);
        (await CountByIdsAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, destinatarioBloqueadoId, destinatarioGlobalId, destinatarioTipoId)).Should().Be(4);
        (await CountByIdsAsync(connection, "IA_USO_USUARIOS", iaUsageReaderId, iaUsageWriterId)).Should().Be(2);
        (await CountByIdsAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(1);

        var adminAlertId = Guid.NewGuid();
        var adminRecipientId = Guid.NewGuid();
        await InsertAlertAsync(connection, adminAlertId, cuentaMismaPaisOtroTitularId, null);
        await InsertRecipientAsync(connection, adminRecipientId, adminAlertId, readerId);
        (await DeleteByIdAsync(connection, "ALERTA_DESTINATARIOS", adminRecipientId)).Should().Be(1);
        (await DeleteByIdAsync(connection, "ALERTAS_SALDO", adminAlertId)).Should().Be(1);

        var adminUsageId = Guid.NewGuid();
        await InsertIaUsageAsync(connection, adminUsageId, adminId, "2099-02");
        (await UpdateByIdAsync(connection, "IA_USO_USUARIOS", adminUsageId, "requests = requests + 1")).Should().Be(1);
        (await DeleteByIdAsync(connection, "IA_USO_USUARIOS", adminUsageId)).Should().Be(1);

        var adminOperationId = Guid.NewGuid();
        await InsertBackupOperationAsync(connection, adminOperationId);
        (await UpdateByIdAsync(connection, "BACKUP_OPERATIONS", adminOperationId, "estado = 'RUNNING'")).Should().Be(1);
        (await DeleteByIdAsync(connection, "BACKUP_OPERATIONS", adminOperationId)).Should().Be(1);

        await SetRlsContextAsync(connection, "system", null, null, isAdmin: true, isSystem: true, "system");
        (await CountByIdsAsync(connection, "ALERTAS_SALDO", alertPermitidaId, alertBloqueadaId, alertGlobalId, alertTipoId)).Should().Be(4);
        (await CountByIdsAsync(connection, "ALERTA_DESTINATARIOS", destinatarioPermitidoId, destinatarioBloqueadoId, destinatarioGlobalId, destinatarioTipoId)).Should().Be(4);
        (await CountByIdsAsync(connection, "IA_USO_USUARIOS", iaUsageReaderId, iaUsageWriterId)).Should().Be(2);
        (await CountByIdsAsync(connection, "BACKUP_OPERATIONS", backupOperationId)).Should().Be(1);
        (await UpdateByIdAsync(connection, "BACKUP_OPERATIONS", backupOperationId, "estado = 'RUNNING'")).Should().Be(1);
        (await UpdateByIdAsync(connection, "BACKUP_OPERATIONS", backupOperationId, "estado = 'PENDING'")).Should().Be(1);
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
        var escapedDatabase = builder.Database?.Replace("\"", "\"\"", StringComparison.Ordinal) ?? "atlas_balance_tests";

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

    private static async Task<HashSet<Guid>> SelectAllIdsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM \"{table}\" ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new HashSet<Guid>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
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

    private static async Task<int> SoftDeleteExportacionesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE "EXPORTACIONES"
            SET deleted_at = now()
            WHERE deleted_at IS NULL
            """;
        return await command.ExecuteNonQueryAsync();
    }

    private static Task<int> InsertAlertAsync(NpgsqlConnection connection, Guid id, Guid? cuentaId, int? tipoTitular) =>
        ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO "ALERTAS_SALDO" (id, cuenta_id, tipo_titular, saldo_minimo, activa, fecha_creacion)
            VALUES (@id, @cuenta_id, @tipo_titular, 1, true, now())
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("cuenta_id", (object?)cuentaId ?? DBNull.Value);
                command.Parameters.AddWithValue("tipo_titular", (object?)tipoTitular ?? DBNull.Value);
            });

    private static Task<int> InsertRecipientAsync(NpgsqlConnection connection, Guid id, Guid alertId, Guid userId) =>
        ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO "ALERTA_DESTINATARIOS" (id, alerta_id, usuario_id)
            VALUES (@id, @alerta_id, @usuario_id)
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("alerta_id", alertId);
                command.Parameters.AddWithValue("usuario_id", userId);
            });

    private static Task<int> InsertIaUsageAsync(NpgsqlConnection connection, Guid id, Guid userId, string monthKey) =>
        ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO "IA_USO_USUARIOS"
                (id, usuario_id, month_key, requests, input_tokens, output_tokens,
                 coste_estimado_eur, fecha_ultimo_uso_utc, fecha_modificacion)
            VALUES (@id, @usuario_id, @month_key, 0, 0, 0, 0, now(), now())
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("usuario_id", userId);
                command.Parameters.AddWithValue("month_key", monthKey);
            });

    private static Task<int> UpdateIaUsageOwnerAsync(NpgsqlConnection connection, Guid id, Guid userId) =>
        ExecuteNonQueryAsync(
            connection,
            "UPDATE \"IA_USO_USUARIOS\" SET usuario_id = @usuario_id WHERE id = @id",
            command =>
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("usuario_id", userId);
            });

    private static Task<int> InsertBackupOperationAsync(NpgsqlConnection connection, Guid id) =>
        ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO "BACKUP_OPERATIONS" (id, tipo, estado, fecha_creacion)
            VALUES (@id, 'MANUAL', 'PENDING', now())
            """,
            command => command.Parameters.AddWithValue("id", id));

    private static Task<int> UpdateByIdAsync(NpgsqlConnection connection, string table, Guid id, string assignment) =>
        ExecuteNonQueryAsync(
            connection,
            $"UPDATE \"{table}\" SET {assignment} WHERE id = @id",
            command => command.Parameters.AddWithValue("id", id));

    private static Task<int> DeleteByIdAsync(NpgsqlConnection connection, string table, Guid id) =>
        ExecuteNonQueryAsync(
            connection,
            $"DELETE FROM \"{table}\" WHERE id = @id",
            command => command.Parameters.AddWithValue("id", id));

    private static async Task<int> ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        string sql,
        Action<NpgsqlCommand> configure)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        return await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
