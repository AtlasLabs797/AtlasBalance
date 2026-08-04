using System.ComponentModel.DataAnnotations;
using AtlasBalance.API.Constants;

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

// V-02.07: la respuesta de configuracion no declara la password SMTP. Antes
// existia la propiedad y el controller la forzaba a string.Empty; bastaba un
// descuido en un refactor para devolverla al navegador. Sin propiedad, ese
// error no compila. El valor sigue viajando en UpdateSmtpConfigRequest.
public sealed class SmtpConfigResponse
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public sealed class GeneralConfigResponse
{
    public string AppBaseUrl { get; set; } = string.Empty;
    public string AppUpdateCheckUrl { get; set; } = string.Empty;
    public bool AppUpdateAutoEnabled { get; set; }
    public int AppUpdateAutoHourUtc { get; set; } = 3;
    public string AppUpdateAutoLastCheckedUtc { get; set; } = string.Empty;
    public string AppUpdateAutoLastStartedUtc { get; set; } = string.Empty;
    public string AppUpdateAutoLastResult { get; set; } = string.Empty;
    public bool MfaRememberDeviceEnabled { get; set; }
    public int MfaRememberDeviceDays { get; set; } = SecurityConfigurationDefaults.MfaRememberDeviceDays;
    public bool RequireMfaForNonAdminUsers { get; set; } = true;
    public string BackupPath { get; set; } = string.Empty;
    public string ExportPath { get; set; } = string.Empty;
}

public sealed class DashboardConfigResponse
{
    public string ColorIngresos { get; set; } = "#43B430";
    public string ColorEgresos { get; set; } = "#FF4757";
    public string ColorSaldo { get; set; } = "#7B7B7B";
}

// V-02.07: misma politica que SmtpConfigResponse. El frontend solo necesita
// saber si hay clave configurada, nunca su valor. Mismo patron que
// IaConfigResponse, que ya solo expone los flags *_configurada.
public sealed class ExchangeRateConfigResponse
{
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
    // V-02.07: sin [Required] a proposito. Dejar el SMTP sin configurar es un
    // estado legitimo (HasNullTextFields solo rechaza null, no cadena vacia);
    // un [Required]/[EmailAddress] aqui bloquearia guardar la config en blanco.
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;
    [Range(1, 65535)]
    public int Port { get; set; } = 587;
    [MaxLength(254)]
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    [MaxLength(254)]
    public string From { get; set; } = string.Empty;
}

public sealed class UpdateGeneralConfigRequest
{
    // V-02.07: solo longitud. El CONTENIDO de estos cuatro ya lo valida
    // ConfiguracionController (esquema de la URL, ruta absoluta, nada de UNC);
    // lo que faltaba era la cota de tamano. 2048 es el limite practico de una URL
    // y 260 el de MAX_PATH en Windows, que es donde corre esto.
    [MaxLength(2048)]
    public string AppBaseUrl { get; set; } = string.Empty;
    [MaxLength(2048)]
    public string AppUpdateCheckUrl { get; set; } = string.Empty;
    public bool AppUpdateAutoEnabled { get; set; }
    [Range(0, 23)]
    public int AppUpdateAutoHourUtc { get; set; } = 3;
    public bool MfaRememberDeviceEnabled { get; set; }
    public bool RequireMfaForNonAdminUsers { get; set; } = true;
    [MaxLength(260)]
    public string BackupPath { get; set; } = string.Empty;
    [MaxLength(260)]
    public string ExportPath { get; set; } = string.Empty;
}

public sealed class UpdateExchangeRateConfigRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class UpdateDashboardConfigRequest
{
    // V-02.07: solo longitud, sin regex de formato hex. El input del frontend es
    // texto libre (ConfiguracionPage.tsx, no es un `type="color"`), asi que puede
    // haber guardado ya un "red", un "rgb(...)" o un hex de 8 digitos. Como el
    // PUT de configuracion es un unico payload con todas las secciones, un solo
    // color heredado que no casara con el regex bloquearia el guardado ENTERO de
    // la configuracion, no solo ese campo. El riesgo real del campo era la
    // longitud sin tope: el valor se pinta en un `style={{ backgroundColor }}`
    // de React, que no ejecuta nada. Si algun dia el input pasa a `type="color"`,
    // entonces si tiene sentido apretar el formato aqui.
    [MaxLength(32)]
    public string ColorIngresos { get; set; } = "#43B430";
    [MaxLength(32)]
    public string ColorEgresos { get; set; } = "#FF4757";
    [MaxLength(32)]
    public string ColorSaldo { get; set; } = "#7B7B7B";
}

public sealed class RevisionConfigResponse
{
    public decimal ComisionesImporteMinimo { get; set; } = 1m;
    public int SaldoBajoCooldownHoras { get; set; } = 24;
}

public sealed class UpdateRevisionConfigRequest
{
    [Range(typeof(decimal), "0", "9999999999.9999", ParseLimitsInInvariantCulture = true)]
    public decimal ComisionesImporteMinimo { get; set; } = 1m;
    // V-02.07: el controller ademas clampa este valor a [1, 720] al persistirlo
    // (Math.Clamp), asi que 721-8760 pasa la validacion del modelo pero se
    // recorta despues; no es una contradiccion, solo un limite mas laxo que el
    // efectivo. Ver deviaciones reportadas por el agente.
    [Range(1, 8760)]
    public int SaldoBajoCooldownHoras { get; set; } = 24;
}

public sealed class SendTestEmailRequest
{
    // V-02.07: sin [EmailAddress] a proposito. El campo es opcional (si viene
    // vacio, el controller usa smtp_from) y EmailAddressAttribute rechaza la
    // cadena vacia, lo que rompería ese fallback.
    [MaxLength(254)]
    public string? To { get; set; }
}
