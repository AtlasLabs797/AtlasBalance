using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class PlazoFijoServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Mark_ProximoVencer_At_14_Days()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);

        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.plazos@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Plazos",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Plazo", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Plazo 14", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
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

        var sut = new PlazoFijoService(
            db,
            new RecordingEmailService(),
            new AuditService(db),
            NullLogger<PlazoFijoService>.Instance);

        var changes = await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        changes.Should().Be(1);
        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.PROXIMO_VENCER);
        plazo.FechaUltimaNotificacion.Should().Be(hoy);
        (await db.NotificacionesAdmin.CountAsync(n => n.Tipo == "PLAZO_FIJO")).Should().Be(1);
    }

    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Mark_Vencido_On_Due_Date()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Vencida", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Plazo Hoy", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazoId,
            CuentaId = cuentaId,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy,
            Estado = EstadoPlazoFijo.ACTIVO
        });
        await db.SaveChangesAsync();

        var sut = new PlazoFijoService(
            db,
            new RecordingEmailService(),
            new AuditService(db),
            NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.VENCIDO);
    }

    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Not_Mark_Notified_When_Email_Fails_And_Should_Retry()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);

        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.retry@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Retry",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Retry", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Plazo Retry", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazoId,
            CuentaId = cuentaId,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy,
            Estado = EstadoPlazoFijo.ACTIVO
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService { FailPlazo = true };
        var sut = new PlazoFijoService(db, emailService, new AuditService(db), NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.VENCIDO);
        plazo.FechaUltimaNotificacion.Should().BeNull();
        (await db.NotificacionesAdmin.CountAsync(n => n.Tipo == "PLAZO_FIJO")).Should().Be(1);

        emailService.FailPlazo = false;
        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.FechaUltimaNotificacion.Should().Be(hoy);
        emailService.PlazoMessages.Should().HaveCount(2);
        (await db.NotificacionesAdmin.CountAsync(n => n.Tipo == "PLAZO_FIJO")).Should().Be(1);
    }

    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Not_Mark_Notified_When_No_Admin_Recipients()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Sin Admin", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Plazo Sin Admin", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazoId,
            CuentaId = cuentaId,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy,
            Estado = EstadoPlazoFijo.ACTIVO
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var sut = new PlazoFijoService(db, emailService, new AuditService(db), NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.VENCIDO);
        plazo.FechaUltimaNotificacion.Should().BeNull();
        emailService.PlazoMessages.Should().BeEmpty();
        (await db.NotificacionesAdmin.CountAsync(n => n.Tipo == "PLAZO_FIJO")).Should().Be(1);
    }

    private sealed class RecordingEmailService : IEmailService
    {
        public List<PlazoEmailMessage> PlazoMessages { get; } = [];
        public bool FailPlazo { get; set; }

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
            PlazoMessages.Add(new PlazoEmailMessage([.. recipients], cuentaId, fechaVencimiento, estado));
            if (FailPlazo)
            {
                throw new InvalidOperationException("SMTP roto en test");
            }

            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed record PlazoEmailMessage(
        IReadOnlyList<string> Recipients,
        Guid CuentaId,
        DateOnly FechaVencimiento,
        EstadoPlazoFijo Estado);
}
