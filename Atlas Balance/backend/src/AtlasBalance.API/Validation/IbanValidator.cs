namespace AtlasBalance.API.Validation;

/// <summary>
/// Validacion de IBAN (ISO 13616) para las cuentas de tipo NORMAL.
///
/// V-02.07: antes el IBAN solo pasaba por <c>Trim()</c>, asi que un numero con
/// una errata se guardaba en silencio. En una app de tesoreria eso acaba en una
/// transferencia a una cuenta que no existe, y el error no aparece hasta que el
/// banco la rechaza.
///
/// La comprobacion es la estandar: longitud 15-34, dos letras de pais, dos
/// digitos de control y resto alfanumerico; despues el modulo 97 del ISO 7064,
/// que detecta erratas de un digito y la mayoria de transposiciones.
///
/// No valida la longitud exacta por pais (Espana 24, Alemania 22, ...): esa
/// tabla hay que mantenerla y el modulo 97 ya corta el caso real, que es la
/// errata al teclear. Si algun dia hace falta, se anade aqui.
/// </summary>
public static class IbanValidator
{
    private const int MinLength = 15;
    private const int MaxLength = 34;

    /// <summary>
    /// Valida el IBAN ignorando espacios y mayusculas/minusculas. Un valor vacio
    /// se considera valido: el IBAN es opcional y quien decide si es obligatorio
    /// es el llamante, no esta funcion.
    /// </summary>
    public static bool TryValidate(string? iban, out string error)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            error = string.Empty;
            return true;
        }

        // Se corta por la entrada cruda antes de normalizar: sin esto un campo de
        // varios MB se copiaria entero solo para acabar rechazado por longitud.
        if (iban.Length > MaxLength * 2)
        {
            error = $"El IBAN debe tener entre {MinLength} y {MaxLength} caracteres";
            return false;
        }

        var normalized = Normalize(iban);

        if (normalized.Length is < MinLength or > MaxLength)
        {
            error = $"El IBAN debe tener entre {MinLength} y {MaxLength} caracteres";
            return false;
        }

        if (!char.IsAsciiLetterUpper(normalized[0]) || !char.IsAsciiLetterUpper(normalized[1]))
        {
            error = "El IBAN debe empezar por el codigo de pais (dos letras)";
            return false;
        }

        if (!char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3]))
        {
            error = "Los digitos de control del IBAN no son validos";
            return false;
        }

        foreach (var c in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                error = "El IBAN solo puede contener letras y numeros";
                return false;
            }
        }

        if (Mod97(normalized) != 1)
        {
            error = "El IBAN no supera el digito de control. Revisa que este bien copiado.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Quita espacios y pasa a mayusculas. No altera lo que se persiste.</summary>
    public static string Normalize(string iban) =>
        new(iban.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Modulo 97 del ISO 7064: se mueven los cuatro primeros caracteres al final,
    /// cada letra se sustituye por su posicion + 10 (A=10 ... Z=35) y el numero
    /// resultante debe dar resto 1. Se calcula por trozos porque el numero
    /// completo no cabe en ningun entero nativo.
    /// </summary>
    private static int Mod97(string normalized)
    {
        var remainder = 0;

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[(i + 4) % normalized.Length];

            if (char.IsAsciiDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else
            {
                var value = c - 'A' + 10;
                remainder = ((remainder * 100) + value) % 97;
            }
        }

        return remainder;
    }
}
