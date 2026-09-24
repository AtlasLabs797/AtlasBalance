using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// SECURITY (P1a/P1b/P3): ElevatedUpdateRunner corre como SYSTEM via la
/// tarea programada AtlasBalance.Update y es el unico punto donde una
/// solicitud escrita por la cuenta de Watchdog (baja privilegios) se
/// traduce en la ejecucion de un script con privilegios elevados. Estos
/// tests fijan, via reflexion sobre el metodo interno RunAsync, que:
/// - nunca se ejecuta nada desde el PackageRoot de la solicitud (escribible
///   por Watchdog), solo desde la copia verificada del ZIP firmado;
/// - una firma invalida o ausente aborta antes de extraer o ejecutar nada;
/// - rutas fuera de InstallPath\updates se rechazan.
/// </summary>
public sealed class ElevatedUpdateRunnerTests : IDisposable
{
    private readonly string _installPath;
    private readonly RSA _signingKey = RSA.Create(2048);

    public ElevatedUpdateRunnerTests()
    {
        _installPath = Path.Combine(Path.GetTempPath(), "AtlasElevatedUpdateRunnerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installPath);

        var watchdogDirectory = Directory.CreateDirectory(Path.Combine(_installPath, "watchdog"));
        var watchdogConfig = JsonSerializer.Serialize(new
        {
            UpdateSecurity = new { ReleaseSigningPublicKeyPem = _signingKey.ExportSubjectPublicKeyInfoPem() }
        });
        File.WriteAllText(Path.Combine(watchdogDirectory.FullName, "appsettings.Production.json"), watchdogConfig);
    }

    public void Dispose()
    {
        _signingKey.Dispose();
        try { Directory.Delete(_installPath, recursive: true); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public async Task RunAsync_Should_Reject_PackageZipPath_Outside_Updates_Folder()
    {
        var outsideZip = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(outsideZip, CreateZipBytes(scriptContent: "exit 0"));
        try
        {
            var exitCode = await InvokeRunAsync(new
            {
                PackageRoot = Path.Combine(_installPath, "updates", "V-99.00"),
                PackageZipPath = outsideZip,
                InstallPath = _installPath
            });

            exitCode.Should().Be(3, "un ZIP fuera de InstallPath\\updates debe rechazarse antes de tocarlo");
        }
        finally
        {
            File.Delete(outsideZip);
        }
    }

    [Fact]
    public async Task RunAsync_Should_Reject_Missing_Signature_File()
    {
        var (zipPath, packageRoot) = CreateUpdatesLayout("V-99.01", scriptContent: "exit 0", writeSignature: false);

        var exitCode = await InvokeRunAsync(new
        {
            PackageRoot = packageRoot,
            PackageZipPath = zipPath,
            InstallPath = _installPath
        });

        exitCode.Should().Be(4, "sin fichero .sig junto al ZIP no hay nada que verificar");
    }

    [Fact]
    public async Task RunAsync_Should_Reject_Invalid_Signature()
    {
        var (zipPath, packageRoot) = CreateUpdatesLayout("V-99.02", scriptContent: "exit 0", writeSignature: true);

        // Firma de OTRA clave: no corresponde al ZIP ni a la clave publica configurada.
        using var otherKey = RSA.Create(2048);
        var zipBytes = File.ReadAllBytes(zipPath);
        var badSignature = otherKey.SignData(zipBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        File.WriteAllBytes(zipPath + ".sig", badSignature);

        var exitCode = await InvokeRunAsync(new
        {
            PackageRoot = packageRoot,
            PackageZipPath = zipPath,
            InstallPath = _installPath
        });

        exitCode.Should().Be(4, "una firma que no valida con la clave publica configurada debe rechazarse");
    }

    [Fact]
    public async Task RunAsync_Should_Never_Execute_The_Script_From_The_Requested_PackageRoot()
    {
        // El ZIP firmado (lo unico verificado) NO trae scripts\Actualizar-AtlasBalance.ps1.
        var (zipPath, packageRoot) = CreateUpdatesLayout("V-99.03", scriptContent: null, writeSignature: true);

        // El PackageRoot de la solicitud (escribible por Watchdog) SI trae un script,
        // que si se llegase a ejecutar dejaria un fichero centinela.
        var sentinelPath = Path.Combine(_installPath, "sentinel-tampered-root-executed.txt");
        var maliciousScriptDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "scripts"));
        File.WriteAllText(
            Path.Combine(maliciousScriptDirectory.FullName, "Actualizar-AtlasBalance.ps1"),
            $"New-Item -ItemType File -Path '{sentinelPath}' -Force | Out-Null");

        var exitCode = await InvokeRunAsync(new
        {
            PackageRoot = packageRoot,
            PackageZipPath = zipPath,
            InstallPath = _installPath
        });

        exitCode.Should().Be(5, "el script solo existe en el PackageRoot manipulado, no en la copia verificada del ZIP");
        File.Exists(sentinelPath).Should().BeFalse("nunca debe ejecutarse nada fuera de la copia verificada del ZIP firmado");
    }

    [Fact]
    public async Task RunAsync_Should_Execute_The_Script_From_The_Verified_Zip_Copy_Not_PackageRoot()
    {
        // El ZIP firmado SI trae un script propio, que deja su propio centinela al ejecutarse.
        var verifiedSentinelPath = Path.Combine(_installPath, "sentinel-verified-copy-executed.txt");
        var (zipPath, packageRoot) = CreateUpdatesLayout(
            "V-99.04",
            scriptContent: BuildScriptWithSentinel(verifiedSentinelPath),
            writeSignature: true);

        // El PackageRoot de la solicitud trae un script DISTINTO (manipulado) con su propio centinela.
        var tamperedSentinelPath = Path.Combine(_installPath, "sentinel-tampered-root-executed.txt");
        var maliciousScriptDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "scripts"));
        File.WriteAllText(
            Path.Combine(maliciousScriptDirectory.FullName, "Actualizar-AtlasBalance.ps1"),
            BuildScriptWithSentinel(tamperedSentinelPath));

        var exitCode = await InvokeRunAsync(new
        {
            PackageRoot = packageRoot,
            PackageZipPath = zipPath,
            InstallPath = _installPath
        });

        exitCode.Should().Be(0, "el script de la copia verificada es valido y debe ejecutarse hasta completar");
        File.Exists(verifiedSentinelPath).Should().BeTrue("debe ejecutarse el script de la copia verificada del ZIP firmado");
        File.Exists(tamperedSentinelPath).Should().BeFalse("el script del PackageRoot manipulado nunca debe ejecutarse");

        var runnerWorkDirectory = Path.Combine(_installPath, "config", "update-runner");
        if (Directory.Exists(runnerWorkDirectory))
        {
            Directory.GetDirectories(runnerWorkDirectory, "verified-*")
                .Should().BeEmpty("la carpeta de trabajo aislada debe limpiarse tras ejecutar, exista o no config\\update-runner");
        }
    }

