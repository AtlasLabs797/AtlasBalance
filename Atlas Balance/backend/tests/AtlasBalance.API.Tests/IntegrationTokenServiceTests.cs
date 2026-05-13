using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Xunit;

namespace AtlasBalance.API.Tests;

public class IntegrationTokenServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ValidateActiveTokenAsync_Should_Return_Active_Token()
    {
        await using var db = BuildDbContext();
        var service = new IntegrationTokenService(db);
        var plain = service.GeneratePlainToken();

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token-openclaw",
            TokenHash = service.ComputeSha256(plain),
            Estado = EstadoTokenIntegracion.Activo,
            PermisoLectura = true,
            UsuarioCreadorId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var token = await service.ValidateActiveTokenAsync(plain, CancellationToken.None);
        token.Should().NotBeNull();
        token!.Nombre.Should().Be("token-openclaw");
    }

    [Fact]
    public async Task ValidateActiveTokenAsync_Should_Return_Null_For_Revoked_Token()
    {
        await using var db = BuildDbContext();
        var service = new IntegrationTokenService(db);
        var plain = service.GeneratePlainToken();

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token-revocado",
            TokenHash = service.ComputeSha256(plain),
            Estado = EstadoTokenIntegracion.Revocado,
            PermisoLectura = true,
            UsuarioCreadorId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var token = await service.ValidateActiveTokenAsync(plain, CancellationToken.None);
        token.Should().BeNull();
    }

    [Fact]
    public async Task ValidateActiveTokenAsync_Should_Return_WriteOnly_Token_When_Active()
    {
        await using var db = BuildDbContext();
        var service = new IntegrationTokenService(db);
        var plain = service.GeneratePlainToken();

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token-escritura",
            TokenHash = service.ComputeSha256(plain),
            Estado = EstadoTokenIntegracion.Activo,
            PermisoLectura = false,
            PermisoEscritura = true,
            UsuarioCreadorId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var token = await service.ValidateActiveTokenAsync(plain, CancellationToken.None);
        token.Should().NotBeNull();
        token!.PermisoLectura.Should().BeFalse();
        token.PermisoEscritura.Should().BeTrue();
    }

    [Fact]
    public void GeneratePlainToken_Should_Use_Base64Url_Format()
    {
        using var db = BuildDbContext();
        var service = new IntegrationTokenService(db);

        var token = service.GeneratePlainToken();

        token.Should().StartWith("sk_atlas_balance_");
        Regex.IsMatch(token, "^sk_atlas_balance_[A-Za-z0-9_-]{32}$").Should().BeTrue();
    }

}
