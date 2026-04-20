using System.Diagnostics;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GestionCaja.API.Middleware;

public static class IntegrationHttpContextItemKeys
{
    public const string CurrentIntegrationToken = "current_integration_token";
}

public sealed class IntegrationAuthMiddleware
{
    private const string RedactedMarker = "[REDACTED]";

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

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly object _rateLimitLock = new();

    public IntegrationAuthMiddleware(RequestDelegate next, IMemoryCache cache, IClock clock)
    {
        _next = next;
        _cache = cache;
        _clock = clock;
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
        var integrationToken = await integrationTokenService.ValidateActiveTokenAsync(plainToken, CancellationToken.None);
        if (integrationToken is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(IntegrationApiResponses.Failure("UNAUTHORIZED: Token de integracion invalido o revocado"));
            return;
        }

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
        context.Items[IntegrationHttpContextItemKeys.CurrentIntegrationToken] = integrationToken;

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
                    x => SensitiveQueryKeys.Contains(x.Key) ? RedactedMarker : x.Value.ToString()))
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

}
