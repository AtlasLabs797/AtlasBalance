using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

public class AccountLockoutNotificationTests
{
    private const string UserPassword = "Valid1234!Ab";
    private const string WrongPassword = "BadPass-Secret-9!";

    private sealed record LockedMessage(IReadOnlyList<string> Recipients, DateTime LockedAtUtc, int LockMinutes, string? IpSummary);

    private sealed class RecordingEmailService : IEmailService
    {
        public List<LockedMessage> Locked { get; } = [];
        public bool Throw { get; set; }

        public Task SendAccountLockedAsync(IReadOnlyList<string> recipients, DateTime lockedAtUtc, int lockMinutes, string? ipSummary, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("smtp caido");
            }

            Locked.Add(new LockedMessage(recipients, lockedAtUtc, lockMinutes, ipSummary));
            return Task.CompletedTask;
        }

        public Task SendSaldoBajoAlertAsync(IReadOnlyList<string> recipients, string titularNombre, string cuentaNombre, Guid cuentaId, string divisa, decimal saldoActual, decimal saldoMinimo, string? conceptoUltimoMovimiento, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendPlazoFijoVencimientoAsync(IReadOnlyList<string> recipients, string titularNombre, string cuentaNombre, Guid cuentaId, DateOnly fechaVencimiento, EstadoPlazoFijo estado, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendTestEmailAsync(string recipient, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendSecurityAlertAsync(IReadOnlyList<string> recipients, string regla, string severidad, string resumen, IReadOnlyList<string> detalles, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Fixture
    {
        public required AppDbContext Db { get; init; }
        public required RecordingEmailService Email { get; init; }
        public required AuthService Sut { get; init; }
        public required Usuario User { get; init; }
        public required int MaxAttempts { get; init; }
        public required TimeSpan LockDuration { get; init; }
    }

    private static async Task<Fixture> BuildAsync(bool smtpConfigured = true)
    {
        var dbName = Guid.NewGuid().ToString();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        var db = new AppDbContext(dbOptions);

        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "titular@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(UserPassword, workFactor: 12),
            NombreCompleto = "Titular Prueba",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        };
        db.Usuarios.Add(user);
        db.UsuarioEmails.Add(new UsuarioEmail { Id = Guid.NewGuid(), UsuarioId = user.Id, Email = "extra@test.local" });
        if (smtpConfigured)
        {
            db.Configuraciones.Add(new Configuracion { Clave = "smtp_host", Valor = "smtp.test.local" });
        }

        await db.SaveChangesAsync();

        var email = new RecordingEmailService();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IEmailService>(email);
        var provider = services.BuildServiceProvider();

        // Dispatch sincrono: el envio real va en Task.Run; aqui se espera sin sleeps.
        var notifier = new AccountLockoutNotifier(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccountLockoutNotifier>.Instance,
            work => work());

        var rateLimiting = new RateLimitingOptions
        {
            LoginMaxFailuresPerIpAndEmail = int.MaxValue,
            LoginMaxFailuresPerIp = int.MaxValue
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
                ["JwtSettings:AccessTokenExpMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpDays"] = "7",
                ["Security:RequireMfaForWebUsers"] = "false"
            })
            .Build();

        var sut = new AuthService(
            db,
            config,
            TestAuditService.Create(db),
            new PlainTextSecretProtector(),
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()),
            Options.Create(rateLimiting),
            lockoutNotifier: notifier);

        return new Fixture
        {
            Db = db,
            Email = email,
            Sut = sut,
            User = user,
            MaxAttempts = rateLimiting.LoginMaxFailedAttemptsPerAccount,
            LockDuration = rateLimiting.LoginLockDuration
        };
    }

    private static async Task FailLoginAsync(Fixture f)
    {
        Func<Task> action = () => f.Sut.LoginAsync(f.User.Email, WrongPassword, "203.0.113.7", CancellationToken.None);
        var ex = await action.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Login_Should_Send_Exactly_One_Email_When_Account_Gets_Locked()
    {
        var f = await BuildAsync();

        for (var i = 1; i < f.MaxAttempts; i++)
        {
            await FailLoginAsync(f);
        }

        f.Email.Locked.Should().BeEmpty("antes del umbral no hay bloqueo");

        await FailLoginAsync(f);

        f.Email.Locked.Should().ContainSingle();
        var message = f.Email.Locked[0];
        message.Recipients.Should().BeEquivalentTo(["titular@test.local", "extra@test.local"]);
        message.IpSummary.Should().Be("203.0.113.7");
        message.LockMinutes.Should().Be((int)f.LockDuration.TotalMinutes);
    }

    [Fact]
    public async Task Login_Attempt_While_Already_Locked_Should_Not_Send_Another_Email()
    {
        var f = await BuildAsync();
        for (var i = 0; i < f.MaxAttempts; i++)
        {
            await FailLoginAsync(f);
        }

        f.Email.Locked.Should().ContainSingle();

        await FailLoginAsync(f);
        await FailLoginAsync(f);

        f.Email.Locked.Should().ContainSingle();
    }

    [Fact]
    public async Task Email_Failure_Should_Not_Change_Login_Response()
    {
        var f = await BuildAsync();
        f.Email.Throw = true;

        for (var i = 0; i < f.MaxAttempts; i++)
        {
            await FailLoginAsync(f);
        }

        var persisted = await f.Db.Usuarios.FirstAsync(x => x.Id == f.User.Id);
        persisted.LockedUntil.Should().BeAfter(DateTime.UtcNow);
        f.Email.Locked.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_Smtp_Configuration_Should_Skip_Email_Silently()
    {
        var f = await BuildAsync(smtpConfigured: false);

        for (var i = 0; i < f.MaxAttempts; i++)
        {
            await FailLoginAsync(f);
        }

        f.Email.Locked.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_With_Wrong_Current_Password_Should_Notify_When_It_Locks()
    {
        var f = await BuildAsync();

        for (var i = 0; i < f.MaxAttempts; i++)
        {
            Func<Task> action = () => f.Sut.ChangePasswordAsync(f.User.Id, WrongPassword, "Nueva1234!Segura-X", "203.0.113.7", null, CancellationToken.None);
            var ex = await action.Should().ThrowAsync<AuthException>();
            ex.Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }

        f.Email.Locked.Should().ContainSingle();
    }

    [Fact]
    public async Task Email_Content_Should_Not_Contain_Links_Or_Password()
    {
        var f = await BuildAsync();
        for (var i = 0; i < f.MaxAttempts; i++)
        {
            await FailLoginAsync(f);
        }

        var message = f.Email.Locked.Should().ContainSingle().Subject;
        var html = EmailService.BuildAccountLockedHtml(message.LockedAtUtc, message.LockMinutes, message.IpSummary);

        html.Should().NotContainEquivalentOf("http");
        html.Should().NotContain(WrongPassword);
        html.Should().NotContain(UserPassword);
        html.Should().Contain("avisa al administrador");
        html.Should().Contain($"{message.LockMinutes} minutos");
        html.Should().Contain("203.0.113.7");
        message.IpSummary.Should().NotContain(WrongPassword);
    }
}
