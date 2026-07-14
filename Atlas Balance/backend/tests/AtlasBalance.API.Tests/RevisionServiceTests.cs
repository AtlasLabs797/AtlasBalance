using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class RevisionServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetComisionesAsync_Should_Use_Absolute_Threshold_And_Persist_State()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var smallCommissionId = Guid.NewGuid();
        var negativeCommissionId = Guid.NewGuid();
        var positiveCommissionId = Guid.NewGuid();

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "revision_comisiones_importe_minimo",
            Valor = "1",
            Tipo = "number",
            Descripcion = "Importe minimo"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = smallCommissionId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Comision bancaria",
                Monto = -1m,
                Saldo = 100m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = negativeCommissionId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 2),
                Concepto = "Comision mantenimiento",
                Monto = -1.20m,
                Saldo = 98.80m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = positiveCommissionId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 3),
                Concepto = "Comision tarjeta devuelta",
                Monto = 1.20m,
                Saldo = 100m,
                FilaNumero = 3
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 4),
                Concepto = "Pago proveedor",
                Monto = -10m,
                Saldo = 90m,
                FilaNumero = 4
            });
        await db.SaveChangesAsync();

        var scope = AdminScope();
        var sut = new RevisionService(db, new UserAccessService(db));

        var result = await sut.GetComisionesAsync(scope, new RevisionQueryRequest(), CancellationToken.None);

        result.Total.Should().Be(2);
        result.Data.Select(x => x.ExtractoId).Should().BeEquivalentTo([negativeCommissionId, positiveCommissionId]);
        result.Data.Should().OnlyContain(x => Math.Abs(x.Monto) > 1m);
        result.Data.Should().OnlyContain(x => x.EstadoDevolucion == RevisionService.EstadoPendiente);

        await sut.SetEstadoAsync(scope, negativeCommissionId, RevisionService.TipoComision, RevisionService.EstadoDevuelta, CancellationToken.None);

        var devueltas = await sut.GetComisionesAsync(
            scope,
            new RevisionQueryRequest { Estado = RevisionService.EstadoDevuelta },
            CancellationToken.None);

        devueltas.Data.Should().ContainSingle();
        devueltas.Data[0].ExtractoId.Should().Be(negativeCommissionId);

        await sut.SetEstadoAsync(scope, positiveCommissionId, RevisionService.TipoComision, RevisionService.EstadoDescartada, CancellationToken.None);

        var descartadas = await sut.GetComisionesAsync(
            scope,
            new RevisionQueryRequest { Estado = RevisionService.EstadoDescartada },
            CancellationToken.None);

        descartadas.Data.Should().ContainSingle();
        descartadas.Data[0].ExtractoId.Should().Be(positiveCommissionId);
        descartadas.Data[0].EstadoDevolucion.Should().Be(RevisionService.EstadoDescartada);

        var todasTrasDescartar = await sut.GetComisionesAsync(
            scope,
            new RevisionQueryRequest(),
            CancellationToken.None);

        todasTrasDescartar.Data.Select(x => x.ExtractoId).Should().NotContain(positiveCommissionId);
        todasTrasDescartar.Data.Should().OnlyContain(x => x.EstadoDevolucion != RevisionService.EstadoDescartada);
    }

    [Fact]
    public async Task GetSegurosAsync_Should_Detect_Insurance_Concepts_And_Filter_State()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var seguroId = Guid.NewGuid();

        db.Extractos.AddRange(
            new Extracto
            {
                Id = seguroId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 1),
                Concepto = "Recibo poliza MAPFRE hogar",
                Monto = -300m,
                Saldo = 700m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 2),
                Concepto = "Pago alquiler",
                Monto = -800m,
                Saldo = -100m,
                FilaNumero = 2
            });
        await db.SaveChangesAsync();

        var scope = AdminScope();
        var sut = new RevisionService(db, new UserAccessService(db));

        var pendientes = await sut.GetSegurosAsync(
            scope,
            new RevisionQueryRequest { Estado = RevisionService.EstadoPendiente },
            CancellationToken.None);

        pendientes.Data.Should().ContainSingle();
        pendientes.Data[0].ExtractoId.Should().Be(seguroId);
        pendientes.Data[0].Estado.Should().Be(RevisionService.EstadoPendiente);

        await sut.SetEstadoAsync(scope, seguroId, RevisionService.TipoSeguro, RevisionService.EstadoCorrecto, CancellationToken.None);

        var correctos = await sut.GetSegurosAsync(
            scope,
            new RevisionQueryRequest { Estado = RevisionService.EstadoCorrecto },
            CancellationToken.None);

        correctos.Data.Should().ContainSingle();
        correctos.Data[0].ExtractoId.Should().Be(seguroId);

        await sut.SetEstadoAsync(scope, seguroId, RevisionService.TipoSeguro, "NO_ES_SEGURO", CancellationToken.None);

        var descartados = await sut.GetSegurosAsync(
            scope,
            new RevisionQueryRequest { Estado = "DESCARTADOS" },
            CancellationToken.None);

        descartados.Data.Should().ContainSingle();
        descartados.Data[0].ExtractoId.Should().Be(seguroId);
        descartados.Data[0].Estado.Should().Be(RevisionService.EstadoDescartada);

        var todosTrasDescartar = await sut.GetSegurosAsync(
            scope,
            new RevisionQueryRequest(),
            CancellationToken.None);

        todosTrasDescartar.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetComisionesAsync_Should_Not_Treat_Plain_Transfers_As_Commissions()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var commissionId = Guid.NewGuid();

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "revision_comisiones_importe_minimo",
            Valor = "0",
            Tipo = "number",
            Descripcion = "Importe minimo"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Transferencia SEPA alquiler",
                Monto = -500m,
                Saldo = 500m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = commissionId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 2),
                Concepto = "Comision transferencia SEPA",
                Monto = -2.50m,
                Saldo = 497.50m,
                FilaNumero = 2
            });
        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));
        var result = await sut.GetComisionesAsync(AdminScope(), new RevisionQueryRequest(), CancellationToken.None);

        RevisionService.IsCommissionConcept("Transferencia SEPA alquiler").Should().BeFalse();
        RevisionService.IsCommissionConcept("Comision transferencia SEPA").Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data[0].ExtractoId.Should().Be(commissionId);
    }

    [Fact]
    public async Task GetSegurosAsync_Should_Not_Treat_Social_Security_As_Insurance()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var seguroId = Guid.NewGuid();

        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Seguro Social autonomos",
                Monto = -310m,
                Saldo = 690m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 2),
                Concepto = "Seguridad Social autonomos",
                Monto = -310m,
                Saldo = 380m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = seguroId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 3),
                Concepto = "Seguro MAPFRE hogar",
                Monto = -180m,
                Saldo = 200m,
                FilaNumero = 3
            });
        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));
        var result = await sut.GetSegurosAsync(AdminScope(), new RevisionQueryRequest(), CancellationToken.None);

        RevisionService.IsInsuranceConcept("Seguro Social autonomos").Should().BeFalse();
        RevisionService.IsInsuranceConcept("Seguridad Social autonomos").Should().BeFalse();
        RevisionService.IsInsuranceConcept("Seguro MAPFRE hogar").Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data[0].ExtractoId.Should().Be(seguroId);
    }

    [Fact]
    public async Task GetComisionesAsync_Should_Ignore_Reported_False_Positive_Concepts()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var commissionId = Guid.NewGuid();
        var falsePositiveConcepts = new[]
        {
            "TRANSFERENCIA A PINTURAS SILVANO",
            "ABONO TRANSFERENCIA DE FRANCISCO GUTIERREZ GALVEZ",
            "TRANSFERENCIA REALE SEGUROS GENERALES, S.A.",
            "TRANSFERENCIA ANDAMIOS OMEGA 2005 S.L.",
            "TRANSFERENCIA TELEFONICA DE ESPANA, SAU",
            "TRANSFERENCIAS",
            "Cuota leasing 592500026",
            "PRESTAMOS ADEUDO CUOTA N.8077279515 31/03/26",
            "CUOTAS DE LA SEGURIDAD SOCIAL",
            "ADEUDO DE CUOTA DE LA SEGURIDAD SOCIAL",
            "TARJETA CREDITO DIDAC FERNANDEZ ROMERO",
            "Adeudo mensual de tarjeta",
            "TRANSFERENCIA DE SERVICIOS PROFESIONALES ON TRADING LOGISTIC VALLES, S.L",
            "Transferencia recibida",
            "Transferencia realizada"
        };

        db.Configuraciones.Add(new Configuracion
        {
            Clave = "revision_comisiones_importe_minimo",
            Valor = "0",
            Tipo = "number",
            Descripcion = "Importe minimo"
        });

        for (var i = 0; i < falsePositiveConcepts.Length; i++)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 1).AddDays(i),
                Concepto = falsePositiveConcepts[i],
                Monto = i % 2 == 0 ? -100m - i : 100m + i,
                Saldo = 1_000m - i,
                FilaNumero = i + 1
            });
        }

        db.Extractos.Add(new Extracto
        {
            Id = commissionId,
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 5, 20),
            Concepto = "Comision mantenimiento cuenta",
            Monto = -4.25m,
            Saldo = 900m,
            FilaNumero = falsePositiveConcepts.Length + 1
        });
        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));
        var result = await sut.GetComisionesAsync(AdminScope(), new RevisionQueryRequest(), CancellationToken.None);

        falsePositiveConcepts.Should().OnlyContain(concept => !RevisionService.IsCommissionConcept(concept));
        result.Data.Should().ContainSingle();
        result.Data[0].ExtractoId.Should().Be(commissionId);
    }

    [Fact]
    public async Task GetSegurosAsync_Should_Ignore_Reported_False_Positive_Concepts()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var seguroId = Guid.NewGuid();
        var falsePositiveRows = new (string Concepto, decimal Monto)[]
        {
            ("SEGUROS SOCIALES TGSS. COTIZACION 005 R.E.AUTONOMOS", -299.57m),
            ("TRANSFERENCIA Generalitat de Catalunya", 880.25m),
            ("TRANSFERENCIA REALE SEGUROS GENERALES, S.A.", 11_042.84m),
            ("ANUL.SEGUROS REALE SEGUROS GENERALES, S.A.", 460.94m),
            ("TRANSFERENCIA OCCIDENT GCO, S.A.U. DE SEGUROS Y REASEG", 500m),
            ("CUOTAS DE LA SEGURIDAD SOCIAL", -2_771.36m),
            ("ADEUDO DE CUOTA DE LA SEGURIDAD SOCIAL", -315m),
            ("Adeudo de cuota de la seguridad social", -302.60m)
        };

        for (var i = 0; i < falsePositiveRows.Length; i++)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 1).AddDays(i),
                Concepto = falsePositiveRows[i].Concepto,
                Monto = falsePositiveRows[i].Monto,
                Saldo = 1_000m - i,
                FilaNumero = i + 1
            });
        }

        db.Extractos.Add(new Extracto
        {
            Id = seguroId,
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 4, 20),
            Concepto = "Recibo poliza MAPFRE hogar",
            Monto = -180m,
            Saldo = 700m,
            FilaNumero = falsePositiveRows.Length + 1
        });
        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));
        var result = await sut.GetSegurosAsync(AdminScope(), new RevisionQueryRequest(), CancellationToken.None);

        falsePositiveRows.Select(row => row.Concepto).Should().OnlyContain(concept => !RevisionService.IsInsuranceConcept(concept));
        result.Data.Should().ContainSingle();
        result.Data[0].ExtractoId.Should().Be(seguroId);
    }

    [Fact]
    public async Task GetComisionesAsync_Should_Page_In_Query_And_Report_Total()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "revision_comisiones_importe_minimo",
            Valor = "0",
            Tipo = "number",
            Descripcion = "Importe minimo"
        });

        for (var i = 1; i <= 25; i++)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, i),
                Concepto = $"Comision mantenimiento {i}",
                Monto = -i,
                Saldo = 100m - i,
                FilaNumero = i
            });
        }

        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));

        var result = await sut.GetComisionesAsync(
            AdminScope(),
            new RevisionQueryRequest { Page = 2, PageSize = 10 },
            CancellationToken.None);

        result.Total.Should().Be(25);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.Data.Should().HaveCount(10);
        result.Data[0].Fecha.Should().Be(new DateOnly(2026, 5, 15));
    }

    [Fact]
    public async Task GetRevisionAsync_Should_Filter_Comisiones_And_Seguros_By_PaisId()
    {
        await using var db = BuildDbContext();
        var paisAId = Guid.NewGuid();
        var paisBId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaAId = Guid.NewGuid();
        var cuentaBId = Guid.NewGuid();
        var comisionAId = Guid.NewGuid();
        var comisionBId = Guid.NewGuid();
        var seguroAId = Guid.NewGuid();
        var seguroBId = Guid.NewGuid();

        db.Paises.AddRange(
            new Pais { Id = paisAId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true },
            new Pais { Id = paisBId, Nombre = "Mexico", CodigoIso2 = "MX", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Pais", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaAId, TitularId = titularId, Nombre = "Cuenta ES", Divisa = "EUR", PaisId = paisAId, Activa = true },
            new Cuenta { Id = cuentaBId, TitularId = titularId, Nombre = "Cuenta MX", Divisa = "MXN", PaisId = paisBId, Activa = true });
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "revision_comisiones_importe_minimo",
            Valor = "0",
            Tipo = "number",
            Descripcion = "Importe minimo"
        });
        db.Extractos.AddRange(
            new Extracto { Id = comisionAId, CuentaId = cuentaAId, Fecha = new DateOnly(2026, 5, 1), Concepto = "Comision mantenimiento ES", Monto = -2m, Saldo = 98m, FilaNumero = 1 },
            new Extracto { Id = comisionBId, CuentaId = cuentaBId, Fecha = new DateOnly(2026, 5, 1), Concepto = "Comision mantenimiento MX", Monto = -20m, Saldo = 80m, FilaNumero = 1 },
            new Extracto { Id = seguroAId, CuentaId = cuentaAId, Fecha = new DateOnly(2026, 5, 2), Concepto = "Seguro MAPFRE ES", Monto = -100m, Saldo = -2m, FilaNumero = 2 },
            new Extracto { Id = seguroBId, CuentaId = cuentaBId, Fecha = new DateOnly(2026, 5, 2), Concepto = "Seguro MAPFRE MX", Monto = -200m, Saldo = -120m, FilaNumero = 2 });
        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));

        var comisiones = await sut.GetComisionesAsync(AdminScope(), new RevisionQueryRequest { PaisId = paisAId }, CancellationToken.None);
        var seguros = await sut.GetSegurosAsync(AdminScope(), new RevisionQueryRequest { PaisId = paisAId }, CancellationToken.None);

        comisiones.Data.Select(x => x.ExtractoId).Should().ContainSingle().Which.Should().Be(comisionAId);
        seguros.Data.Select(x => x.ExtractoId).Should().ContainSingle().Which.Should().Be(seguroAId);
        comisiones.Data.Select(x => x.ExtractoId).Should().NotContain(comisionBId);
        seguros.Data.Select(x => x.ExtractoId).Should().NotContain(seguroBId);
    }

    [Fact]
    public void GetComisionesAsync_Query_Should_Be_Translatable_By_Npgsql()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=atlas_balance;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new AppDbContext(options);
        var sut = new RevisionService(db, new UserAccessService(db));
        var query = InvokeRevisionBaseQuery(sut, RevisionService.TipoComision, GetSearchTerms("ComisionSearchTerms"));
        var filtered = ApplyDecimalThreshold(query, nameof(RevisionRawRowShape.Monto), 1m);

        var sql = filtered.ToQueryString();

        sql.Should().Contain("\"EXTRACTOS\"");
        sql.Should().Contain("monto");
    }

    [Fact]
    public async Task SetEstadoAsync_Should_Deny_ReadOnly_User()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedBaseAsync(db);
        var userId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();
        var titularId = await db.Cuentas.Where(x => x.Id == cuentaId).Select(x => x.TitularId).SingleAsync();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "lector@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Lector",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularId,
            PuedeVerCuentas = true,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 5, 1),
            Concepto = "Comision mantenimiento",
            Monto = -3m,
            Saldo = 97m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var sut = new RevisionService(db, new UserAccessService(db));
        var scope = new UserAccessScope
        {
            UserId = userId,
            HasPermissions = true,
            TitularIds = [titularId]
        };

        var act = () => sut.SetEstadoAsync(scope, extractoId, RevisionService.TipoComision, RevisionService.EstadoDevuelta, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static async Task<Guid> SeedBaseAsync(AppDbContext db)
    {
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular QA",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta QA",
            Divisa = "EUR",
            Activa = true
        });
        await db.SaveChangesAsync();

        return cuentaId;
    }

    private static UserAccessScope AdminScope() => new()
    {
        UserId = Guid.NewGuid(),
        IsAdmin = true,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static IReadOnlyList<string> GetSearchTerms(string fieldName)
    {
        var field = typeof(RevisionService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        var value = field!.GetValue(null);
        value.Should().BeAssignableTo<IReadOnlyList<string>>();
        return (IReadOnlyList<string>)value!;
    }

    private static IQueryable InvokeRevisionBaseQuery(RevisionService sut, string tipo, IReadOnlyList<string> terms)
    {
        var method = typeof(RevisionService).GetMethod("BuildRevisionBaseQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var value = method!.Invoke(sut, [AdminScope(), null, tipo, terms]);
        value.Should().BeAssignableTo<IQueryable>();
        return (IQueryable)value!;
    }

    private static IQueryable ApplyDecimalThreshold(IQueryable source, string propertyName, decimal threshold)
    {
        var parameter = Expression.Parameter(source.ElementType, "x");
        var property = Expression.Property(parameter, propertyName);
        var body = Expression.OrElse(
            Expression.GreaterThan(property, Expression.Constant(threshold)),
            Expression.LessThan(property, Expression.Constant(-threshold)));
        var predicate = Expression.Lambda(body, parameter);
        var whereCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            [source.ElementType],
            source.Expression,
            Expression.Quote(predicate));

        return source.Provider.CreateQuery(whereCall);
    }

    private sealed class RevisionRawRowShape
    {
        public decimal Monto { get; init; }
    }
}
