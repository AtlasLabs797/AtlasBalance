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
        var hayTransaccionActiva = _dbContext.Database.CurrentTransaction is not null;

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

        if (hayTransaccionActiva)
        {
            // La auditoria se persiste en el mismo SaveChanges/Commit que el caller,
            // garantizando atomicidad. Si la transaccion hace rollback, la auditoria
            // tambien se descarta. Esto es la unica forma de que la tabla AUDITORIAS
            // no mienta sobre operaciones revertidas.
            return;
        }

        // Legacy / fuera de transaccion: persistir en commit propio. El caller debe
        // migrar a una transaccion explicita para garantizar atomicidad.
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
