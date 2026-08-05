using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.08: matriz explicita de las combinaciones pais/titular/cuenta para
// asegurar que la jerarquia Pais > Titular > Cuenta se respeta en runtime.
// La semantica esperada es interseccion estricta: si una dimension esta
// fijada, limita; si es null, ampla.
public class UserAccessScopeMatrixTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static IUserAccessService BuildService(AppDbContext db)
    {
        return new UserAccessService(
            db,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()));
    }

    private record Fixtures(
        Pais PaisA,
        Pais PaisB,
        Titular TitularCompartido,
        Titular TitularSoloA,
        Titular TitularSoloB,
        Cuenta CuentaTitularCompartidoEnA,
        Cuenta CuentaTitularCompartidoEnB,
        Cuenta CuentaTitularSoloAEnA,
        Cuenta CuentaTitularSoloBEnB);

    private static async Task<Fixtures> SeedAsync(AppDbContext db)
    {
        var paisA = new Pais { Id = Guid.NewGuid(), Nombre = "Pais A", Activo = true };
        var paisB = new Pais { Id = Guid.NewGuid(), Nombre = "Pais B", Activo = true };
        var titularCompartido = new Titular { Id = Guid.NewGuid(), Nombre = "Compartido", Tipo = TipoTitular.EMPRESA };
        var titularSoloA = new Titular { Id = Guid.NewGuid(), Nombre = "SoloA", Tipo = TipoTitular.EMPRESA };
        var titularSoloB = new Titular { Id = Guid.NewGuid(), Nombre = "SoloB", Tipo = TipoTitular.EMPRESA };
        var cuentaCompA = new Cuenta { Id = Guid.NewGuid(), TitularId = titularCompartido.Id, PaisId = paisA.Id, Nombre = "Compartido A", Divisa = "EUR" };
        var cuentaCompB = new Cuenta { Id = Guid.NewGuid(), TitularId = titularCompartido.Id, PaisId = paisB.Id, Nombre = "Compartido B", Divisa = "EUR" };
        var cuentaSoloA = new Cuenta { Id = Guid.NewGuid(), TitularId = titularSoloA.Id, PaisId = paisA.Id, Nombre = "SoloA A", Divisa = "EUR" };
        var cuentaSoloB = new Cuenta { Id = Guid.NewGuid(), TitularId = titularSoloB.Id, PaisId = paisB.Id, Nombre = "SoloB B", Divisa = "EUR" };

        db.Paises.AddRange(paisA, paisB);
        db.Titulares.AddRange(titularCompartido, titularSoloA, titularSoloB);
        db.Cuentas.AddRange(cuentaCompA, cuentaCompB, cuentaSoloA, cuentaSoloB);
        await db.SaveChangesAsync();

        return new Fixtures(paisA, paisB, titularCompartido, titularSoloA, titularSoloB,
            cuentaCompA, cuentaCompB, cuentaSoloA, cuentaSoloB);
    }

    private static UserAccessScope ScopeFor(IReadOnlyList<PermisoUsuario> permisos)
    {
        return new UserAccessScope
        {
            UserId = permisos[0].UsuarioId,
            HasPermissions = true,
            HasGlobalAccess = permisos.Any(p =>
                p.PaisId is null && p.TitularId is null && p.CuentaId is null &&
                p.PuedeVerCuentas),
            TitularIds = permisos.Where(p => p.TitularId.HasValue).Select(p => p.TitularId!.Value).Distinct().ToList(),
            CuentaIds = permisos.Where(p => p.CuentaId.HasValue).Select(p => p.CuentaId!.Value).Distinct().ToList(),
        };
    }

    [Fact]
    public async Task PermisoGlobal_ConVerCuentas_DeberiaOcultarLasCuentasDeOtroTitular()
    {
        // Comprobacion cruzada de una cuenta que NO es del titular del permiso global.
        // El alcance global NO debe filtrar por titular, pero queremos asegurar que
        // la jerarquia Pais > Titular > Cuenta funciona al limitar.
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(), UsuarioId = userId,
            PaisId = null, TitularId = f.TitularSoloA.Id, CuentaId = null,
            PuedeVerCuentas = true,
        });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = ScopeFor(await db.PermisosUsuario.Where(p => p.UsuarioId == userId).ToListAsync());

        var visibles = await svc.ApplyCuentaScope(db.Cuentas, scope).Select(c => c.Id).ToListAsync();

        visibles.Should().Contain(f.CuentaTitularSoloAEnA.Id);
        visibles.Should().NotContain(f.CuentaTitularSoloBEnB.Id);
        visibles.Should().NotContain(f.CuentaTitularCompartidoEnA.Id);
        visibles.Should().NotContain(f.CuentaTitularCompartidoEnB.Id);
    }

    [Fact]
    public async Task PermisoPorTitular_PaisDistinto_DeberiaPermitirAccesoATitularEnTodosLosPaises()
    {
        // Titular con cuentas en dos paises. Permiso SOLO por titular (pais null)
        // debe permitir todas las cuentas del titular.
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(), UsuarioId = userId,
            PaisId = null, TitularId = f.TitularCompartido.Id, CuentaId = null,
            PuedeVerCuentas = true,
        });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = ScopeFor(await db.PermisosUsuario.Where(p => p.UsuarioId == userId).ToListAsync());

        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnA.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnB.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularSoloAEnA.Id, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task PermisoPorTitular_PaisEspecifico_DeberiaPermitirAccesoSoloEnEsePais()
    {
        // Titular con cuentas en dos paises. Permiso por titular + pais especifico
        // debe permitir solo las cuentas del titular en ESE pais.
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(), UsuarioId = userId,
            PaisId = f.PaisA.Id, TitularId = f.TitularCompartido.Id, CuentaId = null,
            PuedeVerCuentas = true,
        });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = ScopeFor(await db.PermisosUsuario.Where(p => p.UsuarioId == userId).ToListAsync());

        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnA.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnB.Id, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task PermisoPorPaisSinTitular_DeberiaPermitirAccesoATodasLasCuentasDeEsePais()
    {
        // Pais especifico + titular null => todas las cuentas de cualquier titular en ese pais.
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(), UsuarioId = userId,
            PaisId = f.PaisA.Id, TitularId = null, CuentaId = null,
            PuedeVerCuentas = true,
        });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = ScopeFor(await db.PermisosUsuario.Where(p => p.UsuarioId == userId).ToListAsync());

        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnA.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularSoloAEnA.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnB.Id, scope, CancellationToken.None)).Should().BeFalse();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularSoloBEnB.Id, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task PermisoPorCuentaEspecifica_NoConcederaAccesoAOtraCuentaDelMismoTitular()
    {
        // Cuenta explicita => solo esa cuenta, no las demas del titular.
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(), UsuarioId = userId,
            PaisId = f.PaisA.Id, TitularId = f.TitularCompartido.Id,
            CuentaId = f.CuentaTitularCompartidoEnA.Id,
            PuedeVerCuentas = true,
        });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = ScopeFor(await db.PermisosUsuario.Where(p => p.UsuarioId == userId).ToListAsync());

        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnA.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnB.Id, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task PermisoSinVerCuentas_DeberiaNoConcederAccesoATravesDeCanAccessCuenta()
    {
        // Aunque el resto de flags esten activos, sin PuedeVerCuentas el acceso de
        // lectura no se concede. Pero el resto de checks (escritura/conciliacion)
        // siguen su propia logica.
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(), UsuarioId = userId,
            PaisId = null, TitularId = f.TitularCompartido.Id, CuentaId = null,
            PuedeVerCuentas = false,
            PuedeEditarLineas = true,
            PuedeImportar = true,
        });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = ScopeFor(await db.PermisosUsuario.Where(p => p.UsuarioId == userId).ToListAsync());

        (await svc.CanAccessCuentaAsync(f.CuentaTitularCompartidoEnA.Id, scope, CancellationToken.None)).Should().BeFalse();
        (await svc.CanEditCuentaAsync(f.CuentaTitularCompartidoEnA.Id, scope, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task GetScopeAsync_DeberiaDevolverTitularIdsYPaisIdsDistintosDeLosNull()
    {
        await using var db = BuildDbContext();
        var f = await SeedAsync(db);
        var userId = Guid.NewGuid();
        db.PermisosUsuario.AddRange(
            new PermisoUsuario
            {
                Id = Guid.NewGuid(), UsuarioId = userId,
                PaisId = null, TitularId = f.TitularCompartido.Id, CuentaId = null,
                PuedeVerCuentas = true,
            },
            new PermisoUsuario
            {
                Id = Guid.NewGuid(), UsuarioId = userId,
                PaisId = f.PaisA.Id, TitularId = null, CuentaId = null,
                PuedeVerCuentas = true,
            });
        await db.SaveChangesAsync();
        var svc = BuildService(db);
        var scope = await svc.GetScopeAsync(
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, nameof(RolUsuario.GERENTE))
            ], "TestAuth")),
            CancellationToken.None);

        scope.HasGlobalAccess.Should().BeFalse();
        scope.TitularIds.Should().BeEquivalentTo(new[] { f.TitularCompartido.Id });
        scope.PaisIds.Should().BeEquivalentTo(new[] { f.PaisA.Id });
    }
}
