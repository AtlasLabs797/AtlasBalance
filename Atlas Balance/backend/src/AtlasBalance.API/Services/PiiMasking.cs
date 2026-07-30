namespace AtlasBalance.API.Services;

// V-02-07: helper de enmascarado de PII (IBAN, numero de cuenta, identificacion)
// para respuestas de listado y para la integracion externa OpenClaw.
public static class PiiMasking
{
    private const int IbanVisibleChars = 4;
    private const int IdentificacionVisibleChars = 3;

    public static string? MaskIban(string? valor) => Mask(valor, IbanVisibleChars);

    public static string? MaskNumeroCuenta(string? valor) => Mask(valor, IbanVisibleChars);

    public static string? MaskIdentificacion(string? valor) => Mask(valor, IdentificacionVisibleChars);

    private static string? Mask(string? valor, int visibleChars)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return valor;
        }

        if (valor.Length <= visibleChars)
        {
            return new string('*', valor.Length);
        }

        var maskedLength = valor.Length - visibleChars;
        return new string('*', maskedLength) + valor[^visibleChars..];
    }
}
