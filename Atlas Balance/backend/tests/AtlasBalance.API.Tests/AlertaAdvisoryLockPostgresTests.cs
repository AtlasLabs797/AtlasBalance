using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-03.02 (B-8): pg_advisory_xact_lock es un lock DE TRANSACCION. Sin una
// transaccion explicita el SELECT corre en autocommit, el lock se libera al
// terminar la sentencia y dos importaciones simultaneas pasan las dos el
// lock: se envian los dos el email de alerta de saldo bajo.
//
// El defecto solo se ve contra PostgreSQL de verdad (en InMemory no hay
// locks ni transacciones), asi que el test va contra el contenedor real.
// -----------------------------------------------------------------------
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class AlertaAdvisoryLockPostgresTests
{
    private readonly PostgresFixture _fixture;

    public AlertaAdvisoryLockPostgresTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EvaluateSaldoPostAsync_Concurrente_No_Debe_Enviar_Dos_Alertas()
    {
        var connectionString = _fixture.ConnectionString + ";Include Error Detail=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var usuarioId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var alertaId = Guid.NewGuid();
        // Misma clave que usa AlertaService: cuenta en formato N (sin guiones).
        var cooldownKey = $"alerta_saldo_last_sent_utc:{cuentaId:N}:CUENTA";

        await using (var db = new AppDbContext(options))
        {
            db.Usuarios.Add(new Usuario
            {
                Id = usuarioId,
                Email = $"destinatario-{usuarioId:N}@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Destinatario Alerta",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PrimerLogin = false,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });

            db.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Alerta",
                Tipo = TipoTitular.EMPRESA
            });

            db.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Alerta",
                Divisa = "EUR",
                Activa = true
            });

            db.AlertasSaldo.Add(new AlertaSaldo
            {
                Id = alertaId,
                CuentaId = cuentaId,
                SaldoMinimo = 250m,
                Activa = true,
                FechaCreacion = DateTime.UtcNow.AddDays(-1)
            });

            db.AlertaDestinatarios.Add(new AlertaDestinatario
            {
                Id = Guid.NewGuid(),
                AlertaId = alertaId,
                UsuarioId = usuarioId
            });

            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow),
                Concepto = "Pago urgente",
                Monto = -20m,
                Saldo = 200m,
                FilaNumero = 1
            });

            db.Configuraciones.Add(new Configuracion
            {
                Clave = "alerta_saldo_cooldown_horas",
                Valor = "24",
                Tipo = "number",
                Descripcion = "Cooldown de saldo bajo"
            });

            // Cooldown ya caducado: las dos ejecuciones tienen que leer un
            // cooldown vencido para que la que pierda la carrera no se
            // descarte sola y el resultado dependa solo del lock.
            db.Configuraciones.Add(new Configuracion
            {
                Clave = cooldownKey,
                Valor = DateTime.UtcNow.AddHours(-48).ToString("O", CultureInfo.InvariantCulture),
                Tipo = "datetime",
                Descripcion = "Ultimo envio de alerta de saldo bajo por cuenta"
            });

            await db.SaveChangesAsync();
        }

        var interceptor = new CooldownRaceInterceptor();
        var optionsConInterceptor = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;

        var emailService = new CountingEmailService();

        await using var db1 = new AppDbContext(optionsConInterceptor);
        await using var db2 = new AppDbContext(optionsConInterceptor);

        var sut1 = new AlertaService(db1, emailService, new NoOpAuditService(), NullLogger<AlertaService>.Instance);
        var sut2 = new AlertaService(db2, emailService, new NoOpAuditService(), NullLogger<AlertaService>.Instance);

        var tarea1 = Task.Run(() => sut1.EvaluateSaldoPostAsync(cuentaId, usuarioId, CancellationToken.None));
        var tarea2 = Task.Run(() => sut2.EvaluateSaldoPostAsync(cuentaId, usuarioId, CancellationToken.None));

        await Task.WhenAll(tarea1, tarea2);

        emailService.SendCount.Should().Be(
            1,
            "el lock de transaccion tiene que impedir que dos evaluaciones simultaneas envien las dos el email de saldo bajo");
    }

    [Fact]
    public async Task Transaccion_Comprometida_Sin_Liberar_No_Bloquea_Nueva_Transaccion()
    {
        // ImportacionService compromete la transaccion del lote y evalua la alerta
        // DESPUES, liberando la transaccion en el finally. Cuando la alerta llega a
        // su zona critica, la transaccion anterior esta comprometida pero no
        // liberada todavia. Si el DbContext la siguiera dando por abierta, la zona
        // critica no podria abrir su propia transaccion y la alerta se perderia en
        // silencio (EvaluateSaldoAlertSafelyAsync se come cualquier excepcion).
        var connectionString = _fixture.ConnectionString + ";Include Error Detail=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var tx1 = await db.Database.BeginTransactionAsync();
        await tx1.CommitAsync();
        // tx1 se deja sin Dispose a proposito: es el estado real en el servicio de
        // importacion cuando se evalua la alerta de saldo.

        var action = async () =>
        {
            await using var tx2 = await db.Database.BeginTransactionAsync();
            await tx2.CommitAsync();
        };

        await action.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------
    // Fuerza la carrera sin depender del reloj: ninguna de las dos
    // ejecuciones puede escribir el cooldown hasta que las dos han leido el
    // cooldown caducado. Asi las dos pasan el filtro de cooldown y solo el
    // lock decide cual envia.
    //
    // Si el lock es correcto (transaccion explicita), la segunda ejecucion se
    // queda bloqueada en pg_advisory_xact_lock y no llega a leer nunca. En ese
    // caso la escritura corre dentro de una transaccion, que es justo lo que
    // demuestra que el lock sigue retenido, y se le deja escribir.
    // -------------------------------------------------------------------
    private sealed class CooldownRaceInterceptor : DbCommandInterceptor
    {
        private const string CooldownPrefix = "alerta_saldo_last_sent_utc:";
        private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(20);

        private readonly ConcurrentDictionary<DbConnection, byte> _cooldownLeidaPorConexion = new();
        private int _cooldownsLeidos;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (EsEscrituraCooldown(command))
            {
                await EsperarARaceAsync(command, cancellationToken);
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (EsEscrituraCooldown(command))
            {
                await EsperarARaceAsync(command, cancellationToken);
            }

            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (EsLecturaCooldown(command)
                && command.Connection is not null
                && _cooldownLeidaPorConexion.TryAdd(command.Connection, 0))
            {
                Interlocked.Increment(ref _cooldownsLeidos);
            }

            return ValueTask.FromResult(result);
        }

        private async Task EsperarARaceAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var limite = DateTime.UtcNow + EsperaMaxima;
            while (DateTime.UtcNow < limite)
            {
                if (Volatile.Read(ref _cooldownsLeidos) >= 2)
                {
                    return;
                }

                // Si la escritura corre dentro de una transaccion, el lock de
                // transaccion sigue retenido y la otra ejecucion esta
                // bloqueada: no va a leer nunca, asi que no se espera.
                if (command.Transaction is not null)
                {
                    return;
                }

                await Task.Delay(20, cancellationToken);
            }
        }

        private static bool EsLecturaCooldown(DbCommand command)
            => command.CommandText.Contains("CONFIGURACION", StringComparison.OrdinalIgnoreCase)
                && TieneParametroCooldown(command);

        private static bool EsEscrituraCooldown(DbCommand command)
            => command.CommandText.Contains("CONFIGURACION", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && TieneParametroCooldown(command);

        private static bool TieneParametroCooldown(DbCommand command)
        {
            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is string valor
                    && valor.StartsWith(CooldownPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class CountingEmailService : IEmailService
    {
        private int _sendCount;

        public int SendCount => Volatile.Read(ref _sendCount);

        public Task SendSaldoBajoAlertAsync(
            IReadOnlyList<string> recipients,
            string titularNombre,
            string cuentaNombre,
            Guid cuentaId,
            string divisa,
            decimal saldoActual,
            decimal saldoMinimo,
            string? conceptoUltimoMovimiento,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            return Task.CompletedTask;
        }

        public Task SendPlazoFijoVencimientoAsync(
            IReadOnlyList<string> recipients,
            string titularNombre,
            string cuentaNombre,
            Guid cuentaId,
            DateOnly fechaVencimiento,
            EstadoPlazoFijo estado,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendSecurityAlertAsync(
            IReadOnlyList<string> recipients,
            string regla,
            string severidad,
            string resumen,
            IReadOnlyList<string> detalles,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendAccountLockedAsync(
            IReadOnlyList<string> recipients,
            DateTime lockedAtUtc,
            int lockMinutes,
            string? ipSummary,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(
            Guid? usuarioId,
            string tipoAccion,
            string? entidadTipo,
            Guid? entidadId,
            HttpContext httpContext,
            string? detallesJson,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task LogAsync(
            Guid? usuarioId,
            string tipoAccion,
            string? entidadTipo,
            Guid? entidadId,
            string? ipAddress,
            string? detallesJson,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
