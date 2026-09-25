using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using AtlasBalance.Shared.Packaging;
using AtlasBalance.Watchdog.Models;

namespace AtlasBalance.Watchdog.Services;

internal static class ElevatedUpdateRunner
{
    public static bool IsRequested(string[] args) =>
        args.Any(arg => string.Equals(arg, "--run-elevated-update", StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        int exitCode;
        try
        {
            exitCode = await RunCoreAsync(args, cancellationToken);
        }
        catch
        {
            exitCode = 1;
        }

        // Watchdog solo dispara la tarea programada y deja el estado en
        // RUNNING. El resultado final lo conoce este proceso, asi que lo
        // persiste aqui; si no, /estado quedaria en RUNNING para siempre.
        TryWriteTerminalState(ReadArgument(args, "--install-path"), exitCode);
        return exitCode;
    }

    private static async Task<int> RunCoreAsync(string[] args, CancellationToken cancellationToken)
    {
        var installPath = ReadArgument(args, "--install-path");
        var requestPath = ReadArgument(args, "--request-path");
        if (!IsAbsolute(installPath) || !IsAbsolute(requestPath) || !File.Exists(requestPath))
        {
            return 2;
        }

        var request = JsonNode.Parse(await File.ReadAllTextAsync(requestPath, cancellationToken)) as JsonObject;
        var packageRoot = request?["PackageRoot"]?.GetValue<string>();
        var packageZipPath = request?["PackageZipPath"]?.GetValue<string>();
        var requestedInstallPath = request?["InstallPath"]?.GetValue<string>();
        if (!IsAbsolute(installPath) || !IsAbsolute(requestPath) ||
            !IsAbsolute(packageRoot) || !IsAbsolute(packageZipPath) || !IsAbsolute(requestedInstallPath))
        {
            return 3;
        }

        var resolvedInstallPath = installPath!;
        // SECURITY (P1a): PackageRoot en la solicitud lo escribio la cuenta
        // de Watchdog (baja privilegios). Solo se usa aqui como comprobacion
        // de cordura de que apunta dentro de updates\; NUNCA se ejecuta nada
        // desde esa ruta, porque Watchdog tiene permiso de escritura sobre
        // updates\requests y podria haber dejado un ZIP legitimo firmado
        // junto a una carpeta ya extraida y manipulada. Lo unico que se
        // ejecuta es la copia verificada mas abajo.
        var resolvedPackageRoot = packageRoot!;
        var resolvedPackageZipPath = packageZipPath!;
        var resolvedRequestedInstallPath = requestedInstallPath!;
        if (!PathsEqual(resolvedInstallPath, resolvedRequestedInstallPath) ||
            !IsWithinRoot(resolvedPackageRoot, Path.Combine(resolvedInstallPath, "updates")) ||
            !IsWithinRoot(resolvedPackageZipPath, Path.Combine(resolvedInstallPath, "updates")) ||
            !File.Exists(resolvedPackageZipPath))
        {
            return 3;
        }

        // SECURITY (P1a): en vez de confiar en PackageRoot (ya extraido en
        // una carpeta que Watchdog puede escribir), se copia el ZIP+firma a
        // una carpeta nueva dentro de config\update-runner\ (solo
        // Administrators/SYSTEM tienen acceso ahi; este proceso corre como
        // SYSTEM via la tarea programada), se verifica la firma SOBRE ESA
        // COPIA y se extrae ESA MISMA COPIA verificada. Lo que se ejecuta a
        // continuacion sale siempre de esta carpeta aislada, nunca de
        // PackageRoot ni de cualquier otra ruta escribible por Watchdog.
        var runnerRoot = Path.Combine(resolvedInstallPath, "config", "update-runner");
        var verifiedRoot = Path.Combine(runnerRoot, "verified-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(verifiedRoot);
            var verifiedZipPath = Path.Combine(verifiedRoot, "package.zip");
            var sourceSignaturePath = resolvedPackageZipPath + ".sig";
            if (!File.Exists(sourceSignaturePath))
            {
                return 4;
            }

            File.Copy(resolvedPackageZipPath, verifiedZipPath, overwrite: true);
            File.Copy(sourceSignaturePath, verifiedZipPath + ".sig", overwrite: true);

            var signatureError = VerifyPackageSignature(verifiedZipPath, Path.Combine(resolvedInstallPath, "watchdog", "appsettings.Production.json"));
            if (signatureError is not null)
            {
                return 4;
            }

            var extractedRoot = Path.Combine(verifiedRoot, "extracted");
            if (!PackageExtraction.TryExtractSafely(verifiedZipPath, extractedRoot))
            {
                return 8;
            }

            var verifiedPackageRoot = ResolveExtractedPackageRoot(extractedRoot);
            var updaterScript = Path.Combine(verifiedPackageRoot, "scripts", "Actualizar-AtlasBalance.ps1");
            if (!File.Exists(updaterScript))
            {
                return 5;
            }

            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var powershell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell))
            {
                return 6;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = powershell,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updaterScript}\" -InstallPath \"{resolvedInstallPath}\" -PackageRoot \"{verifiedPackageRoot}\" -ElevatedUpdate"
                }
            };
            if (!process.Start())
            {
                return 7;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        finally
        {
            TryDeleteDirectory(verifiedRoot);
            // La copia que Watchdog preparo en updates\requests ya no hace
            // falta: lo que se ejecuta sale siempre de verifiedRoot.
            if (IsWithinRoot(resolvedPackageZipPath, Path.Combine(resolvedInstallPath, "updates", "requests")))
            {
                TryDeleteFile(resolvedPackageZipPath);
                TryDeleteFile(resolvedPackageZipPath + ".sig");
            }
        }
    }

    private static void TryWriteTerminalState(string? installPath, int exitCode)
    {
        try
        {
            if (!IsAbsolute(installPath))
            {
                return;
            }

            // SECURITY: la ruta sale de la configuracion protegida del
            // Watchdog (solo lectura para su cuenta), nunca de la solicitud,
            // que la escribe una cuenta de baja privilegio. Sin ruta
            // configurada no se escribe nada.
            var configPath = Path.Combine(installPath!, "watchdog", "appsettings.Production.json");
            if (!File.Exists(configPath))
            {
                return;
            }

            var configuration = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
            var configuredStatePath = configuration?["WatchdogSettings"]?["StateFilePath"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(configuredStatePath))
            {
                return;
            }

            var stateFilePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredStatePath));
            var directory = Path.GetDirectoryName(stateFilePath);
            if (!Path.IsPathRooted(stateFilePath) || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var state = new WatchdogState
            {
                Estado = exitCode == 0 ? "SUCCESS" : "FAILED",
                Operacion = "UPDATE_APP",
                Mensaje = exitCode == 0
                    ? "Actualizacion completada"
                    : $"Actualizacion protegida fallo (codigo {exitCode}). Revise los logs protegidos del servidor.",
                UpdatedAt = DateTime.UtcNow
            };

            var temporaryPath = $"{stateFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                if (OperatingSystem.IsWindows())
                {
                    ProtectStateFileLikeDirectory(temporaryPath, directory);
                }

                File.Move(temporaryPath, stateFilePath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }
        catch
        {
            // El resultado de la actualizacion no depende de poder publicar
            // el estado; queda en los logs del propio actualizador.
        }
    }

    // El fichero lo crea SYSTEM y Watchdog no podra reescribir su DACL. Si
    // quedara con herencia, WatchdogLogConfiguration.EnsureStatePath lo
    // rechazaria al arrancar. Se fija una DACL protegida con las mismas
    // identidades que ya tiene el directorio de estado.
    [SupportedOSPlatform("windows")]
    private static void ProtectStateFileLikeDirectory(string filePath, string directory)
    {
        var directorySecurity = new DirectoryInfo(directory).GetAccessControl();
        var fileSecurity = new FileSecurity();
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        foreach (FileSystemAccessRule rule in directorySecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow)
            {
                fileSecurity.AddAccessRule(new FileSystemAccessRule(rule.IdentityReference, rule.FileSystemRights, AccessControlType.Allow));
            }
        }

        new FileInfo(filePath).SetAccessControl(fileSecurity);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string ResolveExtractedPackageRoot(string extractionRoot)
    {
        if (IsValidReleasePackage(extractionRoot))
        {
            return extractionRoot;
        }

        var children = Directory.Exists(extractionRoot) ? Directory.GetDirectories(extractionRoot) : [];
        return children.Length == 1 ? children[0] : extractionRoot;
    }

    private static bool IsValidReleasePackage(string packageRoot) =>
        File.Exists(Path.Combine(packageRoot, "VERSION")) &&
        File.Exists(Path.Combine(packageRoot, "api", "AtlasBalance.API.exe")) &&
        File.Exists(Path.Combine(packageRoot, "watchdog", "AtlasBalance.Watchdog.exe")) &&
        File.Exists(Path.Combine(packageRoot, "scripts", "Actualizar-AtlasBalance.ps1"));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string? ReadArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool IsAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? VerifyPackageSignature(string zipPath, string apiConfigPath)
    {
        if (!File.Exists(apiConfigPath) || !File.Exists(zipPath + ".sig"))
        {
            return "missing-signature-assets";
        }

        var configuration = JsonNode.Parse(File.ReadAllText(apiConfigPath)) as JsonObject;
        var publicKey = configuration?["UpdateSecurity"]?["ReleaseSigningPublicKeyPem"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return "missing-public-key";
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        var signature = File.ReadAllBytes(zipPath + ".sig");
        using var content = File.OpenRead(zipPath);
        return rsa.VerifyData(content, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            ? null
            : "invalid-signature";
    }
}
