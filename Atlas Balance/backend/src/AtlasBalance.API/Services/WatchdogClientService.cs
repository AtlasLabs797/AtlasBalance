using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using AtlasBalance.API.DTOs;

namespace AtlasBalance.API.Services;

public interface IWatchdogClientService
{
    Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken);
    Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken);
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

    public async Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken)
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
            solicitadoPorId
        });

        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning("Watchdog restore request failed with status code {StatusCode}", (int)response.StatusCode);
        return false;
    }

    public async Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
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
            targetPath
        });

        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning("Watchdog update request failed with status code {StatusCode}", (int)response.StatusCode);
        return false;
    }

    public async Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken)
    {
        var stateFilePath = _configuration["WatchdogSettings:StateFilePath"] ?? "watchdog-state.json";
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
}
