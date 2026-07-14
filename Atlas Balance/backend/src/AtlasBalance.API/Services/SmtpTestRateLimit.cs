using System.Collections.Concurrent;

namespace AtlasBalance.API.Services;

/// <summary>
/// V-02-05 (MED-4): rate limit en memoria para SMTP test. 5 intentos por minuto por
/// usuario. Suficiente para uso legitimo y bloquea el abuso del endpoint como relay.
/// Si en el futuro la app se ejecuta detras de varios procesos Kestrel, sustituir
/// por un rate limit distribuido (Redis/Postgres).
/// </summary>
public sealed class SmtpTestRateLimit
{
    private const int MaxPerWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, (int Count, DateTime WindowStart)> _state = new();

    public Task<bool> TryAcquireAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return Task.FromResult(true);
        }
        var key = userId.Value;
        var now = DateTime.UtcNow;
        var allowed = false;
        _state.AddOrUpdate(
            key,
            _ => (1, now),
            (_, current) =>
            {
                if (now - current.WindowStart > Window)
                {
                    allowed = true;
                    return (1, now);
                }
                if (current.Count < MaxPerWindow)
                {
                    allowed = true;
                    return (current.Count + 1, current.WindowStart);
                }
                allowed = false;
                return current;
            });
        return Task.FromResult(allowed);
    }
}
