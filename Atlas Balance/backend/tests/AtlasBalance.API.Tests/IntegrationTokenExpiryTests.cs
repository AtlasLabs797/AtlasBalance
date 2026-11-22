using System.Security.Claims;
using System.Text.Json;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// S-23: un PUT sin FechaExpiracion conserva la caducidad existente, la
/// vigencia esta topada a 365 dias, no se admiten fechas pasadas y rotar un
/// token revocado (o caducado) no emite ningun token nuevo. Al rotar, los scopes
/// y la caducidad se heredan tal cual.
/// </summary>
public sealed class IntegrationTokenExpiryTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IntegracionesController BuildController(AppDbContext dbContext)
    {
        var controller = new IntegracionesController(
            dbContext,
            TestAuditService.Create(dbContext),
            new IntegrationTokenService(
                dbContext,
                new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
                Options.Create(new CachingOptions())),
            new IntegrationRateLimitCleaner(
                new MemoryCache(new MemoryCacheOptions()),
                new SystemClock()));

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "ADMIN")
        ], "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private static SaveIntegrationTokenRequest SaveRequest(DateTime? fechaExpiracion)
    {
        return new SaveIntegrationTokenRequest
        {
            Nombre = "token-editado",
            PermisoLectura = true,
            PermisoEscritura = false,
            FechaExpiracion = fechaExpiracion,
            Permisos = [new SaveIntegrationPermissionRequest { AccesoTipo = "lectura" }]
        };
    }

    private static async Task<IntegrationToken> SeedTokenAsync(
        AppDbContext dbContext,
        string estado = "activo",
        string scopesJson = "[\"saldos\"]",
        DateTime? fechaExpiracion = null)
    {
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token-base",
            TokenHash = Guid.NewGuid().ToString("N"),
            Estado = estado == "revocado" ? EstadoTokenIntegracion.Revocado : EstadoTokenIntegracion.Activo,
            PermisoLectura = true,
            EndpointScopesJson = scopesJson,
            FechaCreacion = DateTime.UtcNow,
            FechaExpiracion = fechaExpiracion,
            UsuarioCreadorId = Guid.NewGuid()
        };

        dbContext.IntegrationTokens.Add(token);
        await dbContext.SaveChangesAsync();
        return token;
    }

    [Fact]
    public async Task Actualizar_Sin_FechaExpiracion_Debe_Conservar_La_Caducidad_Existente()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        var caducidadOriginal = new DateTime(2027, 3, 15, 8, 0, 0, DateTimeKind.Utc);
        var token = await SeedTokenAsync(dbContext, fechaExpiracion: caducidadOriginal);

        var result = await controller.Actualizar(token.Id, SaveRequest(fechaExpiracion: null), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var guardado = await dbContext.IntegrationTokens.SingleAsync(x => x.Id == token.Id);
        guardado.FechaExpiracion.Should().Be(caducidadOriginal,
            "un PUT que no informa de FechaExpiracion no debe renovar la caducidad del token");
    }

    [Fact]
    public async Task Actualizar_Con_Caducidad_Superior_A_365_Dias_Debe_Devolver_400()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        var token = await SeedTokenAsync(dbContext, fechaExpiracion: DateTime.UtcNow.AddDays(30));

        var result = await controller.Actualizar(
            token.Id,
            SaveRequest(fechaExpiracion: DateTime.UtcNow.AddDays(366)),
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(badRequest.Value).Should().Contain("365");
    }

    [Fact]
    public async Task Actualizar_Con_Caducidad_En_El_Pasado_Debe_Devolver_400()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        var token = await SeedTokenAsync(dbContext, fechaExpiracion: DateTime.UtcNow.AddDays(30));

        var result = await controller.Actualizar(
            token.Id,
            SaveRequest(fechaExpiracion: DateTime.UtcNow.AddDays(-1)),
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(badRequest.Value).Should().Contain("pasado");
    }

    [Fact]
    public async Task Rotar_Un_Token_Revocado_Debe_Devolver_400_Y_No_Crear_Token_Nuevo()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        var token = await SeedTokenAsync(dbContext, estado: "revocado");

        var result = await controller.Rotar(token.Id, new RotarIntegrationTokenRequest(), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(badRequest.Value).Should().Contain("revocado");
        (await dbContext.IntegrationTokens.CountAsync()).Should().Be(1, "no debe emitirse ningun token nuevo");
    }

    [Fact]
    public async Task Rotar_Un_Token_Caducado_Debe_Devolver_400_Y_No_Crear_Token_Nuevo()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        var token = await SeedTokenAsync(dbContext, fechaExpiracion: DateTime.UtcNow.AddDays(-2));

        var result = await controller.Rotar(token.Id, new RotarIntegrationTokenRequest(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await dbContext.IntegrationTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rotar_Un_Token_Sin_Scopes_Debe_Heredar_Los_Scopes_Vacios()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        // Scopes vacios de verdad (cadena vacia): con "[]" la herencia ya
        // funcionaba, el defecto estaba en el caso de scopes en blanco, que
        // caia a DefaultOpenClawScopes.
        var token = await SeedTokenAsync(dbContext, scopesJson: string.Empty);

        var result = await controller.Rotar(token.Id, new RotarIntegrationTokenRequest(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var rotado = await dbContext.IntegrationTokens.SingleAsync(x => x.RotatedFromTokenId == token.Id);
        rotado.EndpointScopesJson.Should().Be("[]",
            "un token sin scopes (deny-all) no puede rotar a uno con todos los scopes por defecto");
    }

    [Fact]
    public async Task Rotar_Debe_Heredar_La_Caducidad_Del_Token_Antiguo()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);
        var caducidadOriginal = new DateTime(2027, 5, 20, 10, 0, 0, DateTimeKind.Utc);
        var token = await SeedTokenAsync(dbContext, fechaExpiracion: caducidadOriginal);

        var result = await controller.Rotar(token.Id, new RotarIntegrationTokenRequest(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var rotado = await dbContext.IntegrationTokens.SingleAsync(x => x.RotatedFromTokenId == token.Id);
        rotado.FechaExpiracion.Should().Be(caducidadOriginal);
    }

    [Fact]
    public void ResolveExpiration_Debe_Rechazar_Una_Vigencia_Superior_A_365_Dias()
    {
        using var dbContext = BuildDbContext();
        var now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var service = new IntegrationTokenService(
            dbContext,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()),
            new FakeClock(now));

        service.ResolveExpiration(now.AddDays(365), noExpirationConfirmed: false)
            .Should().Be(now.AddDays(365));

        Action act = () => service.ResolveExpiration(now.AddDays(366), noExpirationConfirmed: false);
        act.Should().Throw<ArgumentException>().WithMessage("*365*");
    }

    [Fact]
    public void ResolveExpiration_Debe_Rechazar_Una_Fecha_En_El_Pasado()
    {
        using var dbContext = BuildDbContext();
        var now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var service = new IntegrationTokenService(
            dbContext,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()),
            new FakeClock(now));

        Action act = () => service.ResolveExpiration(now.AddDays(-1), noExpirationConfirmed: false);
        act.Should().Throw<ArgumentException>().WithMessage("*pasado*");
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
