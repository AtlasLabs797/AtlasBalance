using FluentAssertions;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCaja.API.Tests;

public class DashboardServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static DashboardService BuildService(AppDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tiposCambioService = new TiposCambioService(
            db,
            cache,
            new StaticHttpClientFactory(),
            NullLogger<TiposCambioService>.Instance,
            new PlainTextSecretProtector());
        return new DashboardService(db, tiposCambioService);
    }

    private static void SeedDashboardConfig(AppDbContext db, Guid adminId)
    {
        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false });

        db.Configuraciones.AddRange(
            new Configuracion { Clave = "divisa_principal_default", Valor = "EUR", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_ingresos", Valor = "#43B430", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_egresos", Valor = "#FF4757", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_saldo", Valor = "#7B7B7B", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId });

        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.20m,
            FechaActualizacion = DateTime.UtcNow,
            Fuente = FuenteTipoCambio.MANUAL
        });
    }

    [Fact]
    public async Task GetPrincipalAsync_Should_Aggregate_CurrentBalances_And_PeriodFlows_In_TargetCurrency()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var monthStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var periodStart = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(-1);

        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.dashboard@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Dashboard Admin",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });

        SeedDashboardConfig(db, adminId);

        var titularEurId = Guid.NewGuid();
        var titularUsdId = Guid.NewGuid();
        var cuentaEurId = Guid.NewGuid();
        var cuentaUsdId = Guid.NewGuid();

        db.Titulares.AddRange(
            new Titular { Id = titularEurId, Nombre = "Titular EUR", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularUsdId, Nombre = "Titular USD", Tipo = TipoTitular.EMPRESA });

        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaEurId, TitularId = titularEurId, Nombre = "Cuenta EUR", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaUsdId, TitularId = titularUsdId, Nombre = "Cuenta USD", Divisa = "USD", Activa = true });

        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaEurId,
                Fecha = periodStart.AddDays(-1),
                Monto = 999m,
                Saldo = 999m,
                FilaNumero = 4
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaEurId,
                Fecha = periodStart,
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 3
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaEurId,
                Fecha = monthStart,
                Monto = 100m,
                Saldo = 100m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaEurId,
                Fecha = monthStart.AddDays(1),
                Monto = -40m,
                Saldo = 60m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaUsdId,
                Fecha = monthStart.AddDays(2),
                Monto = 120m,
                Saldo = 120m,
                FilaNumero = 1
            });

        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(adminId, "USD", CancellationToken.None);

        result.DivisaPrincipal.Should().Be("USD");
        result.SaldosPorDivisa.Should().ContainKey("EUR").WhoseValue.Should().Be(60m);
        result.SaldosPorDivisa.Should().ContainKey("USD").WhoseValue.Should().Be(120m);
        result.TotalConvertido.Should().Be(192m);
        result.IngresosMes.Should().Be(252m);
        result.EgresosMes.Should().Be(48m);
        result.SaldosPorTitular.Should().HaveCount(2);
        result.SaldosPorTitular[0].TitularNombre.Should().Be("Titular USD");
        result.SaldosPorTitular[0].TotalConvertido.Should().Be(120m);
        result.SaldosPorTitular[1].TitularNombre.Should().Be("Titular EUR");
        result.SaldosPorTitular[1].TotalConvertido.Should().Be(72m);
        result.ChartColors.Ingresos.Should().Be("#43B430");

        var saldosTitular = await sut.GetSaldosDivisaAsync(adminId, "USD", titularEurId, CancellationToken.None);
        saldosTitular.Divisas.Should().ContainSingle();
        saldosTitular.Divisas[0].Divisa.Should().Be("EUR");
        saldosTitular.Divisas[0].Saldo.Should().Be(60m);
        saldosTitular.Divisas[0].SaldoConvertido.Should().Be(72m);
    }

    [Fact]
    public async Task GetTitularAsync_Should_Reject_Manager_Without_Access_To_Requested_Titular()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        db.Usuarios.AddRange(
            new Usuario
            {
                Id = adminId,
                Email = "admin.scope@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Admin Scope",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PrimerLogin = false
            },
            new Usuario
            {
                Id = managerId,
                Email = "manager.scope@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Manager Scope",
                Rol = RolUsuario.GERENTE,
                Activo = true,
                PrimerLogin = false
            });

        SeedDashboardConfig(db, adminId);

        var allowedTitularId = Guid.NewGuid();
        var blockedTitularId = Guid.NewGuid();
        var allowedCuentaId = Guid.NewGuid();
        var blockedCuentaId = Guid.NewGuid();

        db.Titulares.AddRange(
            new Titular { Id = allowedTitularId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = blockedTitularId, Nombre = "Titular Bloqueado", Tipo = TipoTitular.EMPRESA });

        db.Cuentas.AddRange(
            new Cuenta { Id = allowedCuentaId, TitularId = allowedTitularId, Nombre = "Cuenta Permitida", Divisa = "EUR", Activa = true },
            new Cuenta { Id = blockedCuentaId, TitularId = blockedTitularId, Nombre = "Cuenta Bloqueada", Divisa = "EUR", Activa = true });

        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = managerId,
            TitularId = allowedTitularId,
            PuedeVerDashboard = true
        });

        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = blockedCuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Monto = 50m,
            Saldo = 50m,
            FilaNumero = 1
        });

        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var act = async () => await sut.GetTitularAsync(managerId, blockedTitularId, "EUR", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardAccessException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        exception.Which.Message.Should().Contain("No tienes permisos");
    }

    [Fact]
    public async Task GetPrincipalAsync_Should_Prioritize_Active_Base_Currency_Over_Stale_Config_Default()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.base@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Base",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });

        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = true });

        db.Configuraciones.AddRange(
            new Configuracion { Clave = "divisa_principal_default", Valor = "EUR", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_ingresos", Valor = "#43B430", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_egresos", Valor = "#FF4757", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_saldo", Valor = "#7B7B7B", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId });

        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.20m,
            FechaActualizacion = DateTime.UtcNow,
            Fuente = FuenteTipoCambio.MANUAL
        });

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Base", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Base", Divisa = "EUR", Activa = true });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1
        });

        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(adminId, null, CancellationToken.None);

        result.DivisaPrincipal.Should().Be("USD");
        result.TotalConvertido.Should().Be(120m);
    }

    [Fact]
    public async Task GetSaldosDivisaAsync_Should_Separate_Disponible_And_Inmovilizado()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var normalId = Guid.NewGuid();
        var plazoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.inmovilizado@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Inmovilizado",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        SeedDashboardConfig(db, adminId);
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Mixto", Tipo = TipoTitular.AUTONOMO });
        db.Cuentas.AddRange(
            new Cuenta { Id = normalId, TitularId = titularId, Nombre = "Operativa", Divisa = "EUR", TipoCuenta = TipoCuenta.NORMAL, Activa = true },
            new Cuenta { Id = plazoId, TitularId = titularId, Nombre = "Plazo", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = Guid.NewGuid(),
            CuentaId = plazoId,
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            FechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(30),
            InteresPrevisto = 12.5m,
            Estado = EstadoPlazoFijo.ACTIVO
        });
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = normalId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow), Monto = 80m, Saldo = 80m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = plazoId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow), Monto = 200m, Saldo = 200m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var divisas = await sut.GetSaldosDivisaAsync(adminId, "EUR", null, CancellationToken.None);
        var principal = await sut.GetPrincipalAsync(adminId, "EUR", CancellationToken.None);

        divisas.Divisas.Should().ContainSingle();
        divisas.Divisas[0].SaldoDisponible.Should().Be(80m);
        divisas.Divisas[0].SaldoInmovilizado.Should().Be(200m);
        divisas.Divisas[0].SaldoTotal.Should().Be(280m);
        principal.SaldosPorTitular.Should().ContainSingle();
        principal.SaldosPorTitular[0].TipoTitular.Should().Be(nameof(TipoTitular.AUTONOMO));
        principal.SaldosPorTitular[0].SaldoDisponibleConvertido.Should().Be(80m);
        principal.SaldosPorTitular[0].SaldoInmovilizadoConvertido.Should().Be(200m);
        principal.PlazosFijos.MontoTotalConvertido.Should().Be(200m);
        principal.PlazosFijos.InteresesPrevistosConvertidos.Should().Be(12.5m);
        principal.PlazosFijos.DiasHastaProximoVencimiento.Should().Be(30);
        principal.PlazosFijos.TotalCuentas.Should().Be(1);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client = new()
        {
            BaseAddress = new Uri("https://example.invalid/")
        };

        public HttpClient CreateClient(string name) => _client;
    }
}
