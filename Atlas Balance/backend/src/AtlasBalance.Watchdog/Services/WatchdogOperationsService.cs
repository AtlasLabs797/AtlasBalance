using System.Diagnostics;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;
using AtlasBalance.Watchdog.Logging;
using AtlasBalance.Watchdog.Models;

namespace AtlasBalance.Watchdog.Services;

public interface IWatchdogOperationsService
{
    Task<bool> StartRestoreAsync(string backupPath, CancellationToken cancellationToken);
    Task<bool> StartRestoreAsync(string backupPath, Guid operationId, CancellationToken cancellationToken) =>
        StartRestoreAsync(backupPath, cancellationToken);
    Task<bool> StartUpdateAsync(string? sourcePath, string? targetPath, string? packageZipPath, CancellationToken cancellationToken);
}

public sealed class WatchdogOperationsService : IWatchdogOperationsService
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> PreservedTopLevelDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs"
    };

    private static readonly string[] PackageScriptFiles =
    [
        "Actualizar-AtlasBalance.ps1",
        "Instalar-AtlasBalance.ps1",
        "Launch-AtlasBalance.ps1",
        "Reset-AdminPassword.ps1",
        "install-cert-client.ps1",
        "install.ps1",
        "start.ps1",
        "uninstall-services.ps1",
        "uninstall.ps1",
        "update.ps1"
    ];

    private static readonly string[] PackageRootFiles =
    [
        "Atlas Balance.cmd",
        "Actualizar Atlas Balance.cmd",
        "Instalar Atlas Balance.cmd",
        "install.cmd",
        "start.cmd",
        "uninstall.cmd",
        "update.cmd"
    ];

    private readonly IConfiguration _configuration;
    private readonly IWatchdogStateStore _stateStore;
    private readonly ILogger<WatchdogOperationsService> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public WatchdogOperationsService(
        IConfiguration configuration,
        IWatchdogStateStore stateStore,
        ILogger<WatchdogOperationsService> logger)
    {
        _configuration = configuration;
        _stateStore = stateStore;
        _logger = logger;
    }

    public Task<bool> StartRestoreAsync(string backupPath, CancellationToken cancellationToken) =>
        StartRestoreAsync(backupPath, Guid.Empty, cancellationToken);

    public async Task<bool> StartRestoreAsync(string backupPath, Guid operationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return false;
        }

        if (!IsAllowedBackupPath(backupPath))
        {
            return false;
        }

        string fullBackupPath;
        try
        {
            fullBackupPath = Path.GetFullPath(backupPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!File.Exists(fullBackupPath))
        {
            return false;
        }

        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        // SECURITY (C3, V-02-03): si la escritura del estado falla ANTES
        // de lanzar la Task.Run, el lock se queda tomado para siempre.
        // Soltarlo explicitamente para no dejar el Watchdog muerto.
        try
        {
            await _stateStore.SetAsync(
                CreateState("RUNNING", "RESTORE_BACKUP", "Restauracion en progreso", operationId),
                cancellationToken);
        }
        catch
        {
            _operationLock.Release();
            throw;
        }

        _ = Task.Run(async () =>
        {
            var finalState = CreateState("FAILED", "RESTORE_BACKUP", "Operacion interrumpida", operationId);
            try
            {
                await StopApiServiceSafeAsync(CancellationToken.None);
                var restoreResult = await RunPgRestoreAsync(fullBackupPath, CancellationToken.None);
                finalState = restoreResult.Success
                    ? CreateState("SUCCESS", "RESTORE_BACKUP", "Restauracion completada", operationId)
                    : CreateState("FAILED", "RESTORE_BACKUP", "Error en pg_restore. Revise los logs protegidos del servidor.", operationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore operation failed");
                finalState = CreateState("FAILED", "RESTORE_BACKUP", "Error inesperado en restauracion. Revise los logs protegidos del servidor.", operationId);
            }
            finally
            {
                try
                {
                    await StartApiServiceSafeAsync(CancellationToken.None);
                    await _stateStore.SetAsync(finalState, CancellationToken.None);
                }
                finally
                {
                    _operationLock.Release();
                }
            }
        });

        return true;
    }

    public async Task<bool> StartUpdateAsync(string? sourcePath, string? targetPath, string? packageZipPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.IsNullOrWhiteSpace(targetPath) ||
            string.IsNullOrWhiteSpace(packageZipPath) ||
            !TryGetFullPath(sourcePath, out var fullSourcePath) ||
            !TryGetFullPath(targetPath, out var fullTargetPath) ||
            !TryGetFullPath(packageZipPath, out _) ||
            !Directory.Exists(fullSourcePath))
        {
            return false;
        }

        if (string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase) ||
            !IsAllowedUpdateSourcePath(fullSourcePath) ||
            !IsAllowedUpdateInstallPath(fullTargetPath) ||
            !IsValidReleasePackage(fullSourcePath))
        {
            return false;
        }

        // V-02-05 (CRIT-3): si la API nos pasa el path al ZIP original, lo verificamos
        // antes de aplicar la actualizacion. La verificacion es obligatoria: si
        // el campo viene vacio o falta la clave publica configurada, se rechaza
        // el update (fail-closed). Era el bypass que permitia instalar paquetes
        // sin firma cuando faltaban los assets.
        var zipVerification = VerifyPackageZipIntegrity(packageZipPath);
        if (zipVerification is not null)
        {
            // V-02-06 (CodeQL #13): sanear {ReasonSafe} para evitar CWE-117 (log forging).
            // zipVerification se construye a partir de packageZipPath, que llega del caller API.
            _logger.LogError("Update rechazado por verificacion de integridad del ZIP: {ReasonSafe}", LogScrubber.Scrub(zipVerification));
            return false;
        }

        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        // SECURITY (C3, V-02-03): si la escritura del estado falla ANTES
        // de lanzar la Task.Run, el lock se queda tomado para siempre.
        // Soltarlo explicitamente para no dejar el Watchdog muerto.
        try
        {
            await _stateStore.SetAsync(
                CreateState("RUNNING", "UPDATE_APP", "Actualizacion en progreso"),
                cancellationToken);
        }
        catch
        {
            _operationLock.Release();
            throw;
        }

        _ = Task.Run(async () =>
        {
            var finalState = CreateState("FAILED", "UPDATE_APP", "Operacion interrumpida");
            string? rollbackPath = null;
            var apiStartedInOperation = false;
            var externalUpdater = ShouldUseExternalPackageUpdater(fullTargetPath);
            try
            {
                if (externalUpdater)
                {
                    var updateResult = await RunPackageUpdateViaHelperAsync(fullSourcePath, fullTargetPath, CancellationToken.None);
                    finalState = updateResult.Success
                        ? CreateState("SUCCESS", "UPDATE_APP", "Actualizacion completada")
                        : CreateState("FAILED", "UPDATE_APP", "Actualizacion externa fallo. Revise los logs protegidos del servidor.");
                    return;
                }

                if (RequireDatabaseBackupBeforeUpdate())
                {
                    var backupResult = await CreateDatabaseBackupAsync(CancellationToken.None);
                    if (!backupResult.Success)
                    {
                        finalState = CreateState("FAILED", "UPDATE_APP", "No se actualiza sin backup previo de base de datos. Revise los logs protegidos del servidor.");
                        return;
                    }
                }

                await StopApiServiceSafeAsync(CancellationToken.None);
                rollbackPath = CreatePackageRollbackCopy(fullTargetPath);
                ApplyReleasePackage(fullSourcePath, fullTargetPath);
                await StartApiServiceSafeAsync(CancellationToken.None);
                apiStartedInOperation = true;

                if (RequireHealthCheckAfterUpdate() &&
                    !await WaitForApiHealthAsync(CancellationToken.None))
                {
                    finalState = CreateState("FAILED", "UPDATE_APP", "Health check fallo tras actualizar; rollback de binarios aplicado.");
                    await StopApiServiceSafeAsync(CancellationToken.None);
                    TryRestorePackageRollback(rollbackPath, fullTargetPath);
                    await StartApiServiceSafeAsync(CancellationToken.None);
                    return;
                }

                finalState = CreateState("SUCCESS", "UPDATE_APP", "Actualizacion completada");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update operation failed");
                TryRestorePackageRollback(rollbackPath, fullTargetPath);
                finalState = CreateState("FAILED", "UPDATE_APP", "Error inesperado en actualizacion. Revise los logs protegidos del servidor.");
            }
            finally
            {
                try
                {
                    if (!apiStartedInOperation && !externalUpdater)
                    {
                        await StartApiServiceSafeAsync(CancellationToken.None);
                    }

                    await _stateStore.SetAsync(finalState, CancellationToken.None);
                }
                finally
                {
                    _operationLock.Release();
                }
            }
        });

        return true;
    }

    private async Task<(bool Success, string? Error)> RunPgRestoreAsync(string backupPath, CancellationToken cancellationToken)
    {
        var executable = ResolveConfiguredExecutable(_configuration["WatchdogSettings:PostgresBinPath"], "pg_restore.exe");

        var dbHost = _configuration["WatchdogSettings:DbHost"] ?? "localhost";
        var dbPort = int.TryParse(_configuration["WatchdogSettings:DbPort"], out var parsedPort) ? parsedPort : 5432;
        var dbName = _configuration["WatchdogSettings:DbName"] ?? "atlas_balance";
        var dbUser = _configuration["WatchdogSettings:DbUser"] ?? "app_user";
        var dbPassword = _configuration["WatchdogSettings:DbPassword"];
        if (string.IsNullOrWhiteSpace(dbPassword))
        {
            return (false, "WatchdogSettings:DbPassword no configurado");
        }

        var localArgs = new List<string>
        {
            "-h", dbHost,
            "-p", dbPort.ToString(),
            "-U", dbUser,
            "-d", dbName,
            "--clean",
            "--if-exists",
            "-v",
            backupPath
        };
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var localResult = await RunProcessAsync(executable, localArgs, dbPassword, cancellationToken);
            if (localResult.Success)
            {
                return (true, null);
            }

            _logger.LogWarning("pg_restore local fallo: {Error}. Se intentara fallback docker.", localResult.ErrorMessage);
        }
        else
        {
            _logger.LogWarning("pg_restore local omitido: WatchdogSettings:PostgresBinPath no apunta a pg_restore.exe absoluto.");
        }

        return await RunPgRestoreViaDockerAsync(backupPath, dbUser, dbName, cancellationToken);
    }

    private async Task StopApiServiceSafeAsync(CancellationToken cancellationToken)
    {
        var serviceName = _configuration["WatchdogSettings:ApiServiceName"] ?? "AtlasBalance.API";
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("StopApiServiceSafeAsync omitido: host no Windows");
            return;
        }

        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                service.Stop();
                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo detener servicio {ServiceName}. Continuando.", serviceName);
        }
    }

    private async Task StartApiServiceSafeAsync(CancellationToken cancellationToken)
    {
        var serviceName = _configuration["WatchdogSettings:ApiServiceName"] ?? "AtlasBalance.API";
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("StartApiServiceSafeAsync omitido: host no Windows");
            return;
        }

        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                service.Start();
                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo iniciar servicio {ServiceName}. Continuando.", serviceName);
        }
    }

    private static void SyncDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        var sourceFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        var sourceFileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in sourceFiles)
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            sourceFileSet.Add(relative);
            var destination = Path.Combine(targetPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        foreach (var targetFile in Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(targetPath, targetFile);
            if (sourceFileSet.Contains(relative) || IsPreservedRelativePath(relative))
            {
                continue;
            }

            File.Delete(targetFile);
        }

        var sourceDirectories = Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourcePath, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var targetDirectory in Directory.GetDirectories(targetPath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            var relative = Path.GetRelativePath(targetPath, targetDirectory);
            if (sourceDirectories.Contains(relative) || IsPreservedRelativePath(relative))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            {
                Directory.Delete(targetDirectory, recursive: false);
            }
        }
    }

    private static WatchdogState CreateState(string estado, string operacion, string mensaje, Guid? operationId = null) =>
        new()
        {
            Estado = estado,
            Operacion = operacion,
            OperationId = operationId,
            Mensaje = mensaje,
            UpdatedAt = DateTime.UtcNow
        };

    private static string Trim(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static bool PathsOverlap(string sourcePath, string targetPath)
    {
        var sourceWithSeparator = EnsureTrailingSeparator(sourcePath);
        var targetWithSeparator = EnsureTrailingSeparator(targetPath);

        return sourceWithSeparator.StartsWith(targetWithSeparator, StringComparison.OrdinalIgnoreCase) ||
               targetWithSeparator.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }

    private static bool IsPreservedRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (PreservedTopLevelDirectories.Contains(segments[0]))
        {
            return true;
        }

        var fileName = Path.GetFileName(relativePath);
        return fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAllowedUpdateSourcePath(string sourcePath)
    {
        var sourceRoot = _configuration["WatchdogSettings:UpdateSourceRoot"] ?? @"C:\AtlasBalance\updates";
        return IsPathWithinRoot(sourcePath, sourceRoot);
    }

    private bool IsAllowedUpdateInstallPath(string targetPath)
    {
        var configuredTarget = ResolveConfiguredUpdateInstallPath();
        return PathsEqual(targetPath, configuredTarget);
    }

    private string ResolveConfiguredUpdateInstallPath()
    {
        var installPath = _configuration["WatchdogSettings:UpdateInstallPath"];
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            return installPath.Trim();
        }

        var configuredTarget = _configuration["WatchdogSettings:UpdateTargetPath"] ?? @"C:\AtlasBalance\api";
        return TryDeriveInstallPathFromLegacyTarget(configuredTarget.Trim());
    }

    private static string TryDeriveInstallPathFromLegacyTarget(string configuredTarget)
    {
        try
        {
            var fullPath = Path.GetFullPath(configuredTarget);
            var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
            if (leaf.Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath)) ?? configuredTarget;
            }
        }
        catch
        {
            return configuredTarget;
        }

        return configuredTarget;
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        if (!IsExplicitlyRooted(path) || !IsExplicitlyRooted(root))
        {
            return false;
        }

        try
        {
            var fullPath = EnsureTrailingSeparator(Path.GetFullPath(path));
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (!IsExplicitlyRooted(left) || !IsExplicitlyRooted(right))
        {
            return false;
        }

        try
        {
            var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetFullPath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private async Task<(bool Success, string? Error)> RunPgRestoreViaDockerAsync(
        string backupPath,
        string dbUser,
        string dbName,
        CancellationToken cancellationToken)
    {
        var dockerExe = ResolveDockerExecutable();
        if (string.IsNullOrWhiteSpace(dockerExe))
        {
            return (false, "Docker fallback no configurado con ruta absoluta.");
        }

        var container = _configuration["WatchdogSettings:DockerPostgresContainer"] ?? "atlas_balance_db";
        var containerFile = $"/tmp/{Guid.NewGuid():N}.dump";

        var cpIn = await RunProcessAsync(dockerExe, ["cp", backupPath, $"{container}:{containerFile}"], null, cancellationToken);
        if (!cpIn.Success)
        {
            return (false, $"Fallback docker copy-in fallo: {cpIn.ErrorMessage}");
        }

        var restore = await RunProcessAsync(
            dockerExe,
            ["exec", container, "pg_restore", "-U", dbUser, "-d", dbName, "--clean", "--if-exists", "-v", containerFile],
            null,
            cancellationToken);
        await RunProcessAsync(dockerExe, ["exec", container, "rm", "-f", containerFile], null, cancellationToken);
        return restore.Success
            ? (true, null)
            : (false, $"Fallback docker restore fallo: {restore.ErrorMessage}");
    }

    private bool IsAllowedBackupPath(string backupPath)
    {
        if (!IsExplicitlyRooted(backupPath))
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(backupPath), ".dump", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var backupRoot = _configuration["WatchdogSettings:BackupPath"] ?? @"C:\AtlasBalance\backups";
        if (!IsExplicitlyRooted(backupRoot))
        {
            return false;
        }

        try
        {
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(backupRoot));
            var fullBackupPath = Path.GetFullPath(backupPath);
            return fullBackupPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool Success, string? Error)> CreateDatabaseBackupAsync(CancellationToken cancellationToken)
    {
        var dbPassword = _configuration["WatchdogSettings:DbPassword"];
        if (string.IsNullOrWhiteSpace(dbPassword))
        {
            return (false, "WatchdogSettings:DbPassword no configurado; no se actualiza sin backup previo.");
        }

        var backupRoot = _configuration["WatchdogSettings:BackupPath"] ?? @"C:\AtlasBalance\backups";
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, $"pre_update_watchdog_{DateTime.UtcNow:yyyyMMdd_HHmmss}.dump");

        var executable = ResolveConfiguredExecutable(_configuration["WatchdogSettings:PostgresBinPath"], "pg_dump.exe");
        if (string.IsNullOrWhiteSpace(executable))
        {
            return (false, "WatchdogSettings:PostgresBinPath no apunta a pg_dump.exe absoluto; no se actualiza sin backup previo.");
        }

        var dbHost = _configuration["WatchdogSettings:DbHost"] ?? "localhost";
        var dbPort = int.TryParse(_configuration["WatchdogSettings:DbPort"], out var parsedPort) ? parsedPort : 5432;
        var dbName = _configuration["WatchdogSettings:DbName"] ?? "atlas_balance";
        var dbUser = _configuration["WatchdogSettings:DbUser"] ?? "app_user";

        var result = await RunProcessAsync(
            executable,
            ["-h", dbHost, "-p", dbPort.ToString(), "-U", dbUser, "-F", "c", "-b", "-f", backupPath, dbName],
            dbPassword,
            cancellationToken);

        return result.Success
            ? (true, null)
            : (false, $"pg_dump fallo antes de actualizar: {result.ErrorMessage}");
    }

    private string CreatePackageRollbackCopy(string installPath)
    {
        var backupRoot = _configuration["WatchdogSettings:BackupPath"] ??
                         Path.Combine(installPath, "backups");
        Directory.CreateDirectory(backupRoot);
        var rollbackPath = Path.Combine(backupRoot, $"app_before_watchdog_update_{DateTime.UtcNow:yyyyMMdd_HHmmss}");

        CopyDirectoryIfExists(Path.Combine(installPath, "api"), Path.Combine(rollbackPath, "api"));
        CopyDirectoryIfExists(Path.Combine(installPath, "watchdog"), Path.Combine(rollbackPath, "watchdog"));
        CopyDirectoryIfExists(Path.Combine(installPath, "scripts"), Path.Combine(rollbackPath, "scripts"));
        CopyFileIfExists(Path.Combine(installPath, "VERSION"), Path.Combine(rollbackPath, "VERSION"));
        CopyFileIfExists(Path.Combine(installPath, "atlas-balance.runtime.json"), Path.Combine(rollbackPath, "atlas-balance.runtime.json"));
        foreach (var file in PackageRootFiles)
        {
            CopyFileIfExists(Path.Combine(installPath, file), Path.Combine(rollbackPath, file));
        }

        return rollbackPath;
    }

    private static void ApplyReleasePackage(string packageRoot, string installPath)
    {
        SyncDirectory(Path.Combine(packageRoot, "api"), Path.Combine(installPath, "api"));
        SyncDirectory(Path.Combine(packageRoot, "watchdog"), Path.Combine(installPath, "watchdog"));

        var packageScripts = Path.Combine(packageRoot, "scripts");
        var installScripts = Path.Combine(installPath, "scripts");
        Directory.CreateDirectory(installScripts);
        foreach (var script in PackageScriptFiles)
        {
            CopyFileIfExists(Path.Combine(packageScripts, script), Path.Combine(installScripts, script));
        }

        foreach (var file in PackageRootFiles)
        {
            CopyFileIfExists(Path.Combine(packageRoot, file), Path.Combine(installPath, file));
        }

        var newVersion = ReadPackageVersion(packageRoot);
        var previousVersion = ReadInstalledVersion(installPath);
        WriteInstalledVersionMetadata(installPath, newVersion, previousVersion);
    }

    private static string ReadPackageVersion(string packageRoot)
    {
        var versionPath = Path.Combine(packageRoot, "VERSION");
        return File.Exists(versionPath)
            ? File.ReadAllText(versionPath).Trim()
            : "desconocida";
    }

    private static string ReadInstalledVersion(string installPath)
    {
        var runtimePath = Path.Combine(installPath, "atlas-balance.runtime.json");
        if (File.Exists(runtimePath))
        {
            try
            {
                var runtime = JsonNode.Parse(File.ReadAllText(runtimePath)) as JsonObject;
                var version = runtime?["Version"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
            catch
            {
                // Fall back to VERSION below.
            }
        }

        var versionPath = Path.Combine(installPath, "VERSION");
        return File.Exists(versionPath)
            ? File.ReadAllText(versionPath).Trim()
            : "desconocida";
    }

    private static void WriteInstalledVersionMetadata(string installPath, string newVersion, string previousVersion)
    {
        File.WriteAllText(Path.Combine(installPath, "VERSION"), newVersion);

        var runtimePath = Path.Combine(installPath, "atlas-balance.runtime.json");
        JsonObject runtime;
        if (File.Exists(runtimePath))
        {
            try
            {
                runtime = JsonNode.Parse(File.ReadAllText(runtimePath)) as JsonObject ?? new JsonObject();
            }
            catch
            {
                runtime = new JsonObject();
            }
        }
        else
        {
            runtime = new JsonObject();
        }

        runtime["Version"] = newVersion;
        runtime["PreviousVersion"] = previousVersion;
        runtime["UpdatedAt"] = DateTime.UtcNow.ToString("O");

        var json = runtime.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(runtimePath, json);
    }

    private bool ShouldUseExternalPackageUpdater(string installPath)
    {
        var configured = _configuration["WatchdogSettings:UseExternalPackageUpdater"];
        if (bool.TryParse(configured, out var parsed))
        {
            return parsed;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var watchdogInstallPath = Path.Combine(installPath, "watchdog");
        return IsPathWithinRoot(AppContext.BaseDirectory, watchdogInstallPath);
    }

    private async Task<(bool Success, string? Error)> RunPackageUpdateViaHelperAsync(
        string packageRoot,
        string installPath,
        CancellationToken cancellationToken)
    {
        var updaterScript = Path.Combine(packageRoot, "scripts", "Actualizar-AtlasBalance.ps1");
        if (!File.Exists(updaterScript))
        {
            return (false, "El paquete no incluye scripts\\Actualizar-AtlasBalance.ps1.");
        }

        var sourceRoot = _configuration["WatchdogSettings:UpdateSourceRoot"] ?? Path.Combine(installPath, "updates");
        if (!IsExplicitlyRooted(sourceRoot))
        {
            return (false, "WatchdogSettings:UpdateSourceRoot no es absoluto.");
        }

        var helperPath = Path.Combine(sourceRoot, $"run-online-update-{Guid.NewGuid():N}.ps1");
        Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(helperPath, BuildOnlineUpdateHelperScript());

        var stateFilePath = _configuration["WatchdogSettings:StateFilePath"] ??
                            Path.Combine(installPath, "watchdog-state.json");

        return await RunProcessAsync(
            ResolvePowerShellExecutable(),
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                helperPath,
                "-UpdaterScript",
                updaterScript,
                "-InstallPath",
                installPath,
                "-StateFilePath",
                stateFilePath
            ],
            null,
            cancellationToken);
    }

    private static string ResolvePowerShellExecutable()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrWhiteSpace(systemDirectory))
        {
            var windowsPowerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(windowsPowerShell))
            {
                return windowsPowerShell;
            }
        }

        return "powershell.exe";
    }

    private static string BuildOnlineUpdateHelperScript() =>
        """
        param(
            [Parameter(Mandatory = $true)][string]$UpdaterScript,
            [Parameter(Mandatory = $true)][string]$InstallPath,
            [Parameter(Mandatory = $true)][string]$StateFilePath
        )

        $ErrorActionPreference = "Stop"

        function Write-AtlasUpdateState {
            param(
                [string]$Estado,
                [string]$Mensaje
            )

            $directory = Split-Path -Parent $StateFilePath
            if (-not [string]::IsNullOrWhiteSpace($directory)) {
                New-Item -ItemType Directory -Path $directory -Force | Out-Null
            }

            [ordered]@{
                Estado = $Estado
                Operacion = "UPDATE_APP"
                Mensaje = $Mensaje
                UpdatedAt = (Get-Date).ToUniversalTime().ToString("o")
            } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StateFilePath -Encoding UTF8
        }

        try {
            Write-AtlasUpdateState -Estado "RUNNING" -Mensaje "Actualizacion en progreso"
            & $UpdaterScript -InstallPath $InstallPath
            Write-AtlasUpdateState -Estado "SUCCESS" -Mensaje "Actualizacion completada"
        } catch {
            Write-AtlasUpdateState -Estado "FAILED" -Mensaje "Error inesperado en actualizacion. Revise los logs protegidos del servidor."
            Write-Error $_
            exit 1
        }
        """;

    private bool RequireDatabaseBackupBeforeUpdate()
    {
        var raw = _configuration["WatchdogSettings:RequireDatabaseBackupBeforeUpdate"];
        return !bool.TryParse(raw, out var parsed) || parsed;
    }

    private bool RequireHealthCheckAfterUpdate()
    {
        var raw = _configuration["WatchdogSettings:RequireHealthCheckAfterUpdate"];
        return bool.TryParse(raw, out var parsed) && parsed;
    }

    private async Task<bool> WaitForApiHealthAsync(CancellationToken cancellationToken)
    {
        var healthUrl = _configuration["WatchdogSettings:ApiHealthUrl"];
        if (string.IsNullOrWhiteSpace(healthUrl))
        {
            healthUrl = "https://localhost/api/health";
        }

        if (!IsLocalHealthUrl(healthUrl))
        {
            _logger.LogWarning("Health check rechazado por URL no local: {HealthUrlSafe}", LogScrubber.Scrub(healthUrl));
            return false;
        }

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // API can still be booting and applying migrations.
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        return false;
    }

    private void TryRestorePackageRollback(string? rollbackPath, string installPath)
    {
        if (string.IsNullOrWhiteSpace(rollbackPath) || !Directory.Exists(rollbackPath))
        {
            return;
        }

        try
        {
            RestoreDirectoryIfExists(Path.Combine(rollbackPath, "api"), Path.Combine(installPath, "api"));
            RestoreDirectoryIfExists(Path.Combine(rollbackPath, "watchdog"), Path.Combine(installPath, "watchdog"));
            RestoreDirectoryIfExists(Path.Combine(rollbackPath, "scripts"), Path.Combine(installPath, "scripts"));
            CopyFileIfExists(Path.Combine(rollbackPath, "VERSION"), Path.Combine(installPath, "VERSION"));
            CopyFileIfExists(Path.Combine(rollbackPath, "atlas-balance.runtime.json"), Path.Combine(installPath, "atlas-balance.runtime.json"));
            foreach (var file in PackageRootFiles)
            {
                CopyFileIfExists(Path.Combine(rollbackPath, file), Path.Combine(installPath, file));
            }

            _logger.LogWarning("Rollback de binarios aplicado desde {RollbackPathSafe}", LogScrubber.Scrub(rollbackPath));
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "No se pudo aplicar rollback de binarios desde {RollbackPathSafe}", LogScrubber.Scrub(rollbackPath));
        }
    }

    private static bool IsLocalHealthUrl(string healthUrl)
    {
        if (!Uri.TryCreate(healthUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "https" or "http" &&
               (uri.IsLoopback ||
                string.Equals(uri.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, $"{Environment.MachineName}.local", StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var destination = Path.Combine(targetPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void CopyDirectoryIfExists(string sourcePath, string targetPath)
    {
        if (Directory.Exists(sourcePath))
        {
            CopyDirectory(sourcePath, targetPath);
        }
    }

    private static void RestoreDirectoryIfExists(string sourcePath, string targetPath)
    {
        if (Directory.Exists(sourcePath))
        {
            SyncDirectory(sourcePath, targetPath);
        }
    }

    private static void CopyFileIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static bool IsValidReleasePackage(string packageRoot)
    {
        return File.Exists(Path.Combine(packageRoot, "VERSION")) &&
               File.Exists(Path.Combine(packageRoot, "api", "AtlasBalance.API.exe")) &&
               File.Exists(Path.Combine(packageRoot, "watchdog", "AtlasBalance.Watchdog.exe")) &&
               File.Exists(Path.Combine(packageRoot, "scripts", "Actualizar-AtlasBalance.ps1"));
    }

    /// <summary>
    /// V-02-05 (CRIT-3) + V-02.06 (PR F3): verifica la integridad del ZIP de
    /// actualizacion cuando la API lo pasa. Rechaza el update si:
    ///   - el ZIP no se proporcino (fail-closed; no hay "modo legacy")
    ///   - el ZIP no existe
    ///   - el ZIP esta fuera de UpdateSourceRoot
    ///   - falta la clave publica o el archivo .sig
    ///   - la firma RSA no valida contra la clave publica configurada
    ///
    /// Devuelve null si la verificacion pasa. Devuelve un string con la
    /// razon si falla.
    /// </summary>
    private string? VerifyPackageZipIntegrity(string? packageZipPath)
    {
        if (string.IsNullOrWhiteSpace(packageZipPath))
        {
            return "package_zip_path obligatorio: actualizacion sin firma rechazada";
        }

        if (!TryGetFullPath(packageZipPath, out var fullZipPath))
        {
            return "Path al ZIP invalido";
        }

        if (!File.Exists(fullZipPath))
        {
            return "ZIP no encontrado (puede que la API ya lo haya borrado)";
        }

        var sourceRoot = _configuration["WatchdogSettings:UpdateSourceRoot"];
        if (string.IsNullOrWhiteSpace(sourceRoot) || !IsPathWithinRoot(fullZipPath, sourceRoot))
        {
            return $"ZIP fuera de UpdateSourceRoot: '{fullZipPath}'";
        }

        var publicKeyPem = _configuration["UpdateSecurity:ReleaseSigningPublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return "UpdateSecurity:ReleaseSigningPublicKeyPem no configurada en Watchdog; firma no se puede verificar";
        }

        var signaturePath = fullZipPath + ".sig";
        if (!File.Exists(signaturePath))
        {
            return $"Firma RSA no encontrada: '{signaturePath}'";
        }

        try
        {
            using var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var sigBytes = File.ReadAllBytes(signaturePath);
            using var zipStream = File.OpenRead(fullZipPath);
            if (!rsa.VerifyData(zipStream, sigBytes, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1))
            {
                return "Firma RSA invalida para el ZIP";
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar firma RSA del ZIP de actualizacion");
            return "Error al verificar firma RSA: " + ex.Message;
        }
    }

    private static string? ResolveConfiguredExecutable(string? directory, string executableName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !IsExplicitlyRooted(directory))
        {
            return null;
        }

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(directory.Trim(), executableName));
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private string? ResolveDockerExecutable()
    {
        var configured = _configuration["WatchdogSettings:DockerCliPath"];
        if (!string.IsNullOrWhiteSpace(configured) && IsExplicitlyRooted(configured))
        {
            try
            {
                var fullConfigured = Path.GetFullPath(configured.Trim());
                if (File.Exists(fullConfigured))
                {
                    return fullConfigured;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        var docker = Path.Combine(programFiles, "Docker", "Docker", "resources", "bin", "docker.exe");
        return File.Exists(docker) ? docker : null;
    }

    private static bool IsExplicitlyRooted(string path)
    {
        return Path.IsPathRooted(path) ||
               (path.Length >= 3 &&
                char.IsLetter(path[0]) &&
                path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'));
    }

    private static async Task<(bool Success, string? ErrorMessage)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? password,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                startInfo.Environment["PGPASSWORD"] = password;
            }

            using var process = new Process { StartInfo = startInfo };
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ProcessTimeout);
            var processToken = timeoutCts.Token;

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(processToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                return cancellationToken.IsCancellationRequested
                    ? (false, "Proceso externo cancelado.")
                    : (false, $"Proceso externo excedio el timeout de {ProcessTimeout.TotalMinutes:0} minutos.");
            }

            var stdout = await outputTask;
            var stderr = await errorTask;
            if (process.ExitCode == 0)
            {
                return (true, null);
            }

            var error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (false, Trim(error, 1500));
        }
        catch (Exception ex)
        {
            return (false, Trim(ex.Message, 1500));
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup after timeout.
        }
    }
}
