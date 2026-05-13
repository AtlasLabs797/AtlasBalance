using System.Security.Claims;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.Constants;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtlasBalance.API.Controllers;

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
        var currentUserId = GetCurrentUserId();
        var usuarioPuedeUsarIa = currentUserId.HasValue &&
            await _dbContext.Usuarios
                .AsNoTracking()
                .Where(x => x.Id == currentUserId.Value && x.Activo)
                .Select(x => x.PuedeUsarIa)
                .FirstOrDefaultAsync(cancellationToken);

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
            },
            Revision = new RevisionConfigResponse
            {
                ComisionesImporteMinimo = ParseDecimal(GetValue(config, "revision_comisiones_importe_minimo"), 1m),
                SaldoBajoCooldownHoras = ParseInt(GetValue(config, "alerta_saldo_cooldown_horas"), 24)
            },
            Ia = BuildIaConfigResponse(config, usuarioPuedeUsarIa)
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateConfiguracionRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "La solicitud esta incompleta o no tiene el formato esperado." });
        }

        if (request.Smtp is null || request.General is null || request.Dashboard is null)
        {
            return BadRequest(new { error = "Faltan datos obligatorios de configuracion." });
        }

        if (request.Smtp.Port <= 0 || request.Smtp.Port > 65535)
        {
            return BadRequest(new { error = "Puerto SMTP inválido." });
        }

        if (!string.IsNullOrWhiteSpace(request.General.AppBaseUrl) && !IsValidAppBaseUrl(request.General.AppBaseUrl))
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

        if (request.Revision is not null && request.Revision.ComisionesImporteMinimo < 0)
        {
            return BadRequest(new { error = "El importe minimo de comisiones no puede ser negativo." });
        }

        if (request.Revision is not null && request.Revision.SaldoBajoCooldownHoras < 1)
        {
            return BadRequest(new { error = "La ventana antiduplicados de saldo bajo debe ser de al menos 1 hora." });
        }

        var config = await _dbContext.Configuraciones.ToListAsync(cancellationToken);
        var before = config.ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        var aiRequest = request.Ia ?? new UpdateIaConfigRequest();
        var aiProvider = AiConfiguration.NormalizeProvider(aiRequest.Provider);
        if (!AiConfiguration.IsSupportedProvider(aiProvider))
        {
            return BadRequest(new { error = "Proveedor de IA no soportado. Atlas Balance admite OpenRouter u OpenAI con clave API de servidor." });
        }

        var aiModel = AiConfiguration.NormalizeModel(aiProvider, aiRequest.Model);
        if (!AiConfiguration.IsAllowedModel(aiProvider, aiModel))
        {
            return BadRequest(new { error = "Modelo de IA no permitido por la politica de Atlas Balance. Usa un modelo permitido o openrouter/auto." });
        }

        var aiValidationError = ValidateIaGovernance(aiRequest);
        if (aiValidationError is not null)
        {
            return BadRequest(new { error = aiValidationError });
        }

        var userId = GetCurrentUserId();
        var now = DateTime.UtcNow;

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
        Upsert(config, "revision_comisiones_importe_minimo", (request.Revision?.ComisionesImporteMinimo ?? 1m).ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "alerta_saldo_cooldown_horas", Math.Clamp(request.Revision?.SaldoBajoCooldownHoras ?? 24, 1, 720).ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_enabled", aiRequest.Habilitada ? "true" : "false", userId, now);
        Upsert(config, "ai_provider", aiProvider, userId, now);
        Upsert(config, "ai_model", aiModel, userId, now);
        var openRouterApiKey = aiRequest.OpenRouterApiKey;
        if (!string.IsNullOrWhiteSpace(openRouterApiKey))
        {
            Upsert(config, "openrouter_api_key", _secretProtector.ProtectForStorage(openRouterApiKey), userId, now);
        }
        var openAiApiKey = aiRequest.OpenAiApiKey;
        if (!string.IsNullOrWhiteSpace(openAiApiKey))
        {
            Upsert(config, "openai_api_key", _secretProtector.ProtectForStorage(openAiApiKey), userId, now);
        }
        Upsert(config, "ai_requests_per_minute", aiRequest.RequestsPorMinuto.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_requests_per_hour", aiRequest.RequestsPorHora.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_requests_per_day", aiRequest.RequestsPorDia.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_global_requests_per_day", aiRequest.RequestsGlobalesPorDia.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_monthly_budget_eur", aiRequest.PresupuestoMensualEur.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_user_monthly_budget_eur", aiRequest.PresupuestoMensualUsuarioEur.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_total_budget_eur", aiRequest.PresupuestoTotalEur.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_budget_warning_percent", aiRequest.PorcentajeAvisoPresupuesto.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_input_cost_per_1m_tokens_eur", aiRequest.InputCostPerMillionTokensEur.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_output_cost_per_1m_tokens_eur", aiRequest.OutputCostPerMillionTokensEur.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_max_input_tokens", aiRequest.MaxInputTokens.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_max_output_tokens", aiRequest.MaxOutputTokens.ToString(CultureInfo.InvariantCulture), userId, now);
        Upsert(config, "ai_max_context_rows", aiRequest.MaxContextRows.ToString(CultureInfo.InvariantCulture), userId, now);

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
            var safeTargetForLog = string.IsNullOrEmpty(target)
                ? target
                : new string(target
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Where(c => !char.IsControl(c))
                    .ToArray());

            _logger.LogError(ex, "Fallo al enviar email de prueba SMTP a {Target}", safeTargetForLog);
            return BadRequest(new { error = "No se pudo enviar el correo de prueba. Revisa la configuracion SMTP o avisa al administrador." });
        }
    }

    private async Task<Dictionary<string, string>> LoadConfigMapAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Configuraciones
            .ToDictionaryAsync(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }

    private void Upsert(
        IReadOnlyCollection<AtlasBalance.API.Models.Configuracion> existing,
        string key,
        string value,
        Guid? userId,
        DateTime now)
    {
        var item = existing.FirstOrDefault(x => x.Clave.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            _dbContext.Configuraciones.Add(new AtlasBalance.API.Models.Configuracion
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

    private static decimal ParseDecimal(string value, decimal fallback)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ParseBool(string value, bool fallback = false)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static IaConfigResponse BuildIaConfigResponse(
        IReadOnlyDictionary<string, string> config,
        bool usuarioPuedeUsarIa)
    {
        var provider = AiConfiguration.NormalizeProvider(GetValue(config, "ai_provider", "OPENROUTER"));
        var model = AiConfiguration.NormalizeStoredModel(provider, GetValue(config, "ai_model"));
        var enabled = ParseBool(GetValue(config, "ai_enabled"), fallback: false);
        var hasOpenRouterKey = !string.IsNullOrWhiteSpace(GetValue(config, "openrouter_api_key"));
        var hasOpenAiKey = !string.IsNullOrWhiteSpace(GetValue(config, "openai_api_key"));
        var currentMonthKey = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var storedMonthKey = GetValue(config, "ai_usage_month_key");
        var openRouterConfigured = provider == "OPENROUTER" &&
                                   hasOpenRouterKey &&
                                   !string.IsNullOrWhiteSpace(model) &&
                                   AiConfiguration.IsAllowedOpenRouterModel(model);
        var openAiConfigured = provider == "OPENAI" &&
                               hasOpenAiKey &&
                               !string.IsNullOrWhiteSpace(model) &&
                               AiConfiguration.IsAllowedOpenAiModel(model);

        return new IaConfigResponse
        {
            Provider = provider,
            Model = model,
            Habilitada = enabled,
            UsuarioPuedeUsar = usuarioPuedeUsarIa,
            OpenRouterApiKeyConfigurada = hasOpenRouterKey,
            OpenAiApiKeyConfigurada = hasOpenAiKey,
            Configurada = enabled && usuarioPuedeUsarIa && (openRouterConfigured || openAiConfigured),
            MensajeEstado = BuildIaStatusMessage(enabled, usuarioPuedeUsarIa, provider, hasOpenRouterKey, hasOpenAiKey, model),
            RequestsPorMinuto = Math.Max(0, ParseInt(GetValue(config, "ai_requests_per_minute"), AiConfigurationDefaults.RequestsPerMinute)),
            RequestsPorHora = Math.Max(0, ParseInt(GetValue(config, "ai_requests_per_hour"), AiConfigurationDefaults.RequestsPerHour)),
            RequestsPorDia = Math.Max(0, ParseInt(GetValue(config, "ai_requests_per_day"), AiConfigurationDefaults.RequestsPerDay)),
            RequestsGlobalesPorDia = Math.Max(0, ParseInt(GetValue(config, "ai_global_requests_per_day"), AiConfigurationDefaults.GlobalRequestsPerDay)),
            PresupuestoMensualEur = Math.Max(0, ParseDecimal(GetValue(config, "ai_monthly_budget_eur"), 0m)),
            PresupuestoMensualUsuarioEur = Math.Max(0, ParseDecimal(GetValue(config, "ai_user_monthly_budget_eur"), 0m)),
            PresupuestoTotalEur = Math.Max(0, ParseDecimal(GetValue(config, "ai_total_budget_eur"), 0m)),
            CosteMesEstimadoEur = storedMonthKey == currentMonthKey ? Math.Max(0, ParseDecimal(GetValue(config, "ai_usage_month_cost_eur"), 0m)) : 0m,
            CosteTotalEstimadoEur = Math.Max(0, ParseDecimal(GetValue(config, "ai_usage_total_cost_eur"), 0m)),
            PorcentajeAvisoPresupuesto = Math.Clamp(ParseInt(GetValue(config, "ai_budget_warning_percent"), AiConfigurationDefaults.BudgetWarningPercent), 1, 100),
            InputCostPerMillionTokensEur = Math.Max(0, ParseDecimal(GetValue(config, "ai_input_cost_per_1m_tokens_eur"), 0m)),
            OutputCostPerMillionTokensEur = Math.Max(0, ParseDecimal(GetValue(config, "ai_output_cost_per_1m_tokens_eur"), 0m)),
            MaxInputTokens = Math.Clamp(ParseInt(GetValue(config, "ai_max_input_tokens"), AiConfigurationDefaults.MaxInputTokens), 1000, 50000),
            MaxOutputTokens = Math.Clamp(ParseInt(GetValue(config, "ai_max_output_tokens"), AiConfigurationDefaults.MaxOutputTokens), 64, 4000),
            MaxContextRows = Math.Clamp(ParseInt(GetValue(config, "ai_max_context_rows"), AiConfigurationDefaults.MaxContextRows), 0, 500)
        };
    }

    private static string BuildIaStatusMessage(bool enabled, bool userCanUse, string provider, bool hasOpenRouterKey, bool hasOpenAiKey, string model)
    {
        if (!enabled)
        {
            return "La IA esta desactivada globalmente.";
        }

        if (!userCanUse)
        {
            return "Tu usuario no tiene permiso para usar IA.";
        }

        if (!AiConfiguration.IsSupportedProvider(provider))
        {
            return "Proveedor de IA no soportado.";
        }

        if (provider == "OPENROUTER" && !hasOpenRouterKey)
        {
            return "Falta configurar la clave API de OpenRouter.";
        }

        if (provider == "OPENAI" && !hasOpenAiKey)
        {
            return "Falta configurar la clave API de OpenAI.";
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return "Falta seleccionar el modelo de IA.";
        }

        if (!AiConfiguration.IsAllowedModel(provider, model))
        {
            return "El modelo seleccionado no esta permitido.";
        }

        return "IA configurada.";
    }


    private static string? ValidateIaGovernance(UpdateIaConfigRequest request)
    {
        if (request.RequestsPorMinuto < 0 || request.RequestsPorHora < 0 || request.RequestsPorDia < 0 || request.RequestsGlobalesPorDia < 0)
        {
            return "Los limites de requests de IA no pueden ser negativos.";
        }

        if (request.RequestsPorMinuto > 1000 || request.RequestsPorHora > 10000 || request.RequestsPorDia > 100000 || request.RequestsGlobalesPorDia > 100000)
        {
            return "Los limites de requests de IA son demasiado altos.";
        }

        if (request.PresupuestoMensualEur < 0 || request.PresupuestoMensualUsuarioEur < 0 || request.PresupuestoTotalEur < 0)
        {
            return "Los presupuestos de IA no pueden ser negativos.";
        }

        if (request.PorcentajeAvisoPresupuesto < 1 || request.PorcentajeAvisoPresupuesto > 100)
        {
            return "El aviso de presupuesto de IA debe estar entre 1 y 100.";
        }

        if (request.InputCostPerMillionTokensEur < 0 || request.OutputCostPerMillionTokensEur < 0)
        {
            return "Los costes estimados por token no pueden ser negativos.";
        }

        if (request.MaxInputTokens is < 1000 or > 50000)
        {
            return "El limite de tokens de entrada debe estar entre 1000 y 50000.";
        }

        if (request.MaxOutputTokens is < 64 or > 4000)
        {
            return "El limite de tokens de salida debe estar entre 64 y 4000.";
        }

        if (request.MaxContextRows is < 0 or > 500)
        {
            return "El limite de movimientos enviados a IA debe estar entre 0 y 500.";
        }

        return null;
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
            pair => IsSensitiveConfigKey(pair.Key)
                ? (string.IsNullOrEmpty(pair.Value) ? string.Empty : "[REDACTED]")
                : pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveConfigKey(string key)
    {
        var normalized = key.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("api_key", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("credential", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized.Contains("bearer", StringComparison.Ordinal);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
