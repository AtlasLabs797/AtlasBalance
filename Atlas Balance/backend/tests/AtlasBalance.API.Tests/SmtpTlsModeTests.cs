using AtlasBalance.API.Services;
using FluentAssertions;
using MailKit.Security;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// S-17: el modo TLS del SMTP tiene que ser explicito y configurable
// (clave smtp_tls_mode), y tiene que permitir envios sin cifrar porque
// hay relays que no admiten TLS. Antes la decision salia de una
// heuristica implicita (si hay credenciales, TLS; si no, "si el servidor
// lo permite"), asi que las alertas con saldos podian ir en claro sin
// que nadie lo hubiera pedido.
//
// La tabla se prueba sin red: la decision es una funcion estatica que
// recibe puerto y modo.
// -----------------------------------------------------------------------
public sealed class SmtpTlsModeTests
{
    [Theory]
    // 465 es SMTPS: TLS implicito, siempre, sea cual sea el modo.
    [InlineData(465, "required", SecureSocketOptions.SslOnConnect)]
    [InlineData(465, "auto", SecureSocketOptions.SslOnConnect)]
    [InlineData(465, "none", SecureSocketOptions.SslOnConnect)]
    // 587 es el puerto habitual de submission: aqui decide el modo.
    [InlineData(587, "required", SecureSocketOptions.StartTls)]
    [InlineData(587, "auto", SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(587, "none", SecureSocketOptions.None)]
    // El puerto no tiene valor propio: el modo manda.
    [InlineData(25, "required", SecureSocketOptions.StartTls)]
    [InlineData(25, "auto", SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(25, "none", SecureSocketOptions.None)]
    public void ResolverSecureSocketOptions_Should_Map_Port_And_Mode(int port, string mode, SecureSocketOptions expected)
    {
        EmailService.ResolverSecureSocketOptions(port, mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("REQUIRED", SecureSocketOptions.StartTls)]
    [InlineData("Auto", SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(" NONE ", SecureSocketOptions.None)]
    public void ResolverSecureSocketOptions_Should_Ignore_Case_And_Spaces(string mode, SecureSocketOptions expected)
    {
        EmailService.ResolverSecureSocketOptions(587, mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cifrado")]
    public void ResolverSecureSocketOptions_Should_Fallback_To_Auto_When_Mode_Is_Unknown(string? mode)
    {
        // Modo desconocido o ausente = "auto" (el comportamiento anterior),
        // nunca "none": un valor mal escrito no puede abrir el canal en claro.
        EmailService.ResolverSecureSocketOptions(587, mode).Should().Be(SecureSocketOptions.StartTlsWhenAvailable);
    }
}
