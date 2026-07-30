using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-02.07: sobre estos contadores se decide si hay que despertar a alguien a
// las 3 de la manana. Lo que importa es que no infravaloren: un percentil que
// se queda corto o una tasa de error que no cuenta los 500 convierte la alerta
// en un adorno.
// -----------------------------------------------------------------------
public sealed class RequestMetricsTests
{
    private static readonly DateTime Ahora = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Should_Classify_Status_Codes_Into_4xx_And_5xx()
    {
        var metrics = new RequestMetrics(new FakeClock(Ahora));

        metrics.Registrar(200, 10);
        metrics.Registrar(201, 10);
        metrics.Registrar(403, 10);
        metrics.Registrar(404, 10);
        metrics.Registrar(500, 10);
        metrics.Registrar(503, 10);

        var ventana = metrics.Ventana(5);
        ventana.Peticiones.Should().Be(6);
        ventana.Errores4xx.Should().Be(2);
        ventana.Errores5xx.Should().Be(2);
        ventana.TasaErrorPorcentaje.Should().BeApproximately(66.67, 0.01);
        ventana.TasaError5xxPorcentaje.Should().BeApproximately(33.33, 0.01);
    }

    [Fact]
    public void Should_Report_Zero_Rates_Without_Traffic()
    {
        // Division por cero disfrazada: sin peticiones, la tasa tiene que ser 0
        // y no NaN, o la comparacion del job de alertas se comporta de forma
        // impredecible.
        var metrics = new RequestMetrics(new FakeClock(Ahora));

        var ventana = metrics.Ventana(5);

        ventana.Peticiones.Should().Be(0);
        ventana.TasaErrorPorcentaje.Should().Be(0);
        ventana.LatenciaP95Ms.Should().Be(0);
    }

    [Fact]
    public void Percentiles_Should_Never_Underestimate_Latency()
    {
        // El percentil sale de un histograma de cubos, asi que es aproximado.
        // La aproximacion tiene que ser por arriba: quedarse corto significa no
        // avisar de una degradacion real.
        var metrics = new RequestMetrics(new FakeClock(Ahora));

        for (var i = 0; i < 95; i++)
        {
            metrics.Registrar(200, 20);
        }
        for (var i = 0; i < 5; i++)
        {
            metrics.Registrar(200, 3_000);
        }

        var ventana = metrics.Ventana(5);
        ventana.LatenciaP50Ms.Should().BeGreaterThanOrEqualTo(20);
        ventana.LatenciaP95Ms.Should().BeGreaterThanOrEqualTo(20);
        ventana.LatenciaMaxMs.Should().Be(3_000);
    }

    [Fact]
    public void Should_Separate_The_Current_Window_From_The_Previous_One()
    {
        // El job de latencia compara ventana actual contra anterior. Si se
        // solapasen, un pico se compararia consigo mismo y nunca alertaria.
        var reloj = new FakeClockMutable(Ahora);
        var metrics = new RequestMetrics(reloj);

        metrics.Registrar(200, 10);
        metrics.Registrar(200, 10);

        reloj.UtcNow = Ahora.AddMinutes(5);
        metrics.Registrar(500, 4_000);

        metrics.Ventana(5).Peticiones.Should().Be(1);
        metrics.Ventana(5).Errores5xx.Should().Be(1);
        metrics.VentanaAnterior(5).Peticiones.Should().Be(2);
        metrics.VentanaAnterior(5).Errores5xx.Should().Be(0);
    }

    [Fact]
    public void Should_Drop_Samples_Older_Than_The_Retention()
    {
        var reloj = new FakeClockMutable(Ahora);
        var metrics = new RequestMetrics(reloj);

        metrics.Registrar(200, 10);

        // Tres horas despues: el cubo original queda fuera de las 2 horas
        // retenidas y no debe aparecer en ninguna ventana.
        reloj.UtcNow = Ahora.AddHours(3);
        metrics.Registrar(200, 10);

        metrics.Ventana(60).Peticiones.Should().Be(1);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FakeClockMutable(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
