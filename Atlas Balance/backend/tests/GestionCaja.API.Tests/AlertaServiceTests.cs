using FluentAssertions;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCaja.API.Tests;

public class AlertaServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task EvaluateSaldoPostAsync_Should_Prioritize_AccountAlert_And_Update_LastAlert()
    {
        await using var db = BuildDbContext();

        var actorId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var globalAlertId = Guid.NewGuid();
        var cuentaAlertId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = actorId,
            Email = "actor@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Actor Test",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });

        db.UsuarioEmails.Add(new UsuarioEmail
        {
            Id = Guid.NewGuid(),
            UsuarioId = actorId,
            Email = "actor.extra@test.local"
        });

        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Uno",
            Tipo = TipoTitular.EMPRESA
        });

        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Operativa",
            Divisa = "EUR",
            Activa = true
        });

        db.AlertasSaldo.AddRange(
            new AlertaSaldo
            {
                Id = globalAlertId,
                CuentaId = null,
                SaldoMinimo = 100m,
                Activa = true,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-10)
            },
            new AlertaSaldo
            {
                Id = cuentaAlertId,
                CuentaId = cuentaId,
                SaldoMinimo = 250m,
                Activa = true,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-5)
            });

        db.AlertaDestinatarios.Add(new AlertaDestinatario
        {
            Id = Guid.NewGuid(),
            AlertaId = cuentaAlertId,
            UsuarioId = actorId
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

        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var auditService = new RecordingAuditService();
        var sut = new AlertaService(db, emailService, auditService, NullLogger<AlertaService>.Instance);

        await sut.EvaluateSaldoPostAsync(cuentaId, actorId, CancellationToken.None);

        emailService.Messages.Should().ContainSingle();
        emailService.Messages[0].CuentaId.Should().Be(cuentaId);
        emailService.Messages[0].SaldoMinimo.Should().Be(250m);
        emailService.Messages[0].Recipients.Should().BeEquivalentTo(["actor@test.local", "actor.extra@test.local"]);

        var cuentaAlert = await db.AlertasSaldo.SingleAsync(x => x.Id == cuentaAlertId);
        var globalAlert = await db.AlertasSaldo.SingleAsync(x => x.Id == globalAlertId);

        cuentaAlert.FechaUltimaAlerta.Should().NotBeNull();
        globalAlert.FechaUltimaAlerta.Should().BeNull();

        auditService.Entries.Should().ContainSingle(x =>
            x.TipoAccion == AuditActions.AlertaSaldoDisparada &&
            x.EntidadId == cuentaAlertId &&
            x.UsuarioId == actorId);
    }

    [Fact]
    public async Task GetAlertasActivasAsync_Should_Ignore_InactiveAccounts_And_Apply_GlobalFallback()
    {
        await using var db = BuildDbContext();

        var titularActivoId = Guid.NewGuid();
        var titularInactivoId = Guid.NewGuid();
        var cuentaActivaId = Guid.NewGuid();
        var cuentaInactivaId = Guid.NewGuid();
        var alertaGlobalId = Guid.NewGuid();

        db.Titulares.AddRange(
            new Titular { Id = titularActivoId, Nombre = "Titular Activo", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularInactivoId, Nombre = "Titular Inactivo", Tipo = TipoTitular.EMPRESA });

        db.Cuentas.AddRange(
            new Cuenta
            {
                Id = cuentaActivaId,
                TitularId = titularActivoId,
                Nombre = "Cuenta Visible",
                Divisa = "EUR",
                Activa = true
            },
            new Cuenta
            {
                Id = cuentaInactivaId,
                TitularId = titularInactivoId,
                Nombre = "Cuenta Inactiva",
                Divisa = "EUR",
                Activa = false
            });

        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaActivaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow),
                Monto = -50m,
                Saldo = 90m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaInactivaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow),
                Monto = -10m,
                Saldo = 10m,
                FilaNumero = 1
            });

        db.AlertasSaldo.Add(new AlertaSaldo
        {
            Id = alertaGlobalId,
            CuentaId = null,
            SaldoMinimo = 100m,
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var sut = new AlertaService(
            db,
            new RecordingEmailService(),
            new RecordingAuditService(),
            NullLogger<AlertaService>.Instance);

        var result = await sut.GetAlertasActivasAsync(
            new UserAccessScope
            {
                UserId = Guid.NewGuid(),
                IsAdmin = true,
                HasPermissions = true,
                HasGlobalAccess = true
            },
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].CuentaId.Should().Be(cuentaActivaId);
        result[0].AlertaId.Should().Be(alertaGlobalId);
        result[0].SaldoActual.Should().Be(90m);
    }

    private sealed class RecordingEmailService : IEmailService
    {
        public List<EmailMessage> Messages { get; } = [];

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
            Messages.Add(new EmailMessage(
                [.. recipients],
                titularNombre,
                cuentaNombre,
                cuentaId,
                divisa,
                saldoActual,
                saldoMinimo,
                conceptoUltimoMovimiento));

            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record EmailMessage(
        IReadOnlyList<string> Recipients,
        string TitularNombre,
        string CuentaNombre,
        Guid CuentaId,
        string Divisa,
        decimal SaldoActual,
        decimal SaldoMinimo,
        string? ConceptoUltimoMovimiento);

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(
            Guid? usuarioId,
            string tipoAccion,
            string? entidadTipo,
            Guid? entidadId,
            HttpContext httpContext,
            string? detallesJson,
            CancellationToken cancellationToken)
            => LogAsync(usuarioId, tipoAccion, entidadTipo, entidadId, httpContext.Connection.RemoteIpAddress?.ToString(), detallesJson, cancellationToken);

        public Task LogAsync(
            Guid? usuarioId,
            string tipoAccion,
            string? entidadTipo,
            Guid? entidadId,
            string? ipAddress,
            string? detallesJson,
            CancellationToken cancellationToken)
        {
            Entries.Add(new AuditEntry(usuarioId, tipoAccion, entidadTipo, entidadId, ipAddress, detallesJson));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEntry(
        Guid? UsuarioId,
        string TipoAccion,
        string? EntidadTipo,
        Guid? EntidadId,
        string? IpAddress,
        string? DetallesJson);
}
