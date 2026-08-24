using System.ComponentModel.DataAnnotations;

namespace AtlasBalance.API.DTOs;

public sealed class RevisionSettingsResponse
{
    public decimal ComisionesImporteMinimo { get; set; } = 1m;
}

// V-02.07 (nota): esta clase NO se enlaza desde [FromBody] ni [FromQuery];
// RevisionController la construye a mano a partir de parametros de query
// sueltos, asi que el ModelState de [ApiController] nunca la evalua. Por eso no
// lleva anotaciones: puestas aqui no se ejecutarian, y una anotacion que aparenta
// validar sin hacerlo confunde mas de lo que ayuda. `Estado` ya esta acotado de
// verdad por RevisionService.NormalizeEstadoFilter, que es un switch con lista
// blanca y devuelve null ante cualquier valor desconocido.
public sealed class RevisionQueryRequest
{
    public string? Estado { get; set; }
    public Guid? PaisId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class RevisionComisionItemResponse
{
    public Guid ExtractoId { get; set; }
    public Guid CuentaId { get; set; }
    public Guid TitularId { get; set; }
    public Guid? PaisId { get; set; }
    public string Titular { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public decimal Monto { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string EstadoDevolucion { get; set; } = "PENDIENTE";
    // V-02.08: extracto positivo emparejado como devolucion (persistido o
    // sugerido automaticamente). Null = sin devolucion asociada.
    public Guid? DevolucionExtractoId { get; set; }
    public DateOnly? DevolucionFecha { get; set; }
}

// V-02.08: resultado de POST /api/revision/comision/{id}/verificar-devolucion.
public sealed class VerificarDevolucionResponse
{
    public bool Encontrada { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? DevolucionExtractoId { get; set; }
    public DateOnly? DevolucionFecha { get; set; }
}

public sealed class RevisionSeguroItemResponse
{
    public Guid ExtractoId { get; set; }
    public Guid CuentaId { get; set; }
    public Guid TitularId { get; set; }
    public Guid? PaisId { get; set; }
    public string Titular { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Divisa { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public decimal Importe { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDIENTE";
}

public sealed class UpdateRevisionEstadoRequest
{
    // V-02.07: sin [Required] a proposito. RevisionService.NormalizeEstado trata un
    // Estado vacio/omitido como "pendiente" (comportamiento valido, no un error), asi
    // que exigirlo romperia peticiones legitimas que resetean a pendiente sin enviar
    // el campo. MaxLength(24) espeja el HasMaxLength(24) de las columnas Estado en
    // AppDbContext y es inofensivo: cualquier valor mas largo ya lo rechaza
    // NormalizeEstado con su propio 400.
    [MaxLength(24)]
    public string Estado { get; set; } = string.Empty;
}
