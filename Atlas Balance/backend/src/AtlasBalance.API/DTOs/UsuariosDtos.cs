using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.DTOs;

public sealed class PaginatedResponse<T>
{
    public IReadOnlyList<T> Data { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<string>? ColumnasDisponibles { get; set; }
}

public sealed class UsuarioListItemResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool PrimerLogin { get; set; }
    public bool PuedeUsarIa { get; set; }
    public bool MfaEnabled { get; set; }
    public bool MfaRequired { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaUltimaLogin { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class UsuarioDetalleResponse
{
    public UsuarioListItemResponse Usuario { get; set; } = new();
    public IReadOnlyList<string> Emails { get; set; } = [];
    public IReadOnlyList<PermisoUsuarioResponse> Permisos { get; set; } = [];
}

public sealed class SavePermisoUsuarioRequest
{
    public Guid? CuentaId { get; set; }
    public Guid? TitularId { get; set; }
    public Guid? PaisId { get; set; }
    public bool PuedeVerCuentas { get; set; }
    public bool PuedeAgregarLineas { get; set; }
    public bool PuedeEditarLineas { get; set; }
    public bool PuedeEliminarLineas { get; set; }
    public bool PuedeImportar { get; set; }
    public bool PuedeVerDashboard { get; set; }
    public bool PuedeRevisarLineas { get; set; }
    public bool PuedeAprobarImportaciones { get; set; }
    public bool PuedeConciliar { get; set; }
    public bool PuedeCerrarConciliacion { get; set; }
    // V-02.07: acota el numero de columnas por preferencia, no su contenido;
    // evita listas sin limite en el payload (vector de asignacion de memoria).
    [MaxLength(200)]
    public IReadOnlyList<string>? ColumnasVisibles { get; set; }
    [MaxLength(200)]
    public IReadOnlyList<string>? ColumnasEditables { get; set; }
}

public sealed class CreateUsuarioRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    [Required, MaxLength(200)]
    public string NombreCompleto { get; set; } = string.Empty;
    // V-02.07: JsonStringEnumConverter rechaza una cadena desconocida, pero NO
    // valida un entero: `"rol": 99` deserializa a (RolUsuario)99 sin rechistar.
    // RolUsuario esta mapeado como enum nativo de Postgres (HasPostgresEnum), asi
    // que Npgsql revienta al escribirlo y sale un 500 en vez de un 400. No hay
    // corrupcion de rol -la BD no lo acepta- pero el codigo de error enganaba.
    [EnumDataType(typeof(RolUsuario))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RolUsuario Rol { get; set; } = RolUsuario.EMPLEADO;
    public bool Activo { get; set; } = true;
    public bool PrimerLogin { get; set; } = true;
    public bool PuedeUsarIa { get; set; }
    // V-02.07: sin [MinLength]: la longitud minima y complejidad ya las exige
    // SecurityPolicy.TryValidatePassword en el controller; aqui solo se acota
    // el maximo para evitar payloads enormes.
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;
    [MaxLength(20)]
    public IReadOnlyList<string> Emails { get; set; } = [];
    [MaxLength(500)]
    public IReadOnlyList<SavePermisoUsuarioRequest> Permisos { get; set; } = [];
}

public sealed class UpdateUsuarioRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    [Required, MaxLength(200)]
    public string NombreCompleto { get; set; } = string.Empty;
    // V-02.07: JsonStringEnumConverter rechaza una cadena desconocida, pero NO
    // valida un entero: `"rol": 99` deserializa a (RolUsuario)99 sin rechistar.
    // RolUsuario esta mapeado como enum nativo de Postgres (HasPostgresEnum), asi
    // que Npgsql revienta al escribirlo y sale un 500 en vez de un 400. No hay
    // corrupcion de rol -la BD no lo acepta- pero el codigo de error enganaba.
    [EnumDataType(typeof(RolUsuario))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RolUsuario Rol { get; set; } = RolUsuario.EMPLEADO;
    public bool Activo { get; set; }
    public bool PrimerLogin { get; set; }
    public bool PuedeUsarIa { get; set; }
    // V-02.07: nullable y opcional a proposito (cambio de password no obligatorio
    // al actualizar usuario), por eso no lleva [Required].
    [MaxLength(256)]
    public string? PasswordNueva { get; set; }
    [MaxLength(20)]
    public IReadOnlyList<string> Emails { get; set; } = [];
    [MaxLength(500)]
    public IReadOnlyList<SavePermisoUsuarioRequest> Permisos { get; set; } = [];
}

public sealed class UsuarioEmailResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool EsPrincipal { get; set; }
}

public sealed class SaveUsuarioEmailRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    public bool EsPrincipal { get; set; }
}
