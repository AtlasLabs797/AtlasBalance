using System.Diagnostics;
using System.ServiceProcess;
using GestionCaja.Watchdog.Models;

namespace GestionCaja.Watchdog.Services;

public interface IWatchdogOperationsService
{
    Task<bool> StartRestoreAsync(string backupPath, CancellationToken cancellationToken);
    Task<bool> StartUpdateAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken);
}

public sealed class WatchdogOperationsService : IWatchdogOperationsService
{
    private static readonly HashSet<string> PreservedTopLevelDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs"
    };

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

    public async Task<bool> StartRestoreAsync(string backupPath, CancellationToken cancellationToken)
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

        await _stateStore.SetAsync(
            CreateState("RUNNING", "RESTORE_BACKUP", "Restauracion en progreso"),
            cancellationToken);

        _ = Task.Run(async () =>
        {
            var finalState = CreateState("FAILED", "RESTORE_BACKUP", "Operacion interrumpida");
            try
            {
                await StopApiServiceSafeAsync(CancellationToken.None);
                var restoreResult = await RunPgRestoreAsync(fullBackupPath, CancellationToken.None);
                finalState = restoreResult.Success
                    ? CreateState("SUCCESS", "RESTORE_BACKUP", "Restauracion completada")
                    : CreateState("FAILED", "RESTORE_BACKUP", restoreResult.Error ?? "Error en pg_restore");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore operation failed");
                finalState = CreateState("FAILED", "RESTORE_BACKUP", ex.Message);
            }
            finally
            {
                await StartApiServiceSafeAsync(CancellationToken.None);
                await _stateStore.SetAsync(finalState, CancellationToken.None);
                _operationLock.Release();
            }
        });

        return true;
    }

    public async Task<bool> StartUpdateAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.IsNullOrWhiteSpace(targetPath) ||
            !Directory.Exists(sourcePath))
        {
            return false;
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullTargetPath = Path.GetFullPath(targetPath);

        if (string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase) ||
            PathsOverlap(fullSourcePath, fullTargetPath) ||
            !IsAllowedUpdateSourcePath(fullSourcePath) ||
            !IsAllowedUpdateTargetPath(fullTargetPath))
        {
            return false;
        }

        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        await _stateStore.SetAsync(
            CreateState("RUNNING", "UPDATE_APP", "Actualizacion en progreso"),
            cancellationToken);

        _ = Task.Run(async () =>
        {
            var finalState = CreateState("FAILED", "UPDATE_APP", "Operacion interrumpida");
            string? rollbackPath = null;
            var apiStartedInOperation = false;
            try
            {
                if (RequireDatabaseBackupBeforeUpdate())
                {
                    var backupResult = await CreateDatabaseBackupAsync(CancellationToken.None);
                    if (!backupResult.Success)
                    {
                        finalState = CreateState("FAILED", "UPDATE_APP", backupResult.Error ?? "No se actualiza sin backup previo de base de datos");
                        return;
                    }
                }

                await StopApiServiceSafeAsync(CancellationToken.None);
                rollbackPath = CreateRollbackCopy(fullTargetPath);
                SyncDirectory(fullSourcePath, fullTargetPath);
                await StartApiServiceSafeAsync(CancellationToken.None);
                apiStartedInOperation = true;

                if (RequireHealthCheckAfterUpdate() &&
                    !await WaitForApiHealthAsync(CancellationToken.None))
                {
                    finalState = CreateState("FAILED", "UPDATE_APP", "Health check fallo tras actualizar; rollback de binarios aplicado.");
                    await StopApiServiceSafeAsync(CancellationToken.None);
                    TryRestoreRollback(rollbackPath, fullTargetPath);
                    await StartApiServiceSafeAsync(CancellationToken.None);
                    return;
                }

                finalState = CreateState("SUCCESS", "UPDATE_APP", "Actualizacion completada");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update operation failed");
                TryRestoreRollback(rollbackPath, fullTargetPath);
                finalState = CreateState("FAILED", "UPDATE_APP", ex.Message);
            }
            finally
            {
                if (!apiStartedInOperation)
                {
                    await StartApiServiceSafeAsync(CancellationToken.None);
                }

                await _stateStore.SetAsync(finalState, CancellationToken.None);
                _operationLock.Release();
            }
        });

        return true;
    }

    private async Task<(bool Success, string? Error)> RunPgRestoreAsync(string backupPath, CancellationToken cancellationToken)
    {
        var pgBinPath = _configuration["WatchdogSettings:PostgresBinPath"];
        var restoreCandidate = string.IsNullOrWhiteSpace(pgBinPath) ? string.Empty : Path.Combine(pgBinPath, "pg_restore.exe");
        var executable = File.Exists(restoreCandidate) ? restoreCandidate : "pg_restore";

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
        var localResult = await RunProcessAsync(executable, localArgs, dbPassword, cancellationToken);
        if (localResult.Success)
        {
            return (true, null);
        }

        _logger.LogWarning("pg_restore local fallo: {Error}. Se intentara fallback docker.", localResult.ErrorMessage);
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

    private static WatchdogState CreateState(string estado, string operacion, string mensaje) =>
        new()
        {
            Estado = estado,
            Operacion = operacion,
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

    private bool IsAllowedUpdateTargetPath(string targetPath)
    {
        var configuredTarget = _configuration["WatchdogSettings:UpdateTargetPath"] ?? @"C:\AtlasBalance\api";
        return PathsEqual(targetPath, configuredTarget);
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

    private async Task<(bool Success, string? Error)> RunPgRestoreViaDockerAsync(
        string backupPath,
        string dbUser,
        string dbName,
        CancellationToken cancellationToken)
    {
        var container = _configuration["WatchdogSettings:DockerPostgresContainer"] ?? "atlas_balance_db";
        var containerFile = $"/tmp/{Guid.NewGuid():N}.dump";

        var cpIn = await RunProcessAsync("docker", ["cp", backupPath, $"{container}:{containerFile}"], null, cancellationToken);
        if (!cpIn.Success)
        {
            return (false, $"Fallback docker copy-in fallo: {cpIn.ErrorMessage}");
        }

        var restore = await RunProcessAsync(
            "docker",
            ["exec", container, "pg_restore", "-U", dbUser, "-d", dbName, "--clean", "--if-exists", "-v", containerFile],
            null,
            cancellationToken);
        await RunProcessAsync("docker", ["exec", container, "rm", "-f", containerFile], null, cancellationToken);
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

        var pgBinPath = _configuration["WatchdogSettings:PostgresBinPath"];
        var dumpCandidate = string.IsNullOrWhiteSpace(pgBinPath) ? string.Empty : Path.Combine(pgBinPath, "pg_dump.exe");
        var executable = File.Exists(dumpCandidate) ? dumpCandidate : "pg_dump";
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

    private string CreateRollbackCopy(string targetPath)
    {
        var backupRoot = _configuration["WatchdogSettings:BackupPath"] ??
                         Path.Combine(Path.GetDirectoryName(targetPath) ?? targetPath, "backups");
        Directory.CreateDirectory(backupRoot);
        var rollbackPath = Path.Combine(backupRoot, $"app_before_watchdog_update_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
        CopyDirectory(targetPath, rollbackPath);
        return rollbackPath;
    }

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

    private void TryRestoreRollback(string? rollbackPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(rollbackPath) || !Directory.Exists(rollbackPath))
        {
            return;
        }

        try
        {
            SyncDirectory(rollbackPath, targetPath);
            _logger.LogWarning("Rollback de binarios aplicado desde {RollbackPath}", rollbackPath);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "No se pudo aplicar rollback de binarios desde {RollbackPath}", rollbackPath);
        }
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
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

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
}
