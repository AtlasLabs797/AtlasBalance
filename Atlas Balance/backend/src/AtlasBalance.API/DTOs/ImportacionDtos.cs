namespace AtlasBalance.API.DTOs;

public sealed class MapeoColumnaExtraRequest
{
    // V-02.07: [Required] tiene sentido aqui porque Nombre es un string no-nullable:
    // un "nombre": null explicito lo pisa (System.Text.Json ignora la anotacion de
    // referencia no-nullable) y ClaveAlmacenamiento hace Nombre.Trim() sin comprobar
    // null, lo que tiraria un 500. MaxLength(128) deja margen sobre el limite real
    // de negocio (MaxExtraColumnNameLength = 80 en ImportacionService), que sigue
    // aplicando y devuelve su propio mensaje para 81-128 caracteres.
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(128)]
    public string Nombre { get; set; } = string.Empty;
    public int Indice { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(128)]
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
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? TipoMonto { get; set; }
    public int Fecha { get; set; }
    public int Concepto { get; set; }
    public int? Monto { get; set; }
    public int? Ingreso { get; set; }
    public int? Egreso { get; set; }
    public int Saldo { get; set; }
    // V-02.07: 100 es un techo de guardarraiz por encima del limite real de negocio
    // (MaxExtraColumns = 64 en ImportacionService, que sigue aplicando y devuelve su
    // propio mensaje). MaxLength sobre IReadOnlyList<T> resuelve Count por reflexion
    // (no implementa ICollection no generico); confirmado con DtoValidationTests.
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public IReadOnlyList<MapeoColumnaExtraRequest> ColumnasExtra { get; set; } = [];
}

public sealed class ImportacionValidarRequest
{
    // V-02.07: nullable a proposito. [Required] sobre un Guid no-nullable nunca
    // falla, porque un cuenta_id ausente se deserializa a Guid.Empty y no a null:
    // el atributo prometia una validacion que no existia y el campo ausente
    // acababa dando un 404 "Cuenta no encontrada" en vez de un 400.
    [System.ComponentModel.DataAnnotations.Required]
    public Guid? CuentaId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(5 * 1024 * 1024)]
    public string RawData { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Separador { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public MapeoColumnasRequest Mapeo { get; set; } = new();
}

public sealed class ImportacionConfirmarRequest
{
    // V-02.07: nullable a proposito. [Required] sobre un Guid no-nullable nunca
    // falla, porque un cuenta_id ausente se deserializa a Guid.Empty y no a null:
    // el atributo prometia una validacion que no existia y el campo ausente
    // acababa dando un 404 "Cuenta no encontrada" en vez de un 400.
    [System.ComponentModel.DataAnnotations.Required]
    public Guid? CuentaId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(5 * 1024 * 1024)]
    public string RawData { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Separador { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public MapeoColumnasRequest Mapeo { get; set; } = new();
    // V-02.07: nunca tuvo tope. El body de RawData ya esta limitado a 5 MB, asi que
    // 200000 filas es una cota comodamente por encima de cualquier CSV real, pero
    // acota la asignacion frente a un array artificialmente enorme.
    [System.ComponentModel.DataAnnotations.MaxLength(200000)]
    public IReadOnlyList<int>? FilasAImportar { get; set; }
    public Guid? LoteId { get; set; }
}

public sealed class ImportacionPlazoFijoMovimientoRequest
{
    // V-02.07: nullable a proposito. [Required] sobre un Guid no-nullable nunca
    // falla, porque un cuenta_id ausente se deserializa a Guid.Empty y no a null:
    // el atributo prometia una validacion que no existia y el campo ausente
    // acababa dando un 404 "Cuenta no encontrada" en vez de un 400.
    [System.ComponentModel.DataAnnotations.Required]
    public Guid? CuentaId { get; set; }
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(16)]
    public string TipoMovimiento { get; set; } = "INGRESO";
    public DateOnly Fecha { get; set; }
    // V-02.07: faltaba ParseLimitsInInvariantCulture. Sin el, RangeAttribute
    // parsea sus limites con la cultura del proceso, y en es-ES (la del servidor)
    // el separador decimal es la coma: DecimalConverter no traga "0.0001" y lanza
    // FormatException desde SetupConversion(). No es que el rango quedara mal, es
    // que la validacion revienta y el endpoint contesta 500 en cada peticion.
    // DtoValidationTests lo fija forzando es-ES.
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0.0001", "9999999999.9999", ParseLimitsInInvariantCulture = true)]
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
    // V-02.07: sus dos hermanos (ImportacionValidarRequest e
    // ImportacionConfirmarRequest) ya capaban Separador a 8 y esta clase se habia
    // quedado sin el. Se iguala.
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? Separador { get; set; }
    public MapeoColumnasRequest Mapeo { get; set; } = new();
    public string TipoOrigen { get; set; } = "PEGADO";
    // V-02.07: NormalizeOptionalText (ImportacionService) solo hace Trim, no trunca.
    // La columna NombreArchivo esta a HasMaxLength(260) en AppDbContext; sin este
    // limite un nombre de archivo mas largo revienta SaveChangesAsync con un 500
    // en vez de devolver 400.
    [System.ComponentModel.DataAnnotations.MaxLength(260)]
    public string? NombreArchivo { get; set; }
    public long? TamanioBytes { get; set; }

    /// <summary>
    /// V-02-05 (HIGH-1): codigo de divisa declarado por el usuario para los importes pegados.
    /// Si no coincide con la divisa de la cuenta, se registra una advertencia en el lote
    /// para que el operador la vea antes de confirmar. Si se omite, se asume la divisa
    /// de la cuenta (no se valida contra el archivo).
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(8)]
    public string? DivisaEsperada { get; set; }
}

public sealed class ImportacionLoteConfirmarRequest
{
    // V-02.07: mismo tope que ImportacionConfirmarRequest.FilasAImportar y por el
    // mismo motivo: nunca tuvo cota y el CSV origen ya esta limitado a 5 MB.
    [System.ComponentModel.DataAnnotations.MaxLength(200000)]
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
    // V-02.07: unico campo de texto libre real de este fichero (motivo escrito por
    // un humano al revertir un lote). Se serializa dentro de un JSON de auditoria,
    // no hay columna de BD que lo acote, asi que 512 es un limite conservador de
    // guardarraiz, no un espejo de una restriccion existente.
    [System.ComponentModel.DataAnnotations.MaxLength(512)]
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
