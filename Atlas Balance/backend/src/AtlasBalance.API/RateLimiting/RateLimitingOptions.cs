namespace AtlasBalance.API.RateLimiting;

/// <summary>
/// Opciones del rate limiting global. Se enlazan desde la seccion
/// <c>AtlasBalance:RateLimiting</c> de <c>appsettings.json</c> para poder ajustar
/// los limites por entorno sin redeploy, igual que <c>AtlasBalance:Caching</c>.
/// Documentado en V-02.07 (rate limiting y proteccion contra fuerza bruta).
///
/// Los valores por defecto salen de la medicion del trafico real del frontend:
/// el montaje mas pesado son 7 GET (ConfiguracionPage, CuentasPage) y el peor
/// escenario medido (login -> dashboard) son ~10 peticiones. Los limites dejan
/// mas de 3x de margen sobre ese pico para no cortar uso legitimo.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "AtlasBalance:RateLimiting";

    /// <summary>
    /// Interruptor de emergencia. Con <c>false</c> el pipeline no aplica ningun
    /// limite (el de integracion OpenClaw sigue vivo porque es del middleware).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Ventana comun a todas las politicas.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Endpoints que verifican credenciales, por IP. El checklist pide 5-10.</summary>
    public int AuthPerMinutePerIp { get; set; } = 10;

    /// <summary>Resto de rutas anonimas (hoy solo telemetria), por IP.</summary>
    public int AnonymousPerMinutePerIp { get; set; } = 60;

    /// <summary>GET autenticados, por usuario.</summary>
    public int ReadPerMinutePerUser { get; set; } = 300;

    /// <summary>POST/PUT/PATCH/DELETE autenticados, por usuario.</summary>
    public int WritePerMinutePerUser { get; set; } = 60;

    /// <summary>
    /// Politica nominal para operaciones caras (backups, Drive, exportacion
    /// manual, sincronizacion de tasas, actualizacion del sistema). Se suma a
    /// la de escritura: ambas deben permitir la peticion.
    /// </summary>
    public int ExpensivePerMinutePerUser { get; set; } = 5;

    /// <summary>Fallos de login antes de bloquear la cuenta. Persistido en USUARIOS.</summary>
    public int LoginMaxFailedAttemptsPerAccount { get; set; } = 5;

    /// <summary>Duracion del bloqueo de cuenta, en minutos.</summary>
    public int LoginLockMinutes { get; set; } = 30;

    /// <summary>
    /// Fallos por (IP, email) antes de devolver 429. Queda por debajo de
    /// <see cref="LoginMaxFailedAttemptsPerAccount"/> a proposito: desde una
    /// sola IP el 429 corta antes de que el contador de BD llegue al bloqueo
    /// de cuenta, que pasa a exigir intentos desde origenes distintos.
    /// </summary>
    public int LoginMaxFailuresPerIpAndEmail { get; set; } = 3;

    /// <summary>Fallos por IP contra cualquier cuenta antes de devolver 429.</summary>
    public int LoginMaxFailuresPerIp { get; set; } = 7;

    /// <summary>Ventana de los contadores de fallo de login, en minutos.</summary>
    public int LoginFailureWindowMinutes { get; set; } = 15;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
    public TimeSpan LoginLockDuration => TimeSpan.FromMinutes(LoginLockMinutes);
    public TimeSpan LoginFailureWindow => TimeSpan.FromMinutes(LoginFailureWindowMinutes);
}
