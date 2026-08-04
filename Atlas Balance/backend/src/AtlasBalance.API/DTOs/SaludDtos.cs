namespace AtlasBalance.API.DTOs;

public static class EstadoSalud
{
    public const string Sano = "SANO";
    public const string Degradado = "DEGRADADO";
    public const string NoSano = "NO_SANO";
}

public sealed class ComprobacionSalud
{
    public string Nombre { get; set; } = string.Empty;
    public string Estado { get; set; } = EstadoSalud.Sano;
    public string? Detalle { get; set; }
    public double? DuracionMs { get; set; }

    /// <summary>Valor numerico de la comprobacion, si tiene uno (% libre, tamano de pool).</summary>
    public double? Valor { get; set; }
}

public sealed class SaludResponse
{
    public string Estado { get; set; } = EstadoSalud.Sano;
    public DateTime FechaUtc { get; set; }
    public long UptimeSegundos { get; set; }
    public IReadOnlyList<ComprobacionSalud> Comprobaciones { get; set; } = [];
    public long PeticionesUltimos5Min { get; set; }
    public double TasaErrorPorcentaje { get; set; }
    public double LatenciaP50Ms { get; set; }
    public double LatenciaP95Ms { get; set; }
}

// V-02.07: metricas crudas para el panel de admin.
public sealed class MetricasResponse
{
    public DateTime FechaUtc { get; set; }
    public long UptimeSegundos { get; set; }
    public VentanaMetricasResponse Ultimos5Min { get; set; } = new();
    public VentanaMetricasResponse Ultimos60Min { get; set; } = new();

    /// <summary>Ventana de 5 minutos inmediatamente anterior, para comparar.</summary>
    public VentanaMetricasResponse Anterior5Min { get; set; } = new();
}

public sealed class VentanaMetricasResponse
{
    public DateTime DesdeUtc { get; set; }
    public DateTime HastaUtc { get; set; }
    public long Peticiones { get; set; }
    public long Errores4xx { get; set; }
    public long Errores5xx { get; set; }
    public double TasaErrorPorcentaje { get; set; }
    public double LatenciaP50Ms { get; set; }
    public double LatenciaP95Ms { get; set; }
    public double LatenciaMaxMs { get; set; }
}
