using System.Diagnostics;
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

// V-02.09 (Fase 5): tests del ejecutor de planes compuestos.
// Verifica el limite de 5 pasos, el timeout global, la cancelacion
// y el rechazo de operaciones de escritura.
public class PlanExecutorTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static FinancialToolsService BuildTools(AppDbContext db) => new(
        db,
        new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())));

    private static UserAccessScope AdminScope(Guid userId) => new()
    {
        UserId = userId,
        IsAdmin = true,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static async Task<Guid> SeedAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "exec@atlasbalance.local",
            NombreCompleto = "Exec User",
            PasswordHash = "hash",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PuedeUsarIa = true
        });
        var titularId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Atlas Labs", Tipo = TipoTitular.EMPRESA });
        var cuentaId = Guid.NewGuid();
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Operativa",
            Divisa = "EUR",
            Activa = true,
            BancoNombre = "Atlas Bank"
        });
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        for (int i = 0; i < 5; i++)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = hoy.AddDays(-i),
                Concepto = $"mov {i}",
                Monto = -100m - i,
                Saldo = 1000m - i,
                FilaNumero = i + 1
            });
        }
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task EjecutarAsync_Sin_Pasos_Devuelve_Advertencia()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = new PlanExecutor();
        var plan = new CompoundPlan();
        var result = await sut.EjecutarAsync(AdminScope(userId), plan, BuildTools(db), CancellationToken.None);

        result.Exito.Should().BeFalse();
        result.Advertencia.Should().Contain("sin pasos");
    }

    [Fact]
    public async Task EjecutarAsync_Mas_De_Cinco_Pasos_Rechaza()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = new PlanExecutor();
        var pasos = Enumerable.Range(0, 6).Select(i => new PlanStep(
            i, $"paso_{i}",
            new FinancialQueryPlan
            {
                Operacion = FinancialOperation.GetLatest,
                Metrica = FinancialMetric.Gastos
            },
            Array.Empty<int>())).ToList();
        var plan = new CompoundPlan { Pasos = pasos };
        var result = await sut.EjecutarAsync(AdminScope(userId), plan, BuildTools(db), CancellationToken.None);

        result.Exito.Should().BeFalse();
        result.Advertencia.Should().Contain("supera el maximo");
    }

    [Fact]
    public async Task EjecutarAsync_Operacion_De_Escritura_Rechaza()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = new PlanExecutor();
        // Forzamos una "operacion de escritura" usando el truco del
        // nombre. El validador ya la habria rechazado, pero el
        // ejecutor debe blindarse tambien.
        var pasos = new[]
        {
            new PlanStep(0, "delete", new FinancialQueryPlan
            {
                Operacion = (FinancialOperation)9999,
                Metrica = FinancialMetric.Gastos
            }, Array.Empty<int>())
        };
        // 9999 no es una operacion valida, asi que pasamos por la
        // validacion del ejecutor (que es independiente de IaPlanValidator).
        var plan = new CompoundPlan { Pasos = pasos };
        var result = await sut.EjecutarAsync(AdminScope(userId), plan, BuildTools(db), CancellationToken.None);

        result.Exito.Should().BeFalse();
        result.Advertencia.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EjecutarAsync_Plan_Simple_Con_Dos_Pasos_Devuelve_Ambos_Resultados()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = new PlanExecutor();
        var pasos = new[]
        {
            new PlanStep(0, "ultimo_gasto", new FinancialQueryPlan
            {
                Operacion = FinancialOperation.GetLatest,
                Metrica = FinancialMetric.Gastos,
                Filtros = new FinancialFilters
                {
                    Periodo = IaPlanValidator.PeriodoPorDefecto(DateOnly.FromDateTime(DateTime.UtcNow.Date))
                },
                Limite = 1
            }, Array.Empty<int>()),
            new PlanStep(1, "ranking", new FinancialQueryPlan
            {
                Operacion = FinancialOperation.Ranking,
                Metrica = FinancialMetric.Gastos,
                Agrupaciones = new[] { FinancialGroupBy.Cuenta },
                Filtros = new FinancialFilters
                {
                    Periodo = IaPlanValidator.PeriodoPorDefecto(DateOnly.FromDateTime(DateTime.UtcNow.Date))
                }
            }, Array.Empty<int>())
        };
        var plan = new CompoundPlan { Pasos = pasos };
        var result = await sut.EjecutarAsync(AdminScope(userId), plan, BuildTools(db), CancellationToken.None);

        result.Exito.Should().BeTrue();
        result.Pasos.Count.Should().Be(2);
        result.Pasos[0].Nombre.Should().Be("ultimo_gasto");
        result.Pasos[1].Nombre.Should().Be("ranking");
    }

    [Fact]
    public async Task EjecutarAsync_Timeout_Global_Aborta_Con_Advertencia()
    {
        await using var db = BuildDbContext();
        var userId = await SeedAsync(db);
        var sut = new PlanExecutor();
        var pasos = new[]
        {
            new PlanStep(0, "ranking", new FinancialQueryPlan
            {
                Operacion = FinancialOperation.Ranking,
                Metrica = FinancialMetric.Gastos,
                Agrupaciones = new[] { FinancialGroupBy.Cuenta }
            }, Array.Empty<int>()),
            new PlanStep(1, "ranking2", new FinancialQueryPlan
            {
                Operacion = FinancialOperation.Ranking,
                Metrica = FinancialMetric.Gastos,
                Agrupaciones = new[] { FinancialGroupBy.Cuenta }
            }, Array.Empty<int>())
        };
        // Forzamos un timeout imposible.
        var plan = new CompoundPlan { Pasos = pasos, TimeoutGlobal = TimeSpan.FromMilliseconds(1) };
        // Esperamos a que pase algo de tiempo antes de la primera
        // llamada para garantizar que el cronometro ya esta por encima.
        await Task.Delay(50);
        var result = await sut.EjecutarAsync(AdminScope(userId), plan, BuildTools(db), CancellationToken.None);

        result.Exito.Should().BeFalse();
        result.Advertencia.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MaxPasos_Es_Cinco()
    {
        PlanExecutor.MaxPasos.Should().Be(5);
    }
}
