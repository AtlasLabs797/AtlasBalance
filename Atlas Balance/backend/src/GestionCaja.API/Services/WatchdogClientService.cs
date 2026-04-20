using System.Net.Http.Json;
using System.Text.Json;
using GestionCaja.API.DTOs;

namespace GestionCaja.API.Services;

public interface IWatchdogClientService
{
    Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken);
    Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken);
    Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken);
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
}
