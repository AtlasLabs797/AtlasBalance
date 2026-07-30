using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Services;

namespace AtlasBalance.API.Jobs;

/// <summary>
/// V-02.07: verifica a diario que AUDITORIAS no haya sido manipulada.
///
/// Sin esto, la firma HMAC y la secuencia solo sirven si a alguien se le ocurre
/// mirar. Un atacante cuenta justamente con que nadie mire. El job convierte la
/// deteccion en algo que ocurre solo y deja rastro propio (que a su vez esta
/// firmado y espejado al Windows Event Log).
/// </summary>
public sealed class VerificacionIntegridadAuditoriaJob
{
    /// <summary>
    /// Ventana que se revisa en cada pasada. Verificar la tabla entera cada dia
    /// es innecesario: lo anterior ya se verifico y no puede cambiar sin dejar
    /// firma invalida, que la pasada correspondiente habria detectado.
    /// </summary>
    public const int DiasVerificados = 8;

    private readonly IAuditIntegrityService _integridad;
    private readonly IAuditService _auditService;
    private readonly IClock _clock;
    private readonly ILogger<VerificacionIntegridadAuditoriaJob> _logger;

    public VerificacionIntegridadAuditoriaJob(
        IAuditIntegrityService integridad,
        IAuditService auditService,
        IClock clock,
        ILogger<VerificacionIntegridadAuditoriaJob> logger)
    {
        _integridad = integridad;
        _auditService = auditService;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var desde = _clock.UtcNow.AddDays(-DiasVerificados);
        var resultado = await _integridad.VerificarAsync(desde, null, CancellationToken.None);

        if (resultado.Integra)
        {
            _logger.LogInformation(
                "Verificacion de integridad de AUDITORIAS correcta: {Filas} filas, {SinFirma} sin firma (anteriores a V-02.07)",
                resultado.FilasExaminadas,
                resultado.SinFirma);
            return;
        }

        // Registro con el tipo de accion que SecurityEventLog considera critico:
        // sale al Windows Event Log y a los admins.
        await _auditService.LogAsync(
            null,
            AuditActions.AuditoriaIntegridadFallida,
            "AUDITORIAS",
            null,
            ipAddress: null,
            detallesJson: JsonSerializer.Serialize(new
            {
                filas_examinadas = resultado.FilasExaminadas,
                firmas_invalidas = resultado.FirmasInvalidas,
                filas_faltantes = resultado.FilasFaltantes,
                huecos = resultado.Huecos,
                ids_firma_invalida = resultado.IdsFirmaInvalida,
                rango_desde = resultado.RangoDesdeUtc
            }),
            cancellationToken: CancellationToken.None);

        _logger.LogCritical(
            "INTEGRIDAD DE AUDITORIA COMPROMETIDA: {FirmasInvalidas} firmas invalidas y {FilasFaltantes} filas ausentes sobre {Filas} examinadas desde {Desde}. " +
            "Si se acaba de rotar Security:AuditSigningKey, las firmas antiguas dejan de validar y esto es esperado; si no, investiga acceso directo a la base de datos.",
            resultado.FirmasInvalidas,
            resultado.FilasFaltantes,
            resultado.FilasExaminadas,
            desde);
    }
}
