using System.Text;
using System.Text.RegularExpressions;

namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 9): ayuda documental sobre Atlas Balance.
//
// Fuente canonica: Documentacion/DOCUMENTACION_USUARIO.md (cargado
// en memoria al construir el servicio). El alcance se limita a
// este documento y a las definiciones de estados/permisos que ya
// estan en el codigo (no mezcla datos financieros con docs).
//
// Politica (Fase 9 explicita): si la pregunta es claramente de
// "ayuda sobre Atlas Balance" (no financiera), la capa de
// planificacion la redirige a este servicio. Si la pregunta es
// ambigua, gana el plan financiero (mejor un resultado parcial
// que inventar documentacion).
//
// El resultado distingue tres estados via el enum HelpResultado:
//   - Encontrado: el manual tiene secciones relevantes.
//   - NoEncontrado: el manual esta cargado pero la busqueda no
//     encontro coincidencias relevantes. ES UN RECHAZO EXPLICITO:
//     el sistema no inventa contenido.
//   - DocumentoNoCargado: el manual no esta disponible en este
//     despliegue (ruta invalida o archivo ausente).

public enum HelpResultado
{
    Encontrado,
    NoEncontrado,
    DocumentoNoCargado
}

public sealed record DocSection(
    string Titulo,
    string Contenido,
    int LineaInicio);

public sealed record HelpSearchResult
{
    public HelpResultado Resultado { get; init; } = HelpResultado.NoEncontrado;
    public IReadOnlyList<DocSection> Secciones { get; init; } = Array.Empty<DocSection>();
    public string? Mensaje { get; init; }
    public bool Encontrado => Resultado == HelpResultado.Encontrado;
}

public interface IDocumentationHelpService
{
    HelpSearchResult Buscar(string pregunta, int maximo);
}

public sealed class DocumentationHelpService : IDocumentationHelpService
{
    public const int DefaultMaxSecciones = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    private readonly IReadOnlyList<DocSection> _secciones;
    private readonly string _rawText;

    public DocumentationHelpService(string documentacionUsuarioPath)
    {
        if (string.IsNullOrWhiteSpace(documentacionUsuarioPath))
        {
            _secciones = Array.Empty<DocSection>();
            _rawText = string.Empty;
            return;
        }
        if (!File.Exists(documentacionUsuarioPath))
        {
            _secciones = Array.Empty<DocSection>();
            _rawText = string.Empty;
            return;
        }
        _rawText = File.ReadAllText(documentacionUsuarioPath);
        _secciones = Parsear(_rawText);
    }

    public HelpSearchResult Buscar(string pregunta, int maximo)
    {
        if (_secciones.Count == 0)
        {
            return new HelpSearchResult
            {
                Resultado = HelpResultado.DocumentoNoCargado,
                Mensaje = "La documentacion canonica no esta disponible en este despliegue."
            };
        }
        if (string.IsNullOrWhiteSpace(pregunta))
        {
            return new HelpSearchResult
            {
                Resultado = HelpResultado.NoEncontrado,
                Mensaje = "Pregunta vacia."
            };
        }

        var tokens = Tokenizar(pregunta);
        if (tokens.Count == 0)
        {
            return new HelpSearchResult
            {
                Resultado = HelpResultado.NoEncontrado,
                Mensaje = "No se pudo extraer terminos relevantes de la pregunta."
            };
        }

        var ranked = _secciones
            .Select(s => (Seccion: s, Score: Puntuar(s, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maximo)
            .Select(x => x.Seccion)
            .ToList();

        if (ranked.Count == 0)
        {
            // RECHAZO EXPLICITO (Fase 9). El sistema no inventa
            // documentacion: devuelve un NoEncontrado claro con
            // sugerencias de reformulacion.
            return new HelpSearchResult
            {
                Resultado = HelpResultado.NoEncontrado,
                Mensaje = "La documentacion canonica de Atlas Balance no contiene una respuesta a esta pregunta. " +
                          "Atlas Balance IA no inventa funcionalidades: reformula con terminos del manual " +
                          "(ejemplos: extracto, comision, conciliacion, saldo, titular, alerta) o consulta " +
                          "datos financieros con una pregunta concreta."
            };
        }

        return new HelpSearchResult
        {
            Resultado = HelpResultado.Encontrado,
            Secciones = ranked
        };
    }

    public static double Puntuar(DocSection seccion, IReadOnlyList<string> tokens)
    {
        var texto = (seccion.Titulo + " " + seccion.Contenido).ToLowerInvariant();
        var score = 0.0;
        foreach (var t in tokens)
        {
            // Coincidencias en el titulo valen 3x.
            if (seccion.Titulo.ToLowerInvariant().Contains(t, StringComparison.Ordinal)) score += 3;
            // Coincidencias en el cuerpo valen 1x.
            if (texto.Contains(t, StringComparison.Ordinal)) score += 1;
        }
        return score;
    }

    public static IReadOnlyList<string> Tokenizar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Array.Empty<string>();
        var tokens = Regex.Matches(
            texto.Normalize(NormalizationForm.FormD),
            @"[a-z]{3,}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        return tokens
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    public static IReadOnlyList<DocSection> Parsear(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Array.Empty<DocSection>();
        var lineas = texto.Replace("\r\n", "\n").Split('\n');
        var secciones = new List<DocSection>();
        string? tituloActual = null;
        var contenido = new StringBuilder();
        var lineaInicio = 0;
        var nivel2 = new Regex(@"^##\s+(.+?)\s*$");

        for (int i = 0; i < lineas.Length; i++)
        {
            var match = nivel2.Match(lineas[i]);
            if (match.Success)
            {
                if (tituloActual is not null)
                {
                    secciones.Add(new DocSection(tituloActual, contenido.ToString().Trim(), lineaInicio));
                }
                tituloActual = match.Groups[1].Value;
                contenido.Clear();
                lineaInicio = i;
            }
            else if (tituloActual is not null)
            {
                contenido.AppendLine(lineas[i]);
            }
        }
        if (tituloActual is not null)
        {
            secciones.Add(new DocSection(tituloActual, contenido.ToString().Trim(), lineaInicio));
        }
        return secciones;
    }
}
