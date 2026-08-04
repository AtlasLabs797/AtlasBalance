namespace AtlasBalance.API.Constants;

public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string LoginMfaRequired = "LOGIN_MFA_REQUIRED";
    public const string MfaVerified = "MFA_VERIFIED";
    public const string MfaEnabled = "MFA_ENABLED";
    public const string MfaRevoked = "MFA_REVOKED";
    public const string MfaPolicyUpdated = "MFA_POLICY_UPDATED";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string RefreshTokenReuseDetected = "REFRESH_TOKEN_REUSE_DETECTED";
    public const string SessionIpChanged = "SESSION_IP_CHANGED";
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
    public const string BackupConfigUpdated = "BACKUP_CONFIG_UPDATED";
    public const string BackupCloudLinked = "BACKUP_CLOUD_LINKED";
    public const string BackupCloudDisconnected = "BACKUP_CLOUD_DISCONNECTED";
    public const string BackupCloudUpload = "BACKUP_CLOUD_UPLOAD";
    public const string BackupCloudImport = "BACKUP_CLOUD_IMPORT";
    public const string ExportacionGenerada = "EXPORTACION_GENERADA";
    public const string ExportacionBloqueada = "EXPORTACION_BLOQUEADA";
    public const string UpdateConfiguracion = "UPDATE_CONFIGURACION";
    public const string TestSmtp = "TEST_SMTP";
    public const string IaConsulta = "IA_CONSULTA";
    public const string IaConsultaBloqueada = "IA_CONSULTA_BLOQUEADA";
    public const string IaConsultaError = "IA_CONSULTA_ERROR";
    public const string IaPresupuestoAviso = "IA_PRESUPUESTO_AVISO";
    public const string CreateIntegrationToken = "CREATE_INTEGRATION_TOKEN";
    public const string UpdateIntegrationToken = "UPDATE_INTEGRATION_TOKEN";
    public const string RevokeIntegrationToken = "REVOKE_INTEGRATION_TOKEN";
    public const string DeleteIntegrationToken = "DELETE_INTEGRATION_TOKEN";

    // V-02.07 (observabilidad de seguridad)

    /// <summary>403: el usuario esta autenticado pero no tiene permiso sobre el recurso.</summary>
    public const string AuthzDenied = "AUTHZ_DENIED";

    /// <summary>401 sobre un endpoint protegido: token ausente, caducado o invalido.</summary>
    public const string AuthnDenied = "AUTHN_DENIED";

    /// <summary>Lectura masiva: la peticion devolvio mas filas que el umbral configurado.</summary>
    public const string AccesoBulk = "ACCESO_BULK";

    /// <summary>Se lanzo la instalacion de una actualizacion desde /api/sistema/actualizar.</summary>
    public const string SistemaActualizacionIniciada = "SISTEMA_ACTUALIZACION_INICIADA";

    /// <summary>Una regla de SecurityAlertService se disparo y se notifico.</summary>
    public const string AlertaSeguridadDisparada = "ALERTA_SEGURIDAD_DISPARADA";

    /// <summary>La verificacion de integridad de AUDITORIAS encontro firmas invalidas o huecos.</summary>
    public const string AuditoriaIntegridadFallida = "AUDITORIA_INTEGRIDAD_FALLIDA";
}
