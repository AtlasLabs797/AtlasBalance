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
    public async Task ValidateActiveTokenAsync_Should_Return_Null_For_Expired_Token()
    {
        await using var db = BuildDbContext();
        var clock = new FakeClock(new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc));
        var service = new IntegrationTokenService(db, clock);
        var plain = service.GeneratePlainToken();

        db.IntegrationTokens.Add(new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token-expirado",
            TokenHash = service.ComputeSha256(plain),
            Estado = EstadoTokenIntegracion.Activo,
            PermisoLectura = true,
            UsuarioCreadorId = Guid.NewGuid(),
            FechaCreacion = clock.UtcNow.AddDays(-100),
            FechaExpiracion = clock.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var token = await service.ValidateActiveTokenAsync(plain, CancellationToken.None);
        token.Should().BeNull();
    }

    [Fact]
    public void ResolveExpiration_Should_Default_To_Ninety_Days_Unless_NoExpiration_Is_Confirmed()
    {
        using var db = BuildDbContext();
        var now = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        var service = new IntegrationTokenService(db, new FakeClock(now));

        service.ResolveExpiration(null, noExpirationConfirmed: false).Should().Be(now.AddDays(90));
    }

    [Fact]
    public void ResolveExpiration_Should_Return_Null_When_NoExpiration_Confirmed_With_Magic_Phrase()
    {
        // C-NEW-2 (V-02-03): un token sin expiracion exige el texto magico
        // "NO_EXPIRAR" para evitar que un checkbox olvidado cree tokens eternos.
        using var db = BuildDbContext();
        var now = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        var service = new IntegrationTokenService(db, new FakeClock(now));

        service.ResolveExpiration(null, noExpirationConfirmed: true, noExpirationConfirmationText: "NO_EXPIRAR").Should().BeNull();
    }

    [Fact]
    public void ResolveExpiration_Should_Throw_When_NoExpiration_Confirmed_Without_Magic_Phrase()
    {
        using var db = BuildDbContext();
        var service = new IntegrationTokenService(db);

        Action actNoText = () => service.ResolveExpiration(null, noExpirationConfirmed: true, noExpirationConfirmationText: null);
        Action actEmpty = () => service.ResolveExpiration(null, noExpirationConfirmed: true, noExpirationConfirmationText: "");
        Action actWrongCase = () => service.ResolveExpiration(null, noExpirationConfirmed: true, noExpirationConfirmationText: "no_expirar");
        Action actWrongText = () => service.ResolveExpiration(null, noExpirationConfirmed: true, noExpirationConfirmationText: "TOTALLY DIFFERENT");

        actNoText.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWrongCase.Should().Throw<ArgumentException>();
        actWrongText.Should().Throw<ArgumentException>();
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

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
