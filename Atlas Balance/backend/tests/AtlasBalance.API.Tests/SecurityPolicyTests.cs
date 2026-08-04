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
}
