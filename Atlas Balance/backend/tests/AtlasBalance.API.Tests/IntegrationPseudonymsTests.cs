using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class IntegrationPseudonymsTests
{
    [Fact]
    public void ForTitular_Should_Be_Deterministic()
    {
        var id = Guid.Parse("a3f2b901-1111-2222-3333-444455556666");

        IntegrationPseudonyms.ForTitular(id).Should().Be(IntegrationPseudonyms.ForTitular(id));
    }

    [Fact]
    public void ForCuenta_Should_Be_Deterministic()
    {
        var id = Guid.Parse("7b10c204-1111-2222-3333-444455556666");

        IntegrationPseudonyms.ForCuenta(id).Should().Be(IntegrationPseudonyms.ForCuenta(id));
    }

    [Fact]
    public void Pseudonyms_Should_Use_Expected_Opaque_Format()
    {
        var titularId = Guid.Parse("a3f2b901-1111-2222-3333-444455556666");
        var cuentaId = Guid.Parse("7b10c204-1111-2222-3333-444455556666");

        IntegrationPseudonyms.ForTitular(titularId).Should().Be("TITULAR-a3f2b901");
        IntegrationPseudonyms.ForCuenta(cuentaId).Should().Be("CUENTA-7b10c204");
    }

    [Fact]
    public void Different_Entities_Should_Get_Different_Pseudonyms()
    {
        var first = IntegrationPseudonyms.ForTitular(Guid.Parse("a3f2b901-1111-2222-3333-444455556666"));
        var second = IntegrationPseudonyms.ForTitular(Guid.Parse("b4e3c012-1111-2222-3333-444455556666"));

        first.Should().NotBe(second);
    }

    [Fact]
    public void Titular_And_Cuenta_Should_Not_Collide_For_Same_Guid()
    {
        // Si un titular y una cuenta compartieran GUID, sus seudonimos deben
        // seguir distinguiendose por el prefijo del tipo.
        var id = Guid.Parse("a3f2b901-1111-2222-3333-444455556666");

        IntegrationPseudonyms.ForTitular(id).Should().NotBe(IntegrationPseudonyms.ForCuenta(id));
    }

    [Fact]
    public void Empty_Guid_Should_Not_Throw()
    {
        IntegrationPseudonyms.ForTitular(Guid.Empty).Should().Be("TITULAR-00000000");
        IntegrationPseudonyms.ForCuenta(Guid.Empty).Should().Be("CUENTA-00000000");
    }

    [Fact]
    public void Pseudonyms_Should_Not_Leak_The_Real_Name()
    {
        // Regresion: el seudonimo se deriva solo del GUID, nunca del nombre.
        var id = Guid.Parse("a3f2b901-1111-2222-3333-444455556666");

        IntegrationPseudonyms.ForTitular(id).Should().NotContainAny("Acme", "Constructora", "SL");
    }
}
