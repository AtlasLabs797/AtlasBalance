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
        _dbContext.Auditorias.Add(new Auditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TipoAccion = tipoAccion,
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
            Timestamp = DateTime.UtcNow,
            IpAddress = ParseIpAddress(ipAddress),
            DetallesJson = detallesJson
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
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
