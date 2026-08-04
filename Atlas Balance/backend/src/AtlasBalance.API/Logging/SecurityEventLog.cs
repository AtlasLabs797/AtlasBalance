using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;

namespace AtlasBalance.API.Logging;

public interface ISecurityEventLog
{
    /// <summary>
    /// Escribe la fila en el espejo externo si su tipo de accion es relevante
    /// para seguridad. No lanza nunca: un fallo del espejo no debe tumbar la
    /// operacion de negocio que lo origino.
    /// </summary>
    void RegistrarSiEsRelevante(Auditoria auditoria);
}

/// <summary>
/// Espejo de eventos de seguridad FUERA de PostgreSQL.
///
/// Razon de existir: AUDITORIAS es append-only y sus filas van firmadas, asi que
/// modificar o insertar es detectable. Lo que NO es detectable con solo la BD es
/// borrar la cola de la tabla (las N filas mas recientes no dejan hueco en la
/// secuencia). Quien compromete la aplicacion tiene el connection string, asi
/// que necesitamos una copia en un dominio de confianza distinto:
///
/// 1. Fichero dedicado (Serilog, categoria AtlasBalance.Security). El instalador
///    aplica ACL de solo-anexar sobre la carpeta para la cuenta del servicio.
/// 2. Windows Event Log. Borrar entradas exige el privilegio "Manage auditing
///    and security log", que la cuenta del servicio no tiene.
///
/// Ninguna de las dos es infalible contra un atacante con SYSTEM. El objetivo es
/// que comprometer la aplicacion no baste para borrar el rastro.
/// </summary>
public sealed class SecurityEventLog : ISecurityEventLog
{
    /// <summary>Nombre del Event Log de Windows donde se escribe.</summary>
    public const string EventLogName = "Application";

    /// <summary>Origen que el instalador debe registrar (requiere admin).</summary>
    public const string EventLogSource = "AtlasBalance";

    // Solo eventos de seguridad. La auditoria automatica de entidades
    // (entity_update_Extracto y companyia) se queda en la BD: son miles de filas
    // al dia y saturarian el Event Log sin aportar valor de deteccion.
    private static readonly HashSet<string> AccionesRelevantes = new(StringComparer.OrdinalIgnoreCase)
    {
        AuditActions.Login,
        AuditActions.Logout,
        AuditActions.LoginFailed,
        AuditActions.LoginMfaRequired,
        AuditActions.MfaVerified,
        AuditActions.MfaEnabled,
        AuditActions.MfaRevoked,
        AuditActions.MfaPolicyUpdated,
        AuditActions.AccountLocked,
        AuditActions.PasswordChanged,
        AuditActions.PasswordReset,
        AuditActions.RefreshTokenReuseDetected,
        AuditActions.SessionIpChanged,
        AuditActions.CreateUsuario,
        AuditActions.UpdateUsuario,
        AuditActions.DeleteUsuario,
        AuditActions.RestoreUsuario,
        AuditActions.CambioPermisos,
        AuditActions.AuthzDenied,
        AuditActions.AuthnDenied,
        AuditActions.AccesoBulk,
        AuditActions.ExportacionGenerada,
        AuditActions.ExportacionBloqueada,
        AuditActions.UpdateConfiguracion,
        AuditActions.SistemaActualizacionIniciada,
        AuditActions.CreateIntegrationToken,
        AuditActions.UpdateIntegrationToken,
        AuditActions.RevokeIntegrationToken,
        AuditActions.DeleteIntegrationToken,
        AuditActions.AlertaSeguridadDisparada,
        AuditActions.AuditoriaIntegridadFallida,
    };

    // Acciones que merecen severidad de error en vez de aviso: apuntan a
    // compromiso en curso, no a actividad normal.
    private static readonly HashSet<string> AccionesCriticas = new(StringComparer.OrdinalIgnoreCase)
    {
        AuditActions.RefreshTokenReuseDetected,
        AuditActions.AuditoriaIntegridadFallida,
        AuditActions.AlertaSeguridadDisparada,
    };

    private readonly ILogger _logger;
    private readonly bool _eventLogHabilitado;
    private static bool _eventLogSourceAvisado;

