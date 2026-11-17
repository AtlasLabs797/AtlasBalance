using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// Lote 2 (S-11 y S-15) contra PostgreSQL real: el provider InMemory no
/// reproduce ni la atomicidad del UPDATE ni el bloqueo consultivo por sesion.
/// </summary>
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class AuthLot2PostgresTests
{
    private readonly PostgresFixture _fixture;

    public AuthLot2PostgresTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_Failures_En_Paralelo_Deben_Contarse_Sin_Perdidas()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var userId = Guid.NewGuid();
        var email = "s11-paralelo@test.local";

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
                NombreCompleto = "S11 Paralelo",
                Rol = RolUsuario.EMPLEADO,
                Activo = true,
                PrimerLogin = false,
                FechaCreacion = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        // Los limites de cache se suben a maximo para aislar el contador de BD:
        // lo que se comprueba aqui es que el UPDATE sea atomico.
        var rateLimiting = new RateLimitingOptions
        {
            LoginMaxFailuresPerIpAndEmail = int.MaxValue,
            LoginMaxFailuresPerIp = int.MaxValue,
            LoginMaxFailuresPerIpExistingUser = int.MaxValue
        };

        const int intentos = 8;
        var tasks = Enumerable.Range(0, intentos).Select(async _ =>
        {
            await using var db = new AppDbContext(options);
            var sut = BuildSut(db, rateLimiting);

            Func<Task> action = () => sut.LoginAsync(email, "BadPass!", "10.30.40.50", CancellationToken.None);
            var exception = await action.Should().ThrowAsync<AuthException>();
            exception.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        });

        await Task.WhenAll(tasks);

        await using var verify = new AppDbContext(options);
        var persisted = await verify.Usuarios.SingleAsync(x => x.Id == userId);

        persisted.FailedLoginAttempts.Should().Be(intentos, "una rafaga paralela no puede perder incrementos del contador");
        persisted.LockedUntil.Should().NotBeNull("la cuenta debe quedar bloqueada");
        persisted.LockedUntil.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task RefreshToken_Concurrente_Debe_Sobrevivir_La_Sesion()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var userId = Guid.NewGuid();
        var email = "s15-paralelo@test.local";
        string refreshToken;

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
                NombreCompleto = "S15 Paralelo",
                Rol = RolUsuario.EMPLEADO,
                Activo = true,
                PrimerLogin = false,
                FechaCreacion = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();

            await using var db = new AppDbContext(options);
            var sut = BuildSut(db, new RateLimitingOptions());
            var login = await sut.LoginAsync(email, "Valid1234!Ab", "10.30.40.51", CancellationToken.None);
            refreshToken = login.RefreshToken!;
        }

        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = new AppDbContext(options);
            var sut = BuildSut(db, new RateLimitingOptions());
            return await sut.RefreshTokenAsync(refreshToken, "10.30.40.51", CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.RefreshToken));

        await using var verify = new AppDbContext(options);
        var persisted = await verify.Usuarios.SingleAsync(x => x.Id == userId);
        var activeTokens = await verify.RefreshTokens
            .Where(rt => rt.UsuarioId == userId && rt.RevocadoEn == null)
            .ToListAsync();

        activeTokens.Should().NotBeEmpty("la sesion no puede cerrarse por un reintento benigno");
        persisted.SecurityStamp.Should().NotBeNullOrWhiteSpace();
    }

    private static AuthService BuildSut(AppDbContext db, RateLimitingOptions rateLimiting)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
                ["JwtSettings:AccessTokenExpMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpDays"] = "7",
                ["JwtSettings:MaxSessionDays"] = "30",
                ["Security:RequireMfaForWebUsers"] = "false"
            })
            .Build();

        return new AuthService(
            db,
            configuration,
            new NoOpAuditService(),
            new PlainTextSecretProtector(),
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()),
            Options.Create(rateLimiting));
    }

    /// <summary>
    /// La auditoria real escribe en AUDITORIAS, que tiene RLS y firma propia.
    /// Aquí no es lo que se prueba, asi que se anula para no mezclar permisos de
    /// base de datos con la concurrencia del login.
    /// </summary>
    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, HttpContext httpContext, string? detallesJson, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task LogAsync(Guid? usuarioId, string tipoAccion, string? entidadTipo, Guid? entidadId, string? ipAddress, string? detallesJson, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
