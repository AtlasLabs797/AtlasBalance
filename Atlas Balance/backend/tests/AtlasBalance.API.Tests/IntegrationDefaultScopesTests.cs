using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Middleware;
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
/// S-24: los scopes por defecto ya no incluyen `vencimientos` (no existe ningun
/// endpoint que lo use) ni `auditoria` (expone el contenido de las celdas
/// modificadas). `auditoria` sigue siendo concedible de forma explicita;
/// `vencimientos` deja de ser concedible.
/// </summary>
public sealed class IntegrationDefaultScopesTests
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

    private static CreateIntegrationTokenRequest CrearRequest(IReadOnlyList<string>? scopes)
    {
        return new CreateIntegrationTokenRequest
        {
            Nombre = "token-scopes",
            PermisoLectura = true,
            PermisoEscritura = false,
            Scopes = scopes!,
            Permisos = [new SaveIntegrationPermissionRequest { AccesoTipo = "lectura" }]
        };
    }

    private static IReadOnlyList<string> GetScopeArray(string fieldName)
    {
        var field = typeof(IntegracionesController)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull($"IntegracionesController debe declarar {fieldName}");

        var valor = field!.GetValue(null) as string[];
        valor.Should().NotBeNull($"{fieldName} debe ser un string[]");

        return valor!;
    }

    [Fact]
    public async Task Token_Creado_Sin_Scopes_Explicitos_No_Debe_Incluir_Vencimientos_Ni_Auditoria()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);

        // Scopes = null es la unica forma de alcanzar la lista por defecto
        // (omitir el campo en el JSON deja la lista vacia del DTO).
        var result = await controller.Crear(CrearRequest(scopes: null), CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        var guardado = await dbContext.IntegrationTokens.SingleAsync();
        var scopes = JsonSerializer.Deserialize<List<string>>(guardado.EndpointScopesJson)!;

        scopes.Should().Contain("saldos");
        scopes.Should().NotContain("vencimientos");
        scopes.Should().NotContain("auditoria");
    }

    [Fact]
    public void Los_Scopes_Por_Defecto_Deben_Ser_Un_Subconjunto_De_Los_Conocidos()
    {
        var conocidos = GetScopeArray("KnownOpenClawScopes");
        var porDefecto = GetScopeArray("DefaultOpenClawScopes");

        porDefecto.Should().BeSubsetOf(conocidos);
    }

    [Fact]
    public async Task Vencimientos_No_Debe_Ser_Concedible()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);

        var result = await controller.Crear(CrearRequest(scopes: ["vencimientos"]), CancellationToken.None);

        JsonSerializer.Serialize(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value)
            .Should().Contain("Scope desconocido");
        (await dbContext.IntegrationTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Auditoria_Sigue_Siendo_Concedible_De_Forma_Explicita()
    {
        await using var dbContext = BuildDbContext();
        var controller = BuildController(dbContext);

        var result = await controller.Crear(CrearRequest(scopes: ["auditoria"]), CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        var guardado = await dbContext.IntegrationTokens.SingleAsync();
        var scopes = JsonSerializer.Deserialize<List<string>>(guardado.EndpointScopesJson)!;

        scopes.Should().Contain("auditoria");
    }
}
