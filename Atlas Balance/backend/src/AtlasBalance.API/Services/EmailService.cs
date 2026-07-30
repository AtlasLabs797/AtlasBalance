using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace AtlasBalance.API.Services;

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

    /// <summary>
    /// V-02.07: aviso de una alerta de seguridad disparada. Formato generico a
    /// proposito: las reglas viven en SecurityAlertService, no aqui.
    /// </summary>
    Task SendSecurityAlertAsync(
        IReadOnlyList<string> recipients,
        string regla,
        string severidad,
        string resumen,
        IReadOnlyList<string> detalles,
        CancellationToken cancellationToken);
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

        var smtp = await LoadSmtpConfigAsync("smtp_host no configurado para alerta de saldo bajo.", cancellationToken);
        var appBaseUrl = (await GetConfigValueAsync("app_base_url", cancellationToken))?.TrimEnd('/')
            ?? "https://localhost:5000";

        // V-02-05 (MED-5): validar smtpFrom contra CRLF y otros caracteres que
        // permitirian header injection. MailKit lo sanea, pero el contrato del
        // operador es "solo una direccion de email".
        ValidateEmailAddress(smtp.From, "smtp_from");

        var cuentaUrl = EscapeHtml($"{appBaseUrl}/cuentas/{cuentaId}");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
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

        await SendMessageAsync(message, smtp.Host, smtp.Port, smtp.User, smtp.Password, cancellationToken);
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

        var smtp = await LoadSmtpConfigAsync("smtp_host no configurado para alerta de plazo fijo.", cancellationToken);
        var appBaseUrl = (await GetConfigValueAsync("app_base_url", cancellationToken))?.TrimEnd('/')
            ?? "https://localhost:5000";

        var cuentaUrl = EscapeHtml($"{appBaseUrl}/cuentas/{cuentaId}");
        var estadoTexto = estado == EstadoPlazoFijo.VENCIDO ? "vencido" : "próximo a vencer";

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
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
                $"<p><strong>Vencimiento:</strong> {fechaVencimiento:dd/MM/yyyy}</p>" +
                $"<p><a href=\"{cuentaUrl}\">Abrir cuenta</a></p>"
        }.ToMessageBody();

        await SendMessageAsync(message, smtp.Host, smtp.Port, smtp.User, smtp.Password, cancellationToken);
    }

    public async Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken)
    {
        var smtp = await LoadSmtpConfigAsync("smtp_host no configurado.", cancellationToken);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "[Atlas Balance] Correo de prueba SMTP";
        message.Body = new BodyBuilder
        {
            HtmlBody = $"<p>SMTP configurado correctamente.</p><p>Fecha UTC: {DateTime.UtcNow:O}</p>"
        }.ToMessageBody();

        await SendMessageAsync(message, smtp.Host, smtp.Port, smtp.User, smtp.Password, cancellationToken);
    }

    public async Task SendSecurityAlertAsync(
        IReadOnlyList<string> recipients,
        string regla,
        string severidad,
        string resumen,
        IReadOnlyList<string> detalles,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var smtp = await LoadSmtpConfigAsync("smtp_host no configurado para alertas de seguridad.", cancellationToken);
        ValidateEmailAddress(smtp.From, "smtp_from");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
        foreach (var recipient in recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        // El asunto lleva la severidad delante para que las reglas de bandeja
        // del operador puedan filtrar sin abrir el correo.
        message.Subject = $"[Atlas Balance] [{severidad}] Alerta de seguridad: {regla}";

        var listaDetalles = detalles.Count == 0
            ? string.Empty
            : "<ul>" + string.Concat(detalles.Select(d => $"<li>{EscapeHtml(d)}</li>")) + "</ul>";

        message.Body = new BodyBuilder
        {
            HtmlBody =
                $"<h2>Alerta de seguridad</h2>" +
                $"<p><strong>Regla:</strong> {EscapeHtml(regla)}</p>" +
                $"<p><strong>Severidad:</strong> {EscapeHtml(severidad)}</p>" +
                $"<p><strong>Resumen:</strong> {EscapeHtml(resumen)}</p>" +
                listaDetalles +
                $"<p>Detectada a las {DateTime.UtcNow:O} UTC. Revisa Auditoría en la aplicación.</p>"
        }.ToMessageBody();

        await SendMessageAsync(message, smtp.Host, smtp.Port, smtp.User, smtp.Password, cancellationToken);
    }

    private async Task<string?> GetConfigValueAsync(string key, CancellationToken cancellationToken)
    {
        return await _dbContext.Configuraciones
            .Where(x => x.Clave == key)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private readonly record struct SmtpConfig(string Host, int Port, string? User, string? Password, string From);

    private async Task<SmtpConfig> LoadSmtpConfigAsync(string missingHostMessage, CancellationToken cancellationToken)
    {
        var smtpHost = await GetConfigValueAsync("smtp_host", cancellationToken);
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            throw new InvalidOperationException(missingHostMessage);
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
        return new SmtpConfig(smtpHost, smtpPort, smtpUser, smtpPassword, smtpFrom);
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
        // V-02-05 (LOW-BE-6): timeout duro de 15s para SMTP. Sin esto, un servidor
        // SMTP caido puede dejar el handler colgado indefinidamente.
        client.Timeout = 15_000;
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

    // V-02-05 (MED-5): valida que la direccion de email no contenga CRLF ni
    // caracteres que permitirian header injection aunque MailKit sanea. Limita
    // a formato "local@dominio" sin display-name.
    private static readonly System.Text.RegularExpressions.Regex EmailAddressRegex =
        new(@"^[^<>:\r\n\t\x00-\x1F\x7F]+@[A-Za-z0-9._-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static void ValidateEmailAddressPublic(string value, string configKey) => ValidateEmailAddress(value, configKey);

    private static void ValidateEmailAddress(string value, string configKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{configKey} no puede estar vacio.");
        }
        if (value.Contains('\r') || value.Contains('\n') || value.Contains(':') || value.Contains('<') || value.Contains('>'))
        {
            throw new InvalidOperationException($"{configKey} contiene caracteres no permitidos (CRLF, ':', '<', '>'). Use solo una direccion de email valida.");
        }
        if (!EmailAddressRegex.IsMatch(value.Trim()))
        {
            throw new InvalidOperationException($"{configKey} no tiene formato de direccion de email valido.");
        }
    }
}
