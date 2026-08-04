using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Services;

public interface IAlertDispatcher
{
    /// <summary>
    /// Claves (regla|clave) notificadas dentro del periodo de enfriamiento. El
    /// caller las consulta una vez y filtra sus candidatas contra ellas.
    /// </summary>
    Task<HashSet<string>> ClavesEnEnfriamientoAsync(DateTime ahoraUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Registra la alerta en AUDITORIAS y en las notificaciones de admin, y la
    /// manda por los canales externos configurados.
    /// </summary>
    Task DespacharAsync(SecurityAlert alerta, CancellationToken cancellationToken);
}

/// <summary>
/// V-02.07: canal comun de alertas para seguridad (SecurityAlertService) y salud
/// de la aplicacion (HealthAlertJob). Existe porque hay dos consumidores reales
/// con exactamente las mismas necesidades de entrega y deduplicacion; no es una
/// abstraccion preventiva.
///
/// Orden de entrega deliberado: primero AUDITORIAS, que ademas de registro es el
/// estado de deduplicacion de la siguiente pasada, y solo despues los canales
/// externos, que pueden fallar sin que se pierda la alerta.
/// </summary>
public sealed class AlertDispatcher : IAlertDispatcher
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IEmailService _emailService;
    private readonly ISlackAlertNotifier _slack;
    private readonly IClock _clock;
    private readonly SecurityAlertOptions _options;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(
        AppDbContext dbContext,
        IAuditService auditService,
        IEmailService emailService,
        ISlackAlertNotifier slack,
        IClock clock,
        IOptions<SecurityAlertOptions> options,
        ILogger<AlertDispatcher> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _emailService = emailService;
        _slack = slack;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HashSet<string>> ClavesEnEnfriamientoAsync(DateTime ahoraUtc, CancellationToken cancellationToken)
    {
        var desde = ahoraUtc.AddMinutes(-Math.Max(1, _options.EnfriamientoMinutos));

        var recientes = await _dbContext.Auditorias
            .AsNoTracking()
            .Where(a => a.TipoAccion == AuditActions.AlertaSeguridadDisparada && a.Timestamp >= desde)
            .Select(a => a.DetallesJson)
            .ToListAsync(cancellationToken);

        var claves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detalle in recientes)
        {
            var regla = LeerCampoTexto(detalle, "regla");
            var clave = LeerCampoTexto(detalle, "clave");
            if (regla is not null && clave is not null)
            {
                claves.Add($"{regla}|{clave}");
            }
        }

        return claves;
    }

    public async Task DespacharAsync(SecurityAlert alerta, CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(
            alerta.UsuarioId,
            AuditActions.AlertaSeguridadDisparada,
            "SEGURIDAD",
            null,
            ipAddress: null,
            detallesJson: JsonSerializer.Serialize(new
            {
                regla = alerta.Regla,
                clave = alerta.Clave,
                severidad = alerta.Severidad,
                resumen = alerta.Resumen,
                detalles = alerta.Detalles
            }),
            cancellationToken: cancellationToken);

        _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
        {
            Id = Guid.NewGuid(),
            Tipo = "SEGURIDAD",
            Mensaje = $"[{alerta.Severidad}] {alerta.Resumen}",
            Leida = false,
            Fecha = _clock.UtcNow,
            DetallesJson = JsonSerializer.Serialize(new
            {
                regla = alerta.Regla,
                clave = alerta.Clave,
                detalles = alerta.Detalles
            })
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnviarEmailAsync(alerta, cancellationToken);
        await _slack.NotificarAsync(alerta.Regla, alerta.Severidad, alerta.Resumen, alerta.Detalles, cancellationToken);
    }

    private async Task EnviarEmailAsync(SecurityAlert alerta, CancellationToken cancellationToken)
    {
        try
        {
            var destinatarios = _options.DestinatariosEmail
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .ToList();

            if (destinatarios.Count == 0)
            {
                // Sin lista explicita, a los admins activos: mejor eso que dejar
                // una alerta sin destinatario porque nadie configuro nada.
                destinatarios = await _dbContext.Usuarios
                    .AsNoTracking()
                    .Where(u => u.Rol == RolUsuario.ADMIN && u.Activo)
                    .Select(u => u.Email)
                    .ToListAsync(cancellationToken);
            }

            await _emailService.SendSecurityAlertAsync(
                destinatarios,
                alerta.Regla,
                alerta.Severidad,
                alerta.Resumen,
                alerta.Detalles,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo enviar por correo la alerta {Regla}. Queda registrada en AUDITORIAS y en notificaciones de admin.",
                alerta.Regla);
        }
    }

    /// <summary>
    /// Lee una propiedad de texto de la raiz del JSON. null si el JSON no es
    /// valido: los detalles son datos, no contrato, y una fila corrupta no puede
    /// tumbar la deduplicacion.
    /// </summary>
    internal static string? LeerCampoTexto(string? json, string propiedad)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty(propiedad, out var valor))
            {
                return null;
            }

            return valor.ValueKind == JsonValueKind.String ? valor.GetString() : valor.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
