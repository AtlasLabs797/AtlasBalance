using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AtlasBalance.API.Logging;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 6): capa unica de DLP antes de cualquier salida al
// proveedor de IA. Refuerza la redaction que ya hacia AtlasAiService
// (RedactIbanLike) y la extiende a todos los tipos de PII:
//
//   - IBAN internacional: ES91 2100 0418 4502 0005 1332, es91210004184500200051332,
//     con o sin separadores, en mayusculas o minusculas.
//   - Email: juan.perez@empresa.com
//   - Telefono espanol: +34 600 123 456, 600123456, 0034600123456
//   - DNI / NIF / NIE: 12345678Z, X1234567A
//   - CIF: B12345678
//   - Tarjeta: 4111 1111 1111 1111, 4111111111111111
//   - BIC: DEUTDEFFXXX, CAIXESBBXXX
//
// El servicio se compone:
//   - Pseudonimizacion de entidades (titulares, cuentas, terceros)
//     via AiPseudonymMap (ya en V-02.07).
//   - Redaction de patrones PII via DlpScrubber (Fase 6).
//   - Validacion final: si el regex tarda demasiado, NO se envia
//     el payload. Es deliberado: preferimos abortar la consulta a
//     enviar un documento sin redaction completa (fail-closed).
//
// Lo que NO se hace en esta fase:
//   - Sustituir IBAN por ****1234 con el formato amigable: eso se
//     hace en la respuesta al usuario (V-02.07 ya tenia un helper
//     que aqui se mantiene).
//   - Sustituir nombres por ****: la politica es seudonimos
//     reversibles ([TITULAR_1]) para que la explicacion al usuario
//     siga siendo legible.

public sealed record DlpScanResult
{
    public string Texto { get; init; } = string.Empty;
    public int EntidadesSustituidas { get; init; }
    public int PatronesPIIRedactados { get; init; }
    public IReadOnlyList<string> TiposPIIEncontrados { get; init; } = Array.Empty<string>();
    public bool FalloCerrado { get; init; }
    public string? MotivoFalloCerrado { get; init; }
}

