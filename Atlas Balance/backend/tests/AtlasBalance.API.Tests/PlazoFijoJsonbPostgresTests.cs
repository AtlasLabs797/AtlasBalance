using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// A-9: la deduplicacion de las notificaciones de plazo fijo consulta
// NOTIFICACIONES_ADMIN.detalles_json (jsonb) con Contains de string. EF lo
// traduce a "detalles_json ~~ @p", y PostgreSQL no define el operador
// "jsonb ~~ jsonb": 42883 operator does not exist. Con EF InMemory la consulta
// se evalua en memoria y el fallo nunca aparece, asi que esto solo se puede
// reproducir contra un PostgreSQL de verdad.
//
// El test cubre ademas el reintento del aviso: la primera pasada deja el
// email caido (la notificacion se crea pero no se marca como entregada), y la
// segunda pasada debe reintentar sin duplicar la notificacion de admin.
// -----------------------------------------------------------------------
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class PlazoFijoJsonbPostgresTests
{
    private readonly PostgresFixture _fixture;

    public PlazoFijoJsonbPostgresTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcesarVencimientosAsync_Contra_Postgres_Real_No_Debe_Fallar_Y_No_Debe_Duplicar_La_Notificacion()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString + ";Include Error Detail=true")
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var hoy = new DateOnly(2026, 11, 17);
        var manana = hoy.AddDays(1);
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();

        await using (var db = new AppDbContext(options))
        {
            db.Usuarios.Add(new Usuario
            {
                Id = Guid.NewGuid(),
                Email = $"admin.plazo.jsonb-{cuentaId:N}@test.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
                NombreCompleto = "Admin Plazo Jsonb",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            db.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Plazo Jsonb",
                Tipo = TipoTitular.EMPRESA
            });
            db.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Plazo Jsonb",
                Divisa = "EUR",
                TipoCuenta = TipoCuenta.PLAZO_FIJO,
                Activa = true
            });
            db.PlazosFijos.Add(new PlazoFijo
            {
                Id = plazoId,
                CuentaId = cuentaId,
                FechaInicio = hoy.AddMonths(-6),
                FechaVencimiento = hoy.AddDays(14),
                Renovable = true,
                Estado = EstadoPlazoFijo.ACTIVO
            });
            await db.SaveChangesAsync();
        }

        // Primera pasada: el email falla, asi que la notificacion se crea pero
        // el plazo no queda marcado como notificado.
        var emailService = new RecordingEmailService { FailPlazo = true };
        await using (var db = new AppDbContext(options))
        {
            var sut = new PlazoFijoService(
                db,
                emailService,
                TestAuditService.Create(db),
                NullLogger<PlazoFijoService>.Instance);

            await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);
        }

        await using (var db = new AppDbContext(options))
        {
            var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
            plazo.Estado.Should().Be(EstadoPlazoFijo.PROXIMO_VENCER);
            plazo.FechaUltimaNotificacion.Should().BeNull();
        }

        // Segunda pasada: el estado ya no cambia (sigue PROXIMO_VENCER) pero el
        // aviso nunca llego, asi que tiene que reintentarse. La notificacion de
        // admin ya existe y no debe duplicarse.
        emailService.FailPlazo = false;
        await using (var db = new AppDbContext(options))
        {
            var sut = new PlazoFijoService(
                db,
                emailService,
                TestAuditService.Create(db),
                NullLogger<PlazoFijoService>.Instance);

            await sut.ProcesarVencimientosAsync(manana, CancellationToken.None);
        }

        emailService.PlazoMessages.Should().HaveCount(1);

        await using (var db = new AppDbContext(options))
        {
            var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
            plazo.Estado.Should().Be(EstadoPlazoFijo.PROXIMO_VENCER);
            plazo.FechaUltimaNotificacion.Should().Be(manana);

            // El filtro va en memoria a proposito: un Contains sobre jsonb en
            // LINQ es justo lo que genera el operador inexistente.
            var todas = await db.NotificacionesAdmin
                .Where(n => n.Tipo == "PLAZO_FIJO")
                .ToListAsync();
            var mias = todas
                .Where(n => n.DetallesJson != null && n.DetallesJson.Contains(cuentaId.ToString()))
                .ToList();

            mias.Should().HaveCount(1);

            using var doc = JsonDocument.Parse(mias[0].DetallesJson!);
            doc.RootElement.GetProperty("cuenta_id").GetGuid().Should().Be(cuentaId);
            doc.RootElement.GetProperty("estado").GetString().Should().Be(nameof(EstadoPlazoFijo.PROXIMO_VENCER));
            doc.RootElement.GetProperty("fecha_vencimiento").GetString().Should().Be(hoy.AddDays(14).ToString("yyyy-MM-dd"));
        }
    }

    private sealed class RecordingEmailService : IEmailService
    {
        public List<string> PlazoMessages { get; } = [];
        public bool FailPlazo { get; set; }

        public Task SendAccountLockedAsync(IReadOnlyList<string> recipients, DateTime lockedAtUtc, int lockMinutes, string? ipSummary, CancellationToken cancellationToken)
            => Task.CompletedTask;

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
            => Task.CompletedTask;

        public Task SendPlazoFijoVencimientoAsync(
            IReadOnlyList<string> recipients,
            string titularNombre,
            string cuentaNombre,
            Guid cuentaId,
            DateOnly fechaVencimiento,
            EstadoPlazoFijo estado,
            CancellationToken cancellationToken)
        {
            if (FailPlazo)
            {
                throw new InvalidOperationException("SMTP roto en test");
            }

            PlazoMessages.Add(cuentaNombre);
            return Task.CompletedTask;
        }

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
    }
}
