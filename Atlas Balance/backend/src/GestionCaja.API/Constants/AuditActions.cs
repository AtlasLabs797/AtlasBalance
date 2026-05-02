namespace GestionCaja.API.Constants;

public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string LoginMfaRequired = "LOGIN_MFA_REQUIRED";
    public const string MfaVerified = "MFA_VERIFIED";
    public const string MfaEnabled = "MFA_ENABLED";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string RefreshTokenReuseDetected = "REFRESH_TOKEN_REUSE_DETECTED";
    public const string CreateUsuario = "CREATE_USUARIO";
    public const string UpdateUsuario = "UPDATE_USUARIO";
    public const string DeleteUsuario = "DELETE_USUARIO";
    public const string RestoreUsuario = "RESTORE_USUARIO";
    public const string CambioPermisos = "CAMBIO_PERMISOS";
    public const string ConfigAlerta = "CONFIG_ALERTA";
    public const string AlertaSaldoDisparada = "ALERTA_SALDO_DISPARADA";
    public const string PlazoFijoProximoVencer = "PLAZO_FIJO_PROXIMO_VENCER";
    public const string PlazoFijoVencido = "PLAZO_FIJO_VENCIDO";
    public const string PlazoFijoRenovado = "PLAZO_FIJO_RENOVADO";
    public const string BackupGenerado = "BACKUP_GENERADO";
    public const string BackupRetencionAutomatica = "BACKUP_RETENCION_AUTOMATICA";
    public const string ExportacionGenerada = "EXPORTACION_GENERADA";
    public const string UpdateConfiguracion = "UPDATE_CONFIGURACION";
    public const string TestSmtp = "TEST_SMTP";
    public const string CreateIntegrationToken = "CREATE_INTEGRATION_TOKEN";
    public const string UpdateIntegrationToken = "UPDATE_INTEGRATION_TOKEN";
    public const string RevokeIntegrationToken = "REVOKE_INTEGRATION_TOKEN";
    public const string DeleteIntegrationToken = "DELETE_INTEGRATION_TOKEN";
}
