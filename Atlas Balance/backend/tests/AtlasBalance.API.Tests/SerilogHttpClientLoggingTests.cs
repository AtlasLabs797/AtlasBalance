using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public class SerilogHttpClientLoggingTests
{
    // El secreto del proveedor de tipos de cambio viaja en la RUTA de la URL y el webhook
    // de Slack es una URL secreta. HttpClient registra la URL completa a nivel Information,
    // asi que el unico freno es este override de Serilog. Si alguien lo borra o lo baja,
    // los secretos acabarian en los logs. No se puede probar con red/clave real, por eso
    // se valida la configuracion real que se despliega.
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json.template")]
    [InlineData("appsettings.Development.json.template")]
    public void HttpClient_Log_Override_Must_Be_Warning_Or_Higher(string fileName)
    {
        // TestSourceLocator (y no un localizador propio) porque el suyo tambien
        // encuentra la carpeta de fuentes cuando la suite se compila con
        // --artifacts-path: con el localizador propio este test no encontraba
        // nada, fallaba por DirectoryNotFoundException y dejaba SIN VALIDAR los
        // tres ficheros de configuracion, que son justo lo que protege.
        var path = TestSourceLocator.Find("AtlasBalance.API", fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        JsonElement level = default;
        var found = doc.RootElement.TryGetProperty("Serilog", out var serilog)
            && serilog.TryGetProperty("MinimumLevel", out var minimum)
            && minimum.TryGetProperty("Override", out var overrides)
            && overrides.TryGetProperty("System.Net.Http.HttpClient", out level);

        found.Should().BeTrue($"{fileName} debe definir Serilog:MinimumLevel:Override:System.Net.Http.HttpClient");
        level.GetString().Should().BeOneOf("Warning", "Error", "Fatal");
    }
}
