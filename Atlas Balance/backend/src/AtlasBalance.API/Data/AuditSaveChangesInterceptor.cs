using System.Text.Json;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtlasBalance.API.Data;

/// <summary>
/// Captura INSERT/UPDATE/DELETE en entidades financieras criticas y los registra
/// en AUDITORIAS dentro de la MISMA transaccion que el SaveChanges del caller.
/// Si el SaveChanges falla, las auditorias se descartan automaticamente con EF Core.
///
/// Cobertura: solo entidades criticas para no inflar AUDITORIAS con ruido. Eventos
/// de seguridad sin cambio de entidad (login, logout, etc.) siguen yendo por
/// IAuditService.LogAsync.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private const int MaxDetallesBytes = 32 * 1024;
    private static readonly HashSet<Type> EntidadesAuditables = new()
    {
        typeof(Usuario),
        typeof(UsuarioEmail),
        typeof(RefreshToken),
        typeof(MfaTrustedDevice),
        typeof(Pais),
        typeof(Titular),
        typeof(Cuenta),
        typeof(PlazoFijo),
        typeof(FormatoImportacion),
        typeof(Extracto),
        typeof(ExtractoColumnaExtra),
        typeof(ExtractoDesglose),
        typeof(RevisionExtractoEstado),
        typeof(PermisoUsuario),
        typeof(PreferenciaUsuarioCuenta),
        typeof(AlertaSaldo),
        typeof(AlertaDestinatario),
        typeof(IaUsoUsuario),
        typeof(IntegrationToken),
        typeof(IntegrationPermission),
        typeof(MovimientoEsperado),
        typeof(Conciliacion),
        typeof(Configuracion),
        typeof(Backup),
        typeof(BackupCloudConnection),
        typeof(BackupCloudCopy),
        typeof(Exportacion),
        typeof(TipoCambio),
        typeof(DivisaActiva),
    };

    private static readonly HashSet<string> ColumnasSecretas = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "MfaSecret", "TokenHash", "RefreshToken", "EndpointScopesJson"
    };

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;
    private readonly AsyncLocal<Guid?> _usuarioIdActual;
    private readonly AsyncLocal<string?> _ipActual;

    public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor, ILogger<AuditSaveChangesInterceptor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _usuarioIdActual = new AsyncLocal<Guid?>();
        _ipActual = new AsyncLocal<string?>();
    }

    /// <summary>
    /// Permite al caller fijar el usuario/IP para la operacion actual. Lo usan
    /// IAuditService.LogAsync cuando se conoce la identidad. Si no se fija, se
    /// toma del HttpContext en cada SaveChanges.
    /// </summary>
    public void SetContextoAuditoria(Guid? usuarioId, string? ipAddress)
    {
        _usuarioIdActual.Value = usuarioId;
        _ipActual.Value = ipAddress;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CapturarCambios(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        CapturarCambios(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // EF Core hace rollback automatico; las auditorias capturadas se descartan
        // con la transaccion. Nada que limpiar aqui.
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void CapturarCambios(DbContextEventData eventData)
    {
        if (eventData.Context is not AppDbContext dbContext)
        {
            return;
        }

        Guid? usuarioId = _usuarioIdActual.Value;
        string? ip = _ipActual.Value ?? _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var timestamp = DateTime.UtcNow;

        var entries = dbContext.ChangeTracker.Entries()
            .Where(e => EntidadesAuditables.Contains(e.Entity.GetType()))
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                var auditoria = ConstruirAuditoria(entry, usuarioId, ip, timestamp);
                if (auditoria is null)
                {
                    continue;
                }
                dbContext.Auditorias.Add(auditoria);
            }
            catch (Exception ex)
            {
                // El interceptor NO debe romper el SaveChanges del negocio. Si falla la
                // construccion de la auditoria, logueamos y seguimos.
                _logger.LogWarning(ex, "No se pudo construir auditoria para {Entity} {State}", entry.Entity.GetType().Name, entry.State);
            }
        }
    }

    private Auditoria? ConstruirAuditoria(EntityEntry entry, Guid? usuarioId, string? ip, DateTime timestamp)
    {
        var tipoAccion = entry.State switch
        {
            EntityState.Added => "INSERT",
            EntityState.Modified => "UPDATE",
            EntityState.Deleted => "DELETE",
            _ => null
        };

        if (tipoAccion is null)
        {
            return null;
        }

        var entityName = entry.Entity.GetType().Name;
        var entidadId = TryGetId(entry);

        var oldValues = entry.State == EntityState.Added ? null : CapturarValores(entry.OriginalValues);
        var newValues = entry.State == EntityState.Deleted ? null : CapturarValores(entry.CurrentValues);

        var detalles = new
        {
            entity = entityName,
            state = tipoAccion,
            old = oldValues,
            @new = newValues
        };

        var detallesJson = Truncar(JsonSerializer.Serialize(detalles));

        return new Auditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TipoAccion = $"entity_{tipoAccion.ToLowerInvariant()}_{entityName}",
            EntidadTipo = entityName,
            EntidadId = entidadId,
            Timestamp = timestamp,
            IpAddress = ParseIp(ip),
            DetallesJson = detallesJson
        };
    }

    private static Guid? TryGetId(EntityEntry entry)
    {
        // Las PK son Guid (salvo lookup tables como CONFIGURACION/DIVISAS_ACTIVAS).
        var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
        if (idProp is null)
        {
            return null;
        }
        return idProp.CurrentValue is Guid g ? g : null;
    }

    private static Dictionary<string, object?> CapturarValores(PropertyValues values)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var name in values.Properties.Select(p => p.Name))
        {
            if (ColumnasSecretas.Contains(name))
            {
                dict[name] = "[REDACTED]";
                continue;
            }
            var v = values[name];
            if (v is null)
            {
                continue;
            }
            if (v is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
            {
                continue;
            }
            if (v is string s && string.IsNullOrEmpty(s))
            {
                continue;
            }
            if (v is DateOnly || v is TimeOnly || v is Guid || v is int || v is long || v is decimal || v is double || v is bool || v is Enum)
            {
                dict[name] = v;
                continue;
            }
            // Para strings no vacios y otros tipos serializables
            dict[name] = v;
        }
        return dict;
    }

    private static string Truncar(string s)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(s) <= MaxDetallesBytes)
        {
            return s;
        }
        return s[..Math.Min(s.Length, MaxDetallesBytes - 64)] + $"... [+{MaxDetallesBytes} bytes truncados]";
    }

    private static System.Net.IPAddress? ParseIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }
        return System.Net.IPAddress.TryParse(ip, out var parsed) ? parsed : null;
    }
}
