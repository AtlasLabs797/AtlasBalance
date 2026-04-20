namespace GestionCaja.API.DTOs;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string PasswordActual { get; set; } = string.Empty;
    public string PasswordNueva { get; set; } = string.Empty;
}

public sealed class AuthUsuarioResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool PrimerLogin { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaUltimaLogin { get; set; }
}

public sealed class AuthResponse
{
    public string CsrfToken { get; set; } = string.Empty;
    public AuthUsuarioResponse Usuario { get; set; } = new();
    public IReadOnlyList<PermisoUsuarioResponse> Permisos { get; set; } = [];
}

public sealed class PermisoUsuarioResponse
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? CuentaId { get; set; }
    public Guid? TitularId { get; set; }
    public bool PuedeAgregarLineas { get; set; }
    public bool PuedeEditarLineas { get; set; }
    public bool PuedeEliminarLineas { get; set; }
    public bool PuedeImportar { get; set; }
    public bool PuedeVerDashboard { get; set; }
    public IReadOnlyList<string>? ColumnasVisibles { get; set; }
    public IReadOnlyList<string>? ColumnasEditables { get; set; }
}
