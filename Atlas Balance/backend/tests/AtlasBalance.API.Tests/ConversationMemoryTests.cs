using FluentAssertions;
using AtlasBalance.API.Services.IaPlanner;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 8): tests de la memoria conversacional.
// Verifica TTL deslizante, aislamiento entre usuarios, reset por
// cambio de pais, y la API de "Nueva conversacion".
public class ConversationMemoryTests
{
    [Fact]
    public void Obtener_Sin_Sesiones_Devuelve_Nulo()
    {
        var mem = new InMemoryConversationMemory();
        mem.Obtener(Guid.NewGuid(), null).Should().BeNull();
    }

    [Fact]
    public void Actualizar_Crea_Sesion_Y_Renueva_TTL()
    {
        var clockAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);
        var now = clockAt;
        var mem = new InMemoryConversationMemory(clock: () => now);

        var sesion = mem.Actualizar(Guid.NewGuid(), null, ctx => ctx with { UltimaIntencion = "saldo" });
        sesion.UltimaIntencion.Should().Be("saldo");

        now = clockAt.AddMinutes(10);
        var sesion2 = mem.Actualizar(sesion.UsuarioId, null, ctx => ctx with { UltimaIntencion = "gastos" });
        sesion2.UltimaIntencion.Should().Be("gastos");
    }

    [Fact]
    public void Obtener_Despues_De_TTL_Devuelve_Nulo_Y_Limpia()
    {
        var clockAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);
        var now = clockAt;
        var mem = new InMemoryConversationMemory(ttl: TimeSpan.FromMinutes(5), clock: () => now);
        var userId = Guid.NewGuid();

        mem.Actualizar(userId, null, ctx => ctx with { UltimaIntencion = "saldo" });
        now = clockAt.AddMinutes(10);
        mem.Obtener(userId, null).Should().BeNull();
        mem.Count.Should().Be(0);
    }

    [Fact]
    public void Obtener_TTL_Deslizante_Reabre_Ventana()
    {
        var clockAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);
        var now = clockAt;
        var mem = new InMemoryConversationMemory(ttl: TimeSpan.FromMinutes(15), clock: () => now);
        var userId = Guid.NewGuid();

        mem.Actualizar(userId, null, ctx => ctx with { UltimaIntencion = "saldo" });
        now = clockAt.AddMinutes(10);
        mem.Actualizar(userId, null, ctx => ctx); // renueva TTL
        now = clockAt.AddMinutes(20);
        mem.Obtener(userId, null).Should().NotBeNull();
    }

    [Fact]
    public void Obtener_Usuario_Distinto_No_Accede_A_Sesion_Ajena()
    {
        var mem = new InMemoryConversationMemory();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        mem.Actualizar(user1, null, ctx => ctx with { UltimaIntencion = "privado" });
        mem.Obtener(user2, null).Should().BeNull();
    }

    [Fact]
    public void Obtener_Cambio_De_Pais_Devuelve_Nulo_Y_Limpia()
    {
        var mem = new InMemoryConversationMemory();
        var userId = Guid.NewGuid();
        var paisA = Guid.NewGuid();
        var paisB = Guid.NewGuid();

        mem.Actualizar(userId, paisA, ctx => ctx with { UltimaIntencion = "saldo" });
        mem.Obtener(userId, paisA).Should().NotBeNull();

        // Cambio de pais: la sesion con paisA ya no aplica para paisB.
        mem.Obtener(userId, paisB).Should().BeNull();
    }

    [Fact]
    public void Invalidar_Por_Pais_Elimina_Sesion()
    {
        var mem = new InMemoryConversationMemory();
        var userId = Guid.NewGuid();
        var paisA = Guid.NewGuid();
        var paisB = Guid.NewGuid();

        mem.Actualizar(userId, paisA, ctx => ctx with { UltimaIntencion = "saldo" });
        mem.InvalidarPorPais(userId, paisA);
        mem.Obtener(userId, paisA).Should().BeNull();
        mem.Count.Should().Be(0);
    }

    [Fact]
    public void Invalidar_Por_Pais_No_Toca_Otras_Sesiones()
    {
        var mem = new InMemoryConversationMemory();
        var userId = Guid.NewGuid();
        var paisA = Guid.NewGuid();
        var paisB = Guid.NewGuid();

        mem.Actualizar(userId, paisA, ctx => ctx with { UltimaIntencion = "A" });
        mem.Actualizar(userId, paisB, ctx => ctx with { UltimaIntencion = "B" });
        mem.InvalidarPorPais(userId, paisA);

        mem.Obtener(userId, paisA).Should().BeNull();
        mem.Obtener(userId, paisB).Should().NotBeNull();
    }

    [Fact]
    public void Invalidar_Usuario_Elimina_Todas_Sus_Sesiones()
    {
        var mem = new InMemoryConversationMemory();
        var userId = Guid.NewGuid();
        var paisA = Guid.NewGuid();
        var paisB = Guid.NewGuid();

        mem.Actualizar(userId, paisA, ctx => ctx with { UltimaIntencion = "A" });
        mem.Actualizar(userId, paisB, ctx => ctx with { UltimaIntencion = "B" });
        mem.Invalidar(userId);

        mem.Obtener(userId, paisA).Should().BeNull();
        mem.Obtener(userId, paisB).Should().BeNull();
        mem.Count.Should().Be(0);
    }

    [Fact]
    public void DefaultTtl_Es_30_Minutos()
    {
        // Constante documentada: la memoria conversacional vive 30
        // minutos despues del ultimo uso. Los tests la pinzan para
        // que un cambio accidental requiera actualizar.
        InMemoryConversationMemory.DefaultTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Nueva_Conversacion_Es_Invalidar()
    {
        // El boton "Nueva conversacion" del frontend es semanticamente
        // equivalente a Invalidar. Verificamos que el contrato se
        // cumple: tras Invalidar, Obtener devuelve nulo.
        var mem = new InMemoryConversationMemory();
        var userId = Guid.NewGuid();
        mem.Actualizar(userId, null, ctx => ctx with { UltimaIntencion = "saldo" });
        mem.Invalidar(userId);
        mem.Obtener(userId, null).Should().BeNull();
    }
}
