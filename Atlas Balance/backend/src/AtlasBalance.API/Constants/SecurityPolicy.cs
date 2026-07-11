namespace AtlasBalance.API.Constants;

public static class SecurityPolicy
{
    public const int MinPasswordLength = 12;

    // V-02-05 (LOW-BE-4): lista de contrasenas comunes ampliada a las 100 mas usadas
    // globally (top-100 de SecLists/Common-Credentials + top-100-Spanish variants).
    // NO es exhaustiva (HIBP es la solucion real) pero cubre los casos mas tipicos
    // que las herramientas automatizadas de brute-force prueban primero.
    // Para produccion real: integrar HIBP k-anonymity (https://haveibeenpwned.com/API/v3#PwnedPasswords).
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Top global
        "password", "123456", "12345678", "qwerty", "abc123", "monkey", "1234567",
        "letmein", "trustno1", "dragon", "baseball", "iloveyou", "master", "sunshine",
        "ashley", "bailey", "shadow", "123123", "654321", "superman", "qazwsx",
        "michael", "football", "password1", "password123", "batman", "login",
        "admin", "admin123", "admin1234", "administrator", "root", "toor", "changeme",
        "welcome", "welcome1", "welcome123", "test", "test123", "demo", "demo123",
        // Espanol / variaciones
        "hola1234", "clave123", "secreto", "contraseña", "qwerty123", "qwertyuiop",
        "micontrasena", "mipassword", "atlas", "atlasbalance", "atlas123", "balance",
        // Numericos
        "11111111", "12341234", "12344321", "123456789", "1234567890", "0987654321",
        "111111", "000000", "121212", "696969", "112233", "qwerty12", "asdf1234",
        // Palabras comunes
        "starwars", "trustme", "whatever", "passw0rd", "p@ssw0rd", "p@ssword",
        "p@$$w0rd", "letmein123", "access", "master123", "superman123", "iloveu",
        // Patrones del ano
        "summer2024", "summer2025", "summer2026", "winter2024", "winter2025",
        "spring2024", "spring2025", "fall2024", "fall2025", "january2024",
        // Atlas Balance especificos
        "atlas2024", "atlas2025", "atlas2026", "atlasbalance2024", "atlasbalance2025",
        "atlasbalance2026", "tesoreria", "tesoreria123", "tesoro", "finanzas", "finanzas123",
        "banco", "banco123", "cuentas", "extractos", "extracto", "balance123"
    };

    public static bool TryValidatePassword(string? password, out string error)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            error = $"La contraseña debe tener al menos {MinPasswordLength} caracteres";
            return false;
        }

        var normalized = password.Trim();
        if (CommonPasswords.Contains(normalized))
        {
            error = "La contraseña es demasiado comun";
            return false;
        }

        if (normalized.Distinct().Count() == 1)
        {
            error = "La contraseña no puede repetir un solo caracter";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
