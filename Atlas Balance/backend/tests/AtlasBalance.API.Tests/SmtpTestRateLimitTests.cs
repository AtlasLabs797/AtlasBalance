using FluentAssertions;
using AtlasBalance.API.Services;
using Xunit;

namespace AtlasBalance.API.Tests;

public class SmtpTestRateLimitTests
{
    [Fact]
    public async Task PrimerIntento_Debe_Permitir()
    {
        var rateLimit = new SmtpTestRateLimit();
        var userId = Guid.NewGuid();

        (await rateLimit.TryAcquireAsync(userId, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CincoIntentos_Deben_Permitir_Y_Sexto_Debe_Bloquear()
    {
        var rateLimit = new SmtpTestRateLimit();
        var userId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            (await rateLimit.TryAcquireAsync(userId, CancellationToken.None)).Should().BeTrue($"el intento {i + 1} esta dentro del limite de 5 por minuto");
        }

        (await rateLimit.TryAcquireAsync(userId, CancellationToken.None)).Should().BeFalse("el sexto intento excede el limite de 5 por minuto");
    }

    [Fact]
    public async Task UsuarioDistinto_Debe_Permitir_Aunque_Otro_Usuario_Haya_Agotado_El_Limite()
    {
        var rateLimit = new SmtpTestRateLimit();
        var usuarioA = Guid.NewGuid();
        var usuarioB = Guid.NewGuid();

        for (var i = 0; i < 6; i++)
        {
            await rateLimit.TryAcquireAsync(usuarioA, CancellationToken.None);
        }

        (await rateLimit.TryAcquireAsync(usuarioB, CancellationToken.None)).Should().BeTrue("el limite es por usuario");
    }

    [Fact]
    public async Task UserId_Nulo_Debe_Permitir()
    {
        var rateLimit = new SmtpTestRateLimit();

        (await rateLimit.TryAcquireAsync(null, CancellationToken.None)).Should().BeTrue();
    }
}
