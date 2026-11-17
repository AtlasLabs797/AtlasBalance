using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// V-02.07 (retencion de PII): ExportacionService escribe .xlsx en disco con el
// nombre del titular en claro y ImportacionService guarda el pegado bruto del
// extracto en ImportacionLote.ContenidoOriginal (truncado a 2KB, pero PII
// igualmente). Ninguno de los dos se purgaba nunca. Este job cubre ambas
// purgas: se agrupan aqui en vez de crear un segundo job porque son dos
// retenciones de PII de bajo volumen que corren en el mismo ciclo diario y no
// justifican un job separado (AGENTS.md 2.2, simplicidad primero).
//
// V-03.02 (J-1): bloqueo concurrente y reintento automatico.
[DisableConcurrentExecution(timeoutInSeconds: 900)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class LimpiezaExportacionesJob
{
    public const string ExportacionRetentionDaysKey = "exportacion_retention_days";
    public const int DefaultExportacionRetentionDays = 90;

    public const string ImportacionContenidoRetentionDaysKey = "importacion_contenido_retention_days";
    public const int DefaultImportacionContenidoRetentionDays = 180;

    // V-03.02 (J-4): raiz de exportaciones. Mismo valor por defecto que usa
    // ExportacionService.ResolveSafeDirectory.
    public const string ExportPathConfigKey = "export_path";
    public const string DefaultExportPath = @"C:\atlas-balance\exports";

    private const int MinRetentionDays = 1;
    private const int MaxRetentionDays = 3650;

    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<LimpiezaExportacionesJob> _logger;

    public LimpiezaExportacionesJob(AppDbContext dbContext, IClock clock, ILogger<LimpiezaExportacionesJob> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var now = _clock.UtcNow;

        await PurgeExportacionesAsync(now, CancellationToken.None);
        await PurgeContenidoOriginalAsync(now, CancellationToken.None);
    }

    private async Task PurgeExportacionesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var retentionDays = await GetIntConfigValueAsync(ExportacionRetentionDaysKey, DefaultExportacionRetentionDays, cancellationToken);
        var cutoff = now.AddDays(-retentionDays);
        var exportRoot = await GetExportRootAsync(cancellationToken);

        var exportaciones = await _dbContext.Exportaciones
            .Where(e => e.FechaExportacion < cutoff && e.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var deletedFiles = 0;
        var purgedRows = 0;
        foreach (var exportacion in exportaciones)
        {
            var ruta = exportacion.RutaArchivo;

            // V-03.02 (J-4): sin ruta no hay fichero en disco, la fila se puede
            // marcar como borrada.
            if (string.IsNullOrWhiteSpace(ruta))
            {
                exportacion.DeletedAt = now;
                exportacion.DeletedById = null;
                purgedRows++;
                continue;
            }

            // V-03.02 (J-4): antes de borrar hay que comprobar que el fichero
            // esta bajo la raiz de exportaciones configurada. Una ruta fuera de
            // esa raiz no se borra (puede ser de otra instalacion o estar
            // manipulada) y la fila se deja viva para que quede constancia.
            if (!IsAllowedExportFile(ruta, exportRoot))
            {
                _logger.LogWarning(
                    "No se purgo la exportacion {ExportacionId}: ruta fuera de la raiz permitida. Ruta: {Ruta}",
                    exportacion.Id,
                    ruta);
                continue;
            }

            var borradoCorrectamente = true;
            try
            {
                if (File.Exists(ruta))
                {
                    File.Delete(ruta);
                    deletedFiles++;
                }
            }
            catch (Exception ex)
            {
                // V-03.02 (J-4): si el fichero no se puede borrar, la fila NO se
                // marca como borrada. Antes se marcaba siempre y el fichero con
                // datos personales se quedaba en disco sin que nadie lo supiera.
                borradoCorrectamente = false;
                _logger.LogWarning(ex, "No se pudo borrar el fichero fisico de la exportacion {ExportacionId}", exportacion.Id);
            }

            if (!borradoCorrectamente)
            {
                continue;
            }

            exportacion.DeletedAt = now;
            exportacion.DeletedById = null;
            purgedRows++;
        }

        if (exportaciones.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "LimpiezaExportacionesJob purgo {Count} exportaciones anteriores a {CutoffUtc} ({DeletedFiles} ficheros fisicos borrados)",
            purgedRows,
            cutoff,
            deletedFiles);
    }

    private async Task PurgeContenidoOriginalAsync(DateTime now, CancellationToken cancellationToken)
    {
        var retentionDays = await GetIntConfigValueAsync(ImportacionContenidoRetentionDaysKey, DefaultImportacionContenidoRetentionDays, cancellationToken);
        var cutoff = now.AddDays(-retentionDays);

        var lotes = await _dbContext.ImportacionLotes
            .Where(l => l.FechaCreacion < cutoff && l.ContenidoOriginal != "")
            .ToListAsync(cancellationToken);

        foreach (var lote in lotes)
        {
            lote.ContenidoOriginal = string.Empty;
        }

        if (lotes.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "LimpiezaExportacionesJob vacio ContenidoOriginal de {Count} lotes de importacion anteriores a {CutoffUtc}",
            lotes.Count,
            cutoff);
    }

    private async Task<string> GetExportRootAsync(CancellationToken cancellationToken)
    {
        var raw = await _dbContext.Configuraciones
            .Where(c => c.Clave == ExportPathConfigKey)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(raw) ? DefaultExportPath : raw.Trim();
    }

    // V-03.02 (J-4): comprueba que el fichero esta bajo la raiz de
    // exportaciones. Mismo criterio que BackupService.IsAllowedBackupFile:
    // ruta completa con Path.GetFullPath y comparacion contra la raiz con el
    // separador final, para que un directorio con prefijo similar
    // ("exports-mal", "exports2") no se trate como hijo de la raiz.
    internal static bool IsAllowedExportFile(string filePath, string exportRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(exportRoot))
        {
            return false;
        }

        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullRoot = Path.GetFullPath(exportRoot);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            return fullFilePath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task<int> GetIntConfigValueAsync(string key, int fallback, CancellationToken cancellationToken)
    {
        var raw = await _dbContext.Configuraciones
            .Where(c => c.Clave == key)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, MinRetentionDays, MaxRetentionDays)
            : fallback;
    }
}
