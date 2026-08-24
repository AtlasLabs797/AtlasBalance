using FluentAssertions;
using AtlasBalance.API.Services.IaPlanner;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 2): tests del validador de FinancialQueryPlan.
// Cubre las reglas explicitadas en IaPlanValidator: rechazo de campos
// arbitrarios, recortes de limite, periodos ambiguos, escritura y
// operacion vacia.
public class IaPlanValidatorTests
{
    private static readonly DateOnly Anchor = new(2026, 8, 5);

    [Fact]
    public void Validar_Plan_Nulo_Devuelve_Rechazado()
    {
        var resultado = IaPlanValidator.Validar(null, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
        resultado.Motivo.Should().NotBeNullOrWhiteSpace();
        resultado.Plan.Should().BeNull();
    }

    [Fact]
    public void Validar_Operacion_Desconocida_Devuelve_Rechazado()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = (FinancialOperation)9999,
            Metrica = FinancialMetric.Gastos
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Limite_Excesivo_Se_Recorta_A_MaxLimit()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Gastos,
            Limite = 50_000
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Plan!.Limite.Should().Be(IaPlanValidator.MaxLimit);
    }

    [Fact]
    public void Validar_Limite_Cero_O_Negativo_Usa_Default()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Gastos,
            Limite = 0
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Plan!.Limite.Should().Be(IaPlanValidator.DefaultLimit);
    }

    [Fact]
    public void Validar_Importe_Min_Mayor_Que_Max_Devuelve_Rechazado()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                ImporteMinimo = 1000m,
                ImporteMaximo = 100m
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Periodo_Explicito_Sin_Fechas_Devuelve_Aclaracion()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Periodo = new FinancialPeriod { Tipo = FinancialPeriodKind.Explicito }
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.AclaracionRequerida);
        resultado.Opciones.Should().NotBeNull();
        resultado.Opciones.Should().NotBeEmpty();
    }

    [Fact]
    public void Validar_Periodo_Explicito_Con_From_Mayor_Que_To_Rechaza()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Periodo = new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 7, 1)
                }
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Periodo_Mes_Actual_Resuelve_From_To()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Periodo = new FinancialPeriod { Tipo = FinancialPeriodKind.MesActual, Anchor = Anchor }
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Plan!.Filtros.Periodo!.From.Should().Be(new DateOnly(2026, 8, 1));
        resultado.Plan!.Filtros.Periodo!.To.Should().Be(Anchor);
    }

    [Fact]
    public void Validar_Periodo_Mes_Anterior_Resuelve_From_To()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Periodo = new FinancialPeriod { Tipo = FinancialPeriodKind.MesAnterior, Anchor = Anchor }
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Plan!.Filtros.Periodo!.From.Should().Be(new DateOnly(2026, 7, 1));
        resultado.Plan!.Filtros.Periodo!.To.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public void Validar_Periodo_Anual_Resuelve_From()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Periodo = new FinancialPeriod { Tipo = FinancialPeriodKind.AnoActual, Anchor = Anchor }
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Plan!.Filtros.Periodo!.From.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void Validar_Agrupacion_Mayor_Que_Dos_Devuelve_Rechazado()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[]
            {
                FinancialGroupBy.Cuenta,
                FinancialGroupBy.Titular,
                FinancialGroupBy.Mes
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Agrupacion_None_Devuelve_Rechazado()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.None }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Agrupacion_En_Operacion_Que_No_Agrupa_Rechaza()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.GetLatest,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Cuenta }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Search_Sin_Termino_Devuelve_Aclaracion()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Search,
            Metrica = FinancialMetric.Gastos,
            TerminoBusqueda = null
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.AclaracionRequerida);
        resultado.Opciones.Should().NotBeNull();
    }

    [Fact]
    public void Validar_Search_Con_Termino_Largo_Rechaza()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Search,
            Metrica = FinancialMetric.Gastos,
            TerminoBusqueda = new string('x', IaPlanValidator.MaxSearchLength + 1)
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Compare_Sin_Comparacion_Usa_Mes_Anterior_Por_Defecto()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Compare,
            Metrica = FinancialMetric.Gastos
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
        resultado.Plan!.Comparacion.Should().NotBeNull();
        resultado.Plan!.Comparacion!.Base.From.Should().Be(new DateOnly(2026, 8, 1));
        resultado.Plan!.Comparacion!.Base.To.Should().Be(Anchor);
        resultado.Plan!.Comparacion!.Referencia.From.Should().Be(new DateOnly(2026, 7, 1));
        resultado.Plan!.Comparacion!.Referencia.To.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public void Validar_Operacion_No_Permitida_Rechaza()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.GetLatest,
            Metrica = FinancialMetric.Gastos
        };

        var resultado = IaPlanValidator.ValidarCompleto(plan, Anchor, new[] { "Sum" });

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Operacion_Permitida_Pasa()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos
        };

        var resultado = IaPlanValidator.ValidarCompleto(plan, Anchor, new[] { "Sum", "List" });

        resultado.Estado.Should().Be(FinancialPlanStatus.Ok);
    }

    [Fact]
    public void Validar_Filtros_Concepto_Largo_Rechaza()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Concepto = new string('a', IaPlanValidator.MaxConceptoLength + 1)
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }

    [Fact]
    public void Validar_Filtros_Importe_Negativo_Rechaza()
    {
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                ImporteMinimo = -1m
            }
        };

        var resultado = IaPlanValidator.Validar(plan, Anchor);

        resultado.Estado.Should().Be(FinancialPlanStatus.Rechazado);
    }
}
