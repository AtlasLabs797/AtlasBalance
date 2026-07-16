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
    [System.ComponentModel.DataAnnotations.Required]
    public Guid CuentaId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(5 * 1024 * 1024)]
    public string RawData { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Separador { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public MapeoColumnasRequest Mapeo { get; set; } = new();
}

public sealed class ImportacionConfirmarRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid CuentaId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(5 * 1024 * 1024)]
    public string RawData { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Separador { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public MapeoColumnasRequest Mapeo { get; set; } = new();
    public IReadOnlyList<int>? FilasAImportar { get; set; }
    public Guid? LoteId { get; set; }
}

public sealed class ImportacionPlazoFijoMovimientoRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid CuentaId { get; set; }
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(16)]
    public string TipoMovimiento { get; set; } = "INGRESO";
    public DateOnly Fecha { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0.0001", "9999999999.9999")]
    public decimal Monto { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(512)]
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
    public IReadOnlyList<string> Advertencias { get; set; } = [];
}

public sealed class ImportacionLoteCrearRequest
{
    public Guid CuentaId { get; set; }
    public string RawData { get; set; } = string.Empty;
    public string? Separador { get; set; }
    public MapeoColumnasRequest Mapeo { get; set; } = new();
    public string TipoOrigen { get; set; } = "PEGADO";
    public string? NombreArchivo { get; set; }
    public long? TamanioBytes { get; set; }

    /// <summary>
    /// V-02-05 (HIGH-1): codigo de divisa declarado por el usuario para los importes pegados.
    /// Si no coincide con la divisa de la cuenta, se registra una advertencia en el lote
    /// para que el operador la vea antes de confirmar. Si se omite, se asume la divisa
    /// de la cuenta (no se valida contra el archivo).
    /// </summary>
    public string? DivisaEsperada { get; set; }
}

public sealed class ImportacionLoteConfirmarRequest
{
    public IReadOnlyList<int>? FilasAImportar { get; set; }
    public bool AceptaAdvertencias { get; set; }

    /// <summary>
    /// V-02.06 (HIGH-1, bloqueante): cuando el lote se creo con <c>divisa_esperada</c>
    /// distinta de la divisa de la cuenta, el backend rechaza el guardado con 400
    /// salvo que el cliente envie este flag en <c>true</c>. Defensa frente a
    /// importes erroneos por pegar un archivo de otra divisa sin darse cuenta.
    /// </summary>
    public bool ForceConfirmDivisaMismatch { get; set; }
}

public sealed class ImportacionLoteRevertirRequest
{
    public string? Motivo { get; set; }
}

public sealed class ImportacionLoteResponse
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public string? CuentaNombre { get; set; }
    public Guid UsuarioCreadorId { get; set; }
    public string TipoOrigen { get; set; } = string.Empty;
    public string? NombreArchivo { get; set; }
    public long TamanioBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Separador { get; set; } = string.Empty;
    public string LoteHash { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int FilasTotal { get; set; }
    public int FilasValidas { get; set; }
    public int FilasError { get; set; }
    public int FilasAdvertencia { get; set; }
    public bool AdvertenciasAceptadas { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public Guid? ConfirmadoPorId { get; set; }
    public DateTime? FechaReversion { get; set; }
    public Guid? RevertidoPorId { get; set; }

    /// <summary>
    /// V-02.06 (HIGH-1, bloqueante): true si el lote se creo con
    /// <c>divisa_esperada</c> y no coincide con la divisa de la cuenta.
    /// El frontend debe mostrar UI de confirmacion explicita antes de
    /// enviar <c>force_confirm_divisa_mismatch=true</c>.
    /// </summary>
    public bool DivisaMismatch { get; set; }

    /// <summary>Divisa oficial de la cuenta destino.</summary>
    public string DivisaCuenta { get; set; } = string.Empty;

    /// <summary>Divisa declarada por el usuario al crear el lote (puede ser null).</summary>
    public string? DivisaEsperada { get; set; }
}

public sealed class ImportacionLoteDetalleResponse
{
    public ImportacionLoteResponse Lote { get; set; } = new();
    public MapeoColumnasRequest Mapeo { get; set; } = new();
    public ImportacionValidarResponse Validacion { get; set; } = new();
}

public sealed class ImportacionLoteFilaResponse
{
    public Guid Id { get; set; }
    public Guid LoteId { get; set; }
    public int Indice { get; set; }
    public bool Valida { get; set; }
    public bool SeleccionadaDefault { get; set; }
    public string Estado { get; set; } = string.Empty;
    public Dictionary<string, string?> Datos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Errores { get; set; } = [];
    public IReadOnlyList<string> Advertencias { get; set; } = [];
    public string? Fingerprint { get; set; }
}

public sealed class CuentaImportacionContextoResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string TitularNombre { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public Guid? PaisId { get; set; }
    public bool EsEfectivo { get; set; }
    public string TipoCuenta { get; set; } = string.Empty;
    public Guid? FormatoId { get; set; }
    public MapeoColumnasRequest? FormatoPredefinido { get; set; }
}

public sealed class ImportacionContextoResponse
{
    public IReadOnlyList<CuentaImportacionContextoResponse> Cuentas { get; set; } = [];
}
