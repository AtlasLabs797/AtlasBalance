namespace AtlasBalance.API.Services;

/// <summary>
/// Umbrales de las reglas de alerta de seguridad. Seccion Security:Alertas.
///
/// Los defaults estan calibrados para esta app: on-premise, LAN, 4-8 usuarios.
/// En un despliegue con cientos de usuarios habria que subirlos o el ruido
/// enterraria las senales de verdad.
/// </summary>
public sealed class SecurityAlertOptions
{
    public const string SectionName = "Security:Alertas";

    /// <summary>Ventana deslizante que evalua cada pasada del job.</summary>
    public int VentanaMinutos { get; set; } = 5;

    /// <summary>
    /// Tiempo que una alerta ya emitida silencia a sus repeticiones. Sin esto,
    /// un ataque sostenido genera un correo cada 5 minutos durante horas.
    /// </summary>
    public int EnfriamientoMinutos { get; set; } = 60;

    /// <summary>Regla 1: fallos de login sobre una misma cuenta en la ventana.</summary>
    public int MaxLoginFallidosPorCuenta { get; set; } = 5;

    /// <summary>Regla 2: cuentas distintas que una misma IP puede tocar en la ventana.</summary>
    public int MaxCuentasPorIp { get; set; } = 10;

    /// <summary>Regla 3: peticiones de una misma sesion en la ventana (acceso secuencial rapido).</summary>
    public int MaxPeticionesSecuenciales { get; set; } = 300;

    /// <summary>Regla 4: dias de historico que se miran para decidir si una IP es nueva.</summary>
    public int DiasHistoricoIpConocida { get; set; } = 90;

    /// <summary>Regla 5: reinicios/cambios de password de una cuenta en la ventana.</summary>
    public int MaxPasswordResets { get; set; } = 3;

    /// <summary>
    /// Regla 6: minimo absoluto de errores 401/403 en la ventana antes de
    /// comparar con la linea base. Evita que 2 errores frente a una media de 0
    /// disparen una alerta.
    /// </summary>
    public int MinErroresAuthParaAlertar { get; set; } = 20;

    /// <summary>Regla 6: cuantas veces la linea base hay que superar para alertar.</summary>
    public double FactorSobreLineaBase { get; set; } = 3.0;

    /// <summary>Regla 6: ventanas anteriores que forman la linea base.</summary>
    public int VentanasLineaBase { get; set; } = 12;

    /// <summary>
    /// Destinatarios de los correos de alerta. Si esta vacio, se usan los emails
    /// de los usuarios con rol ADMIN activos.
    /// </summary>
    public string[] DestinatariosEmail { get; set; } = Array.Empty<string>();

    /// <summary>Webhook de Slack. Vacio = canal desactivado. Es un secreto.</summary>
    public string? SlackWebhookUrl { get; set; }

    /// <summary>Permite apagar la evaluacion entera sin desregistrar el job.</summary>
    public bool Habilitado { get; set; } = true;
}
