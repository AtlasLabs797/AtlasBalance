using System.Security.Claims;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.Constants;
using GestionCaja.API.DTOs;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/configuracion")]
public sealed class ConfiguracionController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ConfiguracionController> _logger;
    private readonly ISecretProtector _secretProtector;

    public ConfiguracionController(
        AppDbContext dbContext,
        IEmailService emailService,
        IAuditService auditService,
        ILogger<ConfiguracionController> logger,
        ISecretProtector secretProtector)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _auditService = auditService;
        _logger = logger;
        _secretProtector = secretProtector;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var config = await LoadConfigMapAsync(cancellationToken);

        return Ok(new ConfiguracionSistemaResponse
        {
            Smtp = new SmtpConfigResponse
            {
                Host = GetValue(config, "smtp_host"),
                Port = ParseInt(GetValue(config, "smtp_port"), 587),
                User = GetValue(config, "smtp_user"),
                Password = string.Empty,
                From = GetValue(config, "smtp_from")
            },
            General = new GeneralConfigResponse
            {
                AppBaseUrl = GetValue(config, "app_base_url"),
                AppUpdateCheckUrl = GetValue(config, "app_update_check_url", ConfigurationDefaults.UpdateCheckUrl),
                BackupPath = GetValue(config, "backup_path"),
                ExportPath = GetValue(config, "export_path")
            },
            Exchange = new ExchangeRateConfigResponse
            {
                ApiKey = string.Empty,
                ApiKeyConfigurada = !string.IsNullOrWhiteSpace(GetValue(config, "exchange_rate_api_key"))
            },
            Dashboard = new DashboardConfigResponse
            {
                ColorIngresos = GetValue(config, "dashboard_color_ingresos", "#43B430"),
                ColorEgresos = GetValue(config, "dashboard_color_egresos", "#FF4757"),
                ColorSaldo = GetValue(config, "dashboard_color_saldo", "#7B7B7B")
            }
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateConfiguracionRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request invalido." });
        }

        if (request.Smtp is null || request.General is null || request.Dashboard is null)
        {
            return BadRequest(new { error = "Configuracion incompleta." });
        }

        if (request.Smtp.Port <= 0 || request.Smtp.Port > 65535)
        {
            return BadRequest(new { error = "Puerto SMTP inválido." });
        }

        if (!IsValidAppBaseUrl(request.General.AppBaseUrl))
        {
            return BadRequest(new { error = "La URL base debe ser absoluta y usar http o https." });
        }

        if (!ConfigurationDefaults.TryNormalizeUpdateCheckUrl(request.General.AppUpdateCheckUrl, out var updateCheckUrl))
        {
            return BadRequest(new { error = "La URL de actualizaciones debe apuntar al repositorio oficial de Atlas Balance en GitHub por HTTPS." });
        }

        if (!IsSafeAbsoluteDirectory(request.General.BackupPath))
        {
            return BadRequest(new { error = "La ruta de backups debe ser absoluta y no contener traversal." });
        }

        if (!IsSafeAbsoluteDirectory(request.General.ExportPath))
        {
            return BadRequest(new { error = "La ruta de exportaciones debe ser absoluta y no contener traversal." });
        }

        var userId = GetCurrentUserId();
        var now = DateTime.UtcNow;
        var config = await _dbContext.Configuraciones.ToListAsync(cancellationToken);
        var before = config.ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);

        Upsert(config, "smtp_host", request.Smtp.Host.Trim(), userId, now);
        Upsert(config, "smtp_port", request.Smtp.Port.ToString(), userId, now);
        Upsert(config, "smtp_user", request.Smtp.User.Trim(), userId, now);
        if (!string.IsNullOrWhiteSpace(request.Smtp.Password))
        {
            Upsert(config, "smtp_password", _secretProtector.ProtectForStorage(request.Smtp.Password), userId, now);
        }
        Upsert(config, "smtp_from", request.Smtp.From.Trim(), userId, now);

        Upsert(config, "app_base_url", request.General.AppBaseUrl.Trim(), userId, now);
        Upsert(config, "app_update_check_url", updateCheckUrl, userId, now);
        Upsert(config, "backup_path", request.General.BackupPath.Trim(), userId, now);
        Upsert(config, "export_path", request.General.ExportPath.Trim(), userId, now);
        var exchangeApiKey = request.Exchange?.ApiKey;
        if (!string.IsNullOrWhiteSpace(exchangeApiKey))
        {
            Upsert(config, "exchange_rate_api_key", _secretProtector.ProtectForStorage(exchangeApiKey), userId, now);
        }

        Upsert(config, "dashboard_color_ingresos", request.Dashboard.ColorIngresos.Trim(), userId, now);
        Upsert(config, "dashboard_color_egresos", request.Dashboard.ColorEgresos.Trim(), userId, now);
        Upsert(config, "dashboard_color_saldo", request.Dashboard.ColorSaldo.Trim(), userId, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = config.ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        await _auditService.LogAsync(
            userId,
            AuditActions.UpdateConfiguracion,
            "CONFIGURACION",
            null,
            HttpContext,
            JsonSerializer.Serialize(new
            {
                before = RedactSensitiveConfig(before),
                after = RedactSensitiveConfig(after)
            }),
            cancellationToken);

        return Ok(new { message = "Configuración actualizada" });
    }

    [HttpPost("smtp/test")]
    public async Task<IActionResult> SendTestEmail([FromBody] SendTestEmailRequest request, CancellationToken cancellationToken)
    {
        var config = await LoadConfigMapAsync(cancellationToken);
        var target = request.To?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            target = GetValue(config, "smtp_from");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return BadRequest(new { error = "Debe indicar un destinatario para el correo de prueba." });
        }

        try
        {
            await _emailService.SendTestEmailAsync(target, cancellationToken);
            await _auditService.LogAsync(
                GetCurrentUserId(),
                AuditActions.TestSmtp,
                "CONFIGURACION",
                null,
                HttpContext,
                JsonSerializer.Serialize(new { to = target }),
                cancellationToken);
            return Ok(new { message = "Correo de prueba enviado." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al enviar email de prueba SMTP a {Target}", target);
            return BadRequest(new { error = "No se pudo enviar el correo de prueba. Revise los logs del servidor para más detalles." });
        }
    }

    private async Task<Dictionary<string, string>> LoadConfigMapAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Configuraciones
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }

    private void Upsert(
        IReadOnlyCollection<GestionCaja.API.Models.Configuracion> existing,
        string key,
        string value,
        Guid? userId,
        DateTime now)
    {
        var item = existing.FirstOrDefault(x => x.Clave.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            _dbContext.Configuraciones.Add(new GestionCaja.API.Models.Configuracion
            {
                Clave = key,
                Valor = value,
                FechaModificacion = now,
                UsuarioModificacionId = userId
            });
            return;
        }

        item.Valor = value;
        item.FechaModificacion = now;
        item.UsuarioModificacionId = userId;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> map, string key, string defaultValue = "")
    {
        return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static bool IsValidAppBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeAbsoluteDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Path.IsPathRooted(trimmed) && !LooksLikeWindowsRootedPath(trimmed))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(trimmed);
            return !string.IsNullOrWhiteSpace(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool LooksLikeWindowsRootedPath(string value)
    {
        return value.Length >= 3 &&
               char.IsLetter(value[0]) &&
               value[1] == ':' &&
               (value[2] == '\\' || value[2] == '/');
    }

    private static Dictionary<string, string> RedactSensitiveConfig(IReadOnlyDictionary<string, string> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Key.Equals("smtp_password", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Equals("exchange_rate_api_key", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrEmpty(pair.Value) ? string.Empty : "[REDACTED]")
                : pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
