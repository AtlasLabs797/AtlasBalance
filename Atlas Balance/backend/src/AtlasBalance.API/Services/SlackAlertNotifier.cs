using System.Text;
using System.Text.Json;
using AtlasBalance.API.Logging;

namespace AtlasBalance.API.Services;

public interface ISlackAlertNotifier
{
    /// <summary>true si hay webhook configurado y el canal esta operativo.</summary>
    bool Configurado { get; }

    /// <summary>
    /// Publica la alerta en Slack. No lanza: un canal de notificacion caido no
    /// puede tumbar el job que evalua las reglas.
    /// </summary>
    Task NotificarAsync(
        string regla,
        string severidad,
        string resumen,
        IReadOnlyList<string> detalles,
        CancellationToken cancellationToken);
}

/// <summary>
/// V-02.07: canal Slack por Incoming Webhook.
///
/// Opt-in: si Security:Alertas:SlackWebhookUrl no esta configurado, el canal
/// queda inerte. La app es on-premise y puede no tener salida a internet, asi
/// que activarlo es una decision explicita del operador.
///
/// La URL del webhook ES un secreto (quien la tenga puede publicar en el canal):
/// se guarda en appsettings de produccion, nunca en la BD ni en la documentacion,
/// y no se escribe en ningun log.
/// </summary>
public sealed class SlackAlertNotifier : ISlackAlertNotifier
{
    /// <summary>Nombre del cliente HTTP con timeout propio (ver Program.cs).</summary>
    public const string HttpClientName = "slack-alerts";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SlackAlertNotifier> _logger;
    private readonly string? _webhookUrl;

    public SlackAlertNotifier(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SlackAlertNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var configurada = configuration["Security:Alertas:SlackWebhookUrl"]?.Trim();
        // Solo https y solo el host oficial de webhooks: evita que una
        // configuracion erronea (o manipulada) mande las alertas a un tercero.
        if (!string.IsNullOrEmpty(configurada) &&
            Uri.TryCreate(configurada, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase))
        {
            _webhookUrl = configurada;
        }
        else if (!string.IsNullOrEmpty(configurada))
        {
            // Sin volcar la URL: puede llevar el token del webhook.
            _logger.LogError(
                "Security:Alertas:SlackWebhookUrl no es una URL https de hooks.slack.com. El canal Slack queda desactivado.");
        }
    }

    public bool Configurado => _webhookUrl is not null;

    public async Task NotificarAsync(
        string regla,
        string severidad,
        string resumen,
        IReadOnlyList<string> detalles,
        CancellationToken cancellationToken)
    {
        if (_webhookUrl is null)
        {
            return;
        }

        try
        {
            var lineas = new StringBuilder();
            lineas.Append("*[").Append(severidad).Append("] Atlas Balance - ").Append(regla).Append("*\n");
            lineas.Append(resumen);
            foreach (var detalle in detalles)
            {
                lineas.Append("\n• ").Append(detalle);
            }

            var payload = JsonSerializer.Serialize(new { text = lineas.ToString() });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsync(_webhookUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Sin cuerpo de respuesta ni URL en el log.
                _logger.LogWarning(
                    "Slack rechazo la alerta de seguridad con codigo {StatusCode}",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo publicar la alerta {Regla} en Slack", LogScrubber.Scrub(regla));
        }
    }
}
