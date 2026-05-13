namespace AtlasBalance.API.DTOs;

public sealed class ConfiguracionSistemaResponse
{
    public SmtpConfigResponse Smtp { get; set; } = new();
    public GeneralConfigResponse General { get; set; } = new();
    public ExchangeRateConfigResponse Exchange { get; set; } = new();
    public DashboardConfigResponse Dashboard { get; set; } = new();
    public RevisionConfigResponse Revision { get; set; } = new();
    public IaConfigResponse Ia { get; set; } = new();
}

public sealed class SmtpConfigResponse
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public sealed class GeneralConfigResponse
{
    public string AppBaseUrl { get; set; } = string.Empty;
    public string AppUpdateCheckUrl { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string ExportPath { get; set; } = string.Empty;
}

public sealed class DashboardConfigResponse
{
    public string ColorIngresos { get; set; } = "#43B430";
    public string ColorEgresos { get; set; } = "#FF4757";
    public string ColorSaldo { get; set; } = "#7B7B7B";
}

public sealed class ExchangeRateConfigResponse
{
    public string ApiKey { get; set; } = string.Empty;
    public bool ApiKeyConfigurada { get; set; }
}

public sealed class UpdateConfiguracionRequest
{
    public UpdateSmtpConfigRequest Smtp { get; set; } = new();
    public UpdateGeneralConfigRequest General { get; set; } = new();
    public UpdateExchangeRateConfigRequest Exchange { get; set; } = new();
    public UpdateDashboardConfigRequest Dashboard { get; set; } = new();
    public UpdateRevisionConfigRequest Revision { get; set; } = new();
    public UpdateIaConfigRequest Ia { get; set; } = new();
}

public sealed class UpdateSmtpConfigRequest
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public sealed class UpdateGeneralConfigRequest
{
    public string AppBaseUrl { get; set; } = string.Empty;
    public string AppUpdateCheckUrl { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string ExportPath { get; set; } = string.Empty;
}

public sealed class UpdateExchangeRateConfigRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class UpdateDashboardConfigRequest
{
    public string ColorIngresos { get; set; } = "#43B430";
    public string ColorEgresos { get; set; } = "#FF4757";
    public string ColorSaldo { get; set; } = "#7B7B7B";
}

public sealed class RevisionConfigResponse
{
    public decimal ComisionesImporteMinimo { get; set; } = 1m;
    public int SaldoBajoCooldownHoras { get; set; } = 24;
}

public sealed class UpdateRevisionConfigRequest
{
    public decimal ComisionesImporteMinimo { get; set; } = 1m;
    public int SaldoBajoCooldownHoras { get; set; } = 24;
}

public sealed class SendTestEmailRequest
{
    public string? To { get; set; }
}
