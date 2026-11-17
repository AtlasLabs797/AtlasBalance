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

// B-10: agregados que suman o comparan divisas distintas, y
// "duplicado probable" que marca pagos recurrentes legitimos.
//
//   (i)   SnapshotAsync agrupaba por 1: sumaba EUR y USD en el mismo
//         total y comparaba ese total mixto entre dos periodos.
//   (ii)  El ranking tomaba el top-N global sobre filas que mezclan
//         divisas: comparaba importes de EUR con importes de USD.
//   (iii) El duplicado probable agrupaba por (cuenta, concepto,
//         importe) sin mirar la fecha: una prima de seguro mensual se
//         marcaba como duplicado.
public class FinancialToolsDivisaTests
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
        var titularId = db.Titulares.Local.Any() ? db.Titulares.Local.First().Id : Guid.NewGuid();
        if (!db.Titulares.Local.Any())
        {
            db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        }
        var userId = db.Usuarios.Local.Any() ? db.Usuarios.Local.First().Id : Guid.NewGuid();
        if (!db.Usuarios.Local.Any())
        {
            db.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = "divisa@atlasbalance.local",
                NombreCompleto = "Divisa User",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PuedeUsarIa = true
            });
        }
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

    [Fact]
    public async Task ComparePeriods_No_Debe_Mezclar_Divisas_Distintas()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "EUR-1", "EUR",
            new Mov("Gasto EUR", -100m, 5));
        await SeedAsync(db, "USD-1", "USD",
            new Mov("Gasto USD", -200m, 5));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Compare,
            Metrica = FinancialMetric.Gastos,
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

        // 100 EUR + 200 USD no es 300 de nada: la comparacion se rechaza
        // con motivo en vez de devolver un total mixto.
        result.Rechazo.Should().BeTrue();
        result.Advertencia.Should().NotBeNullOrEmpty();
        result.Advertencia.Should().Contain("EUR");
        result.Advertencia.Should().Contain("USD");
    }

    [Fact]
    public async Task ComparePeriods_Con_Una_Sola_Divisa_Fija_Compara_Esa_Divisa()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "EUR-1", "EUR",
            new Mov("Gasto EUR", -100m, 5),
            new Mov("Gasto EUR", -80m, 15));
        await SeedAsync(db, "USD-1", "USD",
            new Mov("Gasto USD", -200m, 5),
            new Mov("Gasto USD", -50m, 15));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Compare,
            Metrica = FinancialMetric.Gastos,
            Filtros = new FinancialFilters { Divisas = new[] { "EUR" } },
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

        result.Rechazo.Should().BeFalse();
        result.Data.Should().NotBeNull();
        result.Data!.Base.Gastos.Should().Be(100m);
        result.Data.Referencia.Gastos.Should().Be(80m);
        result.Data.Base.Divisa.Should().Be("EUR");
    }

    [Fact]
    public async Task GetRanking_Debe_Aplicar_El_Top_N_Dentro_De_Cada_Divisa()
    {
        await using var db = BuildDbContext();
        var (userId, _) = await SeedAsync(db, "EUR-1", "EUR",
            new Mov("Gasto", -900m, 0));
        await SeedAsync(db, "EUR-2", "EUR",
            new Mov("Gasto", -100m, 0));
        await SeedAsync(db, "USD-1", "USD",
            new Mov("Gasto", -500m, 0));
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Ranking,
            Metrica = FinancialMetric.Gastos,
            Agrupaciones = new[] { FinancialGroupBy.Cuenta },
            Limite = 1
        };
        var sut = BuildSut(db);

        var result = await sut.GetRankingAsync(AdminScope(userId), plan, CancellationToken.None);

        // El top-1 global (viejo) comparaba 900 EUR con 500 USD y se
        // quedaba solo con la cuenta EUR. Ahora hay un top-1 por divisa.
        result.FilasDevueltas.Should().Be(2);
        result.Data!.Should().Contain(x => x.Cuenta == "EUR-1" && x.Divisa == "EUR" && x.Gastos == 900m);
        result.Data!.Should().Contain(x => x.Cuenta == "USD-1" && x.Divisa == "USD" && x.Gastos == 500m);
    }

    [Fact]
    public async Task DetectAnomalies_No_Debe_Marcar_El_Pago_Recurrente_Mensual_Como_Duplicado()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago", -100m, 0));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        // La misma prima, un mes antes: pago recurrente legitimo.
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-1),
            Concepto = "Pago",
            Monto = -100m,
            Saldo = 900m,
            FilaNumero = 2
        });
        await db.SaveChangesAsync();
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);

        var result = await sut.DetectAnomaliesAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Data.Should().NotContain(a => a.Tipo == "DUPLICADO_PROBABLE");
    }

    [Fact]
    public async Task DetectAnomalies_Debe_Marcar_El_Duplicado_Proximo_En_El_Tiempo()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedAsync(db, "C1", "EUR",
            new Mov("Pago duplicado", -100m, 1));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = hoy.AddDays(-3),
            Concepto = "Pago duplicado",
            Monto = -100m,
            Saldo = 900m,
            FilaNumero = 2
        });
        await db.SaveChangesAsync();
        var plan = new FinancialQueryPlan
        {
            Operacion = FinancialOperation.Anomalies,
            Metrica = FinancialMetric.Gastos
        };
        var sut = BuildSut(db);

        var result = await sut.DetectAnomaliesAsync(AdminScope(userId), plan, CancellationToken.None);

        result.Data.Should().Contain(a => a.Tipo == "DUPLICADO_PROBABLE");
    }

    [Fact]
    public void Constante_AnomalyDuplicateMaxDays_Es_Tres_Dias()
    {
        // La ventana de proximidad la fijan los tests: sin ella, un pago
        // recurrente mensual se marcaba como duplicado probable.
        FinancialToolsService.AnomalyDuplicateMaxDays.Should().Be(3);
    }
}