    public SecurityEventLog(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        // Categoria propia para que Serilog pueda enrutarla a su fichero
        // dedicado sin arrastrar el resto del log de aplicacion.
        _logger = loggerFactory.CreateLogger("AtlasBalance.Security");
        _eventLogHabilitado = configuration.GetValue("Security:MirrorToWindowsEventLog", true);
    }

    public void RegistrarSiEsRelevante(Auditoria auditoria)
    {
        if (auditoria is null || !AccionesRelevantes.Contains(auditoria.TipoAccion))
        {
            return;
        }

        var critico = AccionesCriticas.Contains(auditoria.TipoAccion);

        // Los campos van por plantilla estructurada (no interpolados) para que
        // Serilog los emita como propiedades y no haya inyeccion de log.
        // DetallesJson NO se incluye: puede llevar valores de negocio y ya vive
        // en la BD; aqui solo interesa el quien/que/cuando/donde.
#pragma warning disable CA2254
        var plantilla = "SECURITY {TipoAccion} usuario={UsuarioId} entidad={EntidadTipo}/{EntidadId} ip={Ip} origen={Origen} sesion={SessionId} ua={UserAgent} ts={TimestampUtc} id={AuditoriaId}";
        var args = new object?[]
        {
            LogScrubber.Scrub(auditoria.TipoAccion),
            auditoria.UsuarioId,
            LogScrubber.Scrub(auditoria.EntidadTipo),
            auditoria.EntidadId,
            AuditSigner.NormalizarIp(auditoria.IpAddress),
            LogScrubber.Scrub(auditoria.Origen),
            LogScrubber.Scrub(auditoria.SessionId),
            LogScrubber.RedactPii(auditoria.UserAgent),
            auditoria.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            auditoria.Id
        };

        if (critico)
        {
            _logger.LogError(plantilla, args);
        }
        else
        {
            _logger.LogWarning(plantilla, args);
        }
#pragma warning restore CA2254

        EscribirEnEventLog(auditoria, critico);
    }

    private void EscribirEnEventLog(Auditoria auditoria, bool critico)
    {
        if (!_eventLogHabilitado || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // El origen lo registra el instalador (New-EventLog); crearlo aqui
            // exigiria admin en runtime, que el servicio no deberia tener.
            if (!EventLog.SourceExists(EventLogSource))
            {
                if (!_eventLogSourceAvisado)
                {
                    _eventLogSourceAvisado = true;
                    _logger.LogWarning(
                        "El origen de Event Log {Source} no esta registrado: el espejo de eventos de seguridad fuera de la BD queda solo en fichero. Registralo con New-EventLog (requiere admin).",
                        EventLogSource);
                }

                return;
            }

            var mensaje = string.Create(CultureInfo.InvariantCulture, $"""
                Atlas Balance - evento de seguridad
                Accion:    {auditoria.TipoAccion}
                Usuario:   {auditoria.UsuarioId}
                Entidad:   {auditoria.EntidadTipo}/{auditoria.EntidadId}
                IP:        {AuditSigner.NormalizarIp(auditoria.IpAddress)}
                Origen:    {auditoria.Origen}
                Sesion:    {auditoria.SessionId}
                UserAgent: {auditoria.UserAgent}
                UTC:       {auditoria.Timestamp:O}
                Auditoria: {auditoria.Id}
                """);

            EventLog.WriteEntry(
                EventLogSource,
                mensaje,
                critico ? EventLogEntryType.Error : EventLogEntryType.Warning);
        }
        catch (Exception ex)
        {
            // Nunca romper la operacion de negocio por el espejo. El evento ya
            // esta en la BD y en el fichero de seguridad.
            _logger.LogWarning(ex, "No se pudo escribir el evento de seguridad en el Windows Event Log");
        }
    }

    /// <summary>
    /// Serializa detalle adicional para eventos que no vienen de una fila de
    /// AUDITORIAS (integridad, alertas). Se expone para reutilizar el mismo
    /// formato de DetallesJson en toda la capa de seguridad.
    /// </summary>
    public static string Detalles(object payload) => JsonSerializer.Serialize(payload);
}
