using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class IntegrationAuthorizationServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetScopeAsync_Should_Set_Global_Access_When_Permission_Is_Null_Null()
    {
        await using var db = BuildDbContext();
        var tokenId = Guid.NewGuid();
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            TitularId = null,
            CuentaId = null,
            AccesoTipo = "lectura",
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = new IntegrationAuthorizationService(db);
        var scope = await sut.GetScopeAsync(tokenId, CancellationToken.None);

        scope.HasPermissions.Should().BeTrue();
        scope.HasGlobalAccess.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyCuentaScope_Should_Filter_By_Cuenta_And_Titular()
    {
        await using var db = BuildDbContext();
        var titularAllowed = new Titular { Id = Guid.NewGuid(), Nombre = "Titular OK", Tipo = TipoTitular.EMPRESA };
        var titularDenied = new Titular { Id = Guid.NewGuid(), Nombre = "Titular NO", Tipo = TipoTitular.EMPRESA };
        var cuentaAllowed = new Cuenta { Id = Guid.NewGuid(), Nombre = "Cuenta OK", Divisa = "EUR", TitularId = titularAllowed.Id };
        var cuentaByTitular = new Cuenta { Id = Guid.NewGuid(), Nombre = "Cuenta por titular", Divisa = "EUR", TitularId = titularAllowed.Id };
        var cuentaDenied = new Cuenta { Id = Guid.NewGuid(), Nombre = "Cuenta NO", Divisa = "EUR", TitularId = titularDenied.Id };
        var tokenId = Guid.NewGuid();
        db.Titulares.AddRange(titularAllowed, titularDenied);
        db.Cuentas.AddRange(cuentaAllowed, cuentaByTitular, cuentaDenied);
        db.IntegrationPermissions.AddRange(
            new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                CuentaId = cuentaAllowed.Id,
                AccesoTipo = "lectura",
                FechaCreacion = DateTime.UtcNow
            },
            new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                TitularId = titularAllowed.Id,
                AccesoTipo = "lectura",
                FechaCreacion = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var scope = new IntegrationAccessScope
        {
            TokenId = tokenId,
            HasPermissions = true,
            HasGlobalAccess = false,
            CuentaIds = [cuentaAllowed.Id],
            TitularIds = [titularAllowed.Id]
        };

        var sut = new IntegrationAuthorizationService(db);
        var result = await sut.ApplyCuentaScope(db.Cuentas.AsQueryable(), scope).ToListAsync();

        result.Select(x => x.Id).Should().Contain(cuentaAllowed.Id);
        result.Select(x => x.Id).Should().Contain(cuentaByTitular.Id);
        result.Select(x => x.Id).Should().NotContain(cuentaDenied.Id);
    }

    [Fact]
    public async Task ApplyCuentaScope_Should_Keep_Country_And_Titular_In_Same_Permission_Row()
    {
        await using var db = BuildDbContext();
        var tokenId = Guid.NewGuid();
        var paisPermitidoId = Guid.NewGuid();
        var paisBloqueadoId = Guid.NewGuid();
        var titularPermitido = new Titular { Id = Guid.NewGuid(), Nombre = "Titular OK", Tipo = TipoTitular.EMPRESA };
        var titularBloqueado = new Titular { Id = Guid.NewGuid(), Nombre = "Titular NO", Tipo = TipoTitular.EMPRESA };
        var cuentaPermitida = new Cuenta { Id = Guid.NewGuid(), Nombre = "Cuenta OK", Divisa = "EUR", TitularId = titularPermitido.Id, PaisId = paisPermitidoId };
        var mismaPaisOtroTitular = new Cuenta { Id = Guid.NewGuid(), Nombre = "Pais OK Titular NO", Divisa = "EUR", TitularId = titularBloqueado.Id, PaisId = paisPermitidoId };
        var mismoTitularOtroPais = new Cuenta { Id = Guid.NewGuid(), Nombre = "Titular OK Pais NO", Divisa = "EUR", TitularId = titularPermitido.Id, PaisId = paisBloqueadoId };

        db.Titulares.AddRange(titularPermitido, titularBloqueado);
        db.Cuentas.AddRange(cuentaPermitida, mismaPaisOtroTitular, mismoTitularOtroPais);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            PaisId = paisPermitidoId,
            TitularId = titularPermitido.Id,
            AccesoTipo = "lectura",
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = new IntegrationAuthorizationService(db);
        var scope = await sut.GetScopeAsync(tokenId, CancellationToken.None, "lectura");

        var result = await sut.ApplyCuentaScope(db.Cuentas.AsQueryable(), scope).ToListAsync();

        result.Select(x => x.Id).Should().ContainSingle().Which.Should().Be(cuentaPermitida.Id);
    }

    [Fact]
    public async Task ApplyCuentaScope_Should_Hide_Accounts_When_Titular_Is_SoftDeleted()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular eliminado",
            Tipo = TipoTitular.EMPRESA,
            DeletedAt = DateTime.UtcNow
        });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, Nombre = "Cuenta filtrada", Divisa = "EUR", TitularId = titularId });
        await db.SaveChangesAsync();

        var scope = new IntegrationAccessScope
        {
            TokenId = Guid.NewGuid(),
            HasPermissions = true,
            HasGlobalAccess = true
        };

        var sut = new IntegrationAuthorizationService(db);
        var result = await sut.ApplyCuentaScope(db.Cuentas.IgnoreQueryFilters(), scope).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyTitularScope_Should_Not_Use_SoftDeleted_Account_Permission_As_Visibility_Bridge()
    {
        await using var db = BuildDbContext();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular activo",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            Nombre = "Cuenta eliminada",
            Divisa = "EUR",
            TitularId = titularId,
            DeletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var scope = new IntegrationAccessScope
        {
            TokenId = Guid.NewGuid(),
            HasPermissions = true,
            HasGlobalAccess = false,
            CuentaIds = [cuentaId]
        };

        var sut = new IntegrationAuthorizationService(db);
        var result = await sut.ApplyTitularScope(db.Titulares.IgnoreQueryFilters(), scope).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetScopeAsync_Should_Filter_By_Access_Type()
    {
        await using var db = BuildDbContext();
        var tokenId = Guid.NewGuid();
        var readCuentaId = Guid.NewGuid();
        var writeCuentaId = Guid.NewGuid();

        db.IntegrationPermissions.AddRange(
            new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                CuentaId = readCuentaId,
                AccesoTipo = "lectura",
                FechaCreacion = DateTime.UtcNow
            },
            new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                CuentaId = writeCuentaId,
                AccesoTipo = "escritura",
                FechaCreacion = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var sut = new IntegrationAuthorizationService(db);

        var readScope = await sut.GetScopeAsync(tokenId, CancellationToken.None, "lectura");
        var writeScope = await sut.GetScopeAsync(tokenId, CancellationToken.None, "escritura");

        readScope.CuentaIds.Should().ContainSingle().Which.Should().Be(readCuentaId);
        writeScope.CuentaIds.Should().ContainSingle().Which.Should().Be(writeCuentaId);
    }
}
