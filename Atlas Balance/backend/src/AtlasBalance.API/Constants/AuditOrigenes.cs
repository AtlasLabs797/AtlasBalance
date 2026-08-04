namespace AtlasBalance.API.Constants;

// V-02.07: canal por el que entro la accion auditada. La distincion es real en
// este codebase, no una etiqueta decorativa: la UI autentica con JWT en cookie
// httpOnly + header X-CSRF-Token, y la integracion OpenClaw con bearer token en
// /api/integration/openclaw (ver Program.cs, JwtBearerEvents.OnMessageReceived).
public static class AuditOrigenes
{
    /// <summary>Navegador: JWT en cookie httpOnly, protegido con CSRF.</summary>
    public const string Ui = "UI";

    /// <summary>Integracion externa: bearer token sobre /api/integration/*.</summary>
    public const string Api = "API";

    /// <summary>Job de Hangfire o proceso de fondo, sin HttpContext.</summary>
    public const string Job = "JOB";

    /// <summary>Arranque, seed o migracion.</summary>
    public const string Sistema = "SISTEMA";

    /// <summary>No se pudo determinar (peticion anonima sin cookie ni bearer).</summary>
    public const string Desconocido = "DESCONOCIDO";
}
