using FluentAssertions;
using AtlasBalance.API.Services.IaPlanner;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 9): tests del servicio de ayuda documental.
// Verifica el parseo por secciones, la tokenizacion, el ranking y
// los casos limite (pregunta vacia, doc no disponible, sin
// coincidencias).
public class DocumentationHelpServiceTests
{
    private const string DocumentoCanary = """
        # Doc de prueba

        ## Importacion de extractos

        Para importar extractos ve a Extractos > Importar. Necesitas un
        formato configurado. Si no tienes, ve a Formatos y crea uno.

        ## Comisiones pendientes

        Las comisiones pendientes se revisan desde el menu Revision.
        Marca cada fila con la banderola para arrastrarla al estado
        REVISADO.

        ## Conciliacion

        Las conciliaciones sugeridas aparecen en Conciliacion. Acepta
        o rechaza segun corresponda.
        """;

    [Fact]
    public void Parsear_Divide_Por_Encabezados_Nivel_2()
    {
        var secciones = DocumentationHelpService.Parsear(DocumentoCanary);
        secciones.Should().HaveCount(3);
        secciones[0].Titulo.Should().Be("Importacion de extractos");
        secciones[1].Titulo.Should().Be("Comisiones pendientes");
        secciones[2].Titulo.Should().Be("Conciliacion");
    }

    [Fact]
    public void Buscar_Palabra_Clave_Devuelve_Seccion_Relevante()
    {
        var sut = Construir();
        var resultado = sut.Buscar("Como importo un extracto?", 3);

        resultado.Encontrado.Should().BeTrue();
        resultado.Secciones[0].Titulo.Should().Be("Importacion de extractos");
    }

    [Fact]
    public void Buscar_Termino_Con_Concurrencia_Devuelve_Mejor_Score()
    {
        var sut = Construir();
        // "conciliacion" matchea exacto con el titulo, mientras que
        // "comision" matchea el titulo de Comisiones. El titulo
        // pesa 3x, asi que Conciliacion debe ganar porque sus dos
        // tokens (conciliacion + movimientos) matchean cuerpo,
        // mientras Comisiones solo tiene comision.
        var resultado = sut.Buscar("conciliacion movimientos conciliaciones", 3);

        resultado.Encontrado.Should().BeTrue();
        resultado.Secciones[0].Titulo.Should().Be("Conciliacion");
    }

    [Fact]
    public void Buscar_Pregunta_Vacia_Devuelve_Rechazo_Explicito()
    {
        var sut = Construir();
        var resultado = sut.Buscar(string.Empty, 3);

        resultado.Encontrado.Should().BeFalse();
        resultado.Resultado.Should().Be(HelpResultado.NoEncontrado);
        resultado.Mensaje.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Buscar_Sin_Coincidencias_Devuelve_Rechazo_Explicito_Sin_Inventar()
    {
        var sut = Construir();
        var resultado = sut.Buscar("Que es la fusion fria de neutrones?", 3);

        resultado.Encontrado.Should().BeFalse();
        resultado.Resultado.Should().Be(HelpResultado.NoEncontrado);
        resultado.Mensaje.Should().Contain("no inventa");
        resultado.Mensaje.Should().Contain("reformula");
        resultado.Secciones.Should().BeEmpty("rechazo explicito no devuelve secciones");
    }

    [Fact]
    public void Buscar_Sin_Documento_Cargado_Devuelve_Estado_Despliegue()
    {
        var sut = new DocumentationHelpService("C:/ruta/inexistente.md");
        var resultado = sut.Buscar("cualquier cosa", 3);

        resultado.Encontrado.Should().BeFalse();
        resultado.Resultado.Should().Be(HelpResultado.DocumentoNoCargado);
        resultado.Mensaje.Should().Contain("despliegue");
    }

    [Fact]
    public void Buscar_Respeta_Maximo_De_Secciones()
    {
        var sut = Construir();
        var resultado = sut.Buscar("documento extracto comision conciliacion formato", 2);

        resultado.Secciones.Count.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public void Tokenizar_Elimina_Palabras_Cortas_Y_Normaliza()
    {
        var tokens = DocumentationHelpService.Tokenizar("Como importo un extracto?");

        tokens.Should().Contain("como");
        tokens.Should().Contain("importo");
        tokens.Should().Contain("extracto");
        // "un" tiene 2 letras, se filtra
        tokens.Should().NotContain("un");
    }

    [Fact]
    public void Tokenizar_Texto_Vacio_Devuelve_Vacio()
    {
        DocumentationHelpService.Tokenizar("").Should().BeEmpty();
        DocumentationHelpService.Tokenizar(null!).Should().BeEmpty();
    }

    [Fact]
    public void Puntuar_Titulo_Pesa_Mas_Que_Cuerpo()
    {
        var seccion = new DocSection("extracto importante", "...cuerpo...", 0);
        var tokens = new[] { "extracto" };

        var score = DocumentationHelpService.Puntuar(seccion, tokens);

        score.Should().Be(4); // 3 del titulo + 1 del cuerpo
    }

    [Fact]
    public void Documento_Real_Esta_Disponible()
    {
        // El documento canonico vive en Documentacion/.
        // Buscamos candidatos relativos al directorio de trabajo.
        string[] candidatos = new[]
        {
            @"..\..\..\..\..\Documentacion\DOCUMENTACION_USUARIO.md",
            @"..\..\..\..\Documentacion\DOCUMENTACION_USUARIO.md",
            @"Documentacion\DOCUMENTACION_USUARIO.md"
        };
        string? ruta = null;
        foreach (var c in candidatos)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) { ruta = full; break; }
        }
        if (ruta is null)
        {
            // Skip si no estamos en el arbol del repo (CI puede
            // trabajar con el repo clonado en otra ruta).
            return;
        }
        var sut = new DocumentationHelpService(ruta);

        var resultado = sut.Buscar("extracto", 3);

        resultado.Encontrado.Should().BeTrue();
        resultado.Secciones[0].Titulo.Should().NotBeNullOrEmpty();
    }

    private static DocumentationHelpService Construir()
    {
        var temp = Path.GetTempFileName();
        File.WriteAllText(temp, DocumentoCanary);
        return new DocumentationHelpService(temp);
    }
}
