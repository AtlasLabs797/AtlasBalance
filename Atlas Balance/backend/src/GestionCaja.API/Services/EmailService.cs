using GestionCaja.API.Data;
using GestionCaja.API.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace GestionCaja.API.Services;

public interface IEmailService
{
    Task SendSaldoBajoAlertAsync(
        IReadOnlyList<string> recipients,
        string titularNombre,
        string cuentaNombre,
        Guid cuentaId,
        string divisa,
        decimal saldoActual,
        decimal saldoMinimo,
        string? conceptoUltimoMovimiento,
        CancellationToken cancellationToken);
    Task SendPlazoFijoVencimientoAsync(
        IReadOnlyList<string> recipients,
        string titularNombre,
        string cuentaNombre,
        Guid cuentaId,
        DateOnly fechaVencimiento,
        EstadoPlazoFijo estado,
        CancellationToken cancellationToken);
    Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken);
}

public sealed class EmailService : IEmailService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<EmailService> _logger;
    private readonly ISecretProtector _secretProtector;

    public EmailService(AppDbContext dbContext, ILogger<EmailService> logger, ISecretProtector secretProtector)
    {
        _dbContext = dbContext;
        _logger = logger;
        _secretProtector = secretProtector;
    }

    public async Task SendSaldoBajoAlertAsync(
        IReadOnlyList<string> recipients,
        string titularNombre,
        string cuentaNombre,
        Guid cuentaId,
        string divisa,
        decimal saldoActual,
        decimal saldoMinimo,
        string? conceptoUltimoMovimiento,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var smtpHost = await GetConfigValueAsync("smtp_host", cancellationToken);
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            _logger.LogWarning("No se envia alerta por saldo bajo: smtp_host no configurado");
            return;
        }

        var smtpPortRaw = await GetConfigValueAsync("smtp_port", cancellationToken) ?? "587";
        var smtpUser = await GetConfigValueAsync("smtp_user", cancellationToken);
        var smtpPassword = _secretProtector.UnprotectFromStorage(await GetConfigValueAsync("smtp_password", cancellationToken));
        var smtpFrom = await GetConfigValueAsync("smtp_from", cancellationToken);
        var appBaseUrl = (await GetConfigValueAsync("app_base_url", cancellationToken))?.TrimEnd('/')
            ?? "https://localhost:5000";

        if (string.IsNullOrWhiteSpace(smtpFrom))
        {
            smtpFrom = "noreply@atlasbalance.local";
        }

        var smtpPort = int.TryParse(smtpPortRaw, out var parsedPort) ? parsedPort : 587;
        var cuentaUrl = EscapeHtml($"{appBaseUrl}/cuentas/{cuentaId}");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtpFrom));
        foreach (var recipient in recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = $"[Atlas Balance] Saldo bajo en {cuentaNombre}";
        message.Body = new BodyBuilder
        {
            HtmlBody =
                $"<h2>Alerta de saldo bajo</h2>" +
                $"<p><strong>Titular:</strong> {EscapeHtml(titularNombre)}</p>" +
                $"<p><strong>Cuenta:</strong> {EscapeHtml(cuentaNombre)}</p>" +
                $"<p><strong>Saldo actual:</strong> {saldoActual:N2} {EscapeHtml(divisa)}</p>" +
                $"<p><strong>Saldo mínimo:</strong> {saldoMinimo:N2} {EscapeHtml(divisa)}</p>" +
                $"<p><strong>Último concepto:</strong> {EscapeHtml(conceptoUltimoMovimiento ?? "Sin concepto")}</p>" +
                $"<p><a href=\"{cuentaUrl}\">Abrir cuenta</a></p>"
        }.ToMessageBody();

        await SendMessageAsync(message, smtpHost, smtpPort, smtpUser, smtpPassword, cancellationToken);
    }

    public async Task SendPlazoFijoVencimientoAsync(
        IReadOnlyList<string> recipients,
        string titularNombre,
        string cuentaNombre,
        Guid cuentaId,
        DateOnly fechaVencimiento,
        EstadoPlazoFijo estado,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var smtpHost = await GetConfigValueAsync("smtp_host", cancellationToken);
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            _logger.LogWarning("No se envia alerta de plazo fijo: smtp_host no configurado");
            return;
        }

        var smtpPortRaw = await GetConfigValueAsync("smtp_port", cancellationToken) ?? "587";
        var smtpUser = await GetConfigValueAsync("smtp_user", cancellationToken);
        var smtpPassword = _secretProtector.UnprotectFromStorage(await GetConfigValueAsync("smtp_password", cancellationToken));
        var smtpFrom = await GetConfigValueAsync("smtp_from", cancellationToken);
        var appBaseUrl = (await GetConfigValueAsync("app_base_url", cancellationToken))?.TrimEnd('/')
            ?? "https://localhost:5000";

        if (string.IsNullOrWhiteSpace(smtpFrom))
        {
            smtpFrom = "noreply@atlasbalance.local";
        }

        var smtpPort = int.TryParse(smtpPortRaw, out var parsedPort) ? parsedPort : 587;
        var cuentaUrl = EscapeHtml($"{appBaseUrl}/cuentas/{cuentaId}");
        var estadoTexto = estado == EstadoPlazoFijo.VENCIDO ? "vencido" : "proximo a vencer";

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtpFrom));
        foreach (var recipient in recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = $"[Atlas Balance] Plazo fijo {estadoTexto}: {cuentaNombre}";
        message.Body = new BodyBuilder
        {
            HtmlBody =
                $"<h2>Plazo fijo {EscapeHtml(estadoTexto)}</h2>" +
                $"<p><strong>Titular:</strong> {EscapeHtml(titularNombre)}</p>" +
                $"<p><strong>Cuenta:</strong> {EscapeHtml(cuentaNombre)}</p>" +
                $"<p><strong>Vencimiento:</strong> {fechaVencimiento:yyyy-MM-dd}</p>" +
                $"<p><a href=\"{cuentaUrl}\">Abrir cuenta</a></p>"
        }.ToMessageBody();

        await SendMessageAsync(message, smtpHost, smtpPort, smtpUser, smtpPassword, cancellationToken);
    }

    public async Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
    {
        var smtpHost = await GetConfigValueAsync("smtp_host", cancellationToken);
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            throw new InvalidOperationException("smtp_host no configurado.");
        }

        var smtpPortRaw = await GetConfigValueAsync("smtp_port", cancellationToken) ?? "587";
        var smtpUser = await GetConfigValueAsync("smtp_user", cancellationToken);
        var smtpPassword = _secretProtector.UnprotectFromStorage(await GetConfigValueAsync("smtp_password", cancellationToken));
        var smtpFrom = await GetConfigValueAsync("smtp_from", cancellationToken);
        if (string.IsNullOrWhiteSpace(smtpFrom))
        {
            smtpFrom = "noreply@atlasbalance.local";
        }

        var smtpPort = int.TryParse(smtpPortRaw, out var parsedPort) ? parsedPort : 587;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtpFrom));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "[Atlas Balance] Correo de prueba SMTP";
        message.Body = new BodyBuilder
        {
            HtmlBody = $"<p>SMTP configurado correctamente.</p><p>Fecha UTC: {DateTime.UtcNow:O}</p>"
        }.ToMessageBody();

        await SendMessageAsync(message, smtpHost, smtpPort, smtpUser, smtpPassword, cancellationToken);
    }

    private async Task<string?> GetConfigValueAsync(string key, CancellationToken cancellationToken)
    {
        return await _dbContext.Configuraciones
            .Where(x => x.Clave == key)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task SendMessageAsync(
        MimeMessage message,
        string smtpHost,
        int smtpPort,
        string? smtpUser,
        string? smtpPassword,
        CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();
        var userName = smtpUser?.Trim();
        var hasCredentials = !string.IsNullOrWhiteSpace(userName);
        var secureSocketOptions = smtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : hasCredentials
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions, cancellationToken);
        if (hasCredentials)
        {
            await client.AuthenticateAsync(userName!, smtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static string EscapeHtml(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }
}