    private (string ZipPath, string PackageRoot) CreateUpdatesLayout(string version, string? scriptContent, bool writeSignature)
    {
        var updatesRoot = Path.Combine(_installPath, "updates");
        Directory.CreateDirectory(updatesRoot);
        var zipPath = Path.Combine(updatesRoot, $"{version}.zip");
        var packageRoot = Path.Combine(updatesRoot, version);
        Directory.CreateDirectory(packageRoot);

        var zipBytes = CreateZipBytes(scriptContent);
        File.WriteAllBytes(zipPath, zipBytes);
        if (writeSignature)
        {
            var signature = _signingKey.SignData(zipBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            File.WriteAllBytes(zipPath + ".sig", signature);
        }

        return (zipPath, packageRoot);
    }

    // El runner invoca el script con -InstallPath/-PackageRoot/-ElevatedUpdate
    // nombrados (igual que Actualizar-AtlasBalance.ps1 real); sin un bloque
    // param() que los declare, PowerShell rechaza esos parametros con nombre
    // y el script de prueba fallaria antes de dejar su centinela.
    private static string BuildScriptWithSentinel(string sentinelPath) =>
        "param([string]$InstallPath,[string]$PackageRoot,[switch]$ElevatedUpdate)" +
        $"\nNew-Item -ItemType File -Path '{sentinelPath}' -Force | Out-Null";

    private static byte[] CreateZipBytes(string? scriptContent)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "VERSION", "V-99.00");
            AddZipEntry(archive, "api/AtlasBalance.API.exe", "api");
            AddZipEntry(archive, "watchdog/AtlasBalance.Watchdog.exe", "watchdog");
            if (scriptContent is not null)
            {
                AddZipEntry(archive, "scripts/Actualizar-AtlasBalance.ps1", scriptContent);
            }
        }

        return stream.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private async Task<int> InvokeRunAsync(object request)
    {
        var requestPath = Path.Combine(_installPath, "pending-update.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request));

        var runnerType = typeof(AtlasBalance.Watchdog.Services.WatchdogOperationsService).Assembly.GetType(
            "AtlasBalance.Watchdog.Services.ElevatedUpdateRunner");
        runnerType.Should().NotBeNull();

        var runAsync = runnerType!.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        runAsync.Should().NotBeNull();

        var args = new[] { "--run-elevated-update", "--install-path", _installPath, "--request-path", requestPath };
        var task = (Task<int>)runAsync!.Invoke(null, [args, CancellationToken.None])!;
        return await task;
    }
}
