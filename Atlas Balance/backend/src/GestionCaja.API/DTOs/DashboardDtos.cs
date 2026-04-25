namespace GestionCaja.API.DTOs;

public sealed class DashboardPrincipalResponse
{
    public string DivisaPrincipal { get; set; } = "EUR";
    public IReadOnlyDictionary<string, decimal> SaldosPorDivisa { get; set; } = new Dictionary<string, decimal>();
    public decimal IngresosMes { get; set; }
    public decimal EgresosMes { get; set; }
    public decimal TotalConvertido { get; set; }
    public DashboardPlazosFijosResumenResponse PlazosFijos { get; set; } = new();
    public IReadOnlyList<DashboardSaldoTitularResponse> SaldosPorTitular { get; set; } = [];
    public DashboardChartColorsResponse ChartColors { get; set; } = new();
}

public sealed class DashboardPlazosFijosResumenResponse
{
    public decimal MontoTotalConvertido { get; set; }
    public decimal InteresesPrevistosConvertidos { get; set; }
    public int? DiasHastaProximoVencimiento { get; set; }
    public DateOnly? ProximoVencimiento { get; set; }
    public int TotalCuentas { get; set; }
}

public sealed class DashboardTitularResponse
{
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public string DivisaPrincipal { get; set; } = "EUR";
    public IReadOnlyDictionary<string, decimal> SaldosPorDivisa { get; set; } = new Dictionary<string, decimal>();
    public decimal IngresosMes { get; set; }
    public decimal EgresosMes { get; set; }
    public decimal TotalConvertido { get; set; }
    public IReadOnlyList<DashboardSaldoCuentaResponse> SaldosPorCuenta { get; set; } = [];
    public DashboardChartColorsResponse ChartColors { get; set; } = new();
}

public sealed class DashboardSaldoTitularResponse
{
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
    public string TipoTitular { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, decimal> SaldosPorDivisa { get; set; } = new Dictionary<string, decimal>();
    public decimal TotalConvertido { get; set; }
    public decimal SaldoInmovilizadoConvertido { get; set; }
    public decimal SaldoDisponibleConvertido { get; set; }
}

public sealed class DashboardSaldoCuentaResponse
{
    public Guid CuentaId { get; set; }
    public string CuentaNombre { get; set; } = string.Empty;
    public string? BancoNombre { get; set; }
    public string Divisa { get; set; } = string.Empty;
    public bool EsEfectivo { get; set; }
    public string TipoCuenta { get; set; } = string.Empty;
    public decimal SaldoActual { get; set; }
    public decimal SaldoConvertido { get; set; }
}

public sealed class DashboardSaldosDivisaResponse
{
    public string DivisaPrincipal { get; set; } = "EUR";
    public IReadOnlyList<DashboardSaldoDivisaResponse> Divisas { get; set; } = [];
    public decimal TotalConvertido { get; set; }
}

public sealed class DashboardSaldoDivisaResponse
{
    public string Divisa { get; set; } = string.Empty;
    public decimal Saldo { get; set; }
    public decimal SaldoConvertido { get; set; }
    public decimal SaldoDisponible { get; set; }
    public decimal SaldoInmovilizado { get; set; }
    public decimal SaldoTotal { get; set; }
    public decimal SaldoTotalConvertido { get; set; }
}

public sealed class DashboardEvolucionResponse
{
    public string Periodo { get; set; } = "1m";
    public string Granularidad { get; set; } = "diaria";
    public string DivisaPrincipal { get; set; } = "EUR";
    public IReadOnlyList<DashboardPuntoEvolucionResponse> Puntos { get; set; } = [];
}

public sealed class DashboardPuntoEvolucionResponse
{
    public DateOnly Fecha { get; set; }
    public decimal Ingresos { get; set; }
    public decimal Egresos { get; set; }
    public decimal Neto { get; set; }
    public decimal Saldo { get; set; }
}

public sealed class DashboardChartColorsResponse
{
    public string Ingresos { get; set; } = "#43B430";
    public string Egresos { get; set; } = "#FF4757";
    public string Saldo { get; set; } = "#7B7B7B";
}
