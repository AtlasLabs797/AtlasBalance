using System.Text.Json;
using AtlasBalance.Watchdog.Logging;
using AtlasBalance.Watchdog.Models;

namespace AtlasBalance.Watchdog.Services;

public interface IWatchdogStateStore
{
    Task<WatchdogState> GetAsync(CancellationToken cancellationToken);
    Task SetAsync(WatchdogState state, CancellationToken cancellationToken);
}

public sealed class WatchdogStateStore : IWatchdogStateStore
{
    private readonly string _stateFilePath;
    private readonly ILogger<WatchdogStateStore> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public WatchdogStateStore(IConfiguration configuration, ILogger<WatchdogStateStore> logger)
    {
        _stateFilePath = WatchdogLogConfiguration.ResolveStateFilePath(configuration);
        _logger = logger;
        WatchdogLogConfiguration.EnsureStatePath(_stateFilePath);
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
            try
            {
                return JsonSerializer.Deserialize<WatchdogState>(json) ?? new WatchdogState();
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "El estado persistido del Watchdog esta corrupto; se rechaza la operacion.");
                throw new InvalidDataException("El estado persistido del Watchdog no es valido.", exception);
            }
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

            WatchdogLogConfiguration.EnsureStatePath(_stateFilePath);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = $"{_stateFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
                if (OperatingSystem.IsWindows())
                {
                    // El fichero temporal hereda la DACL del directorio; se
                    // verifica de nuevo antes de convertirlo en el estado activo.
                    WatchdogLogConfiguration.EnsureStatePath(temporaryPath);
                }

                File.Move(temporaryPath, _stateFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}
