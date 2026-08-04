using System.ComponentModel.DataAnnotations;

namespace AtlasBalance.API.DTOs;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    // V-02.07: solo tope maximo, para acotar el payload. Aqui NO se valida la
    // politica de contrasena: en login la clave se compara contra el hash bcrypt
    // y punto. SecurityPolicy.TryValidatePassword solo corre al crear o cambiar
    // contrasena (UsuariosController y AuthService.CambiarPasswordAsync). Meter
    // aqui la politica rechazaria con un 400 a un usuario cuya clave sea
    // anterior a la politica actual, en vez de dejarle entrar y pedirle cambio.
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [MaxLength(256)]
    public string PasswordActual { get; set; } = string.Empty;
    [MaxLength(256)]
    public string PasswordNueva { get; set; } = string.Empty;
}

public sealed class VerifyMfaRequest
{
    [Required, MaxLength(128)]
    public string ChallengeId { get; set; } = string.Empty;
    [Required, MaxLength(16)]
    public string Code { get; set; } = string.Empty;
    public bool RememberDevice { get; set; }
}

public sealed class AuthUsuarioResponse
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
}

public sealed class AuthResponse
{
    public string CsrfToken { get; set; } = string.Empty;
    public AuthUsuarioResponse? Usuario { get; set; }
    public IReadOnlyList<PermisoUsuarioResponse> Permisos { get; set; } = [];
    public bool MfaRequired { get; set; }
    public bool MfaSetupRequired { get; set; }
    public string? MfaChallengeId { get; set; }
    public string? MfaSecret { get; set; }
    public string? MfaOtpAuthUri { get; set; }
    public bool MfaRememberDeviceAllowed { get; set; }
    public int MfaRememberDeviceDays { get; set; }
}

public sealed class TrustedMfaDeviceResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? UserAgentSummary { get; set; }
    public string? IpAddressSummary { get; set; }
    public bool Current { get; set; }
}

public sealed class PermisoUsuarioResponse
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
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
    public IReadOnlyList<string>? ColumnasVisibles { get; set; }
    public IReadOnlyList<string>? ColumnasEditables { get; set; }
}
