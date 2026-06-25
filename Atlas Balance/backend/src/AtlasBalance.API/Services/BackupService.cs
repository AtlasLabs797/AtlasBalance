using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasBalance.API.Services;

public interface IBackupService
{
    Task<Backup> CreateBackupAsync(TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken);
    Task ApplyRetentionAsync(CancellationToken cancellationToken);
}

public sealed class BackupService : IBackupService
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly IGoogleDriveBackupService _googleDriveBackupService;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IAuditService auditService,
        IGoogleDriveBackupService googleDriveBackupService,
        ILogger<BackupService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _auditService = auditService;
        _googleDriveBackupService = googleDriveBackupService;
        _logger = logger;
    }

    public async Task<Backup> CreateBackupAsync(TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
    {
        var rawBackupDirectory = await GetConfigValueAsync("backup_path", @"C:\atlas-balance\backups", cancellationToken);
        var backupDirectory = ResolveSafeDirectory(rawBackupDirectory, "backup_path");
        Directory.CreateDirectory(backupDirectory);

        var timestamp = DateTime.UtcNow;
        var fileName = tipo == TipoProceso.AUTO
            ? $"backup_{timestamp:yyyy-MM-dd}.dump"
            : $"backup_{timestamp:yyyy-MM-dd_HHmmss}.dump";
        var backupPath = Path.Combine(backupDirectory, fileName);

        var backup = new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = timestamp,
            RutaArchivo = backupPath,
            Estado = EstadoProceso.PENDING,
            Tipo = tipo,
            IniciadoPorId = iniciadoPorId
        };

        _dbContext.Backups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await RunPgDumpAsync(backupPath, cancellationToken);
            if (!result.Success)
            {
                backup.Estado = EstadoProceso.FAILED;
                backup.Notas = "La herramienta de copia fallo. Revisa el diagnostico protegido del servidor.";
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("pg_dump fallo al crear backup {BackupId}: {Error}", backup.Id, result.ErrorMessage);
                throw new InvalidOperationException("No se pudo crear la copia de seguridad. Revisa la configuracion del servidor o avisa al administrador.");
            }

            var fileInfo = new FileInfo(backupPath);
            backup.Estado = EstadoProceso.SUCCESS;
            backup.TamanioBytes = fileInfo.Exists ? fileInfo.Length : null;
            backup.Notas = null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                iniciadoPorId,
                AuditActions.BackupGenerado,
                "BACKUPS",
                backup.Id,
                ipAddress: null,
                detallesJson: JsonSerializer.Serialize(new { backup.RutaArchivo, backup.TamanioBytes, backup.Tipo }),
                cancellationToken: cancellationToken);

            try
            {
                await ApplyRetentionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retention falló tras backup exitoso {BackupId}", backup.Id);
            }

            try
            {
                await _googleDriveBackupService.UploadBackupAsync(backup, backupPath, cancellationToken);
            }
            catch (Exception ex)
            {
                backup.Notas = "Copia local correcta; subida a Google Drive fallida.";
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "Backup local {BackupId} correcto, pero fallo la subida a Google Drive", backup.Id);
            }

            return backup;
        }
        catch
        {
            if (backup.Estado == EstadoProceso.PENDING)
            {
                backup.Estado = EstadoProceso.FAILED;
                backup.Notas = "No se pudo crear la copia de seguridad.";
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    public async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        var weeksRaw = await GetConfigValueAsync("backup_retention_weeks", "6", cancellationToken);
        var retentionWeeks = int.TryParse(weeksRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 1, 52)
            : 6;
        var rawBackupDirectory = await GetConfigValueAsync("backup_path", @"C:\atlas-balance\backups", cancellationToken);
        var backupDirectory = ResolveSafeDirectory(rawBackupDirectory, "backup_path");

        var cutoff = DateTime.UtcNow.AddDays(-7 * retentionWeeks);
        var oldBackups = await _dbContext.Backups
            .Where(b => b.Estado == EstadoProceso.SUCCESS && b.FechaCreacion < cutoff && b.DeletedAt == null)
            .OrderBy(b => b.FechaCreacion)
            .ToListAsync(cancellationToken);

        foreach (var backup in oldBackups)
        {
            try
            {
                if (!IsAllowedBackupFile(backup.RutaArchivo, backupDirectory))
                {
                    _logger.LogWarning("Retention omitio backup {BackupId} por ruta fuera de la raiz permitida", backup.Id);
                    continue;
                }

                if (File.Exists(backup.RutaArchivo))
                {
                    File.Delete(backup.RutaArchivo);
                }

                backup.DeletedAt = DateTime.UtcNow;
                backup.DeletedById = null;
                await _auditService.LogAsync(
                    usuarioId: null,
                    tipoAccion: AuditActions.BackupRetencionAutomatica,
                    entidadTipo: "BACKUPS",
                    entidadId: backup.Id,
                    ipAddress: null,
                    detallesJson: JsonSerializer.Serialize(new
                    {
                        backup.RutaArchivo,
                        fecha_original = backup.FechaCreacion
                    }),
                    cancellationToken: cancellationToken);
                backup.Notas = "Eliminado por retención automática";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo eliminar backup antiguo {BackupId}", backup.Id);
            }
        }

        if (oldBackups.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> RunPgDumpAsync(string backupPath, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no configurado");
        var conn = ParsePostgresConnection(connectionString);

        var executable = ResolveConfiguredExecutable(_configuration["WatchdogSettings:PostgresBinPath"], "pg_dump.exe");
        if (string.IsNullOrWhiteSpace(executable))
        {
            _logger.LogWarning("pg_dump local omitido: WatchdogSettings:PostgresBinPath no apunta a pg_dump.exe absoluto.");
            return await RunPgDumpViaDockerAsync(backupPath, conn, cancellationToken);
        }

        var localArgs = new List<string>
        {
            "-h", conn.Host,
            "-p", conn.Port.ToString(CultureInfo.InvariantCulture),
            "-U", conn.User,
            "-F", "c",
            "-b",
            "-v",
            "-f", backupPath,
            conn.Database
        };
        var localResult = await RunProcessAsync(executable, localArgs, conn.Password, cancellationToken);
        if (localResult.Success)
        {
            return (true, null);
        }

        _logger.LogWarning("pg_dump local falló: {Error}. Se intentará fallback docker.", localResult.ErrorMessage);
        return await RunPgDumpViaDockerAsync(backupPath, conn, cancellationToken);
    }

    private async Task<(bool Success, string? ErrorMessage)> RunPgDumpViaDockerAsync(
        string backupPath,
        (string Host, int Port, string Database, string User, string Password) conn,
        CancellationToken cancellationToken)
    {
        var dockerExe = ResolveDockerExecutable();
        if (string.IsNullOrWhiteSpace(dockerExe))
        {
            return (false, "Docker fallback no configurado con ruta absoluta.");
        }

        var container = _configuration["WatchdogSettings:DockerPostgresContainer"] ?? "atlas_balance_db";
        var containerFile = $"/tmp/{Guid.NewGuid():N}.dump";

        var dumpResult = await RunProcessAsync(
            dockerExe,
            ["exec", container, "pg_dump", "-U", conn.User, "-d", conn.Database, "-F", "c", "-b", "-v", "-f", containerFile],
            null,
            cancellationToken);
        if (!dumpResult.Success)
        {
            return (false, $"Fallback docker dump falló: {dumpResult.ErrorMessage}");
        }

        var cpResult = await RunProcessAsync(dockerExe, ["cp", $"{container}:{containerFile}", backupPath], null, cancellationToken);
        await RunProcessAsync(dockerExe, ["exec", container, "rm", "-f", containerFile], null, cancellationToken);
        return cpResult.Success
            ? (true, null)
            : (false, $"Fallback docker copy falló: {cpResult.ErrorMessage}");
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
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
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

            var stderr = await stderrTask;
            var stdout = await stdoutTask;
            if (process.ExitCode == 0)
            {
                return (true, null);
            }

            var error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (false, Truncate(error, 1800));
        }
        catch (Exception ex)
        {
            return (false, Truncate(ex.Message, 1800));
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

    private async Task<string> GetConfigValueAsync(string key, string fallback, CancellationToken cancellationToken)
    {
        return await _dbContext.Configuraciones
            .Where(c => c.Clave == key)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken) ?? fallback;
    }

    private static (string Host, int Port, string Database, string User, string Password) ParsePostgresConnection(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host;
        var database = string.IsNullOrWhiteSpace(builder.Database) ? "atlas_balance" : builder.Database;
        var user = string.IsNullOrWhiteSpace(builder.Username) ? "app_user" : builder.Username;
        var password = builder.Password ?? string.Empty;
        var port = builder.Port > 0 ? builder.Port : 5432;
        return (host, port, database, user, password);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? ResolveConfiguredExecutable(string? directory, string executableName)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            (!Path.IsPathRooted(directory) && !LooksLikeWindowsRootedPath(directory)))
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
        if (!string.IsNullOrWhiteSpace(configured) &&
            (Path.IsPathRooted(configured) || LooksLikeWindowsRootedPath(configured)))
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

    private static string ResolveSafeDirectory(string rawPath, string configKey)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException($"Configuración '{configKey}' vacía.");
        }

        var trimmed = rawPath.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configuración '{configKey}' contiene segmentos de traversal.");
        }

        if (!Path.IsPathRooted(trimmed) && !LooksLikeWindowsRootedPath(trimmed))
        {
            throw new InvalidOperationException($"Configuracion '{configKey}' debe ser una ruta absoluta.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"Configuración '{configKey}' no es una ruta válida.", ex);
        }

        if (!Path.IsPathRooted(fullPath))
        {
            throw new InvalidOperationException($"Configuración '{configKey}' debe ser una ruta absoluta.");
        }

        return fullPath;
    }

    private static bool LooksLikeWindowsRootedPath(string value)
    {
        return value.Length >= 3 &&
               char.IsLetter(value[0]) &&
               value[1] == ':' &&
               (value[2] == '\\' || value[2] == '/');
    }

    private static bool IsAllowedBackupFile(string filePath, string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !LooksLikeRootedPath(filePath))
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(filePath), ".dump", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(backupDirectory));
            return fullFilePath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool LooksLikeRootedPath(string value) =>
        Path.IsPathRooted(value) || LooksLikeWindowsRootedPath(value);

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }
}
