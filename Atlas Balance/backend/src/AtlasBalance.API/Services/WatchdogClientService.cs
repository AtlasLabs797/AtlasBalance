using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Logging;

namespace AtlasBalance.API.Services;

public interface IWatchdogClientService
{
    Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken);
    Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, Guid operationId, CancellationToken cancellationToken) =>
        SolicitarRestauracionAsync(backupPath, solicitadoPorId, cancellationToken);
    Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, string? packageZipPath, CancellationToken cancellationToken);
    Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken);
    Task<bool> EstaDisponibleAsync(CancellationToken cancellationToken);
}

public sealed class WatchdogClientService : IWatchdogClientService
{
    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WatchdogClientService> _logger;

    public WatchdogClientService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<WatchdogClientService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken) =>
        SolicitarRestauracionAsync(backupPath, solicitadoPorId, Guid.Empty, cancellationToken);

    public async Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, Guid operationId, CancellationToken cancellationToken)
    {
        var secret = _configuration["WatchdogSettings:SharedSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("WatchdogSettings:SharedSecret no configurado");
        }

        var http = _httpClientFactory.CreateClient("watchdog-client");
        EnsureLocalWatchdogBaseAddress(http.BaseAddress);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/watchdog/restaurar-backup");
        request.Headers.Add("X-Watchdog-Secret", secret);
        request.Content = JsonContent.Create(new
        {
            backupPath,
            solicitadoPorId,
            operationId
        });

        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning("Watchdog restore request failed with status code {StatusCode}", (int)response.StatusCode);
        return false;
    }

    public async Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, string? packageZipPath, CancellationToken cancellationToken)
    {
        var secret = _configuration["WatchdogSettings:SharedSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("WatchdogSettings:SharedSecret no configurado");
        }

        var http = _httpClientFactory.CreateClient("watchdog-client");
        EnsureLocalWatchdogBaseAddress(http.BaseAddress);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/watchdog/actualizar-app");
        request.Headers.Add("X-Watchdog-Secret", secret);
        request.Content = JsonContent.Create(new
        {
            sourcePath,
            targetPath,
            packageZipPath
        });

        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Watchdog update request failed with status code {StatusCode}: {BodySafe}", (int)response.StatusCode, LogScrubber.Scrub(body));
        return false;
    }

    public async Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken)
    {
        var stateFilePath = ResolveSafeStateFilePath();
        try
        {
            if (File.Exists(stateFilePath))
            {
                var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
                var parsed = JsonSerializer.Deserialize<WatchdogStateResponse>(json, StateJsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer watchdog state file");
        }

        try
        {
            var secret = _configuration["WatchdogSettings:SharedSecret"];
            var http = _httpClientFactory.CreateClient("watchdog-client");
            EnsureLocalWatchdogBaseAddress(http.BaseAddress);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/watchdog/estado");
            if (!string.IsNullOrWhiteSpace(secret))
            {
                request.Headers.Add("X-Watchdog-Secret", secret);
            }

            var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = JsonSerializer.Deserialize<WatchdogStateResponse>(body, StateJsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo consultar estado de watchdog por HTTP");
        }

        return new WatchdogStateResponse
        {
            Estado = "IDLE",
            Operacion = null,
            Mensaje = "Sin actividad",
            UpdatedAt = DateTime.UtcNow
        };
    }

    public async Task<bool> EstaDisponibleAsync(CancellationToken cancellationToken)
    {
        var secret = _configuration["WatchdogSettings:SharedSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var http = _httpClientFactory.CreateClient("watchdog-client");
            EnsureLocalWatchdogBaseAddress(http.BaseAddress);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/watchdog/estado");
            request.Headers.Add("X-Watchdog-Secret", secret);
            using var response = await http.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Watchdog no disponible para preflight");
            return false;
        }
    }

    private static void EnsureLocalWatchdogBaseAddress(Uri? baseAddress)
    {
        if (baseAddress is null ||
            baseAddress.Scheme is not ("http" or "https") ||
            !IsLoopbackHost(baseAddress.Host))
        {
            throw new InvalidOperationException("Watchdog client rejected a non-local BaseAddress.");
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// V-02-05 (HIGH-8): protege contra path traversal en WatchdogSettings:StateFilePath.
    /// El API expone el contenido de este archivo al cliente admin, por lo que un valor
    /// arbitrario permitia leer ficheros del sistema (p.ej. C:\Windows\System32\drivers\etc\hosts).
    /// El path configurado debe caer dentro de uno de los directorios base de la aplicacion.
    /// </summary>
    private string ResolveSafeStateFilePath()
    {
        var configured = _configuration["WatchdogSettings:StateFilePath"] ?? "watchdog-state.json";
        var baseDirectories = GetAllowedBaseDirectories();
        var candidate = ResolveAndValidate(configured, baseDirectories);
        if (candidate is null)
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "watchdog-state.json");
            _logger.LogWarning(
                "WatchdogSettings:StateFilePath='{Configured}' cae fuera de los directorios permitidos. Usando fallback '{Fallback}'.",
                configured,
                fallback);
            return fallback;
        }
        return candidate;
    }

    private string[] GetAllowedBaseDirectories()
    {
        var list = new List<string>
        {
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AtlasBalance"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasBalance")
        };
        return list
            .Select(p => SafeFullPath(p))
            .Where(p => p is not null)
            .Select(p => p!.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static string? SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveAndValidate(string configured, string[] allowedBases)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WatchdogSettings:StateFilePath invalido: '{Configured}'", configured);
            return null;
        }

        var normalizedFull = full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var baseDir in allowedBases)
        {
            if (normalizedFull.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }
        }

        return null;
    }
}
