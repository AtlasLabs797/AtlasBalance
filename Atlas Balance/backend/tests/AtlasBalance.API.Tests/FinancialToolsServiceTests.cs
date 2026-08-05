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

// V-02.09 (Fase 3): tests de las herramientas financieras. Cada
// herramienta se prueba en aislamiento contra un InMemory sembrado
// con titulares/cuentas/extractos. La verificacion contra PostgreSQL
// real (50k filas) queda para CI (mismo patron que el resto del repo).
public class FinancialToolsServiceTests
{
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

    private static async Task<Guid> SeedAsync(
        AppDbContext db,
        (string Titular, string Cuenta, string Divisa, decimal[] Movimientos)[] datos)
    {
        var titularId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "tools@atlasbalance.local",
            NombreCompleto = "Tools User",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        foreach (var (titular, cuenta, divisa, montos) in datos)
        {
            db.Cuentas.Add(new Cuenta
            {
                Id = Guid.NewGuid(),
                TitularId = titularId,
                Nombre = cuenta,
                Divisa = divisa,
                Activa = true,
                BancoNombre = "Atlas Bank"
            });
            var cid = db.Cuentas.Local.First(x => x.Nombre == cuenta).Id;
            for (int i = 0; i < montos.Length; i++)
            {
                db.Extractos.Add(new Extracto
                {
                    Id = Guid.NewGuid(),
                    CuentaId = cid,
                    Fecha = hoy.AddDays(-i),
                    Concepto = $"mov {i}",
                    Monto = montos[i],
                    Saldo = 1000m + montos[i],
                    FilaNumero = i + 1
                });
            }
        }
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task GetLatestTransaction_Debe_Devolver_Ultimo_Movimiento()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m, 50m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.GetLatest,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);
        var result = await sut.GetLatestTransactionAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Data.Should().NotBeNull();
        result.Data!.Monto.Should().Be(-100m);
        result.FilasDevueltas.Should().Be(1);
    }

    [Fact]
    public async Task GetPeriodTotals_Debe_Agrupar_Por_Cuenta()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m, 50m }),
            ("Atlas Labs", "C2", "EUR", new decimal[] { -500m, 100m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Cuenta }
        };
        var sut = BuildSut(db);
        var result = await sut.GetPeriodTotalsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(2);
        result.FilasAnalizadas.Should().Be(5);
    }

    [Fact]
    public async Task GetPeriodTotals_Debe_Agrupar_Por_Titular()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m }),
            ("Atlas Labs", "C2", "USD", new decimal[] { -50m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Sum,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Titular }
        };
        var sut = BuildSut(db);
        var result = await sut.GetPeriodTotalsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(2);
    }

    [Fact]
    public async Task GetBalances_Debe_Devolver_Saldo_Por_Cuenta()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -50m, 200m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Saldo
        };
        var sut = BuildSut(db);
        var result = await sut.GetBalancesAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(1);
        // Ultimo extracto (FilaNumero 3): -100, -50, 200 -> Saldo = 1000 + 200 = 1200.
        result.Data![0].Saldo.Should().Be(1200m);
    }

    [Fact]
    public async Task GetRanking_Debe_Ordenar_Por_Gastos_Descendente()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m }),
            ("Atlas Labs", "C2", "EUR", new decimal[] { -500m }),
            ("Atlas Labs", "C3", "EUR", new decimal[] { -50m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Ranking,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Cuenta },
            Limite = 2
        };
        var sut = BuildSut(db);
        var result = await sut.GetRankingAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(2);
        result.Data![0].Gastos.Should().Be(500m);
    }

    [Fact]
    public async Task GetRevisionItems_Debe_Filtrar_Por_Estado()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m, 50m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.List,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters
            {
                Estados = new[] { "PENDIENTE" }
            }
        };
        var sut = BuildSut(db);
        var result = await sut.GetRevisionItemsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(3);
    }

    [Fact]
    public async Task GetExpenseTrend_Debe_Resumir_Por_Mes()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m, 50m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Trend,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);
        var result = await sut.GetExpenseTrendAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetPendingMovements_Debe_Devolver_Movimientos_Pendientes()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m })
        });
        // Anado un movimiento esperado pendiente.
        var cuentaId = db.Cuentas.First().Id;
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
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);
        var result = await sut.GetPendingMovementsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(1);
        result.Data![0].Estado.Should().Be("pendiente");
    }

    [Fact]
    public async Task SearchTransactions_Debe_Buscar_Por_Termino()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m })
        });
        // Modifico el concepto del primer extracto.
        var extracto = db.Extractos.First();
        extracto.Concepto = "Pago de nomina mensual";
        await db.SaveChangesAsync();

        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Search,
            Metrica = FinancialMetric.Gastos,
            TerminoBusqueda = "nomina"
        };
        var sut = BuildSut(db);
        var result = await sut.SearchTransactionsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(1);
        result.Data![0].Concepto.Should().Contain("nomina");
    }

    [Fact]
    public async Task SearchTransactions_Sin_Termino_Devuelve_Lista_Vacia_Y_Advertencia()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Search,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);
        var result = await sut.SearchTransactionsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().Be(0);
        result.Advertencia.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ComparePeriods_Debe_Calcular_Variacion()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -200m, -50m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Compare,
            Metrica = FinancialMetric.Gastos,
            Comparacion = new FinancialComparison
            {
                Base = new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-10),
                    To = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    Anchor = DateOnly.FromDateTime(DateTime.UtcNow.Date)
                },
                Referencia = new FinancialPeriod
                {
                    Tipo = FinancialPeriodKind.Explicito,
                    From = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-20),
                    To = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-11),
                    Anchor = DateOnly.FromDateTime(DateTime.UtcNow.Date)
                }
            }
        };
        var sut = BuildSut(db);
        var result = await sut.ComparePeriodsAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Data.Should().NotBeNull();
        result.Data!.Base.Gastos.Should().Be(350m);
    }

    [Fact]
    public async Task DetectAnomalies_Debe_Detectar_Duplicado()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -100m, -100m, -50m, -200m })
        });
        // Pongo el mismo concepto en los dos primeros.
        var extracts = db.Extractos.OrderBy(e => e.FilaNumero).Take(2).ToList();
        foreach (var e in extracts) e.Concepto = "Pago duplicado";
        await db.SaveChangesAsync();

        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);
        var result = await sut.DetectAnomaliesAsync(AdminScope(userId), plan, CancellationToken.None);

        result.FilasDevueltas.Should().BeGreaterThan(0);
        result.Data!.Should().Contain(a => a.Tipo == "DUPLICADO_PROBABLE");
    }

    [Fact]
    public async Task DetectAnomalies_Debe_Detectar_Importe_Atipico()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db, new[] {
            ("Atlas Labs", "C1", "EUR", new decimal[] { -10m, -20m, -15m, -1000m })
        });
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);
        var result = await sut.DetectAnomaliesAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Data!.Should().Contain(a => a.Tipo == "IMPORTE_ATIPICO");
    }

    [Fact]
    public void Constantes_Documentadas_Anomalias_Estan_Expuestas()
    {
        // Los tests pueden pinzar las constantes para que un cambio
        // accidental de umbral (3x / 6 meses) requiera actualizar tests.
        FinancialToolsService.AnomalyHighFactor.Should().Be(3m);
        FinancialToolsService.AnomalyHistoryMonths.Should().Be(6);
    }
}
