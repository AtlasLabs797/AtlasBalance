using AtlasBalance.API.Data;
using AtlasBalance.API.Logging;
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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditSigner _auditSigner;
    private readonly ISecurityEventLog _securityEventLog;

    public AuditService(
        AppDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        IAuditSigner auditSigner,
        ISecurityEventLog securityEventLog)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _auditSigner = auditSigner;
        _securityEventLog = securityEventLog;
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

        // V-02.07: UA/sesion/origen se resuelven del HttpContext en vez de
        // anadirse a la firma de LogAsync, que tiene ~20 call sites. Sin
        // HttpContext (jobs, seed) el origen queda como JOB.
        var contexto = AuditRequestContext.Resolver(_httpContextAccessor.HttpContext);

        var auditoria = new Auditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TipoAccion = tipoAccion,
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
            // Truncado a microsegundos aqui, no solo al firmar: asi el valor que
            // se guarda es exactamente el que se firmo y el que Postgres puede
            // representar en timestamptz.
            Timestamp = AuditSigner.TruncarAMicrosegundos(DateTime.UtcNow),
            IpAddress = ip,
            UserAgent = contexto.UserAgent,
            SessionId = contexto.SessionId,
            Origen = contexto.Origen,
            DetallesJson = detalles
        };

        auditoria.Firma = _auditSigner.Firmar(auditoria);

        // Espejo fuera de la BD para los eventos de seguridad: si alguien con el
        // connection string borra la cola de AUDITORIAS, la copia del Windows
        // Event Log sigue ahi (ver ISecurityEventLog).
        _securityEventLog.RegistrarSiEsRelevante(auditoria);

        // PostgreSQL exige que INSERT ... RETURNING cumpla tambien la policy
        // SELECT. En el flujo auth anonimo la policy permite insertar la
        // auditoria, pero deliberadamente no permite leer AUDITORIAS. Evitamos
        // RETURNING solo en ese flujo; Postgres sigue asignando secuencia y
        // aplicando la policy INSERT sin ampliar acceso de lectura.
        if (IsUnauthenticatedAuthFlow(_httpContextAccessor.HttpContext))
        {
            await InsertWithoutReturningAsync(auditoria, cancellationToken);
            return;
        }

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

    internal static bool IsUnauthenticatedAuthFlow(HttpContext? httpContext) =>
        httpContext is not null &&
        httpContext.User.Identity?.IsAuthenticated != true &&
        httpContext.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);

    private Task<int> InsertWithoutReturningAsync(Auditoria auditoria, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "AUDITORIAS"
                (id, usuario_id, tipo_accion, entidad_tipo, entidad_id,
                 celda_referencia, columna_nombre, valor_anterior, valor_nuevo,
                 "timestamp", ip_address, user_agent, session_id, origen,
                 firma, detalles_json)
            VALUES
                ({auditoria.Id}, {auditoria.UsuarioId}, {auditoria.TipoAccion},
                 {auditoria.EntidadTipo}, {auditoria.EntidadId},
                 {auditoria.CeldaReferencia}, {auditoria.ColumnaNombre},
                 {auditoria.ValorAnterior}, {auditoria.ValorNuevo},
                 {auditoria.Timestamp}, {auditoria.IpAddress},
                 {auditoria.UserAgent}, {auditoria.SessionId}, {auditoria.Origen},
                 {auditoria.Firma}, CAST({auditoria.DetallesJson} AS json))
            """,
            cancellationToken);

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