public sealed class DlpScrubber
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    private readonly AiPseudonymMap _pseudonimos;

    public DlpScrubber(AiPseudonymMap pseudonimos)
    {
        _pseudonimos = pseudonimos;
    }

    public DlpScanResult Escanear(string texto, string etiqueta)
    {
        if (texto is null)
        {
            return new DlpScanResult
            {
                Texto = string.Empty,
                FalloCerrado = true,
                MotivoFalloCerrado = $"{etiqueta}: texto nulo."
            };
        }

        var resultado = texto;
        var tiposEncontrados = new HashSet<string>(StringComparer.Ordinal);
        var totalPatrones = 0;

        // 1. Pseudonimos primero. Si una entidad llamada
        // "juan@empresa.com" existe como titular, se reemplaza
        // antes de evaluar la regex de email (evita doble trabajo).
        try
        {
            resultado = _pseudonimos.Apply(resultado);
        }
        catch (RegexMatchTimeoutException ex)
        {
            return FailClosed(etiqueta, "pseudonimos", ex);
        }

        // 2. Patrones PII. Cada Regex con timeout defensivo.
        foreach (var (patron, etiquetaPII) in PatronesPII)
        {
            try
            {
                var match = patron.Match(resultado);
                if (!match.Success) continue;
                var sb = new StringBuilder(resultado.Length);
                var lastIndex = 0;
                while (match.Success)
                {
                    sb.Append(resultado, lastIndex, match.Index - lastIndex);
                    sb.Append(MascaraPara(etiquetaPII, match.Value));
                    lastIndex = match.Index + match.Length;
                    match = match.NextMatch();
                    totalPatrones++;
                }
                sb.Append(resultado, lastIndex, resultado.Length - lastIndex);
                resultado = sb.ToString();
                tiposEncontrados.Add(etiquetaPII);
            }
            catch (RegexMatchTimeoutException ex)
            {
                return FailClosed(etiqueta, etiquetaPII, ex);
            }
        }

        return new DlpScanResult
        {
            Texto = resultado,
            EntidadesSustituidas = _pseudonimos.Count,
            PatronesPIIRedactados = totalPatrones,
            TiposPIIEncontrados = tiposEncontrados.ToList(),
            FalloCerrado = false
        };
    }

    private DlpScanResult FailClosed(string etiqueta, string motivo, Exception ex)
    {
        // No se hace LogScrubber.Scrub(ex.Message) porque el mensaje
        // de RegexMatchTimeoutException no contiene PII. El log
        // queda con el motivo y la etiqueta para depuracion.
        return new DlpScanResult
        {
            FalloCerrado = true,
            MotivoFalloCerrado = $"{etiqueta}: timeout o error en redaction de '{motivo}'. " + ex.Message,
            Texto = string.Empty
        };
    }

    private static string MascaraPara(string tipo, string original)
    {
        // Mantiene los ultimos 4 caracteres visibles para que el
        // usuario (en su propia UI) pueda seguir el rastro, pero
        // el proveedor solo ve [TIPO_xxxx]. Aqui, lo que sale al
        // proveedor es la mascara corta (sin los ultimos 4) para
        // reducir la superficie.
        return tipo switch
        {
            "IBAN" => "[IBAN_REDACTED]",
            "EMAIL" => "[EMAIL_REDACTED]",
            "PHONE" => "[PHONE_REDACTED]",
            "DNI" => "[DNI_REDACTED]",
            "NIE" => "[NIE_REDACTED]",
            "NIF" => "[NIF_REDACTED]",
            "CIF" => "[CIF_REDACTED]",
            "CARD" => "[CARD_REDACTED]",
            "BIC" => "[BIC_REDACTED]",
            _ => "[REDACTED]"
        };
    }

    // Los patrones se compilan una sola vez. Cada uno lleva su
    // timeout defensivo para que un input adversario no bloquee la
    // peticion.
    private static readonly (Regex Patron, string Etiqueta)[] PatronesPII = new[]
    {
        // IBAN: 2 letras + 2 digitos + hasta 30 alfanumericos, con
        // separadores opcionales (espacios, guiones, sin
        // separador). Limite duro de longitud: 34 chars en
        // formato compacto (sin separadores). Se procesa ANTES que
        // la tarjeta para que la secuencia de digitos con guiones
        // se reconozca como IBAN, no como tarjeta.
        (new Regex(
            @"\b[A-Z]{2}\d{2}(?:[ -]?[A-Z0-9]{1,4}){2,7}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "IBAN"),

        // Email
        (new Regex(
            @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "EMAIL"),

        // Telefono espanol: +34, 0034, o 9/6 digitos con separadores.
        (new Regex(
            @"(?:\+|00)?34[ ]?\d{3}[ ]?\d{3}[ ]?\d{3}|\b\d{9}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "PHONE"),

        // DNI: 8 digitos + letra
        (new Regex(
            @"\b\d{8}[A-Z]\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "DNI"),

        // NIE: X/Y/Z + 7 digitos + letra
        (new Regex(
            @"\b[XYZ]\d{7}[A-Z]\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "NIE"),

        // NIF (mismo patron que DNI pero lo etiquetamos distinto
        // por contexto: letras K/L/M finales).
        (new Regex(
            @"\b\d{8}[KLM]\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "NIF"),

        // CIF: letra + 7 digitos + letra/digito
        (new Regex(
            @"\b[A-HJNP-SUVW]\d{7}[A-Z0-9]\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "CIF"),

        // Tarjeta: 13-19 digitos con separadores opcionales
        (new Regex(
            @"\b(?:\d[ -]?){13,19}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "CARD"),

        // BIC/SWIFT: 4 letras + 2 letras/digitos + 2 letras/digitos + 3 letras/digitos opcionales
        (new Regex(
            @"\b[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}(?:[A-Z0-9]{3})?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout), "BIC")
    };
}
