using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class UserAccessServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task TitularScopedViewPermission_Should_Not_Grant_GlobalAccountAccess()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularPermitido = new Titular { Id = Guid.NewGuid(), Nombre = "Permitido", Tipo = TipoTitular.EMPRESA };
        var titularBloqueado = new Titular { Id = Guid.NewGuid(), Nombre = "Bloqueado", Tipo = TipoTitular.EMPRESA };
        var cuentaPermitida = new Cuenta { Id = Guid.NewGuid(), TitularId = titularPermitido.Id, Nombre = "Cuenta OK", Divisa = "EUR" };
        var cuentaBloqueada = new Cuenta { Id = Guid.NewGuid(), TitularId = titularBloqueado.Id, Nombre = "Cuenta NO", Divisa = "EUR" };

        db.Titulares.AddRange(titularPermitido, titularBloqueado);
        db.Cuentas.AddRange(cuentaPermitida, cuentaBloqueada);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularPermitido.Id,
            CuentaId = null,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var scope = new UserAccessScope
        {
            UserId = userId,
            HasPermissions = true,
            HasGlobalAccess = false,
            TitularIds = [titularPermitido.Id],
            CuentaIds = []
        };

        var service = new UserAccessService(db);

        (await service.CanAccessCuentaAsync(cuentaPermitida.Id, scope, CancellationToken.None)).Should().BeTrue();
        (await service.CanAccessCuentaAsync(cuentaBloqueada.Id, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task GetScopeAsync_Should_Not_Grant_Read_Access_For_ImportOnly_Permission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            TitularId = titularId,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
        ], "TestAuth");

        var service = new UserAccessService(db);
        var scope = await service.GetScopeAsync(new ClaimsPrincipal(identity), CancellationToken.None);

        scope.HasPermissions.Should().BeTrue();
        scope.CuentaIds.Should().BeEmpty();
        scope.TitularIds.Should().BeEmpty();
        scope.HasGlobalAccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetScopeAsync_Should_Not_Grant_Global_Access_When_All_Flags_Are_False()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = null,
            TitularId = null,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false,
            PuedeVerDashboard = false
        });
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.EMPLEADO))
        ], "TestAuth");

        var principal = new ClaimsPrincipal(identity);
        var service = new UserAccessService(db);
        var scope = await service.GetScopeAsync(principal, CancellationToken.None);

        scope.HasPermissions.Should().BeTrue();
        scope.HasGlobalAccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetScopeAsync_Should_Not_Grant_Global_Data_Access_For_DashboardOnly_GlobalPermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = null,
            TitularId = null,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
        ], "TestAuth");

        var principal = new ClaimsPrincipal(identity);
        var service = new UserAccessService(db);
        var scope = await service.GetScopeAsync(principal, CancellationToken.None);

        scope.HasPermissions.Should().BeTrue();
        scope.HasGlobalAccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetScopeAsync_Should_Not_Grant_Scoped_Data_Access_For_DashboardOnly_Permission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            TitularId = titularId,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false,
            PuedeVerDashboard = true
        });
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
        ], "TestAuth");

        var principal = new ClaimsPrincipal(identity);
        var service = new UserAccessService(db);
        var scope = await service.GetScopeAsync(principal, CancellationToken.None);

        scope.HasPermissions.Should().BeTrue();
        scope.CuentaIds.Should().BeEmpty();
        scope.TitularIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetScopeAsync_Should_Grant_Global_Access_For_ViewAccounts_GlobalPermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = null,
            TitularId = null,
            PuedeVerCuentas = true,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false,
            PuedeVerDashboard = false
        });
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
        ], "TestAuth");

        var principal = new ClaimsPrincipal(identity);
        var service = new UserAccessService(db);
        var scope = await service.GetScopeAsync(principal, CancellationToken.None);

        scope.HasPermissions.Should().BeTrue();
        scope.HasGlobalAccess.Should().BeTrue();
    }
}
