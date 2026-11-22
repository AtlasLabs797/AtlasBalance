using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// S-11, S-13 y S-15: los tres ficheros de configuracion del backend
/// (appsettings.json y sus dos plantillas) han recibido claves nuevas. Una
/// coma perdida deja el fichero con JSON invalido y el instalador genera un
/// appsettings de produccion que la API no puede leer.
///
/// Hasta ahora nada lo comprobaba: el unico test que los lee (Serilog) busca
/// la carpeta de fuentes subiendo desde el binario de test, asi que con
/// <c>--artifacts-path</c> no las encuentra y se salta el fichero entero.
/// Este test usa <see cref="TestSourceLocator"/>, que si resuelve en ese caso.
/// </summary>
public sealed class AppsettingsValidezTests
{
    public static TheoryData<string> ConfigurationFiles => new()
    {
        "appsettings.json",
        "appsettings.Development.json.template",
        "appsettings.Production.json.template"
    };

    [Theory]
    [MemberData(nameof(ConfigurationFiles))]
    public void El_Fichero_De_Configuracion_Debe_Ser_Json_Valido(string fileName)
    {
        var path = TestSourceLocator.Find("AtlasBalance.API", fileName);

        var content = File.ReadAllText(path);

        var act = () => JsonDocument.Parse(content);

        act.Should().NotThrow("un appsettings con JSON invalido rompe el arranque de la API y el instalador");
    }

    [Theory]
    [MemberData(nameof(ConfigurationFiles))]
    public void El_Fichero_De_Configuracion_Debe_Traer_Las_Claves_De_Auth(string fileName)
    {
        var path = TestSourceLocator.Find("AtlasBalance.API", fileName);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        // S-13: vida maxima de sesion en dias.
        root.GetProperty("JwtSettings").GetProperty("MaxSessionDays").GetInt32().Should().Be(30);

        // S-11: umbral por IP para cuentas que SI existen.
        var rateLimiting = root.GetProperty("AtlasBalance").GetProperty("RateLimiting");
        rateLimiting.GetProperty("LoginMaxFailuresPerIpExistingUser").GetInt32().Should().Be(20);

        // S-15: ventana de gracia del reuso benigno de refresh token.
        rateLimiting.GetProperty("RefreshReuseGraceSeconds").GetInt32().Should().Be(30);
    }
}
