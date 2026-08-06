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
// Politica: si la pregunta del usuario es claramente de "ayuda
// sobre Atlas Balance" (no financiera), la capa de planificacion
// la redirige a este servicio. Si la pregunta es ambigua, gana
// el plan financiero (mejor un resultado parcial que inventar
// documentacion).

public sealed record DocSection(
    string Titulo,
    string Contenido,
    int LineaInicio);

public sealed record HelpSearchResult
{
    public IReadOnlyList<DocSection> Secciones { get; init; } = Array.Empty<DocSection>();
    public string? Advertencia { get; init; }
    public bool Encontrado => Secciones.Count > 0;
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
        if (string.IsNullOrWhiteSpace(pregunta) || _secciones.Count == 0)
        {
            return new HelpSearchResult
            {
                Advertencia = _secciones.Count == 0
                    ? "La documentacion canonica no esta disponible en este despliegue."
                    : "Pregunta vacia."
            };
        }

        var tokens = Tokenizar(pregunta);
        if (tokens.Count == 0)
        {
            return new HelpSearchResult { Advertencia = "No se pudo extraer terminos de la pregunta." };
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
            return new HelpSearchResult
            {
                Advertencia = "La documentacion canonica no contiene una respuesta a esa pregunta. Reformula con terminos del manual (extracto, comision, conciliacion...)."
            };
        }

        return new HelpSearchResult { Secciones = ranked };
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
