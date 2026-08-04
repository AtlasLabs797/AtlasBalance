using System.Collections.Concurrent;

namespace AtlasBalance.API.Services;

/// <summary>Foto de una ventana de metricas.</summary>
public sealed record VentanaMetricas(
    DateTime DesdeUtc,
    DateTime HastaUtc,
    long Peticiones,
    long Errores4xx,
    long Errores5xx,
    double LatenciaP50Ms,
    double LatenciaP95Ms,
    double LatenciaMaxMs)
{
    public double TasaErrorPorcentaje => Peticiones == 0
        ? 0
        : (Errores4xx + Errores5xx) * 100.0 / Peticiones;

    public double TasaError5xxPorcentaje => Peticiones == 0
        ? 0
        : Errores5xx * 100.0 / Peticiones;
}

public interface IRequestMetrics
{
    void Registrar(int statusCode, double duracionMs);

    /// <summary>Metricas agregadas de los ultimos <paramref name="minutos"/> minutos.</summary>
    VentanaMetricas Ventana(int minutos);

    /// <summary>
    /// Ventana anterior a la actual, del mismo tamano. Es la linea base contra
    /// la que HealthAlertJob compara para detectar degradacion.
    /// </summary>
    VentanaMetricas VentanaAnterior(int minutos);

    DateTime ArranqueUtc { get; }
}

/// <summary>
/// V-02.07: contadores en memoria de tasa de error y latencia.
///
/// En memoria y no en BD a proposito: son datos de altisima frecuencia y valor
/// efimero (detectar una degradacion en curso). Meterlos en PostgreSQL anadiria
/// una escritura por peticion para responder preguntas que solo importan
/// durante minutos. El coste es que se pierden al reiniciar, lo cual es
/// aceptable: lo que hay que conservar ya esta en AUDITORIAS y en los logs.
///
/// La latencia se agrega en un histograma de cubos fijos en vez de guardar las
/// muestras: memoria acotada y constante pase lo que pase con el trafico. Los
/// percentiles salen interpolados del histograma, asi que son aproximados; para
/// decidir "esto va mas lento de lo normal" sobra.
/// </summary>
public sealed class RequestMetrics : IRequestMetrics
{
    /// <summary>Minutos de historico. 120 cubos de un minuto = 2 horas.</summary>
    private const int MinutosRetenidos = 120;

    /// <summary>
    /// Limites superiores de los cubos de latencia, en ms. El ultimo cubo es
    /// todo lo que pase de 10 s.
    /// </summary>
    private static readonly double[] LimitesMs =
        { 5, 10, 25, 50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000, double.PositiveInfinity };

    private readonly ConcurrentDictionary<long, Cubo> _cubos = new();
    private readonly IClock _clock;

    public RequestMetrics(IClock clock)
    {
        _clock = clock;
        ArranqueUtc = clock.UtcNow;
    }

    public DateTime ArranqueUtc { get; }

    public void Registrar(int statusCode, double duracionMs)
    {
        var minuto = AMinuto(_clock.UtcNow);
        var cubo = _cubos.GetOrAdd(minuto, _ => new Cubo());
        cubo.Registrar(statusCode, duracionMs);

        // Poda perezosa: sin un job de limpieza, el diccionario crece con cada
        // minuto de uptime. Se hace aqui porque es O(cubos) y solo cuando cambia
        // de minuto no hay forma barata de saberlo, asi que se acota por tamano.
        if (_cubos.Count > MinutosRetenidos * 2)
        {
            var corte = minuto - MinutosRetenidos;
            foreach (var clave in _cubos.Keys)
            {
                if (clave < corte)
                {
                    _cubos.TryRemove(clave, out _);
                }
            }
        }
    }

    public VentanaMetricas Ventana(int minutos) => Agregar(minutos, desplazamiento: 0);

    public VentanaMetricas VentanaAnterior(int minutos) => Agregar(minutos, desplazamiento: minutos);

    private VentanaMetricas Agregar(int minutos, int desplazamiento)
    {
        minutos = Math.Clamp(minutos, 1, MinutosRetenidos);
        var ahora = _clock.UtcNow;
        var minutoActual = AMinuto(ahora);
        var fin = minutoActual - desplazamiento;
        var inicio = fin - minutos + 1;

        long peticiones = 0, e4xx = 0, e5xx = 0;
        var histograma = new long[LimitesMs.Length];
        double maxMs = 0;

        for (var minuto = inicio; minuto <= fin; minuto++)
        {
            if (!_cubos.TryGetValue(minuto, out var cubo))
            {
                continue;
            }

            cubo.Volcar(ref peticiones, ref e4xx, ref e5xx, histograma, ref maxMs);
        }

        return new VentanaMetricas(
            DesdeUtc: DateTime.UnixEpoch.AddMinutes(inicio),
            HastaUtc: DateTime.UnixEpoch.AddMinutes(fin + 1),
            Peticiones: peticiones,
            Errores4xx: e4xx,
            Errores5xx: e5xx,
            LatenciaP50Ms: Percentil(histograma, peticiones, 0.50),
            LatenciaP95Ms: Percentil(histograma, peticiones, 0.95),
            LatenciaMaxMs: maxMs);
    }

    private static long AMinuto(DateTime utc) => (long)(utc - DateTime.UnixEpoch).TotalMinutes;

    /// <summary>
    /// Percentil interpolado sobre el histograma. Devuelve el limite superior
    /// del cubo donde cae, que es una cota superior honesta: nunca infravalora
    /// la latencia real, que es el error seguro para una alerta.
    /// </summary>
    private static double Percentil(long[] histograma, long total, double percentil)
    {
        if (total == 0)
        {
            return 0;
        }

        var objetivo = (long)Math.Ceiling(total * percentil);
        long acumulado = 0;
        for (var i = 0; i < histograma.Length; i++)
        {
            acumulado += histograma[i];
            if (acumulado >= objetivo)
            {
                return double.IsPositiveInfinity(LimitesMs[i]) ? LimitesMs[^2] : LimitesMs[i];
            }
        }

        return LimitesMs[^2];
    }

    private sealed class Cubo
    {
        private readonly long[] _histograma = new long[LimitesMs.Length];
        private readonly object _lock = new();
        private long _peticiones;
        private long _e4xx;
        private long _e5xx;
        private double _maxMs;

        public void Registrar(int statusCode, double duracionMs)
        {
            var indice = 0;
            while (indice < LimitesMs.Length - 1 && duracionMs > LimitesMs[indice])
            {
                indice++;
            }

            lock (_lock)
            {
                _peticiones++;
                if (statusCode is >= 400 and < 500)
                {
                    _e4xx++;
                }
                else if (statusCode >= 500)
                {
                    _e5xx++;
                }

                _histograma[indice]++;
                if (duracionMs > _maxMs)
                {
                    _maxMs = duracionMs;
                }
            }
        }

        public void Volcar(ref long peticiones, ref long e4xx, ref long e5xx, long[] destino, ref double maxMs)
        {
            lock (_lock)
            {
                peticiones += _peticiones;
                e4xx += _e4xx;
                e5xx += _e5xx;
                for (var i = 0; i < destino.Length; i++)
                {
                    destino[i] += _histograma[i];
                }

                if (_maxMs > maxMs)
                {
                    maxMs = _maxMs;
                }
            }
        }
    }
}
