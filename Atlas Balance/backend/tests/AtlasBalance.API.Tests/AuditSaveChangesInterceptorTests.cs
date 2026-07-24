using System.Security.Claims;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class AuditSaveChangesInterceptorTests
{
    [Fact]
    public async Task SaveChanges_Should_Redact_Secret_Configuration_And_Attribute_Authenticated_User()
    {
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
        var interceptor = new AuditSaveChangesInterceptor(
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<AuditSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new AppDbContext(options);
        const string secretValue = "do-not-store-this-in-audit";
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "smtp_password",
            Valor = secretValue,
            Tipo = "string",
            EsSecreto = true,
            Descripcion = "Configuracion de prueba"
        });

        await db.SaveChangesAsync();

        var audit = await db.Auditorias.SingleAsync();
        audit.UsuarioId.Should().Be(userId);
        audit.DetallesJson.Should().Contain("[REDACTED]");
        audit.DetallesJson.Should().NotContain(secretValue);
    }
}
