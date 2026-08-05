using FluentAssertions;
using AtlasBalance.API.Services.IaPlanner;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 7): tests de la logica de veredictos de tendencia y
// formateo de anomalias. La consulta real a la BD se prueba en
// FinancialToolsServiceTests; aqui verificamos las reglas de
// calculo de la variacion y el lenguaje "posible anomalia" que
// la respuesta envia al proveedor.
public class TendenciasAnomaliasServiceTests
{
    [Fact]
    public void VariacionSignificativa_Es_15_Por_Ciento()
    {
        // Constante documentada: por debajo de este porcentaje la
        // tendencia se considera estable. Tests pinzan para que un
        // cambio accidental requiera actualizar.
        TendenciasAnomaliasService.VariacionSignificativaPorcentaje.Should().Be(15m);
    }

    [Fact]
    public void CalcularVeredictos_Sin_Datos_Devuelve_Vacio()
    {
        var result = TendenciasAnomaliasService.CalcularVeredictos(
            Array.Empty<TrendPoint>(),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        result.Should().BeEmpty();
    }

    [Fact]
    public void CalcularVeredictos_Variacion_Menor_15_Por_Ciento_Es_Estable()
    {
        // 1000 -> 1100 = +10% (estable)
        var puntos = new List<TrendPoint>
        {
            new(2026, 6, "EUR", 0, 1000, -1000, 5),
            new(2026, 7, "EUR", 0, 1100, -1100, 5)
        };
        var result = TendenciasAnomaliasService.CalcularVeredictos(
            puntos,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().HaveCount(1);
        result[0].Veredicto.Should().Be(TrendVerdict.Estable);
    }

    [Fact]
    public void CalcularVeredictos_Variacion_Mayor_15_Por_Ciento_Es_Sube()
    {
        // 1000 -> 1300 = +30% (sube)
        var puntos = new List<TrendPoint>
        {
            new(2026, 6, "EUR", 0, 1000, -1000, 5),
            new(2026, 7, "EUR", 0, 1300, -1300, 5)
        };
        var result = TendenciasAnomaliasService.CalcularVeredictos(
            puntos,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().HaveCount(1);
        result[0].Veredicto.Should().Be(TrendVerdict.Sube);
        result[0].VariacionPorcentaje.Should().Be(30m);
    }

    [Fact]
    public void CalcularVeredictos_Variacion_Negativa_Mayor_15_Por_Ciento_Es_Baja()
    {
        // 1000 -> 700 = -30% (baja)
        var puntos = new List<TrendPoint>
        {
            new(2026, 6, "EUR", 0, 1000, -1000, 5),
            new(2026, 7, "EUR", 0, 700, -700, 5)
        };
        var result = TendenciasAnomaliasService.CalcularVeredictos(
            puntos,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result[0].Veredicto.Should().Be(TrendVerdict.Baja);
    }

    [Fact]
    public void CalcularVeredictos_Sin_Anterior_Pero_Con_Reciente_Devuelve_100_Por_Ciento()
    {
        // Si el trimestre anterior estaba vacio pero ahora hay
        // gastos, marcamos 100% (caso de nuevo gasto sin base
        // historica).
        var puntos = new List<TrendPoint>
        {
            new(2026, 7, "EUR", 0, 500, -500, 3)
        };
        var result = TendenciasAnomaliasService.CalcularVeredictos(
            puntos,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result[0].VariacionPorcentaje.Should().Be(100m);
    }

    [Fact]
    public void CalcularVeredictos_Separa_Por_Divisa()
    {
        var puntos = new List<TrendPoint>
        {
            new(2026, 6, "EUR", 0, 1000, -1000, 5),
            new(2026, 7, "EUR", 0, 1100, -1100, 5),
            new(2026, 6, "USD", 0, 500, -500, 3),
            new(2026, 7, "USD", 0, 600, -600, 3)
        };
        var result = TendenciasAnomaliasService.CalcularVeredictos(
            puntos,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().HaveCount(2);
        result.Should().Contain(v => v.Divisa == "EUR");
        result.Should().Contain(v => v.Divisa == "USD");
    }

    [Fact]
    public void FormatearAnomalias_Sin_Anomalias_Devuelve_Vacio()
    {
        var lineas = TendenciasAnomaliasService.FormatearAnomalias(Array.Empty<Anomaly>());
        lineas.Should().BeEmpty();
    }

    [Fact]
    public void FormatearAnomalias_Usa_Lenguaje_Posible_No_Afirma()
    {
        var anomalias = new[]
        {
            new Anomaly("DUPLICADO_PROBABLE", "media", "Movimiento repetido en C1", null, null, null, null, null, null, null)
        };
        var lineas = TendenciasAnomaliasService.FormatearAnomalias(anomalias);

        lineas.Should().NotBeEmpty();
        lineas[0].Should().Contain("Posible anomalia");
        lineas[0].Should().Contain("duplicado probable");
        lineas[0].Should().NotContain("es fraude");
        lineas[0].Should().NotContain("es un error");
    }

    [Fact]
    public void FormatearAnomalias_Conteo_De_Casos_Por_Tipo()
    {
        var anomalias = new[]
        {
            new Anomaly("DUPLICADO_PROBABLE", "media", "dup 1", null, null, null, null, null, null, null),
            new Anomaly("DUPLICADO_PROBABLE", "media", "dup 2", null, null, null, null, null, null, null),
            new Anomaly("IMPORTE_ATIPICO", "alta", "atipico 1", null, null, null, null, null, null, null)
        };
        var lineas = TendenciasAnomaliasService.FormatearAnomalias(anomalias);

        lineas.Should().Contain(l => l.Contains("duplicado probable") && l.Contains("2 caso"));
        lineas.Should().Contain(l => l.Contains("importe atipico") && l.Contains("1 caso"));
    }
}
