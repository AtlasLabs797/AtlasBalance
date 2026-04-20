using System.Text.Json;
using GestionCaja.Watchdog.Models;

namespace GestionCaja.Watchdog.Services;

public interface IWatchdogStateStore
{
    Task<WatchdogState> GetAsync(CancellationToken cancellationToken);
    Task SetAsync(WatchdogState state, CancellationToken cancellationToken);
}

public sealed class WatchdogStateStore : IWatchdogStateStore
{
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public WatchdogStateStore(IConfiguration configuration)
    {
        _stateFilePath = configuration["WatchdogSettings:StateFilePath"] ?? "watchdog-state.json";
    }

    public async Task<WatchdogState> GetAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new WatchdogState();
            }

            var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            return JsonSerializer.Deserialize<WatchdogState>(json) ?? new WatchdogState();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetAsync(WatchdogState state, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }
}
