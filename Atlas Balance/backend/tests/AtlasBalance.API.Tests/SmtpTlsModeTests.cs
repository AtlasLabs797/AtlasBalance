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
// que nadie lo hubiera pedido. Regresion: "auto" con credenciales volvia
// a degradar a StartTlsWhenAvailable (autenticar y enviar en claro).
//
// La tabla se prueba sin red: la decision es una funcion estatica que
// recibe puerto, modo y si hay credenciales.
// -----------------------------------------------------------------------
public sealed class SmtpTlsModeTests
{
    [Theory]
    // 465 es SMTPS: TLS implicito, siempre, sea cual sea el modo y haya o no credenciales.
    [InlineData(465, "required", true, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, "auto", true, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, "auto", false, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, "none", false, SecureSocketOptions.SslOnConnect)]
    // 587 es el puerto habitual de submission: aqui decide el modo.
    [InlineData(587, "required", true, SecureSocketOptions.StartTls)]
    [InlineData(587, "required", false, SecureSocketOptions.StartTls)]
    // auto (lo que tienen las instalaciones antiguas): con credenciales, STARTTLS
    // obligatorio (nunca se autentica en claro); sin credenciales, si el servidor lo ofrece.
    [InlineData(587, "auto", true, SecureSocketOptions.StartTls)]
    [InlineData(587, "auto", false, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(587, "none", true, SecureSocketOptions.None)]
    [InlineData(587, "none", false, SecureSocketOptions.None)]
    // El puerto no tiene valor propio: el modo manda.
    [InlineData(25, "required", false, SecureSocketOptions.StartTls)]
    [InlineData(25, "auto", true, SecureSocketOptions.StartTls)]
    [InlineData(25, "auto", false, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(25, "none", false, SecureSocketOptions.None)]
    public void ResolverSecureSocketOptions_Should_Map_Port_Mode_And_Credentials(int port, string mode, bool hasCredentials, SecureSocketOptions expected)
    {
        EmailService.ResolverSecureSocketOptions(port, mode, hasCredentials).Should().Be(expected);
    }

    [Theory]
    [InlineData("REQUIRED", SecureSocketOptions.StartTls)]
    [InlineData(" NONE ", SecureSocketOptions.None)]
    public void ResolverSecureSocketOptions_Should_Ignore_Case_And_Spaces(string mode, SecureSocketOptions expected)
    {
        EmailService.ResolverSecureSocketOptions(587, mode, hasCredentials: true).Should().Be(expected);
    }

    [Fact]
    public void ResolverSecureSocketOptions_Auto_Ignora_Mayusculas_Y_Espacios()
    {
        EmailService.ResolverSecureSocketOptions(587, " Auto ", hasCredentials: true).Should().Be(SecureSocketOptions.StartTls);
        EmailService.ResolverSecureSocketOptions(587, "Auto", hasCredentials: false).Should().Be(SecureSocketOptions.StartTlsWhenAvailable);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("cifrado", true)]
    [InlineData("cifrado", false)]
    public void ResolverSecureSocketOptions_Should_Fallback_To_Required_When_Mode_Is_Unknown(string? mode, bool hasCredentials)
    {
        // Modo desconocido o ausente = "required", con o sin credenciales: un
        // valor mal escrito no puede abrir el canal en claro.
        EmailService.ResolverSecureSocketOptions(587, mode, hasCredentials).Should().Be(SecureSocketOptions.StartTls);
    }
}
