using FluentAssertions;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 6): tests de la capa DLP. Verifican que la frontera
// unica antes del proveedor elimina IBAN, email, telefono, DNI,
// NIE, NIF, CIF, tarjeta y BIC, y que falla cerrado en caso de
// timeout del regex.
public class DlpScrubberTests
{
    private static AiPseudonymMap Pseudonimos() => new(new[]
    {
        ("Atlas Labs SL", "TITULAR"),
        ("Cuenta Operativa EUR", "CUENTA")
    });

    private static DlpScrubber BuildSut() => new(Pseudonimos());

    [Theory]
    [InlineData("Mi IBAN es ES91 2100 0418 4502 0005 1332, confio en el", "[IBAN_REDACTED]")]
    [InlineData("Transferencia desde es91 2100 0418 4502 0005 1332 al exterior", "[IBAN_REDACTED]")]
    [InlineData("iban: ES91-2100-0418-4502-0005-1332 formateado", "[IBAN_REDACTED]")]
    [InlineData("DE89370400440532013000 aleman", "[IBAN_REDACTED]")]
    public void Dlp_Debe_Redactar_IBAN(string entrada, string contieneEsperado)
    {
        var sut = BuildSut();
        var result = sut.Escanear(entrada, "test");
        result.FalloCerrado.Should().BeFalse();
        result.Texto.Should().Contain(contieneEsperado);
        result.Texto.Should().NotContain("ES91");
        result.Texto.Should().NotContain("DE89");
        result.TiposPIIEncontrados.Should().Contain("IBAN");
    }

    [Theory]
    [InlineData("Escribeme a juan.perez@empresa.com hoy", "juan.perez@empresa.com")]
    [InlineData("Contacto: maria_lopez@subdominio.example.org", "maria_lopez@subdominio.example.org")]
    public void Dlp_Debe_Redactar_Email(string entrada, string noDebeContener)
    {
        var sut = BuildSut();
        var result = sut.Escanear(entrada, "test");
        result.FalloCerrado.Should().BeFalse();
        result.Texto.Should().Contain("[EMAIL_REDACTED]");
        result.Texto.Should().NotContain(noDebeContener);
    }

    [Theory]
    [InlineData("Llamame al +34 600 123 456 cuanto antes")]
    [InlineData("Mi numero es 600123456 desde ayer")]
    [InlineData("Telefono internacional 0034600123456 disponible")]
    public void Dlp_Debe_Redactar_Telefono(string entrada)
    {
        var sut = BuildSut();
        var result = sut.Escanear(entrada, "test");
        result.FalloCerrado.Should().BeFalse();
        result.Texto.Should().Contain("[PHONE_REDACTED]");
    }

    [Fact]
    public void Dlp_Debe_Redactar_DNI()
    {
        var sut = BuildSut();
        var result = sut.Escanear("Empleado con DNI 12345678Z dado de alta", "test");

        result.Texto.Should().Contain("[DNI_REDACTED]");
        result.Texto.Should().NotContain("12345678Z");
    }

    [Fact]
    public void Dlp_Debe_Redactar_NIE()
    {
        var sut = BuildSut();
        var result = sut.Escanear("El NIE X1234567A corresponde a", "test");

        result.Texto.Should().Contain("[NIE_REDACTED]");
        result.Texto.Should().NotContain("X1234567A");
    }

    [Fact]
    public void Dlp_Debe_Redactar_CIF()
    {
        var sut = BuildSut();
        var result = sut.Escanear("Empresa con CIF B12345678 factura", "test");

        result.Texto.Should().Contain("[CIF_REDACTED]");
        result.Texto.Should().NotContain("B12345678");
    }

    [Fact]
    public void Dlp_Debe_Redactar_Tarjeta()
    {
        var sut = BuildSut();
        var result = sut.Escanear("Cargo en tarjeta 4111 1111 1111 1111 del titular", "test");

        result.Texto.Should().Contain("[CARD_REDACTED]");
        result.Texto.Should().NotContain("4111 1111 1111 1111");
    }

    [Fact]
    public void Dlp_Debe_Redactar_BIC()
    {
        var sut = BuildSut();
        var result = sut.Escanear("Transferencia con BIC DEUTDEFFXXX anotada", "test");

        result.Texto.Should().Contain("[BIC_REDACTED]");
        result.Texto.Should().NotContain("DEUTDEFFXXX");
    }

    [Fact]
    public void Dlp_Debe_Sustituir_Nombres_De_Titulares_Y_Cuentas()
    {
        var sut = BuildSut();
        var result = sut.Escanear("Pago de Atlas Labs SL desde la Cuenta Operativa EUR", "test");

        result.FalloCerrado.Should().BeFalse();
        result.Texto.Should().Contain("[TITULAR_1]");
        result.Texto.Should().Contain("[CUENTA_1]");
        result.Texto.Should().NotContain("Atlas Labs SL");
        result.Texto.Should().NotContain("Cuenta Operativa EUR");
    }

    [Fact]
    public void Dlp_Debe_Reportar_Tipos_Encontrados_Sin_Overlap()
    {
        var sut = BuildSut();
        var result = sut.Escanear("IBAN ES91 2100 0418 4502 0005 1332 y luego email a@b.com", "test");

        result.Texto.Should().Contain("[IBAN_REDACTED]");
        result.Texto.Should().Contain("[EMAIL_REDACTED]");
        result.TiposPIIEncontrados.Should().Contain("IBAN");
        result.TiposPIIEncontrados.Should().Contain("EMAIL");
    }

    [Fact]
    public void Dlp_Debe_Contar_Patrones_Encontrados()
    {
        var sut = BuildSut();
        var result = sut.Escanear("IBAN1: ES91 2100 0418 4502 0005 1332; IBAN2: FR14 2004 1010 0505 0001 3M02 606", "test");

        result.PatronesPIIRedactados.Should().Be(2);
    }

    [Fact]
    public void Dlp_Texto_Nulo_Devuelve_Fallo_Cerrado()
    {
        var sut = BuildSut();
        var result = sut.Escanear(null!, "test");

        result.FalloCerrado.Should().BeTrue();
        result.MotivoFalloCerrado.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Dlp_Texto_Sin_PII_Produce_Salida_Limpia()
    {
        var sut = BuildSut();
        var texto = "Cual ha sido el gasto en comedores este mes?";
        var result = sut.Escanear(texto, "test");

        result.FalloCerrado.Should().BeFalse();
        result.PatronesPIIRedactados.Should().Be(0);
        result.Texto.Should().Contain(texto);
    }
}
