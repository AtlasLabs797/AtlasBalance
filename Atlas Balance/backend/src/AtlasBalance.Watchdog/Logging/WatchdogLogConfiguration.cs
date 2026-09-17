using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace AtlasBalance.Watchdog.Logging;

public static class WatchdogLogConfiguration
{
    public const string DefaultLogFileName = "watchdog-.log";

    public static string ResolveLogFilePath(IConfiguration configuration)
    {
        var configuredFilePath = configuration["Serilog:FilePath"];
        if (!string.IsNullOrWhiteSpace(configuredFilePath))
        {
            return ResolveAbsolutePath(configuredFilePath, "Serilog:FilePath");
        }

        return Path.Combine(
            ResolveLogDirectory(configuration["WatchdogSettings:LogDirectory"]),
            DefaultLogFileName);
    }

    public static string ResolveLogDirectory(
        string? configuredDirectory,
        string? commonApplicationDataPath = null)
    {
        var directory = configuredDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            var commonData = commonApplicationDataPath ??
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(commonData))
            {
                throw new InvalidOperationException(
                    "No se pudo resolver %ProgramData% para la ruta de logs del Watchdog.");
            }

            directory = Path.Combine(commonData, "AtlasBalance", "logs");
        }

        return ResolveAbsolutePath(directory, "WatchdogSettings:LogDirectory");
    }

    public static string ResolveStateFilePath(IConfiguration configuration)
    {
        var configuredPath = configuration["WatchdogSettings:StateFilePath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(commonData))
            {
                throw new InvalidOperationException(
                    "No se pudo resolver %ProgramData% para el estado del Watchdog.");
            }

            configuredPath = Path.Combine(commonData, "AtlasBalance", "watchdog-state.json");
        }

        return ResolveAbsolutePath(configuredPath, "WatchdogSettings:StateFilePath");
    }

    public static void EnsureLogDirectory(string logFilePath)
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("La ruta de log debe incluir un directorio absoluto.");
        }

        Directory.CreateDirectory(directory);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ApplyDirectoryAcl(directory);
        foreach (var existingLog in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            ApplyFileAcl(existingLog);
        }
    }

    public static void EnsureStatePath(string stateFilePath)
    {
        var directory = Path.GetDirectoryName(stateFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("La ruta de estado debe incluir un directorio absoluto.");
        }

        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ApplyDirectoryAcl(directory);
        if (File.Exists(stateFilePath))
        {
            ApplyFileAcl(stateFilePath);
        }
    }

    private static string ResolveAbsolutePath(string rawPath, string settingName)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        if (!Path.IsPathRooted(expandedPath))
        {
            throw new InvalidOperationException(
                $"{settingName} debe ser una ruta absoluta; no se acepta una ruta relativa.");
        }

        try
        {
            return Path.GetFullPath(expandedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"{settingName} no es una ruta valida.", ex);
        }
    }

    private static void AddAccessRule(
        DirectorySecurity security,
        IdentityReference identity,
        FileSystemRights rights)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyDirectoryAcl(string directory)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl);
        AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl);

        var currentIdentity = WindowsIdentity.GetCurrent().User;
        if (currentIdentity is null)
        {
            throw new InvalidOperationException("No se pudo resolver la identidad del servicio para proteger el directorio.");
        }

        AddAccessRule(security, currentIdentity, FileSystemRights.Modify);
        try
        {
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            // En una instalacion existente la DACL la fija el instalador y el
            // servicio no debe tener WRITE_DAC. En ese caso solo se permite
            // continuar si la ACL actual ya es exactamente una allowlist
            // segura; una ACL insegura bloquea el arranque.
            VerifyAllowlistedAcl(new DirectoryInfo(directory).GetAccessControl(), directory);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyFileAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        var currentIdentity = WindowsIdentity.GetCurrent().User;
        if (currentIdentity is null)
        {
            throw new InvalidOperationException("No se pudo resolver la identidad del servicio para proteger el fichero.");
        }

        security.AddAccessRule(new FileSystemAccessRule(
            currentIdentity,
            FileSystemRights.Modify,
            AccessControlType.Allow));
        try
        {
            new FileInfo(path).SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            VerifyAllowlistedAcl(new FileInfo(path).GetAccessControl(), path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyAllowlistedAcl(FileSystemSecurity security, string path)
    {
        if (!security.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException($"La ACL de '{path}' permite herencia no controlada.");
        }

        var currentSid = WindowsIdentity.GetCurrent().User?.Value;
        var allowedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
        };
        if (!string.IsNullOrWhiteSpace(currentSid))
        {
            allowedSids.Add(currentSid);
        }

        var ownerSid = (security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(ownerSid) || !allowedSids.Contains(ownerSid))
        {
            throw new UnauthorizedAccessException($"El propietario de '{path}' no pertenece a la allowlist protegida.");
        }

        foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule is FileSystemAccessRule fileRule &&
                fileRule.AccessControlType == AccessControlType.Allow &&
                !allowedSids.Contains(((SecurityIdentifier)fileRule.IdentityReference).Value))
            {
                throw new UnauthorizedAccessException($"La ACL de '{path}' concede acceso a una identidad no autorizada.");
            }
        }
    }
}
