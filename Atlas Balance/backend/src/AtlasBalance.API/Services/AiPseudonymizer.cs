using System.Text.RegularExpressions;

namespace AtlasBalance.API.Services;

// Seudonimizacion reversible de nombres (titulares, cuentas, terceros) antes de enviar
// datos al proveedor de IA externo. El proveedor solo ve placeholders tipo [TITULAR_1];
// Atlas Balance revierte la respuesta a nombres reales antes de mostrarla al usuario.
public sealed class AiPseudonymMap
{
    private const int MinNombreLength = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly List<(string Nombre, string Placeholder)> _entries;
    private readonly Dictionary<string, string> _placeholderPorNombre;
    private readonly Regex? _matcher;

    public AiPseudonymMap(IEnumerable<(string Nombre, string Tipo)> entidades)
    {
        // Tipo ganador por nombre (case-insensitive), prioridad TITULAR > CUENTA > TERCERO.
        var tipoPorNombre = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nombreCanonico = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (nombreRaw, tipo) in entidades)
        {
            if (string.IsNullOrWhiteSpace(nombreRaw))
            {
                continue;
            }

            var nombre = nombreRaw.Trim();
            if (nombre.Length < MinNombreLength)
            {
                continue;
            }

            if (!nombreCanonico.ContainsKey(nombre))
            {
                nombreCanonico[nombre] = nombre;
            }

            if (!tipoPorNombre.TryGetValue(nombre, out var tipoActual) || TipoPrioridad(tipo) < TipoPrioridad(tipoActual))
            {
                tipoPorNombre[nombre] = tipo;
            }
        }

        _entries = [];
        foreach (var tipo in new[] { "TITULAR", "CUENTA", "TERCERO" })
        {
            var nombres = tipoPorNombre
                .Where(kv => string.Equals(kv.Value, tipo, StringComparison.Ordinal))
                .Select(kv => nombreCanonico[kv.Key])
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < nombres.Count; i++)
            {
                _entries.Add((nombres[i], $"[{tipo}_{i + 1}]"));
            }
        }

        _placeholderPorNombre = _entries.ToDictionary(x => x.Nombre, x => x.Placeholder, StringComparer.OrdinalIgnoreCase);

        // Una sola alternancia y una sola pasada. Sustituir nombre a nombre en pasadas
        // sucesivas corrompe los placeholders ya insertados cuando una entidad se llama
        // igual que una etiqueta (ej. una cuenta llamada "Cuenta" reescribe [CUENTA_1]).
        // Las alternativas van por longitud descendente: la alternancia de .NET es
        // leftmost-first, asi que "Acme Solutions SL" gana sobre "Acme".
        if (_entries.Count > 0)
        {
            var alternativas = string.Join(
                "|",
                _entries.OrderByDescending(x => x.Nombre.Length).Select(x => Regex.Escape(x.Nombre)));
            _matcher = new Regex(
                $"(?<![\\p{{L}}\\p{{N}}])(?:{alternativas})(?![\\p{{L}}\\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
    }

    public int Count => _entries.Count;

    // Lanza RegexMatchTimeoutException si agota el timeout. Es deliberado: esto es un
    // control de privacidad, y fallar es preferible a enviar nombres reales al proveedor.
    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || _matcher is null)
        {
            return text;
        }

        return _matcher.Replace(
            text,
            match => _placeholderPorNombre.TryGetValue(match.Value, out var placeholder)
                ? placeholder
                : match.Value);
    }

    public string Reverse(string text)
    {
        if (string.IsNullOrEmpty(text) || _entries.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var (nombre, placeholder) in _entries)
        {
            result = result.Replace(placeholder, nombre, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static int TipoPrioridad(string tipo) => tipo switch
    {
        "TITULAR" => 0,
        "CUENTA" => 1,
        _ => 2
    };
}
