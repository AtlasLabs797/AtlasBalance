using System;
using System.Text.RegularExpressions;

namespace AtlasBalance.API.Logging;

internal static class LogScrubber
{
    private const int MaxLength = 256;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static string? Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sanitized = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");

        return sanitized.Length > MaxLength ? sanitized[..MaxLength] : sanitized;
    }

    // V-02-07: redaccion de PII (email/IBAN) para logs de aplicacion. Separado
    // de Scrub a proposito: Scrub solo hace anti-inyeccion de logs y tiene
    // codigo/tests que dependen de su firma y comportamiento actuales.
    public static string? RedactPii(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        try
        {
            // Email: conserva la primera letra del local-part y el dominio
            // entero (util para diagnostico, no identifica a la persona).
            var redacted = Regex.Replace(
                value,
                @"\b([A-Za-z0-9])[A-Za-z0-9._%+-]*@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b",
                match => $"{match.Groups[1].Value}***@{match.Groups[2].Value}",
                RegexOptions.CultureInvariant,
                RegexTimeout);

            // Mismos tres patrones que AtlasAiService.RedactIbanLike: se
            // reutilizan tal cual para no mantener dos criterios de deteccion
            // de IBAN distintos en el repo.
            redacted = Regex.Replace(
                redacted,
                @"\bES\d{22}\b",
                "ES****[IBAN redactado]",
                RegexOptions.CultureInvariant,
                RegexTimeout);
            redacted = Regex.Replace(
                redacted,
                @"\bES\d{2}[\s]?\d{4}[\s]?\d{4}[\s]?\d{4}[\s]?\d{4}[\s]?\d{4}\b",
                "ES****[IBAN redactado]",
                RegexOptions.CultureInvariant,
                RegexTimeout);
            redacted = Regex.Replace(
                redacted,
                @"\b[A-Z]{2}\d{2}[A-Z]{4}[\dA-Z]{14,30}\b",
                "[IBAN redactado]",
                RegexOptions.CultureInvariant,
                RegexTimeout);

            return redacted.Length > MaxLength ? redacted[..MaxLength] : redacted;
        }
        catch (RegexMatchTimeoutException)
        {
            // Degradacion aceptable: es un log, no un canal de salida a
            // terceros. Devolvemos el valor truncado sin redactar.
            return value.Length > MaxLength ? value[..MaxLength] : value;
        }
    }
}