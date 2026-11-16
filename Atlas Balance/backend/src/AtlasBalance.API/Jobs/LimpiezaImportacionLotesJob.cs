using System.Globalization;
using AtlasBalance.API.Data;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// V-03.02 (retencion de datos personales): IMPORTACION_LOTE_FILAS.datos_json
// guarda las filas del extracto pegado (concepto, importes...) y nunca se
// purgaba; LimpiezaExportacionesJob ya vacia IMPORTACION_LOTES.contenido_original
// pero las filas parseadas conservaban el mismo dato.
//
// Comportamiento:
//  - Retencion: la misma clave que contenido_original
//    (importacion_contenido_retention_days, default 180 d, clamp 1..3650).
//  - Alcance: lotes con fecha_creacion anterior al corte y en estado CERRADO
//    (confirmado, revertido, error). Los lotes abiertos (validado,
//    validado_con_errores) no se tocan: ConfirmarLoteAsync reconstruye la
//    importacion desde datos_json, asi que vaciarlo dejaria confirmar filas en
//    blanco.
//  - Accion: UPDATE datos_json = '{}' (el valor por defecto). NO se borran
//    filas: conservan indice, estado, valida, errores_json/advertencias_json y
//    fingerprint, por lo que el historial del lote y sus contadores siguen
//    cuadrando y las vistas no se rompen (solo muestran datos vacios), igual
//    que con contenido_original vacio.
//  - Las filas soft-deleted no se tocan: la policy RLS importacion_lote_filas_update
//    exige deleted_at IS NULL tambien para el contexto system.
public sealed class LimpiezaImportacionLotesJob
{
    private const int MinRetentionDays = 1;
    private const int MaxRetentionDays = 3650;
    private const string EmptyJson = "{}";

    private static readonly string[] EstadosAbiertos = ["validado", "validado_con_errores"];

    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<LimpiezaImportacionLotesJob> _logger;

    public LimpiezaImportacionLotesJob(AppDbContext dbContext, IClock clock, ILogger<LimpiezaImportacionLotesJob> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var cutoff = _clock.UtcNow.AddDays(-await GetRetentionDaysAsync());

        var loteIds = _dbContext.ImportacionLotes
            .Where(l => l.FechaCreacion < cutoff && !EstadosAbiertos.Contains(l.Estado))
            .Select(l => l.Id);

        var rows = await _dbContext.ImportacionLoteFilas
            .Where(f => f.DatosJson != EmptyJson && loteIds.Contains(f.LoteId))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.DatosJson, EmptyJson));

        _logger.LogInformation(
            "LimpiezaImportacionLotesJob vacio datos_json de {Count} filas de lotes de importacion anteriores a {CutoffUtc}",
            rows,
            cutoff);
    }

    private async Task<int> GetRetentionDaysAsync()
    {
        var raw = await _dbContext.Configuraciones
            .Where(c => c.Clave == LimpiezaExportacionesJob.ImportacionContenidoRetentionDaysKey)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync();

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, MinRetentionDays, MaxRetentionDays)
            : LimpiezaExportacionesJob.DefaultImportacionContenidoRetentionDays;
    }
}
