using System.Text.Json.Serialization;
using GestionCaja.API.Models;

namespace GestionCaja.API.DTOs;

public sealed class PaginatedResponse<T>
{
    public IReadOnlyList<T> Data { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public sealed class UsuarioListItemResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool PrimerLogin { get; set; }
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
    public bool PuedeVerCuentas { get; set; }
    public bool PuedeAgregarLineas { get; set; }
    public bool PuedeEditarLineas { get; set; }
    public bool PuedeEliminarLineas { get; set; }
    public bool PuedeImportar { get; set; }
    public bool PuedeVerDashboard { get; set; }
    public IReadOnlyList<string>? ColumnasVisibles { get; set; }
    public IReadOnlyList<string>? ColumnasEditables { get; set; }
}

public sealed class CreateUsuarioRequest
{
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; } = true;
    public bool PrimerLogin { get; set; } = true;
    public string Password { get; set; } = string.Empty;
    public IReadOnlyList<string> Emails { get; set; } = [];
    public IReadOnlyList<SavePermisoUsuarioRequest> Permisos { get; set; } = [];
}

public sealed class UpdateUsuarioRequest
{
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; }
    public bool PrimerLogin { get; set; }
    public string? PasswordNueva { get; set; }
    public IReadOnlyList<string> Emails { get; set; } = [];
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
    public string Email { get; set; } = string.Empty;
    public bool EsPrincipal { get; set; }
}
