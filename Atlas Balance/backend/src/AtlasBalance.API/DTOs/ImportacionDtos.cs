namespace AtlasBalance.API.DTOs;

public sealed class MapeoColumnaExtraRequest
{
    public string Nombre { get; set; } = string.Empty;
    public int Indice { get; set; }
    public string? Etiqueta { get; set; }

    // Clave real en EXTRACTOS_COLUMNAS_EXTRA.nombre_columna.
    // Si hay etiqueta, se normaliza; si no, usa el nombre original.
    public string ClaveAlmacenamiento =>
        string.IsNullOrWhiteSpace(Etiqueta)
            ? Nombre.Trim()
            : Etiqueta.Trim().ToLowerInvariant();
}

public sealed class MapeoColumnasRequest
{
    public string? TipoMonto { get; set; }
    public int Fecha { get; set; }
    public int Concepto { get; set; }
    public int? Monto { get; set; }
    public int? Ingreso { get; set; }
    public int? Egreso { get; set; }
    public int Saldo { get; set; }
    public IReadOnlyList<MapeoColumnaExtraRequest> ColumnasExtra { get; set; } = [];
}

public sealed class ImportacionValidarRequest
{
    public Guid CuentaId { get; set; }
    public string RawData { get; set; } = string.Empty;
    public string? Separador { get; set; }
    public MapeoColumnasRequest Mapeo { get; set; } = new();
}

public sealed class ImportacionConfirmarRequest
{
    public Guid CuentaId { get; set; }
    public string RawData { get; set; } = string.Empty;
    public string? Separador { get; set; }
    public MapeoColumnasRequest Mapeo { get; set; } = new();
    public IReadOnlyList<int>? FilasAImportar { get; set; }
}

public sealed class ImportacionPlazoFijoMovimientoRequest
{
    public Guid CuentaId { get; set; }
    public string TipoMovimiento { get; set; } = "INGRESO";
    public DateOnly Fecha { get; set; }
    public decimal Monto { get; set; }
    public string? Concepto { get; set; }
}

public sealed class ImportacionPlazoFijoMovimientoResponse
{
    public Guid ExtractoId { get; set; }
    public int FilaNumero { get; set; }
    public decimal Monto { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal SaldoActual { get; set; }
}

public sealed class FilaValidacionResponse
{
    public int Indice { get; set; }
    public bool Valida { get; set; }
    public Dictionary<string, string?> Datos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Errores { get; set; } = [];
    public IReadOnlyList<string> Advertencias { get; set; } = [];
}

public sealed class ErrorFilaResponse
{
    public int FilaIndice { get; set; }
    public IReadOnlyList<string> Mensajes { get; set; } = [];
}

public sealed class ImportacionValidarResponse
{
    public int FilasOk { get; set; }
    public int FilasError { get; set; }
    public string SeparadorDetectado { get; set; } = string.Empty;
    public IReadOnlyList<FilaValidacionResponse> Filas { get; set; } = [];
    public IReadOnlyList<ErrorFilaResponse> Errores { get; set; } = [];
}

public sealed class ImportacionConfirmarResponse
{
    public int FilasProcesadas { get; set; }
    public int FilasImportadas { get; set; }
    public int FilasDuplicadas { get; set; }
    public int FilasConError { get; set; }
    public IReadOnlyList<ErrorFilaResponse> Errores { get; set; } = [];
}

public sealed class CuentaImportacionContextoResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string TitularNombre { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public bool EsEfectivo { get; set; }
    public string TipoCuenta { get; set; } = string.Empty;
    public Guid? FormatoId { get; set; }
    public MapeoColumnasRequest? FormatoPredefinido { get; set; }
}

public sealed class ImportacionContextoResponse
{
    public IReadOnlyList<CuentaImportacionContextoResponse> Cuentas { get; set; } = [];
}
