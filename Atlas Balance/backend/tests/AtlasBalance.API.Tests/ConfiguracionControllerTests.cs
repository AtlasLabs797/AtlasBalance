using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

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
    public async Task Get_Should_Not_Return_Secret_Values()
    {
        // V-02.07: antes se comprobaba solo que Smtp.Password llegara vacio.
        // Ahora los DTO de respuesta ni siquiera declaran los campos de
        // secreto, asi que verificamos el JSON realmente serializado: cubre
        // password SMTP y clave de ExchangeRate, y detecta cualquier campo
        // nuevo que reintroduzca un valor sensible en la respuesta.
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "smtp_host", Valor = "smtp.local" },
            new Configuracion { Clave = "smtp_port", Valor = "587" },
            new Configuracion { Clave = "smtp_user", Valor = "mailer" },
            new Configuracion { Clave = "smtp_password", Valor = "super-secret" },
            new Configuracion { Clave = "smtp_from", Valor = "noreply@test.local" },
            new Configuracion { Clave = "exchange_rate_api_key", Valor = "exchange-super-secret" });
        await db.SaveChangesAsync();

        var controller = BuildController(db);

        var result = await controller.Get(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ConfiguracionSistemaResponse>().Subject;

        var json = JsonSerializer.Serialize(payload);
        json.Should().NotContain("super-secret");
        json.Should().NotContain("exchange-super-secret");

        payload.Exchange.ApiKeyConfigurada.Should().BeTrue();
        payload.General.AppUpdateCheckUrl.Should().Be(ConfigurationDefaults.UpdateCheckUrl);
        payload.General.MfaRememberDeviceEnabled.Should().BeFalse();
        payload.General.MfaRememberDeviceDays.Should().Be(SecurityConfigurationDefaults.MfaRememberDeviceDays);
        payload.General.RequireMfaForNonAdminUsers.Should().BeTrue();
    }

    [Fact]
    public async Task Update_Should_Persist_RequireMfaForNonAdminUsers_And_Audit_Change()
    {
        // V-02.06: la politica operativa para gerentes/empleados vive en
        // CONFIGURACION. El controller debe persistir el valor y registrar un
        // evento semantico cuando cambia.
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
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                MfaRememberDeviceEnabled = true,
                RequireMfaForNonAdminUsers = false,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest()
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var stored = await db.Configuraciones.SingleAsync(x => x.Clave == SecurityConfigurationDefaults.MfaRequireForNonAdminUsersKey);
        stored.Valor.Should().Be("false");

        var semanticAudit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.MfaPolicyUpdated);
        semanticAudit.DetallesJson.Should().Contain("\"antes\":true");
        semanticAudit.DetallesJson.Should().Contain("\"despues\":false");
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
                MfaRememberDeviceEnabled = true,
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
        var mfaRemember = await db.Configuraciones.SingleAsync(x => x.Clave == SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey);
        mfaRemember.Valor.Should().Be("true");

        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.UpdateConfiguracion);
        audit.DetallesJson.Should().NotContain("super-secret");
        audit.DetallesJson.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task Update_Should_Accept_OpenAi_With_Server_Api_Key_And_Redact_Audit()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion { Clave = "openai_api_key", Valor = "old-openai-key" });
        await db.SaveChangesAsync();
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
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest(),
            Ia = new UpdateIaConfigRequest
            {
                Provider = "OPENAI",
                Model = "gpt-4o-mini",
                Habilitada = true,
                OpenAiApiKey = "openai-test-placeholder"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Configuraciones.SingleAsync(x => x.Clave == "ai_provider")).Valor.Should().Be("OPENAI");
        (await db.Configuraciones.SingleAsync(x => x.Clave == "openai_api_key")).Valor.Should().Be("openai-test-placeholder");

        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.UpdateConfiguracion);
        audit.DetallesJson.Should().NotContain("openai-test-placeholder");
        audit.DetallesJson.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task Update_Should_Accept_MiniMax_With_Server_Api_Key_And_Redact_Audit()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion { Clave = "minimax_api_key", Valor = "old-minimax-key" });
        await db.SaveChangesAsync();
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
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest(),
            Ia = new UpdateIaConfigRequest
            {
                Provider = "MINIMAX",
                Model = AiConfiguration.DefaultMiniMaxModel,
                Habilitada = true,
                MiniMaxApiKey = "minimax-test-placeholder"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Configuraciones.SingleAsync(x => x.Clave == "ai_provider")).Valor.Should().Be("MINIMAX");
        (await db.Configuraciones.SingleAsync(x => x.Clave == "ai_model")).Valor.Should().Be(AiConfiguration.DefaultMiniMaxModel);
        (await db.Configuraciones.SingleAsync(x => x.Clave == "minimax_api_key")).Valor.Should().Be("minimax-test-placeholder");

        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.UpdateConfiguracion);
        audit.DetallesJson.Should().NotContain("minimax-test-placeholder");
        audit.DetallesJson.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task Update_Should_Default_Blank_OpenRouter_Model_To_Auto_And_Save_Key()
    {
        await using var db = BuildDbContext();
        db.Configuraciones.AddRange(
            new Configuracion { Clave = "openrouter_api_key", Valor = "old-openrouter-key" },
            new Configuracion { Clave = "ai_model", Valor = "" });
        await db.SaveChangesAsync();
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
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest(),
            Ia = new UpdateIaConfigRequest
            {
                Provider = "OPENROUTER",
                Model = "",
                Habilitada = true,
                OpenRouterApiKey = "openrouter-test-placeholder"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Configuraciones.SingleAsync(x => x.Clave == "ai_model")).Valor.Should().Be(AiConfiguration.OpenRouterAutoModel);
        (await db.Configuraciones.SingleAsync(x => x.Clave == "openrouter_api_key")).Valor.Should().Be("openrouter-test-placeholder");

        var audit = await db.Auditorias.SingleAsync(x => x.TipoAccion == AuditActions.UpdateConfiguracion);
        audit.DetallesJson.Should().NotContain("openrouter-test-placeholder");
        audit.DetallesJson.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task Update_Should_Normalize_Unknown_OpenRouter_Model_To_Auto()
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
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest(),
            Ia = new UpdateIaConfigRequest
            {
                Provider = "OPENROUTER",
                Model = "random/expensive-model",
                Habilitada = true,
                OpenRouterApiKey = "openrouter-test-placeholder"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Configuraciones.SingleAsync(x => x.Clave == "ai_model")).Valor.Should().Be(AiConfiguration.OpenRouterAutoModel);
        (await db.Configuraciones.SingleAsync(x => x.Clave == "openrouter_api_key")).Valor.Should().Be("openrouter-test-placeholder");
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

    [Fact]
    public async Task Update_Should_Reject_Null_Text_Fields()
    {
        await using var db = BuildDbContext();
        var controller = BuildController(db);

        var result = await controller.Update(new UpdateConfiguracionRequest
        {
            Smtp = new UpdateSmtpConfigRequest
            {
                Host = null!,
                Port = 587,
                User = "user",
                Password = "",
                From = "noreply@test.local"
            },
            General = new UpdateGeneralConfigRequest
            {
                AppBaseUrl = "https://app.local",
                AppUpdateCheckUrl = ConfigurationDefaults.UpdateCheckUrl,
                BackupPath = "C:\\backups",
                ExportPath = "C:\\exports"
            },
            Dashboard = new UpdateDashboardConfigRequest()
        }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Auditorias.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SendTestEmail_Should_Reject_Null_Request()
    {
        await using var db = BuildDbContext();
        var controller = BuildController(db);

        var result = await controller.SendTestEmail(null!, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
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
            new PlainTextSecretProtector(),
            new SmtpTestRateLimit());

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
