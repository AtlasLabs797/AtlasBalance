using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using Microsoft.EntityFrameworkCore;
using Xunit;

using AtlasBalance.API.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace AtlasBalance.API.Tests;

// A-6: los filtros de concepto y de rango de importes del plan deben
// aplicarse en TODAS las herramientas que operan sobre movimientos.
// Antes solo SearchTransactionsAsync los aplicaba: "¿cuanto he gastado
// en seguros?" devolvia el total de todos los gastos sin avisar.
//
// Los importes son magnitudes: se comparan contra el valor ABSOLUTO
// del movimiento (los gastos se guardan en negativo, pero el usuario
// piensa en "gastos entre 100 y 500").
public class FinancialToolsFiltrosTests
{
    private sealed record Mov(string Concepto, decimal Monto, int DiasAtras);

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static FinancialToolsService BuildSut(AppDbContext db)
    {
        return new FinancialToolsService(
            db,
            new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())));
    }

    private static UserAccessScope AdminScope(Guid userId) => new()
    {
        UserId = userId,
        IsAdmin = true,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static async Task<(Guid UsuarioId, Guid CuentaId)> SeedAsync(
        AppDbContext db,
        string nombreCuenta,
        string divisa,
        params Mov[] movimientos)
    {
        var titularId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "filtros@atlasbalance.local",
            NombreCompleto = "Filtros User",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        var cuentaId = Guid.NewGuid();
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = nombreCuenta,
            Divisa = divisa,
            Activa = true,
            BancoNombre = "Atlas Bank"
        });
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        for (int i = 0; i < movimientos.Length; i++)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = hoy.AddDays(-movimientos[i].DiasAtras),
                Concepto = movimientos[i].Concepto,
                Monto = movimientos[i].Monto,
                Saldo = 1000m + movimientos[i].Monto,
                FilaNumero = i + 1
            });
        }
        await db.SaveChangesAsync();
        return (userId, cuentaId);
    }

    private static FinancialFilters FiltroMovimiento(string concepto, decimal? min, decimal? max) => new()
    {
        Concepto = concepto,
        ImporteMinimo = min,
        ImporteMaximo = max
    };

    [Fact]
    public async Task GetLatestTransaction_Debe_Aplicar_Concepto_E_Importes()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Sueldo", -300m, 0),
            new Mov("Pago seguro", -50m, 1),
            new Mov("Pago seguro", -500m, 2));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.GetLatest,
            Metrica = FinancialMetric.Gastos,
            Filtros = FiltroMovimiento("seguro", 100m, 1000m)
        };
        var sut = BuildSut(db);

        var result = await sut.GetLatestTransactionAsync(AdminScope(userId), plan, CancellationToken.None);

        // Sin filtros seria el "Sueldo" de -300; con solo el concepto
        // seria el "Pago seguro" de -50 (fuera del rango de importes).
        result.Data.Should().NotBeNull();
        result.Data!.Monto.Should().Be(-500m);
        result.Data.Concepto.Should().Contain("seguro");
    }

    [Fact]
    public async Task GetPeriodTotals_Debe_Aplicar_Concepto_E_Importes()
    {
        await using var db = BuildDbContext();
        // Con solo el concepto entran 500 + 50 + 1500 = 2050; el rango
        // de importes deja solo el de 500.
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 0),
            new Mov("Compra tienda", -300m, 1),
            new Mov("Pago seguro", -50m, 2),
            new Mov("Pago seguro", -1500m, 3));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Cuenta },
            Filtros = FiltroMovimiento("seguro", 100m, 1000m)
        };
        var sut = BuildSut(db);

        var result = await sut.GetPeriodTotalsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(1);
        result.Data![0].Gastos.Should().Be(500m);
    }

    [Fact]
    public async Task GetRanking_Debe_Aplicar_Concepto_E_Importes()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 0),
            new Mov("Compra tienda", -300m, 1),
            new Mov("Pago seguro", -1500m, 2));
        await SeedAsync(db, "C2", "EUR",
            new Mov("Pago seguro", -800m, 0));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Ranking,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Cuenta },
            Filtros = FiltroMovimiento("seguro", 100m, 1000m)
        };
        var sut = BuildSut(db);

        var result = await sut.GetRankingAsync(AdminScope(userId), plan, CancellationToken.None);

        // C1 con solo el concepto sumaria 2000 (500 + 1500); con el
        // rango de importes, 500.
        result.FilasDevueltas.Should().Be(2);
        var c1 = result.Data!.Single(x => x.Cuenta == "C1");
        c1.Gastos.Should().Be(500m);
        result.Data!.Single(x => x.Cuenta == "C2").Gastos.Should().Be(800m);
    }

    [Fact]
    public async Task GetExpenseTrend_Debe_Aplicar_Concepto_E_Importes()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 0),
            new Mov("Compra tienda", -300m, 0),
            new Mov("Pago seguro", -50m, 0),
            new Mov("Pago seguro", -1500m, 0));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Trend,
            Metrica = FinancialMetric.Gastos,
            Filtros = FiltroMovimiento("seguro", 100m, 1000m)
        };
        var sut = BuildSut(db);

        var result = await sut.GetExpenseTrendAsync(AdminScope(userId), plan, CancellationToken.None);

        // Los cuatro movimientos estan en el mismo mes: un solo punto.
        // Con solo el concepto el gasto del mes seria 2050.
        result.FilasDevueltas.Should().Be(1);
        result.Data![0].Gastos.Should().Be(500m);
    }

    [Fact]
    public async Task ComparePeriods_Debe_Aplicar_Concepto_E_Importes()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 5),
            new Mov("Nomina", -300m, 4),
            new Mov("Pago seguro", -1500m, 6),
            new Mov("Pago seguro", -400m, 15),
            new Mov("Nomina", -200m, 16),
            new Mov("Pago seguro", -1200m, 17));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Compare,
            Metrica = FinancialMetric.Gastos,
            Filtros = FiltroMovimiento("seguro", 100m, 1000m),
            Comparacion = new FinancialComparison
            {
                Base = new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = hoy.AddDays(-10),
                    To = hoy,
                    Anchor = hoy
                },
                Referencia = new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = hoy.AddDays(-20),
                    To = hoy.AddDays(-11),
                    Anchor = hoy
                }
            }
        };
        var sut = BuildSut(db);

        var result = await sut.ComparePeriodsAsync(AdminScope(userId), plan, CancellationToken.None);

        // Con solo el concepto: base 2000 y referencia 1600.
        result.Data.Should().NotBeNull();
        result.Data!.Base.Gastos.Should().Be(500m);
        result.Data.Referencia.Gastos.Should().Be(400m);
    }

    [Fact]
    public async Task GetBalances_Con_Filtro_De_Movimiento_Debe_Rechazar_Con_Motivo()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 0));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Saldo,
            Filtros = FiltroMovimiento("seguro", null, null)
        };
        var sut = BuildSut(db);

        var result = await sut.GetBalancesAsync(AdminScope(userId), plan, CancellationToken.None);

        // Un saldo no es un movimiento: no se puede filtrar por concepto.
        // La herramienta se niega en vez de devolver el saldo sin filtrar.
        result.Rechazo.Should().BeTrue();
        result.Advertencia.Should().NotBeNullOrEmpty();
        result.Advertencia.Should().Contain("seguro");
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingMovements_Con_Filtro_De_Movimiento_Debe_Rechazar_Con_Motivo()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 0));
        db.MovimientosEsperados.Add(new MovimientoEsperado
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            FechaEsperada = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(5),
            Monto = 100m,
            Divisa = "EUR",
            Estado = "pendiente",
            Origen = "manual"
        });
        await db.SaveChangesAsync();
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Gastos,
            Filtros = FiltroMovimiento("seguro", 100m, 500m)
        };
        var sut = BuildSut(db);

        var result = await sut.GetPendingMovementsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Rechazo.Should().BeTrue();
        result.Advertencia.Should().NotBeNullOrEmpty();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAnomalies_Debe_Aplicar_Concepto()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -100m, 1),
            new Mov("Pago seguro", -100m, 2),
            new Mov("Compra tienda", -100m, 1),
            new Mov("Compra tienda", -100m, 2));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos,
            Filtros = FiltroMovimiento("seguro", null, null)
        };

        var result = await BuildSut(db).DetectAnomaliesAsync(AdminScope(userId), plan, CancellationToken.None);

        // Sin el filtro habria un duplicado de "Compra tienda" tambien.
        result.Data.Should().ContainSingle(a => a.Tipo == "DUPLICADO_PROBABLE");
        result.Data!.Should().OnlyContain(a => a.Descripcion.Contains("seguro"));
    }

    [Fact]
    public async Task DetectAnomalies_Debe_Aplicar_Rango_De_Importes_Sobre_El_Valor_Absoluto()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago pequeno", -50m, 1),
            new Mov("Pago pequeno", -50m, 2),
            new Mov("Pago medio", -500m, 1),
            new Mov("Pago medio", -500m, 2),
            new Mov("Pago grande", -5000m, 1),
            new Mov("Pago grande", -5000m, 2));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos,
            Filtros = FiltroMovimiento(null!, 100m, 1000m)
        };

        var result = await BuildSut(db).DetectAnomaliesAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Data.Should().ContainSingle(a => a.Tipo == "DUPLICADO_PROBABLE");
        result.Data!.Should().OnlyContain(a => a.Descripcion.Contains("Pago medio"));
    }

    [Fact]
    public async Task PlanExecutor_Debe_Convertir_El_Filtro_No_Aplicable_En_Rechazo_Visible()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago seguro", -500m, 0));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Saldo,
            Filtros = FiltroMovimiento("seguro", null, null)
        };
        var compound = new CompoundPlan
        {
            Pasos = new[] { new PlanStep(0, "saldo", plan, Array.Empty<int>()) }
        };

        var result = await new PlanExecutor()
            .EjecutarAsync(AdminScope(userId), compound, BuildSut(db), CancellationToken.None);

        // El ejecutor devuelve el motivo como advertencia del plan
        // compuesto: es lo que AtlasAiService muestra en el chat.
        result.Exito.Should().BeFalse();
        result.Advertencia.Should().NotBeNullOrEmpty();
        result.Advertencia.Should().Contain("seguro");
    }
}
