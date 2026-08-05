using System.Security.Claims;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
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
            NullLogger<AuditSaveChangesInterceptor>.Instance,
            new AuditSigner(new AuditSigningKey("clave-de-firma-de-pruebas-de-32-caracteres")));
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

    // -------------------------------------------------------------------
    // V-02.07: la auditoria automatica de entidades tiene que llevar el mismo
    // contexto y la misma firma que la manual. Si el interceptor no firmase,
    // la mayor parte de las filas de AUDITORIAS quedaria fuera de la
    // verificacion de integridad y el mecanismo no valdria de nada.
    // -------------------------------------------------------------------
    [Fact]
    public async Task SaveChanges_Should_Sign_The_Row_And_Record_Request_Context()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(AuditRequestContext.SessionClaim, "sesion-de-prueba")
            ],
            authenticationType: "Test"));
        httpContext.Request.Headers.UserAgent = "Mozilla/5.0 (Test)";
        httpContext.Request.Cookies = new TestCookies("__Host-atlas-access-token", "un-token");

        var signer = new AuditSigner(new AuditSigningKey("clave-de-firma-de-pruebas-de-32-caracteres"));
        var interceptor = new AuditSaveChangesInterceptor(
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<AuditSaveChangesInterceptor>.Instance,
            signer);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new AppDbContext(options);
        db.Paises.Add(new Pais { Id = Guid.NewGuid(), Nombre = "Espana", CodigoIso2 = "ES" });
        await db.SaveChangesAsync();

        var audit = await db.Auditorias.SingleAsync();
        audit.SessionId.Should().Be("sesion-de-prueba");
        audit.UserAgent.Should().Be("Mozilla/5.0 (Test)");
        // Cookie de sesion presente = entro por el navegador, no por la API.
        audit.Origen.Should().Be(AuditOrigenes.Ui);
        audit.Firma.Should().NotBeNullOrEmpty();
        signer.Verificar(audit).Should().BeTrue();
    }

    [Fact]
    public async Task SaveChanges_Should_Mark_Origen_As_Job_Without_HttpContext()
    {
        // Los jobs de Hangfire y el seed no tienen peticion. Marcarlos como UI
        // haria que una purga automatica pareciese una accion de usuario.
        var interceptor = new AuditSaveChangesInterceptor(
            new HttpContextAccessor(),
            NullLogger<AuditSaveChangesInterceptor>.Instance,
            new AuditSigner(new AuditSigningKey("clave-de-firma-de-pruebas-de-32-caracteres")));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new AppDbContext(options);
        db.Paises.Add(new Pais { Id = Guid.NewGuid(), Nombre = "Portugal", CodigoIso2 = "PT" });
        await db.SaveChangesAsync();

        var audit = await db.Auditorias.SingleAsync();
        audit.Origen.Should().Be(AuditOrigenes.Job);
        audit.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task SaveChanges_Should_Not_Add_Automatic_Audit_During_Anonymous_Auth_Flow()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/auth/mfa/verify";
        var interceptor = new AuditSaveChangesInterceptor(
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<AuditSaveChangesInterceptor>.Instance,
            new AuditSigner(new AuditSigningKey("clave-de-firma-de-pruebas-de-32-caracteres")));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new AppDbContext(options);
        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "mfa@example.test",
            NombreCompleto = "Prueba MFA",
            PasswordHash = "hash-de-prueba"
        });

        await db.SaveChangesAsync();

        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    private sealed class TestCookies : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _valores;

        public TestCookies(string clave, string valor)
        {
            _valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [clave] = valor };
        }

        public string? this[string key] => _valores.TryGetValue(key, out var v) ? v : null;
        public int Count => _valores.Count;
        public ICollection<string> Keys => _valores.Keys;
        public bool ContainsKey(string key) => _valores.ContainsKey(key);
        public bool TryGetValue(string key, out string? value) => _valores.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _valores.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
