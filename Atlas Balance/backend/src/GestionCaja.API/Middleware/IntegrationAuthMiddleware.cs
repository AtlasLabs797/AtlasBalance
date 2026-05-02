using System.Diagnostics;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace GestionCaja.API.Middleware;

public static class IntegrationHttpContextItemKeys
{
    public const string CurrentIntegrationToken = "current_integration_token";
}

public sealed class IntegrationAuthMiddleware
{
    private const string RedactedMarker = "[REDACTED]";
    private const string IntegrationTokenPrefix = "sk_gestion_caja_";
    private const int DefaultInvalidAuthLimitPerMinute = 30;

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "access_token",
        "refresh_token",
        "api_key",
        "apikey",
        "secret",
        "password",
        "passwd",
        "pwd",
        "authorization",
        "auth"
    };

    private static readonly string[] SensitiveQueryKeyFragments =
    [
        "token",
        "secret",
        "password",
        "passwd",
        "pwd",
        "apikey",
        "authorization",
        "auth",
        "bearer",
        "credential"
    ];

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly object _rateLimitLock = new();
    private readonly object _invalidAuthRateLimitLock = new();
    private readonly int _invalidAuthLimitPerMinute;

    public IntegrationAuthMiddleware(RequestDelegate next, IMemoryCache cache, IClock clock, IConfiguration? configuration = null)
    {
        _next = next;
        _cache = cache;
        _clock = clock;
        _invalidAuthLimitPerMinute = Math.Max(
            1,
            configuration?.GetValue<int?>("IntegrationSecurity:InvalidAuthLimitPerMinute") ?? DefaultInvalidAuthLimitPerMinute);
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext, IIntegrationTokenService integrationTokenService)
    {
        if (!context.Request.Path.StartsWithSegments("/api/integration/openclaw", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        var plainToken = ExtractBearerToken(authHeader);
        if (IsInvalidAuthRateLimited(context))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(IntegrationApiResponses.Failure("RATE_LIMITED: Demasiados intentos con token invalido"));
            return;
        }

        var integrationToken = await integrationTokenService.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        if (integrationToken is null)
        {
            RecordInvalidAuthFailure(context);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(IntegrationApiResponses.Failure("UNAUTHORIZED: Token de integracion invalido o revocado"));
            return;
        }

        ClearInvalidAuthFailures(context);
        context.Items[IntegrationHttpContextItemKeys.CurrentIntegrationToken] = integrationToken;

        var limit = await ResolveRateLimitAsync(dbContext, CancellationToken.None);
        if (!TryConsumeRateLimit(integrationToken.Id, limit))
        {
            await SaveIntegrationAuditAsync(
                dbContext,
                integrationToken.Id,
                context,
                StatusCodes.Status429TooManyRequests,
                0,
                null,
                _clock.UtcNow);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(IntegrationApiResponses.Failure("RATE_LIMITED: Mas de 100 requests por minuto para este token"));
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Exception? caught = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caught = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            integrationToken.FechaUltimaUso = _clock.UtcNow;

            var statusCode = caught is null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            var parametros = context.Request.Query.Any()
                ? JsonSerializer.Serialize(context.Request.Query.ToDictionary(
                    x => x.Key,
                    x => RedactQueryValue(x.Key, x.Value.ToString())))
                : null;

            await SaveIntegrationAuditAsync(
                dbContext,
                integrationToken.Id,
                context,
                statusCode,
                (int)stopwatch.ElapsedMilliseconds,
                parametros,
                _clock.UtcNow);
        }
    }

    private static async Task SaveIntegrationAuditAsync(
        AppDbContext dbContext,
        Guid tokenId,
        HttpContext context,
        int statusCode,
        int elapsedMs,
        string? parametros,
        DateTime timestamp)
    {
        dbContext.AuditoriaIntegraciones.Add(new AuditoriaIntegracion
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Endpoint = context.Request.Path.Value ?? string.Empty,
            Metodo = context.Request.Method,
            Parametros = parametros,
            CodigoRespuesta = statusCode,
            Timestamp = timestamp,
            IpAddress = context.Connection.RemoteIpAddress,
            TiempoEjecucionMs = elapsedMs
        });

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static string? ExtractBearerToken(string authHeader)
    {
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return null;
        }

        const string prefix = "Bearer ";
        return authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authHeader[prefix.Length..].Trim()
            : null;
    }

    private static string RedactQueryValue(string key, string value)
    {
        return IsSensitiveQueryKey(key) || LooksSensitiveQueryValue(value)
            ? RedactedMarker
            : value;
    }

    private static bool IsSensitiveQueryKey(string key)
    {
        if (SensitiveQueryKeys.Contains(key))
        {
            return true;
        }

        var normalized = NormalizeQueryKey(key);
        return SensitiveQueryKeyFragments.Any(normalized.Contains);
    }

    private static string NormalizeQueryKey(string key)
    {
        return new string(key
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool LooksSensitiveQueryValue(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith(IntegrationTokenPrefix, StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> ResolveRateLimitAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        const string cacheKey = "integration_rate_limit_per_minute";
        if (_cache.TryGetValue<int>(cacheKey, out var cached) && cached > 0)
        {
            return cached;
        }

        var limitRaw = await dbContext.Configuraciones
            .Where(x => x.Clave == "integration_rate_limit_per_minute")
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        var limit = int.TryParse(limitRaw, out var parsed) ? Math.Max(parsed, 1) : 100;
        _cache.Set(cacheKey, limit, TimeSpan.FromMinutes(5));
        return limit;
    }

    private bool TryConsumeRateLimit(Guid tokenId, int limit)
    {
        var now = _clock.UtcNow;
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var previousMinute = currentMinute.AddMinutes(-1);
        var currentKey = $"{tokenId:N}:{currentMinute:yyyyMMddHHmm}";
        var previousKey = $"{tokenId:N}:{previousMinute:yyyyMMddHHmm}";

        lock (_rateLimitLock)
        {
            var currentCount = _cache.Get<int>(currentKey);
            var previousCount = _cache.Get<int>(previousKey);
            var secondsIntoMinute = (now - currentMinute).TotalSeconds;
            var previousWeight = (60.0 - secondsIntoMinute) / 60.0;
            var estimate = currentCount + (int)Math.Ceiling(previousCount * previousWeight);

            if (estimate >= limit)
            {
                return false;
            }

            _cache.Set(currentKey, currentCount + 1, TimeSpan.FromMinutes(2));
            return true;
        }
    }

    private bool IsInvalidAuthRateLimited(HttpContext context)
    {
        var key = BuildInvalidAuthRateLimitKey(context);
        lock (_invalidAuthRateLimitLock)
        {
            return _cache.TryGetValue<int>(key, out var count) &&
                   count >= _invalidAuthLimitPerMinute;
        }
    }

    private void RecordInvalidAuthFailure(HttpContext context)
    {
        var key = BuildInvalidAuthRateLimitKey(context);
        lock (_invalidAuthRateLimitLock)
        {
            var count = _cache.Get<int>(key) + 1;
            _cache.Set(key, count, TimeSpan.FromMinutes(2));
        }
    }

    private void ClearInvalidAuthFailures(HttpContext context)
    {
        _cache.Remove(BuildInvalidAuthRateLimitKey(context));
    }

    private string BuildInvalidAuthRateLimitKey(HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var client = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        var now = _clock.UtcNow;
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        return $"integration:invalid-auth:{client}:{currentMinute:yyyyMMddHHmm}";
    }

}
