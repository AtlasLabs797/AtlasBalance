namespace AtlasBalance.API.Constants;

public static class SecurityPolicy
{
    public const int MinPasswordLength = 12;

    // V-02-07: la lista se reescribio porque `TryValidatePassword` comprueba la longitud
    // minima (MinPasswordLength = 12) ANTES de comparar contra esta lista, asi que
    // cualquier entrada de menos de 12 caracteres es codigo muerto (nunca se puede
    // alcanzar). Invariante obligatoria: TODAS las entradas tienen 12+ caracteres.
    // SecurityPolicyTests.CommonPasswords_AllEntries_MeetMinimumLength recorre el
    // HashSet entero y falla si alguien vuelve a colar una entrada corta.
    // Contenido: variantes de 12+ caracteres de las contrasenas mas repetidas en
    // filtraciones conocidas (top SecLists/rockyou/Common-Credentials) mas variantes
    // en espanol y especificas de Atlas Balance. NO es exhaustiva (HIBP es la solucion
    // real) pero cubre los patrones tipicos que las herramientas de brute-force prueban
    // primero cuando ya conocen el minimo de longitud.
    // Para produccion real: integrar HIBP k-anonymity (https://haveibeenpwned.com/API/v3#PwnedPasswords).
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Word + anio / numeros (patron mas comun en filtraciones)
        "baseball1234", "baseball2024", "baseball2025", "baseball2026", "football1234",
        "football2024", "football2025", "football2026", "iloveyou1234", "iloveyou2024",
        "iloveyou2025", "iloveyou2026", "password1234", "password2024", "password2025",
        "password2026", "princess1234", "princess2024", "princess2025", "princess2026",
        "starwars1234", "starwars2024", "starwars2025", "starwars2026", "sunshine1234",
        "sunshine2024", "sunshine2025", "sunshine2026", "superman1234", "superman2024",
        "superman2025", "superman2026", "trustno11234", "trustno12024", "trustno12025",
        "trustno12026", "whatever1234", "whatever2024", "whatever2025", "whatever2026",

        // Sustituciones leet
        "P@ssw0rd123!", "P@ssw0rd1234", "P@ssword1234", "Passw0rd123!", "Passw0rd1234",
        "adm1n1strat0r", "h4ck3rman123", "l3tm3in12345", "p@ssw0rd!2345", "passw0rd123456",
        "s3cr3tp4ss12",

        // Patrones de teclado largos
        "1234qwerasdf", "1q2w3e4r5t6y", "1qaz2wsx3edc", "1qazxsw23edc", "asdfghjkl123",
        "asdfghjkl1234", "mnbvcxzlkjhg", "poiuytrewq12", "qazwsxedc123", "qwerasdfzxcv",
        "qwerty123456", "qwertyuiop12", "qwertyuiop123", "zaq12wsx3edc", "zxcvbnm123456",

        // Frases pegadas
        "changeme1234", "changepass12", "ilovemylife1", "ilovemyself1", "iloveyoubaby",
        "iloveyoumore", "iloveyoutoo1", "letmein123456", "myspacepass1", "newpassword1",
        "nobodyknows1", "whateveryou1", "youwillneverguess",

        // Numericos / repeticiones
        "000000000000", "0123456789012", "111111111111", "112233445566", "112358132134",
        "121212121212", "123123123123", "123456789012", "1234567890123", "159159159159",
        "987654321098",

        // Nombres + numeros
        "amanda123456", "andrea1234567", "ashley123456", "daniel1234567", "hannah123456",
        "jennifer12345", "jessica123456", "jordan123456", "michael123456", "roberto123456",

        // Espanol
        "administrador", "administrador1", "bienvenida123", "bienvenido123", "cambiar12345",
        "contrasena12", "contrasena123", "empresa123456", "familia123456", "hola12345678",
        "holamundo123", "miclave12345", "miclaveatlas1", "micontrasena", "muchagente12",
        "nuevaclave12", "passwordseguro", "seguridad123", "teamocontodo", "teamomucho12",
        "trabajador12", "trabajador123", "usuario123456", "vamosaganar1",

        // Atlas Balance especificos
        "atlas2024pass", "atlas2025pass", "atlasbalance", "atlasbalance1", "atlasbalance12",
        "atlasbalance123", "atlasbalance2024", "atlasbalance2025", "atlasbalance2026",
        "atlasbalanceadmin", "balance123456", "bancoatlas1234", "contabilidad1",
        "contabilidad12", "cuentasbancarias", "extractobanco1", "finanzas123456",
        "tesoreria123", "tesoreria2024", "tesoreria2025", "tesoreria2026",

        // Administracion / sistema
        "admin12345678", "adminadmin123", "administrator", "changeme12345", "manageraccess1",
        "rootroot123456", "superadmin123", "sysadmin123456", "welcome1234567"
    };

    /// <summary>
    /// Vista de solo lectura para que los tests puedan comprobar la invariante de
    /// longitud. Se expone la vista y no el <see cref="HashSet{T}"/> directamente:
    /// un campo mutable visible para todo el ensamblado permitiria que codigo futuro
    /// hiciera <c>Clear()</c> y desactivara la blocklist en silencio para el proceso.
    /// </summary>
    internal static IReadOnlySet<string> CommonPasswordsView => CommonPasswords;

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
