namespace AtlasBalance.API.Constants;

public static class SecurityPolicy
{
    public const int MinPasswordLength = 12;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "admin123",
        "admin1234",
        "atlasbalance",
        "changeme",
        "password",
        "password123",
        "qwerty123",
        "welcome123"
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
