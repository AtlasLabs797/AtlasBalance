using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AtlasBalance.Watchdog.Services;

internal static class ElevatedUpdateRunner
{
    public static bool IsRequested(string[] args) =>
        args.Any(arg => string.Equals(arg, "--run-elevated-update", StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
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
        var resolvedPackageRoot = packageRoot!;
        var resolvedPackageZipPath = packageZipPath!;
        var resolvedRequestedInstallPath = requestedInstallPath!;
        if (!PathsEqual(resolvedInstallPath, resolvedRequestedInstallPath) ||
            !IsWithinRoot(resolvedPackageRoot, Path.Combine(resolvedInstallPath, "updates")) ||
            !IsWithinRoot(resolvedPackageZipPath, Path.Combine(resolvedInstallPath, "updates")) ||
            !Directory.Exists(resolvedPackageRoot) || !File.Exists(resolvedPackageZipPath))
        {
            return 3;
        }

        var signatureError = VerifyPackageSignature(resolvedPackageZipPath, Path.Combine(resolvedInstallPath, "watchdog", "appsettings.Production.json"));
        if (signatureError is not null)
        {
            return 4;
        }

        var updaterScript = Path.Combine(resolvedPackageRoot, "scripts", "Actualizar-AtlasBalance.ps1");
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
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updaterScript}\" -InstallPath \"{resolvedInstallPath}\" -PackageRoot \"{resolvedPackageRoot}\" -ElevatedUpdate"
            }
        };
        if (!process.Start())
        {
            return 7;
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
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
