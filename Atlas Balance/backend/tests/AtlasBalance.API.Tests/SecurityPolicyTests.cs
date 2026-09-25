using AtlasBalance.API.Constants;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public class SecurityPolicyTests
{
    [Fact]
    public void CommonPasswords_AllEntries_MeetMinimumLength()
    {
        // Garantiza que la lista nunca vuelva a tener entradas inalcanzables: la
        // longitud minima se comprueba ANTES que la pertenencia a esta lista, asi
        // que cualquier entrada de menos de MinPasswordLength caracteres es codigo
        // muerto que nunca se puede evaluar.
        var tooShort = SecurityPolicy.CommonPasswordsView
            .Where(p => p.Length < SecurityPolicy.MinPasswordLength)
            .ToList();

        tooShort.Should().BeEmpty(
            "todas las entradas de CommonPasswords deben tener al menos {0} caracteres " +
            "para ser alcanzables (la longitud minima se comprueba antes)",
            SecurityPolicy.MinPasswordLength);
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_KnownCommonPassword()
    {
        var common = SecurityPolicy.CommonPasswordsView.First();

        var result = SecurityPolicy.TryValidatePassword(common, out var error);

        result.Should().BeFalse();
        error.Should().Be("La contraseña es demasiado comun");
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_CommonPassword_CaseInsensitive()
    {
        var common = SecurityPolicy.CommonPasswordsView.First().ToUpperInvariant();

        var result = SecurityPolicy.TryValidatePassword(common, out var error);

        result.Should().BeFalse();
        error.Should().Be("La contraseña es demasiado comun");
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_ShortPassword_With_LengthMessage()
    {
        var result = SecurityPolicy.TryValidatePassword("Corta123", out var error);

        result.Should().BeFalse();
        error.Should().Be($"La contraseña debe tener al menos {SecurityPolicy.MinPasswordLength} caracteres");
    }

    [Fact]
    public void TryValidatePassword_Should_Accept_LongUncommonPassword()
    {
        var result = SecurityPolicy.TryValidatePassword("Xk9$mQ2vLp7#nR", out var error);

        result.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_SingleRepeatedCharacter()
    {
        var result = SecurityPolicy.TryValidatePassword("aaaaaaaaaaaaaaaa", out var error);

        result.Should().BeFalse();
        error.Should().Be("La contraseña no puede repetir un solo caracter");
    }

    // Tests para mejora a) - validación contra datos del usuario
    [Fact]
    public void TryValidatePassword_Should_Reject_When_Contains_Email_LocalPart_LongEnough()
    {
        var userEmail = "juan.perez@example.com";
        var password = "MySecure#Juan1234";

        var result = SecurityPolicy.TryValidatePassword(password, out var error, userEmail, null);

        result.Should().BeFalse();
        error.Should().Be("La contraseña no puede contener tu nombre o tu email");
    }

    [Fact]
    public void TryValidatePassword_Should_Ignore_Email_LocalPart_If_Too_Short()
    {
        var userEmail = "abc@example.com";
        var password = "AbcSecure123456";

        var result = SecurityPolicy.TryValidatePassword(password, out var error, userEmail, null);

        result.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_When_Contains_FullName_Word()
    {
        var userEmail = "user@example.com";
        var userFullName = "Carlos Alberto Mendez";
        var password = "MySecure#Carlos123";

        var result = SecurityPolicy.TryValidatePassword(password, out var error, userEmail, userFullName);

        result.Should().BeFalse();
        error.Should().Be("La contraseña no puede contener tu nombre o tu email");
    }

    [Fact]
    public void TryValidatePassword_Should_Ignore_Short_Name_Words()
    {
        var userEmail = "user@example.com";
        var userFullName = "Ana Martinez";
        var password = "AnaSecure123456789";

        var result = SecurityPolicy.TryValidatePassword(password, out var error, userEmail, userFullName);

        result.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidatePassword_Should_Be_CaseInsensitive_For_UserData()
    {
        var userEmail = "juan@example.com";
        var password = "MySecure#JUAN1234";

        var result = SecurityPolicy.TryValidatePassword(password, out var error, userEmail, null);

        result.Should().BeFalse();
    }

    // Tests para mejora b) - patrón palabra común + sufijo trivial
    [Fact]
    public void TryValidatePassword_Should_Reject_CommonRoot_With_Digits()
    {
        var password = "Password1234567890";

        var result = SecurityPolicy.TryValidatePassword(password, out var error);

        result.Should().BeFalse();
        error.Should().Be("La contraseña es demasiado comun");
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_CommonRoot_With_Leet_Suffix()
    {
        var password = "P@ssw0rd123!@#";

        var result = SecurityPolicy.TryValidatePassword(password, out var error);

        result.Should().BeFalse();
        error.Should().Be("La contraseña es demasiado comun");
    }

    [Fact]
    public void TryValidatePassword_Should_Reject_Admin_Pattern()
    {
        var password = "Admin2024#$%";

        var result = SecurityPolicy.TryValidatePassword(password, out var error);

        result.Should().BeFalse();
        error.Should().Be("La contraseña es demasiado comun");
    }

    [Fact]
    public void TryValidatePassword_Should_Accept_Long_Passphrase()
    {
        var password = "caballo-bateria-grapa-correcto";

        var result = SecurityPolicy.TryValidatePassword(password, out var error);

        result.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidatePassword_Should_Accept_CommonRoot_With_Letters()
    {
        var password = "PasswordABC123456";

        var result = SecurityPolicy.TryValidatePassword(password, out var error);

        result.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("password1234")]
    [InlineData("P@ssw0rd2024!")]
    [InlineData("adm1n1strad0r99")]
    [InlineData("Welcome!!!!!2026")]
    [InlineData("atlasbalance2027")]
    [InlineData("tesoreria#2031")]
    public void TryValidatePassword_Should_Reject_CommonRoot_Plus_Trivial_Suffix(string password)
    {
        SecurityPolicy.TryValidatePassword(password, out var error).Should().BeFalse();
        error.Should().Be("La contraseña es demasiado comun");
    }

    [Theory]
    [InlineData("caballo-bateria-grapa-correcto")]
    [InlineData("passwordmanagerpro")]
    [InlineData("mi.tesoreria.segura.2026")]
    [InlineData("Zx9!kLm#2qPw")]
    public void TryValidatePassword_Should_Accept_NonTrivial_Passwords(string password)
    {
        SecurityPolicy.TryValidatePassword(password, out var error).Should().BeTrue(error);
    }
}
