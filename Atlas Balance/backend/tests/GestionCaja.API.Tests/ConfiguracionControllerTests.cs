using System.Security.Claims;
using FluentAssertions;
using GestionCaja.API;
using GestionCaja.API.Controllers;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Constants;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCaja.API.Tests;

public sealed class ConfiguracionControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Get_Should_Not_Return_SmtpPassword()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "smtp_host", Valor = "smtp.local" },
            new Configuracion { Clave = "smtp_port", Valor = "587" },
            new Configuracion { Clave = "smtp_user", Valor = "mailer" },
            new Configuracion { Clave = "smtp_password", Valor = "super-secret" },
            new Configuracion { Clave = "smtp_from", Valor = "noreply@test.local" });
        await db.SaveChangesAsync();

        var controller = BuildController(db);

        var result = await controller.Get(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ConfiguracionSistemaResponse>().Subject;
        payload.Smtp.Password.Should().BeEmpty();
        payload.General.AppUpdateCheckUrl.Should().Be(ConfigurationDefaults.UpdateCheckUrl);
    }

    [Fact]
    public async Task Update_Should_Preserve_Blank_SmtpPassword_And_Redact_Audit()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "smtp_host", Valor = "old.local" },
            new Configuracion { Clave = "smtp_port", Valor = "587" },
            new Configuracion { Clave = "smtp_user", Valor = "old-user" },
            new Configuracion { Clave = "smtp_password", Valor = "super-secret" },
            new Configuracion { Clave = "smtp_from", Valor = "old@test.local" });
        await db.SaveChangesAsync();

        var controller = BuildController(db);

        var result = await controller.Update(new UpdateConfiguracionRequest
        {
            Smtp = new UpdateSmtpConfigRequest
            {
                Host = "new.local",
                Port = 2525,
                User = "new-user",
                Password = "",
                From = "new@test.local"
            },
            General = new UpdateGeneralConfigRequest
            {
                AppBaseUrl = "https://app.local",
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest
            {
                ColorIngresos = "#111111",
                ColorEgresos = "#222222",
                ColorSaldo = "#333333"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var smtpPassword = await db.Configuraciones.SingleAsync(x => x.Clave == "smtp_password");
        smtpPassword.Valor.Should().Be("super-secret");
        var updateUrl = await db.Configuraciones.SingleAsync(x => x.Clave == "app_update_check_url");
        updateUrl.Valor.Should().Be(ConfigurationDefaults.UpdateCheckUrl);

        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.UpdateConfiguracion);
        audit.DetallesJson.Should().NotContain("super-secret");
        audit.DetallesJson.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task Update_Should_Reject_NonOfficial_Update_Check_Url()
    {
        await using var db = BuildDbContext();
        var controller = BuildController(db);

        var result = await controller.Update(new UpdateConfiguracionRequest
        {
            Smtp = new UpdateSmtpConfigRequest
            {
                Host = "smtp.local",
                Port = 587,
                User = "user",
                Password = "",
                From = "noreply@test.local"
            },
            General = new UpdateGeneralConfigRequest
            {
                AppBaseUrl = "https://app.local",
                AppUpdateCheckUrl = "http://localhost/internal",
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest()
        }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Configuraciones.AnyAsync(x => x.Clave == "app_update_check_url")).Should().BeFalse();
        (await db.Auditorias.AnyAsync()).Should().BeFalse();
    }

    private static ConfiguracionController BuildController(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var controller = new ConfiguracionController(
            db,
            new NoOpEmailService(),
            new AuditService(db),
            NullLogger<ConfiguracionController>.Instance,
            new PlainTextSecretProtector());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, nameof(RolUsuario.ADMIN))
                ], "TestAuth"))
            }
        };

        return controller;
    }

    private sealed class NoOpEmailService : IEmailService
    {
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

        public Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendPlazoFijoVencimientoAsync(
            IReadOnlyList<string> recipients,
            string titularNombre,
            string cuentaNombre,
            Guid cuentaId,
            DateOnly fechaVencimiento,
            EstadoPlazoFijo estado,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
