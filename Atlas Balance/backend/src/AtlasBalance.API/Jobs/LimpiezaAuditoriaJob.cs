using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AtlasBalance.API.Jobs;

// Excepcion intencional a la regla de soft delete universal de AGENTS.md: las tablas
// de auditoria existen para trazar acciones pasadas, no para ser restauradas, y
// conservarlas indefinidamente via soft delete las haria crecer sin limite. Se purgan
// con hard delete tras la retencion configurada como politica deliberada.
public sealed class LimpiezaAuditoriaJob
{
    /// <summary>
    /// Retencion por defecto de AUDITORIAS.
    ///
    /// V-02.07: sube de 28 a 365 dias. 28 dias era incoherente con lo que esta
    /// tabla es en una app de tesoreria multi-banco: el rastro de quien movio
    /// dinero, cambio permisos o exporto datos tiene que sobrevivir a un cierre
    /// trimestral y a una investigacion que empieza semanas despues del hecho.
    /// El coste es despreciable (filas de texto, no adjuntos).
    ///
    /// Configurable con Auditoria:RetentionDays. El suelo real lo impone la BD
    /// (trigger append-only, 90 dias), no esta constante.
    /// </summary>
    public const int RetentionDays = 365;

    /// <summary>
    /// Retencion de AUDITORIA_INTEGRACIONES. Se mantiene corta a proposito: es un
    /// log de peticiones HTTP de la integracion, de volumen alto y valor forense
    /// bajo comparado con AUDITORIAS. Suelo en BD: 7 dias.
    /// </summary>
    public const int IntegrationRetentionDays = 28;

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LimpiezaAuditoriaJob> _logger;

    // Ya no hace falta IClock: el corte lo calcula PostgreSQL con now() dentro de
    // las funciones de purga, que es donde ademas vive el suelo de retencion.
    public LimpiezaAuditoriaJob(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<LimpiezaAuditoriaJob> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var retentionDays = _configuration.GetValue("Auditoria:RetentionDays", RetentionDays);
        var integrationRetentionDays = _configuration.GetValue(
            "Auditoria:IntegrationRetentionDays",
            IntegrationRetentionDays);

        // V-02.07: ambas tablas de auditoria son append-only. Un DELETE directo
        // no falla: RLS lo filtra y borra cero en silencio, que es exactamente
        // como este job estuvo sin purgar nada durante meses. La purga va por las
        // unicas vias sancionadas, que ademas validan el suelo de retencion
        // dentro de la propia base de datos.
        var auditoriasDeleted = await PurgarAsync(
            "atlas_security.purgar_auditorias",
            retentionDays,
            "Auditoria:RetentionDays");

        var integrationAuditsDeleted = await PurgarAsync(
            "atlas_security.purgar_auditorias_integracion",
            integrationRetentionDays,
            "Auditoria:IntegrationRetentionDays");

        if (auditoriasDeleted is null && integrationAuditsDeleted is null)
        {
            return;
        }

        _logger.LogInformation(
            "LimpiezaAuditoriaJob elimino {AuditoriasDeleted} auditorias (retencion {RetentionDays} dias) y {IntegrationAuditsDeleted} auditorias de integracion (retencion {IntegrationRetentionDays} dias)",
            auditoriasDeleted,
            retentionDays,
            integrationAuditsDeleted,
            integrationRetentionDays);
    }

    /// <summary>
    /// Devuelve las filas borradas, o null si la retencion configurada esta por
    /// debajo del suelo de la base de datos. En ese caso no se purga nada a
    /// proposito: preferimos que la tabla crezca antes que perder rastro por una
    /// configuracion mal puesta.
    /// </summary>
    private async Task<long?> PurgarAsync(string funcion, int retentionDays, string claveConfiguracion)
    {
        var retentionParam = new NpgsqlParameter("retencion_dias", NpgsqlDbType.Integer) { Value = retentionDays };
        try
        {
            return await _dbContext.Database
                .SqlQueryRaw<long>($"SELECT {funcion}(@retencion_dias) AS \"Value\"", retentionParam)
                .SingleAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            _logger.LogError(
                ex,
                "{ClaveConfiguracion}={RetentionDays} esta por debajo del suelo que impone la base de datos. No se purgo {Funcion}.",
                claveConfiguracion,
                retentionDays,
                funcion);
            return null;
        }
    }
}
