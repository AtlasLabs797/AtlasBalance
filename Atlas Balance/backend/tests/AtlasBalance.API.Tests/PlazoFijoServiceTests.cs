using System.Text.Json;
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
            TestAuditService.Create(db),
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
            TestAuditService.Create(db),
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
        var sut = new PlazoFijoService(db, emailService, TestAuditService.Create(db), NullLogger<PlazoFijoService>.Instance);

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
        var sut = new PlazoFijoService(db, emailService, TestAuditService.Create(db), NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.VENCIDO);
        plazo.FechaUltimaNotificacion.Should().BeNull();
        emailService.PlazoMessages.Should().BeEmpty();
        (await db.NotificacionesAdmin.CountAsync(n => n.Tipo == "PLAZO_FIJO")).Should().Be(1);
    }

    // J-3: el aviso de "proximo a vencer" debe reintentarse mientras el plazo
    // siga en PROXIMO_VENCER y nunca se haya notificado con exito.
    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Retry_ProximoVencer_Notice_When_Email_Failed()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);
        var manana = hoy.AddDays(1);

        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "admin.retry.proximo@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Retry Proximo",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Retry Proximo", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Plazo Retry Proximo", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazoId,
            CuentaId = cuentaId,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy.AddDays(14),
            Estado = EstadoPlazoFijo.ACTIVO
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService { FailPlazo = true };
        var sut = new PlazoFijoService(db, emailService, TestAuditService.Create(db), NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.PROXIMO_VENCER);
        plazo.FechaUltimaNotificacion.Should().BeNull();
        emailService.PlazoMessages.Should().HaveCount(1);

        emailService.FailPlazo = false;
        await sut.ProcesarVencimientosAsync(manana, CancellationToken.None);

        plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.Estado.Should().Be(EstadoPlazoFijo.PROXIMO_VENCER);
        plazo.FechaUltimaNotificacion.Should().Be(manana);
        emailService.PlazoMessages.Should().HaveCount(2);
        (await db.NotificacionesAdmin.CountAsync(n => n.Tipo == "PLAZO_FIJO")).Should().Be(1);
    }

    // J-3: si el aviso ya se notifico con exito, no se repite al dia siguiente.
    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Not_Resend_ProximoVencer_Notice_When_Already_Notified()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);
        var manana = hoy.AddDays(1);

        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "admin.sin.retry@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Sin Retry",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Sin Retry", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Plazo Sin Retry", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazoId,
            CuentaId = cuentaId,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy.AddDays(14),
            Estado = EstadoPlazoFijo.ACTIVO
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var sut = new PlazoFijoService(db, emailService, TestAuditService.Create(db), NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.FechaUltimaNotificacion.Should().Be(hoy);
        emailService.PlazoMessages.Should().HaveCount(1);

        var changes = await sut.ProcesarVencimientosAsync(manana, CancellationToken.None);

        changes.Should().Be(0);
        plazo = await db.PlazosFijos.SingleAsync(p => p.Id == plazoId);
        plazo.FechaUltimaNotificacion.Should().Be(hoy);
        emailService.PlazoMessages.Should().HaveCount(1);
    }

    // J-3: cada plazo del grupo (titular, estado) necesita su propia
    // notificacion de admin, aunque el email siga siendo uno por grupo.
    [Fact]
    public async Task ProcesarVencimientosAsync_Should_Create_Admin_Notification_For_Every_Plazo_In_Group()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuenta1Id = Guid.NewGuid();
        var cuenta2Id = Guid.NewGuid();
        var plazo1Id = Guid.NewGuid();
        var plazo2Id = Guid.NewGuid();
        var hoy = new DateOnly(2026, 4, 25);

        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "admin.dos.plazos@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Dos Plazos",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Empresa Dos Plazos", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuenta1Id, TitularId = titularId, Nombre = "Plazo A", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.Cuentas.Add(new Cuenta { Id = cuenta2Id, TitularId = titularId, Nombre = "Plazo B", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazo1Id,
            CuentaId = cuenta1Id,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy.AddDays(14),
            Estado = EstadoPlazoFijo.ACTIVO
        });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = plazo2Id,
            CuentaId = cuenta2Id,
            FechaInicio = hoy.AddMonths(-6),
            FechaVencimiento = hoy.AddDays(7),
            Estado = EstadoPlazoFijo.ACTIVO
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var sut = new PlazoFijoService(db, emailService, TestAuditService.Create(db), NullLogger<PlazoFijoService>.Instance);

        await sut.ProcesarVencimientosAsync(hoy, CancellationToken.None);

        var notificaciones = await db.NotificacionesAdmin.Where(n => n.Tipo == "PLAZO_FIJO").ToListAsync();
        notificaciones.Should().HaveCount(2);

        using var doc1 = JsonDocument.Parse(notificaciones.Single(n => n.DetallesJson!.Contains(cuenta1Id.ToString())).DetallesJson!);
        doc1.RootElement.GetProperty("cuenta_id").GetGuid().Should().Be(cuenta1Id);
        doc1.RootElement.GetProperty("fecha_vencimiento").GetString().Should().Be(hoy.AddDays(14).ToString("yyyy-MM-dd"));

        using var doc2 = JsonDocument.Parse(notificaciones.Single(n => n.DetallesJson!.Contains(cuenta2Id.ToString())).DetallesJson!);
        doc2.RootElement.GetProperty("cuenta_id").GetGuid().Should().Be(cuenta2Id);
        doc2.RootElement.GetProperty("fecha_vencimiento").GetString().Should().Be(hoy.AddDays(7).ToString("yyyy-MM-dd"));

        emailService.PlazoMessages.Should().HaveCount(1);
        emailService.PlazoMessages[0].CuentaNombre.Should().Contain("Plazo A").And.Contain("Plazo B");
    }

    private sealed class RecordingEmailService : IEmailService
    {
        public Task SendAccountLockedAsync(IReadOnlyList<string> recipients, DateTime lockedAtUtc, int lockMinutes, string? ipSummary, CancellationToken cancellationToken)
            => Task.CompletedTask;

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
            PlazoMessages.Add(new PlazoEmailMessage([.. recipients], cuentaNombre, cuentaId, fechaVencimiento, estado));
            if (FailPlazo)
            {
                throw new InvalidOperationException("SMTP roto en test");
            }

            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
            => Task.CompletedTask;

        // V-02.07: canal de alertas de seguridad. Estos tests son de plazos
        // fijos y no lo ejercitan.
        public Task SendSecurityAlertAsync(
            IReadOnlyList<string> recipients,
            string regla,
            string severidad,
            string resumen,
            IReadOnlyList<string> detalles,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed record PlazoEmailMessage(
        IReadOnlyList<string> Recipients,
        string CuentaNombre,
        Guid CuentaId,
        DateOnly FechaVencimiento,
        EstadoPlazoFijo Estado);
}
