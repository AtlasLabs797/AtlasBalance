using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// V-02.07 (retencion de PII): ExportacionService escribe .xlsx en disco con el
// nombre del titular en claro y ImportacionService guarda el pegado bruto del
// extracto en ImportacionLote.ContenidoOriginal (truncado a 2KB, pero PII
// igualmente). Ninguno de los dos se purgaba nunca. Este job cubre ambas
// purgas: se agrupan aqui en vez de crear un segundo job porque son dos
// retenciones de PII de bajo volumen que corren en el mismo ciclo diario y no
// justifican un job separado (AGENTS.md 2.2, simplicidad primero).
public sealed class LimpiezaExportacionesJob
{
    public const string ExportacionRetentionDaysKey = "exportacion_retention_days";
    public const int DefaultExportacionRetentionDays = 90;

    public const string ImportacionContenidoRetentionDaysKey = "importacion_contenido_retention_days";
    public const int DefaultImportacionContenidoRetentionDays = 180;

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
        var now = _clock.UtcNow;

        await PurgeExportacionesAsync(now, CancellationToken.None);
        await PurgeContenidoOriginalAsync(now, CancellationToken.None);
    }

    private async Task PurgeExportacionesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var retentionDays = await GetIntConfigValueAsync(ExportacionRetentionDaysKey, DefaultExportacionRetentionDays, cancellationToken);
        var cutoff = now.AddDays(-retentionDays);

        var exportaciones = await _dbContext.Exportaciones
            .Where(e => e.FechaExportacion < cutoff && e.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var deletedFiles = 0;
        foreach (var exportacion in exportaciones)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(exportacion.RutaArchivo) && File.Exists(exportacion.RutaArchivo))
                {
                    File.Delete(exportacion.RutaArchivo);
                    deletedFiles++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo borrar el fichero fisico de la exportacion {ExportacionId}", exportacion.Id);
            }

            exportacion.DeletedAt = now;
            exportacion.DeletedById = null;
        }

        if (exportaciones.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "LimpiezaExportacionesJob purgo {Count} exportaciones anteriores a {CutoffUtc} ({DeletedFiles} ficheros fisicos borrados)",
            exportaciones.Count,
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
