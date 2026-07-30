namespace AtlasBalance.API.DTOs;

public sealed class AuditoriaListItemResponse
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public string TipoAccion { get; set; } = string.Empty;
    public string? EntidadTipo { get; set; }
    public Guid? EntidadId { get; set; }
    public Guid? CuentaId { get; set; }
    public string? CuentaNombre { get; set; }
    public Guid? TitularId { get; set; }
    public string? TitularNombre { get; set; }
    public string? CeldaReferencia { get; set; }
    public string? ColumnaNombre { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string? IpAddress { get; set; }
    public string? DetallesJson { get; set; }

    // V-02.07: contexto de la peticion que genero la fila.
    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }
    public string Origen { get; set; } = string.Empty;
    public long Secuencia { get; set; }

    /// <summary>
    /// null en filas anteriores a V-02.07 (no llevan firma y no son verificables).
    /// </summary>
    public bool? FirmaValida { get; set; }
}

public sealed class AuditoriaUsuarioFiltroResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public sealed class AuditoriaCuentaFiltroResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public Guid TitularId { get; set; }
    public string TitularNombre { get; set; } = string.Empty;
}

public sealed class AuditoriaFiltrosResponse
{
    public IReadOnlyList<AuditoriaUsuarioFiltroResponse> Usuarios { get; set; } = [];
    public IReadOnlyList<AuditoriaCuentaFiltroResponse> Cuentas { get; set; } = [];
    public IReadOnlyList<string> TiposAccion { get; set; } = [];
}

// V-02.07: resultado de verificar la integridad de AUDITORIAS.
public sealed class AuditoriaIntegridadResponse
{
    public DateTime FechaVerificacionUtc { get; set; }
    public DateTime? RangoDesdeUtc { get; set; }
    public DateTime? RangoHastaUtc { get; set; }

    /// <summary>Filas examinadas en el rango.</summary>
    public int FilasExaminadas { get; set; }

    /// <summary>Filas con firma valida.</summary>
    public int FirmasValidas { get; set; }

    /// <summary>Filas cuya firma NO corresponde al contenido: manipulacion o clave rotada.</summary>
    public int FirmasInvalidas { get; set; }

    /// <summary>Filas anteriores a V-02.07, sin firma. No son verificables ni sospechosas.</summary>
    public int SinFirma { get; set; }

    /// <summary>Numero de filas que faltan segun los huecos de la secuencia.</summary>
    public long FilasFaltantes { get; set; }

    /// <summary>Tramos de secuencia ausentes, para investigar.</summary>
    public IReadOnlyList<AuditoriaHuecoSecuencia> Huecos { get; set; } = [];

    /// <summary>Ids de las primeras filas con firma invalida (limitado).</summary>
    public IReadOnlyList<Guid> IdsFirmaInvalida { get; set; } = [];

    /// <summary>true si no hay firmas invalidas ni huecos.</summary>
    public bool Integra { get; set; }
}

public sealed class AuditoriaHuecoSecuencia
{
    public long DesdeSecuencia { get; set; }
    public long HastaSecuencia { get; set; }
    public long FilasFaltantes { get; set; }
}
