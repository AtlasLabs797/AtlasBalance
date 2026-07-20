using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IAuditService
{
    Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, HttpContext httpContext, string? detallesJson, CancellationToken cancellationToken);
    Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, string? ipAddress, string? detallesJson, CancellationToken cancellationToken);
}

public sealed class AuditService : IAuditService
{
    private const int MaxDetallesBytes = 32 * 1024;
    private readonly AppDbContext _dbContext;

    public AuditService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, HttpContext httpContext, string? detallesJson, CancellationToken cancellationToken)
    {
        await LogAsync(
            usuarioId,
            tipoAccion,
            entidadTipo,
            entidadId,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            detallesJson,
            cancellationToken);
    }

    public async Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, string? ipAddress, string? detallesJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tipoAccion))
        {
            throw new ArgumentException("TipoAccion es obligatorio", nameof(tipoAccion));
        }

        var detalles = TruncarDetalles(detallesJson);
        var ip = ParseIpAddress(ipAddress);

        var auditoria = new Auditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TipoAccion = tipoAccion,
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
            Timestamp = DateTime.UtcNow,
            IpAddress = ip,
            DetallesJson = detalles
        };

        _dbContext.Auditorias.Add(auditoria);

        // V-02.06 (PR F1): SaveChanges siempre. Dentro de una transaccion
        // explicita del caller, SaveChanges encola la insercion en el ChangeTracker
        // sin hacer commit; el Commit la persiste, y un rollback la descarta.
        // Antes, dentro de transaccion se omitia el save y se quedaba en memoria,
        // asi que ImportacionService.ConfirmarAsync y el movimiento de plazo fijo
        // hacian `LogAsync` y luego solo `CommitAsync` -> la auditoria nunca se
        // grababa.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? TruncarDetalles(string? detallesJson)
    {
        if (string.IsNullOrEmpty(detallesJson))
        {
            return detallesJson;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(detallesJson) <= MaxDetallesBytes)
        {
            return detallesJson;
        }

        var prefijo = detallesJson[..Math.Min(detallesJson.Length, MaxDetallesBytes - 64)];
        return prefijo + $"... [+{MaxDetallesBytes} bytes truncados]";
    }

    private static System.Net.IPAddress? ParseIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        return System.Net.IPAddress.TryParse(ipAddress, out var parsed) ? parsed : null;
    }
}
