using AtlasBalance.API.Validation;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// V-02.07: el IBAN solo pasaba por Trim(), asi que una errata se guardaba en
/// silencio. Estos tests fijan el contrato de IbanValidator.
/// </summary>
public class IbanValidatorTests
{
    // ES9121000418450200051332 es el IBAN de ejemplo de la especificacion; el
    // resto son variantes de formato del mismo numero.
    [Theory]
    [InlineData("ES9121000418450200051332")]
    [InlineData("ES91 2100 0418 4502 0005 1332")]
    [InlineData("es9121000418450200051332")]
    [InlineData("  ES91 2100 0418 4502 0005 1332  ")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("DE89370400440532013000")]
    public void TryValidate_Should_Accept_ValidIban(string iban)
    {
        var ok = IbanValidator.TryValidate(iban, out var error);

        ok.Should().BeTrue("'{0}' es un IBAN valido", iban);
        error.Should().BeEmpty();
    }

    // El IBAN es opcional: quien decide si es obligatorio es el llamante.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_Should_Accept_EmptyValue(string? iban)
    {
        var ok = IbanValidator.TryValidate(iban, out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidate_Should_Reject_WrongCheckDigits()
    {
        // Mismo BBAN que el IBAN valido pero con los digitos de control cambiados.
        var ok = IbanValidator.TryValidate("ES9221000418450200051332", out var error);

        ok.Should().BeFalse();
        error.Should().Contain("digito de control");
    }

    [Fact]
    public void TryValidate_Should_Reject_TransposedDigits()
    {
        // Dos digitos intercambiados dentro del BBAN: el caso tipico al teclear,
        // y justo lo que el modulo 97 esta para detectar.
        var ok = IbanValidator.TryValidate("ES9121000418450200051323", out _);

        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("ES91")]
    [InlineData("ES912100")]
    public void TryValidate_Should_Reject_TooShort(string iban)
    {
        var ok = IbanValidator.TryValidate(iban, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("caracteres");
    }

    [Fact]
    public void TryValidate_Should_Reject_TooLong()
    {
        var ok = IbanValidator.TryValidate(new string('1', 40), out var error);

        ok.Should().BeFalse();
        error.Should().Contain("caracteres");
    }

    [Fact]
    public void TryValidate_Should_Reject_HugeInput_Without_Normalizing_It()
    {
        // Corta por la entrada cruda: sin la guarda previa esto copiaria varios MB
        // solo para acabar rechazado por longitud.
        var ok = IbanValidator.TryValidate(new string('A', 5 * 1024 * 1024), out var error);

        ok.Should().BeFalse();
        error.Should().Contain("caracteres");
    }

    [Fact]
    public void TryValidate_Should_Reject_MissingCountryCode()
    {
        var ok = IbanValidator.TryValidate("1291 2100 0418 4502 0005 1332", out var error);

        ok.Should().BeFalse();
        error.Should().Contain("codigo de pais");
    }

    [Fact]
    public void TryValidate_Should_Reject_NonDigitCheckDigits()
    {
        var ok = IbanValidator.TryValidate("ESXX21000418450200051332", out var error);

        ok.Should().BeFalse();
        error.Should().Contain("digitos de control");
    }

    [Fact]
    public void TryValidate_Should_Reject_NonAlphanumericCharacters()
    {
        var ok = IbanValidator.TryValidate("ES91-2100-0418-4502-0005-1332", out var error);

        ok.Should().BeFalse();
        error.Should().Contain("letras y numeros");
    }

    // Regresion: el IBAN de la cuenta demo llevaba los digitos de control 00, que
    // son imposibles en un IBAN real. Si alguien vuelve a ponerlo, la cuenta demo
    // deja de poder editarse en cuanto se guarda.
    [Fact]
    public void TryValidate_Should_Accept_SeedDemoIban_And_Reject_ThePreviousOne()
    {
        IbanValidator.TryValidate("ES55 0000 0000 0000 0000 0001", out _).Should().BeTrue();
        IbanValidator.TryValidate("ES00 0000 0000 0000 0000 0001", out _).Should().BeFalse();
    }
}
