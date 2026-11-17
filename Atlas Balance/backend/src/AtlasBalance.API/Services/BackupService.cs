using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Logging;
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

    // A-04: antiguedad a partir de la cual un temporal se considera huerfano.
    private static readonly TimeSpan OrphanTempFileAge = TimeSpan.FromHours(2);

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly IGoogleDriveBackupService _googleDriveBackupService;
    private readonly ILogger<BackupService> _logger;

    // A-04: costura de pruebas. Si es null se usa el volcado real (pg_dump);
    // los tests inyectan un simulador para no ejecutar el proceso externo.
    private readonly Func<string, CancellationToken, Task<(bool Success, string? ErrorMessage)>>? _dumpExecutor;

    public BackupService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IAuditService auditService,
        IGoogleDriveBackupService googleDriveBackupService,
        ILogger<BackupService> logger)
        : this(dbContext, configuration, auditService, googleDriveBackupService, logger, null)
    {
    }

    // A-04: constructor de pruebas. El contenedor de DI solo ve el constructor
    // publico de 5 argumentos, asi que el registro de Program.cs no cambia.
    internal BackupService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IAuditService auditService,
        IGoogleDriveBackupService googleDriveBackupService,
        ILogger<BackupService> logger,
        Func<string, CancellationToken, Task<(bool Success, string? ErrorMessage)>>? dumpExecutor)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _auditService = auditService;
        _googleDriveBackupService = googleDriveBackupService;
        _logger = logger;
        _dumpExecutor = dumpExecutor;
    }

    public async Task<Backup> CreateBackupAsync(TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
    {
        var rawBackupDirectory = await GetConfigValueAsync("backup_path", @"C:\atlas-balance\backups", cancellationToken);
        var backupDirectory = ResolveSafeDirectory(rawBackupDirectory, "backup_path");
        Directory.CreateDirectory(backupDirectory);

        var timestamp = DateTime.UtcNow;
        var fileName = BuildDumpFileName(timestamp);
        var backupPath = Path.Combine(backupDirectory, fileName);

        // A-04: el volcado se escribe en un temporal del MISMO directorio y
        // solo se renombra cuando termina bien. Asi un volcado a medias no
        // deja un dump corrupto en disco, y el rename es atomico (misma unidad).
        var tempPath = $"{backupPath}.tmp";

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
            var result = _dumpExecutor is not null
                ? await _dumpExecutor(tempPath, cancellationToken)
                : await RunPgDumpAsync(tempPath, cancellationToken);
            if (!result.Success)
            {
                backup.Estado = EstadoProceso.FAILED;
                backup.Notas = "La herramienta de copia fallo. Revisa el diagnostico protegido del servidor.";
                // A-03: con el token de la peticion (ya cancelado cuando el
                // servicio se apaga o hay timeout) este estado no se guardaba
                // y la fila se quedaba en PENDING para siempre.
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("pg_dump fallo al crear backup {BackupId}: {ErrorSafe}", backup.Id, LogScrubber.Scrub(result.ErrorMessage));
                throw new InvalidOperationException("No se pudo crear la copia de seguridad. Revisa la configuracion del servidor o avisa al administrador.");
            }

            // A-04: el volcado solo se publica cuando ha terminado bien.
            File.Move(tempPath, backupPath, overwrite: true);

            var fileInfo = new FileInfo(backupPath);
            backup.Estado = EstadoProceso.SUCCESS;
            backup.TamanioBytes = fileInfo.Exists ? fileInfo.Length : null;
            backup.Notas = null;
            // A-03: el estado del backup no puede depender del token de la
            // peticion: si viene cancelado, la fila se quedaria en PENDING.
            await _dbContext.SaveChangesAsync(CancellationToken.None);

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
            // A-04: no puede quedar un volcado temporal a medias en el disco.
            TryDeleteTempFile(tempPath);

            if (backup.Estado == EstadoProceso.PENDING)
            {
                backup.Estado = EstadoProceso.FAILED;
                backup.Notas = "No se pudo crear la copia de seguridad.";
                // A-03: el token de la peticion puede venir ya cancelado (apagado
                // del servicio o timeout). Con ese token el SaveChanges fallaba
                // y la fila se quedaba en PENDING para siempre.
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
    }

    // A-04: el nombre del volcado lleva siempre la hora, tanto en automatico
    // como en manual. Con frecuencia horaria caben varios backups automaticos
    // el mismo dia y todos escribian en `backup_yyyy-MM-dd.dump`: el segundo
    // volcado a medias destruia el primero.
    internal static string BuildDumpFileName(DateTime timestamp) =>
        $"backup_{timestamp:yyyy-MM-dd_HHmmss}.dump";

    // A-04: limpieza del volcado temporal. Nunca debe quedar un `.tmp` en el
    // directorio de backups, pero si el borrado falla no puede tumbar el backup.
    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo borrar el volcado temporal {TempFile}", Path.GetFileName(tempPath));
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

                // V-02.07 (retencion de PII en la nube): la copia local ya se borro
                // arriba, pero el dump subido a Google Drive seguia vivo
                // indefinidamente. Se borra tambien cada BackupCloudCopy remota
                // asociada; un fallo aqui no debe impedir que la retencion local
                // (ya aplicada en memoria) se guarde para el resto del lote.
                var cloudCopies = await _dbContext.BackupCloudCopies
                    .Where(c => c.BackupId == backup.Id && c.DeletedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var cloudCopy in cloudCopies)
                {
                    try
                    {
                        await _googleDriveBackupService.DeleteRemoteBackupCopyAsync(cloudCopy, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo borrar la copia remota de Google Drive para backup {BackupId}", backup.Id);
                    }
                }
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

        await LimpiarTemporalesHuerfanosAsync(backupDirectory);
    }

    // A-04: desde que el volcado se escribe en un temporal, un proceso que
    // muere a mitad deja un "<nombre>.dump.tmp" en el directorio de backups con
    // datos personales dentro. La retencion no lo toca (solo borra ficheros con
    // extension ".dump"), asi que sin esta limpieza esos temporales se
    // quedarian en disco para siempre. Se borran los que son mas viejos de 2 h:
    // el timeout de pg_dump son 30 minutos, de modo que un temporal mas viejo
    // no puede pertenecer a un volcado en curso.
    private async Task LimpiarTemporalesHuerfanosAsync(string backupDirectory)
    {
        try
        {
            var cutoff = DateTime.UtcNow - OrphanTempFileAge;
            var borrados = 0;

            foreach (var path in Directory.EnumerateFiles(backupDirectory, "*.tmp"))
            {
                try
                {
                    if (new FileInfo(path).LastWriteTimeUtc < cutoff)
                    {
                        File.Delete(path);
                        borrados++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo borrar el volcado temporal {TempFile}", Path.GetFileName(path));
                }
            }

            if (borrados > 0)
            {
                _logger.LogWarning("Se borraron {Count} volcados temporales huerfanos del directorio de backups", borrados);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron limpiar los volcados temporales huerfanos del directorio de backups");
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> RunPgDumpAsync(string backupPath, CancellationToken cancellationToken)
    {
        // V-02.06 (BACKUP-01): forzar la conexion owner para que pg_dump pueda
        // atravesar FORCE ROW LEVEL SECURITY. Sin conexion owner el dump queda
        // filtrado por las policies del rol runtime y el backup es inutil.
        var (conn, isOwner, _) = ResolveDumpConnection();
        if (!isOwner)
        {
            return (false,
                "No se encontro ConnectionStrings:MigrationConnection ni credenciales owner en WatchdogSettings. " +
                "Configure MigrationConnection para que el backup completo funcione con RLS activo.");
        }

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

        _logger.LogWarning("pg_dump local falló: {ErrorSafe}. Se intentará fallback docker.", LogScrubber.Scrub(localResult.ErrorMessage));
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

    // V-02-06 (BACKUP-01): resuelve la cadena owner en este orden para no
    // depender del rol runtime. MigrationConnection es la fuente canonica;
    // si no existe, se intenta WatchdogSettings (que el instalador ya
    // escribe) como fallback. Nunca se devuelve el runtime como owner.
    internal ((string Host, int Port, string Database, string User, string Password) Connection, bool IsOwner, string? Source) ResolveDumpConnection()
    {
        var migration = _configuration.GetConnectionString("MigrationConnection");
        if (!string.IsNullOrWhiteSpace(migration))
        {
            return (ParsePostgresConnection(migration), true, "MigrationConnection");
        }

        var dbOwnerUser = _configuration["WatchdogSettings:DbOwnerUser"];
        var dbOwnerPassword = _configuration["WatchdogSettings:DbOwnerPassword"];
        var defaultConn = _configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(dbOwnerUser)
            && !string.IsNullOrWhiteSpace(dbOwnerPassword)
            && !string.IsNullOrWhiteSpace(defaultConn))
        {
            var parsed = ParsePostgresConnection(defaultConn);
            return (
                (parsed.Host, parsed.Port, parsed.Database, dbOwnerUser, dbOwnerPassword),
                true,
                "WatchdogSettings.DbOwner*");
        }

        if (string.IsNullOrWhiteSpace(defaultConn))
        {
            return (default, false, null);
        }

        return (ParsePostgresConnection(defaultConn), false, "DefaultConnection");
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

        // V-02.07: rechazar rutas de red (UNC). Path.IsPathRooted("\\\\host\\share") devuelve
        // true, asi que sin este filtro un valor UNC ya guardado en BD sacaria el volcado
        // completo fuera de la maquina. Se revalida aqui aunque el controlador ya filtre.
        if (IsUncPath(trimmed))
        {
            throw new InvalidOperationException($"Configuracion '{configKey}' no admite rutas de red (UNC).");
        }

        var isNativeAbsolutePath = !OperatingSystem.IsWindows() && Path.IsPathRooted(trimmed);
        if (!LooksLikeWindowsRootedPath(trimmed) && !isNativeAbsolutePath)
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

    private static bool IsUncPath(string value)
    {
        return value.StartsWith(@"\\", StringComparison.Ordinal) ||
               value.StartsWith("//", StringComparison.Ordinal);
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
