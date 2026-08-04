using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IAuditIntegrityService
{
    Task<AuditoriaIntegridadResponse> VerificarAsync(
        DateTime? desdeUtc,
        DateTime? hastaUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Verifica que AUDITORIAS no haya sido manipulada. Dos comprobaciones
/// independientes, porque detectan cosas distintas:
///
/// - Firma HMAC por fila -> detecta contenido modificado y filas insertadas.
/// - Continuidad de la secuencia -> detecta filas borradas.
///
/// Se ejecuta a demanda desde /api/auditoria/integridad y una vez al dia desde
/// VerificacionIntegridadAuditoriaJob.
/// </summary>
public sealed class AuditIntegrityService : IAuditIntegrityService
{
    /// <summary>Tope de tramos ausentes y de ids invalidos que se devuelven.</summary>
    private const int MaxDetalle = 50;

    /// <summary>
    /// Filas por lote. La verificacion recorre todo el rango, que puede ser un
    /// ano de auditoria, asi que no se materializa entera en memoria.
    /// </summary>
    private const int TamanoLote = 2_000;

    private readonly AppDbContext _dbContext;
    private readonly IAuditSigner _auditSigner;

    public AuditIntegrityService(AppDbContext dbContext, IAuditSigner auditSigner)
    {
        _dbContext = dbContext;
        _auditSigner = auditSigner;
    }

    public async Task<AuditoriaIntegridadResponse> VerificarAsync(
        DateTime? desdeUtc,
        DateTime? hastaUtc,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Auditorias.AsNoTracking();
        if (desdeUtc.HasValue)
        {
            query = query.Where(a => a.Timestamp >= desdeUtc.Value);
        }
        if (hastaUtc.HasValue)
        {
            query = query.Where(a => a.Timestamp < hastaUtc.Value);
        }

        var examinadas = 0;
        var validas = 0;
        var invalidas = 0;
        var sinFirma = 0;
        var idsInvalidos = new List<Guid>();
        var huecos = new List<AuditoriaHuecoSecuencia>();
        long filasFaltantes = 0;

        long? secuenciaAnterior = null;
        long cursor = 0;

        while (true)
        {
            var lote = await query
                .Where(a => a.Secuencia > cursor)
                .OrderBy(a => a.Secuencia)
                .Take(TamanoLote)
                .ToListAsync(cancellationToken);

            if (lote.Count == 0)
            {
                break;
            }

            foreach (var fila in lote)
            {
                examinadas++;

                if (string.IsNullOrEmpty(fila.Firma))
                {
                    // Fila anterior a V-02.07: nunca se firmo. No es sospechosa.
                    sinFirma++;
                }
                else if (_auditSigner.Verificar(fila))
                {
                    validas++;
                }
                else
                {
                    invalidas++;
                    if (idsInvalidos.Count < MaxDetalle)
                    {
                        idsInvalidos.Add(fila.Id);
                    }
                }

                if (secuenciaAnterior.HasValue && fila.Secuencia > secuenciaAnterior.Value + 1)
                {
                    var faltan = fila.Secuencia - secuenciaAnterior.Value - 1;
                    filasFaltantes += faltan;
                    if (huecos.Count < MaxDetalle)
                    {
                        huecos.Add(new AuditoriaHuecoSecuencia
                        {
                            DesdeSecuencia = secuenciaAnterior.Value + 1,
                            HastaSecuencia = fila.Secuencia - 1,
                            FilasFaltantes = faltan
                        });
                    }
                }

                secuenciaAnterior = fila.Secuencia;
            }

            cursor = lote[^1].Secuencia;
        }

        // Un rango parcial empieza y acaba en medio de la secuencia global, asi
        // que los huecos internos siguen siendo validos como senal, pero no se
        // puede reclamar nada sobre los bordes. Se avisa en la documentacion del
        // endpoint; aqui no inventamos huecos en los extremos.
        return new AuditoriaIntegridadResponse
        {
            FechaVerificacionUtc = DateTime.UtcNow,
            RangoDesdeUtc = desdeUtc,
            RangoHastaUtc = hastaUtc,
            FilasExaminadas = examinadas,
            FirmasValidas = validas,
            FirmasInvalidas = invalidas,
            SinFirma = sinFirma,
            FilasFaltantes = filasFaltantes,
            Huecos = huecos,
            IdsFirmaInvalida = idsInvalidos,
            Integra = invalidas == 0 && filasFaltantes == 0
        };
    }
}
