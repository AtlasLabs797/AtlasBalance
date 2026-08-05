using System.Collections.Concurrent;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 8): memoria conversacional de corta duracion.
//
// Reglas:
//   - Vinculada al usuario autenticado (no se comparte entre
//     usuarios).
//   - TTL: 30 minutos, ventana deslizante. Cualquier Actualizar()
//     renueva la ventana.
//   - Almacenada en memoria del proceso. En produccion real
//     iria a Redis o similar; aqui la implementacion
//     in-memory sirve para los tests y para el caso on-premise
//     donde el backend es un unico proceso.
//   - Cambio de pais: invalida la sesion automaticamente (el
//     contexto esta atado al pais del scope; si cambia, los
//     acumulados podrian no aplicar al nuevo ambito).
//   - Cierre de sesion / boton "Nueva conversacion": elimina la
//     sesion por completo.
//   - Solo se guarda contexto estructurado: ultima intencion,
//     metrica, periodo, cuenta, titular, categoria, divisa y
//     comparacion activa. NO se guarda la pregunta completa ni
//     la respuesta: eso iria al log/auditoria de Fase 11, no a
//     la memoria conversacional.

public sealed record ConversationContext
{
    public Guid UsuarioId { get; init; }
    public Guid? PaisId { get; init; }
    public string? UltimaIntencion { get; init; }
    public string? UltimaOperacion { get; init; }
    public string? UltimaMetrica { get; init; }
    public string? UltimoPeriodo { get; init; }
    public IReadOnlyList<string>? UltimasCuentas { get; init; }
    public IReadOnlyList<string>? UltimosTitulares { get; init; }
    public IReadOnlyList<string>? UltimasCategorias { get; init; }
    public IReadOnlyList<string>? UltimasDivisas { get; init; }
    public bool ComparacionActiva { get; init; }
    public DateTime UltimaActividadUtc { get; init; } = DateTime.UtcNow;
}

public interface IConversationMemory
{
    ConversationContext? Obtener(Guid usuarioId, Guid? paisId);
    ConversationContext Actualizar(Guid usuarioId, Guid? paisId, Func<ConversationContext, ConversationContext> mutador);
    void Invalidar(Guid usuarioId);
    void InvalidarPorPais(Guid usuarioId, Guid paisId);
}

public sealed class InMemoryConversationMemory : IConversationMemory
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<Guid, ConversationContext> _sesiones = new();
    private readonly Func<DateTime> _clock;

    public InMemoryConversationMemory(TimeSpan? ttl = null, Func<DateTime>? clock = null)
    {
        _ttl = ttl ?? DefaultTtl;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public ConversationContext? Obtener(Guid usuarioId, Guid? paisId)
    {
        if (!_sesiones.TryGetValue(usuarioId, out var ctx))
        {
            return null;
        }
        if (ctx.UsuarioId != usuarioId)
        {
            return null;
        }
        if (ctx.PaisId != paisId)
        {
            // Cambio de pais: la sesion no aplica.
            return null;
        }
        if (_clock() - ctx.UltimaActividadUtc > _ttl)
        {
            _sesiones.TryRemove(usuarioId, out _);
            return null;
        }
        return ctx;
    }

    public ConversationContext Actualizar(Guid usuarioId, Guid? paisId, Func<ConversationContext, ConversationContext> mutador)
    {
        var now = _clock();
        var baseContext = Obtener(usuarioId, paisId) ?? new ConversationContext
        {
            UsuarioId = usuarioId,
            PaisId = paisId,
            UltimaActividadUtc = now
        };
        var mutado = mutador(baseContext);
        var updated = mutado with
        {
            UsuarioId = usuarioId,
            PaisId = paisId,
            UltimaActividadUtc = now
        };
        _sesiones[usuarioId] = updated;
        return updated;
    }

    public void Invalidar(Guid usuarioId)
    {
        _sesiones.TryRemove(usuarioId, out _);
    }

    public void InvalidarPorPais(Guid usuarioId, Guid paisId)
    {
        if (_sesiones.TryGetValue(usuarioId, out var ctx) && ctx.PaisId == paisId)
        {
            _sesiones.TryRemove(usuarioId, out _);
        }
    }

    public int Count => _sesiones.Count;
}
