using FluentAssertions;
using AtlasBalance.API.Data;
using Xunit;

namespace AtlasBalance.API.Tests.Rls;

// V-02-06 (RLS-SEC-01): tests puros (sin PostgreSQL) del firmador HMAC del
// contexto RLS. Validan el payload canonico, el secret vacio y la sensibilidad
// a cada campo. Son la primera linea de defensa contra cualquier cambio que
// rompa la firma hacia PostgreSQL sin necesidad de Testcontainers.
public sealed class RlsContextSignerTests
{
    [Fact]
    public void BuildPayload_ShouldJoinFieldsWithPipe_InExpectedOrder()
    {
        var payload = RlsContextSigner.BuildPayload(
            authMode: "user",
            userId: "11111111-1111-1111-1111-111111111111",
            integrationTokenId: "",
            isAdmin: "false",
            system: "false",
            requestScope: "data");

        payload.Should().Be("user|11111111-1111-1111-1111-111111111111||false|false|data");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sign_WithEmptySecret_ShouldReturnEmptyString(string secret)
    {
        var signature = RlsContextSigner.Sign(
            secret,
            authMode: "user",
            userId: "11111111-1111-1111-1111-111111111111",
            integrationTokenId: "",
            isAdmin: "false",
            system: "false",
            requestScope: "data");

        signature.Should().BeEmpty();
    }

    [Fact]
    public void Sign_ShouldReturnLowercaseHex_OfExactly64Chars()
    {
        const string secret = "test-rls-context-placeholder-value-32-chars";

        var signature = RlsContextSigner.Sign(
            secret,
            authMode: "user",
            userId: "11111111-1111-1111-1111-111111111111",
            integrationTokenId: "",
            isAdmin: "false",
            system: "false",
            requestScope: "data");

        signature.Length.Should().Be(64);
        signature.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Sign_ShouldBeDeterministic()
    {
        const string secret = "test-rls-context-placeholder-value-32-chars";

        var first = RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "false", "false", "data");
        var second = RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "false", "false", "data");

        first.Should().Be(second);
    }

    [Fact]
    public void Sign_ShouldChangeWithEveryField()
    {
        const string secret = "test-rls-context-placeholder-value-32-chars";
        var baseline = RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "false", "false", "data");

        RlsContextSigner.Sign(secret, "system", "11111111-1111-1111-1111-111111111111", "", "false", "false", "data")
            .Should().NotBe(baseline);
        RlsContextSigner.Sign(secret, "user", "22222222-2222-2222-2222-222222222222", "", "false", "false", "data")
            .Should().NotBe(baseline);
        RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "abc", "false", "false", "data")
            .Should().NotBe(baseline);
        RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "true", "false", "data")
            .Should().NotBe(baseline);
        RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "false", "true", "data")
            .Should().NotBe(baseline);
        RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "false", "false", "write")
            .Should().NotBe(baseline);
    }

    [Fact]
    public void Sign_ShouldMatchKnownVector_ForFixedInputs()
    {
        // V-02-06 (RLS-SEC-02): vector canonico generado localmente. Sirve
        // como regression byte-a-byte y como contrato entre backend y BD.
        // Si cualquier lado cambia el orden o el algoritmo, este test rompe.
        const string secret = "test-rls-context-placeholder-value-32-chars";
        var payload = RlsContextSigner.BuildPayload("user", "11111111-1111-1111-1111-111111111111", "", "false", "false", "data");
        var signature = RlsContextSigner.Sign(secret, "user", "11111111-1111-1111-1111-111111111111", "", "false", "false", "data");

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        expected.Should().Be(signature);
    }
}
