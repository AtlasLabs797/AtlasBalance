using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-03.02 (S-1): el dashboard solo concede lectura de saldos y cuentas a quien
// tiene PuedeVerCuentas. Los permisos de escritura (agregar, editar, eliminar o
// importar) no dan acceso al dashboard y el rol GERENTE tambien exige
// PuedeVerDashboard, igual que el resto de roles no ADMIN.
public class DashboardServicePermisosLecturaTests
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
        var cacheService = new CacheService(cache, NullLogger<CacheService>.Instance);
        var cachingOptions = Options.Create(new CachingOptions());
        var tiposCambioService = new TiposCambioService(
            db,
            cacheService,
            new StaticHttpClientFactory(),
            NullLogger<TiposCambioService>.Instance,
            new PlainTextSecretProtector(),
            cachingOptions);
        return new DashboardService(db, cacheService, tiposCambioService, cachingOptions);
    }

    private static Guid SeedUsuario(AppDbContext db, RolUsuario rol, string email)
    {
        var id = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = id,
            Email = email,
            PasswordHash = "hash",
            NombreCompleto = email,
            Rol = rol,
            Activo = true,
            PrimerLogin = false
        });
        return id;
    }

    private static void SeedConfig(AppDbContext db, Guid adminId)
    {
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "divisa_principal_default",
            Valor = "EUR",
            FechaModificacion = DateTime.UtcNow,
            UsuarioModificacionId = adminId
        });
    }

    private static (Guid TitularId, Guid CuentaId) SeedCuentaConSaldo(
        AppDbContext db,
        string nombreTitular,
        string nombreCuenta,
        Guid? paisId = null,
        decimal saldo = 500m)
    {
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = nombreTitular, Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            PaisId = paisId,
            Nombre = nombreCuenta,
            Divisa = "EUR",
            Activa = true
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Monto = saldo,
            Saldo = saldo,
            FilaNumero = 1
        });
        return (titularId, cuentaId);
    }

    [Fact]
    public async Task Empleado_ConSoloPuedeImportar_NoDebeVerSaldosNiCuentas()
    {
        // ROL: EMPLEADO. PERMISO: solo PuedeImportar (PuedeVerCuentas=false).
        // OBTENIDO: 403, el dashboard no devuelve ni saldos ni cuentas.
        await using var db = BuildDbContext();
        var adminId = SeedUsuario(db, RolUsuario.ADMIN, "admin.s1.importar@test.local");
        var empleadoId = SeedUsuario(db, RolUsuario.EMPLEADO, "empleado.s1.importar@test.local");
        SeedConfig(db, adminId);
        var (titularId, _) = SeedCuentaConSaldo(db, "Titular Solo Importa", "Cuenta Solo Importa");

        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = empleadoId,
            PuedeImportar = true,
            PuedeVerCuentas = false,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db);
        var act = async () => await sut.GetPrincipalAsync(empleadoId, "EUR", null, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardAccessException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Empleado_ConPuedeVerCuentasYPuedeVerDashboard_DebeVerSaldos()
    {
        // ROL: EMPLEADO. PERMISO: PuedeVerCuentas + PuedeVerDashboard.
        // OBTENIDO: saldos de la cuenta permitida.
        await using var db = BuildDbContext();
        var adminId = SeedUsuario(db, RolUsuario.ADMIN, "admin.s1.empleado@test.local");
        var empleadoId = SeedUsuario(db, RolUsuario.EMPLEADO, "empleado.s1.lectura@test.local");
        SeedConfig(db, adminId);
        var (titularId, cuentaId) = SeedCuentaConSaldo(db, "Titular Empleado", "Cuenta Empleado", saldo: 75m);

        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = empleadoId,
            PuedeVerCuentas = true,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(empleadoId, "EUR", null, CancellationToken.None);

        result.TotalConvertido.Should().Be(75m);
        result.SaldosPorTitular.Should().ContainSingle(x => x.TitularId == titularId);
        result.SaldosPorCuenta.Should().ContainSingle(x => x.CuentaId == cuentaId);
    }

    [Fact]
    public async Task Gerente_ConPuedeVerCuentasYSinPuedeVerDashboard_NoDebeVerSaldos()
    {
        // ROL: GERENTE. PERMISO: PuedeVerCuentas=true, PuedeVerDashboard=false.
        // OBTENIDO: 403 (antes el flag PuedeVerDashboard se ignoraba para GERENTE).
        await using var db = BuildDbContext();
        var adminId = SeedUsuario(db, RolUsuario.ADMIN, "admin.s1.gerente@test.local");
        var gerenteId = SeedUsuario(db, RolUsuario.GERENTE, "gerente.s1.sin-dashboard@test.local");
        SeedConfig(db, adminId);
        SeedCuentaConSaldo(db, "Titular Gerente", "Cuenta Gerente");

        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = gerenteId,
            PuedeVerCuentas = true,
            PuedeVerDashboard = false
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db);
        var act = async () => await sut.GetPrincipalAsync(gerenteId, "EUR", null, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardAccessException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Gerente_ConPuedeVerCuentasYPuedeVerDashboard_DebeVerSaldos()
    {
        // ROL: GERENTE. PERMISO: PuedeVerCuentas + PuedeVerDashboard.
        // OBTENIDO: saldos de la cuenta permitida.
        await using var db = BuildDbContext();
        var adminId = SeedUsuario(db, RolUsuario.ADMIN, "admin.s1.gerente-ok@test.local");
        var gerenteId = SeedUsuario(db, RolUsuario.GERENTE, "gerente.s1.con-dashboard@test.local");
        SeedConfig(db, adminId);
        var (titularId, cuentaId) = SeedCuentaConSaldo(db, "Titular Gerente Ok", "Cuenta Gerente Ok", saldo: 120m);

        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = gerenteId,
            PuedeVerCuentas = true,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(gerenteId, "EUR", null, CancellationToken.None);

        result.TotalConvertido.Should().Be(120m);
        result.SaldosPorTitular.Should().ContainSingle(x => x.TitularId == titularId);
        result.SaldosPorCuenta.Should().ContainSingle(x => x.CuentaId == cuentaId);
    }

    [Fact]
    public async Task Admin_SinPermisos_DebeVerSaldos()
    {
        // ROL: ADMIN. PERMISO: ninguno. OBTENIDO: bypass, saldos de todas las cuentas.
        await using var db = BuildDbContext();
        var adminId = SeedUsuario(db, RolUsuario.ADMIN, "admin.s1.bypass@test.local");
        SeedConfig(db, adminId);
        var (titularId, cuentaId) = SeedCuentaConSaldo(db, "Titular Admin", "Cuenta Admin", saldo: 60m);

        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(adminId, "EUR", null, CancellationToken.None);

        result.TotalConvertido.Should().Be(60m);
        result.SaldosPorTitular.Should().ContainSingle(x => x.TitularId == titularId);
        result.SaldosPorCuenta.Should().ContainSingle(x => x.CuentaId == cuentaId);
    }

    [Fact]
    public async Task PermisoConPaisYTitularYCuentaConcretos_NoDebeDarAccesoAOtrasCuentas()
    {
        // ROL: EMPLEADO. PERMISO: pais_id + titular_id + cuenta_id concretos.
        // OBTENIDO: solo ve la cuenta indicada; ni la otra cuenta del mismo titular
        // ni la cuenta de otro titular del mismo pais.
        await using var db = BuildDbContext();
        var adminId = SeedUsuario(db, RolUsuario.ADMIN, "admin.s1.alcance@test.local");
        var empleadoId = SeedUsuario(db, RolUsuario.EMPLEADO, "empleado.s1.alcance@test.local");
        SeedConfig(db, adminId);

        var paisId = Guid.NewGuid();
        db.Paises.Add(new Pais { Id = paisId, Nombre = "Pais Alcance", CodigoIso2 = "ES", Activo = true });

        var (titularPermitidoId, cuentaPermitidaId) = SeedCuentaConSaldo(db, "Titular Permitido", "Cuenta Permitida", paisId, 100m);
        var (_, cuentaDelMismoTitular) = SeedCuentaConSaldo(db, "Titular Permitido", "Cuenta Hermana", paisId, 900m);
        var (titularBloqueadoId, cuentaBloqueada) = SeedCuentaConSaldo(db, "Titular Bloqueado", "Cuenta Bloqueada", paisId, 900m);

        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = empleadoId,
            PaisId = paisId,
            TitularId = titularPermitidoId,
            CuentaId = cuentaPermitidaId,
            PuedeVerCuentas = true,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(empleadoId, "EUR", null, CancellationToken.None);

        result.TotalConvertido.Should().Be(100m);
        result.SaldosPorCuenta.Should().ContainSingle(x => x.CuentaId == cuentaPermitidaId);
        result.SaldosPorCuenta.Should().NotContain(x => x.CuentaId == cuentaDelMismoTitular);
        result.SaldosPorCuenta.Should().NotContain(x => x.CuentaId == cuentaBloqueada);

        var act = async () => await sut.GetTitularAsync(empleadoId, titularBloqueadoId, "EUR", null, CancellationToken.None);
        var exception = await act.Should().ThrowAsync<DashboardAccessException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
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
