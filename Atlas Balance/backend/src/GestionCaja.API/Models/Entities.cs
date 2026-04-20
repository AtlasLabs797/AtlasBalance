namespace GestionCaja.API.Models;

public interface ISoftDelete
{
    DateTime? DeletedAt { get; set; }
    Guid? DeletedById { get; set; }
}

public class Usuario : ISoftDelete
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; } = true;
    public bool PrimerLogin { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaUltimaLogin { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class UsuarioEmail
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool EsPrincipal { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiraEn { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? RevocadoEn { get; set; }
    public string? ReemplazadoPor { get; set; }
    public System.Net.IPAddress? IpAddress { get; set; }
    public Usuario? Usuario { get; set; }
}

public class Titular : ISoftDelete
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TipoTitular Tipo { get; set; }
    public string? Identificacion { get; set; }
    public string? ContactoEmail { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class Cuenta : ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TitularId { get; set; }
    public Titular? Titular { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? NumeroCuenta { get; set; }
    public string? Iban { get; set; }
    public string? BancoNombre { get; set; }
    public string Divisa { get; set; } = "EUR";
    public Guid? FormatoId { get; set; }
    public bool EsEfectivo { get; set; }
    public bool Activa { get; set; } = true;
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class FormatoImportacion : ISoftDelete
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? BancoNombre { get; set; }
    public string? Divisa { get; set; }
    public string MapeoJson { get; set; } = "{}";
    public Guid? UsuarioCreadorId { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public bool Activo { get; set; } = true;
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class Extracto : ISoftDelete
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public DateOnly Fecha { get; set; }
    public string? Concepto { get; set; }
    public string? Comentarios { get; set; }
    public decimal Monto { get; set; }
    public decimal Saldo { get; set; }
    public int FilaNumero { get; set; }
    public bool Checked { get; set; }
    public DateTime? CheckedAt { get; set; }
    public Guid? CheckedById { get; set; }
    public bool Flagged { get; set; }
    public string? FlaggedNota { get; set; }
    public DateTime? FlaggedAt { get; set; }
    public Guid? FlaggedById { get; set; }
    public Guid? UsuarioCreacionId { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public Guid? UsuarioModificacionId { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class ExtractoColumnaExtra
{
    public Guid Id { get; set; }
    public Guid ExtractoId { get; set; }
    public string NombreColumna { get; set; } = string.Empty;
    public string? Valor { get; set; }
}

public class PermisoUsuario
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
}

public class PreferenciaUsuarioCuenta
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? CuentaId { get; set; }
    public string? ColumnasVisibles { get; set; }
    public string? ColumnasEditables { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AlertaSaldo
{
    public Guid Id { get; set; }
    public Guid? CuentaId { get; set; }
    public decimal SaldoMinimo { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaUltimaAlerta { get; set; }
}

public class AlertaDestinatario
{
    public Guid Id { get; set; }
    public Guid AlertaId { get; set; }
    public Guid UsuarioId { get; set; }
}

public class Auditoria
{
    public Guid Id { get; set; }
    public Guid? UsuarioId { get; set; }
    public string TipoAccion { get; set; } = string.Empty;
    public string? EntidadTipo { get; set; }
    public Guid? EntidadId { get; set; }
    public string? CeldaReferencia { get; set; }
    public string? ColumnaNombre { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public System.Net.IPAddress? IpAddress { get; set; }
    public string? DetallesJson { get; set; }
}

public class IntegrationToken : ISoftDelete
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Tipo { get; set; } = "openclaw";
    public EstadoTokenIntegracion Estado { get; set; } = EstadoTokenIntegracion.Activo;
    public bool PermisoLectura { get; set; } = true;
    public bool PermisoEscritura { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaUltimaUso { get; set; }
    public DateTime? FechaRevocacion { get; set; }
    public Guid UsuarioCreadorId { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class IntegrationPermission
{
    public Guid Id { get; set; }
    public Guid TokenId { get; set; }
    public Guid? TitularId { get; set; }
    public Guid? CuentaId { get; set; }
    public string AccesoTipo { get; set; } = "lectura";
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

public class AuditoriaIntegracion
{
    public Guid Id { get; set; }
    public Guid TokenId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Metodo { get; set; } = string.Empty;
    public string? Parametros { get; set; }
    public int? CodigoRespuesta { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public System.Net.IPAddress? IpAddress { get; set; }
    public int? TiempoEjecucionMs { get; set; }
}

public class TipoCambio
{
    public Guid Id { get; set; }
    public string DivisaOrigen { get; set; } = string.Empty;
    public string DivisaDestino { get; set; } = string.Empty;
    public decimal Tasa { get; set; }
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    public FuenteTipoCambio Fuente { get; set; }
}

public class DivisaActiva
{
    public string Codigo { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string? Simbolo { get; set; }
    public bool Activa { get; set; } = true;
    public bool EsBase { get; set; }
}

public class Configuracion
{
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string? Tipo { get; set; }
    public string? Descripcion { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public Guid? UsuarioModificacionId { get; set; }
}

public class Backup : ISoftDelete
{
    public Guid Id { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public string RutaArchivo { get; set; } = string.Empty;
    public long? TamanioBytes { get; set; }
    public EstadoProceso Estado { get; set; }
    public TipoProceso Tipo { get; set; }
    public Guid? IniciadoPorId { get; set; }
    public string? Notas { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class Exportacion : ISoftDelete
{
    public Guid Id { get; set; }
    public Guid CuentaId { get; set; }
    public DateTime FechaExportacion { get; set; } = DateTime.UtcNow;
    public string? RutaArchivo { get; set; }
    public long? TamanioBytes { get; set; }
    public EstadoProceso Estado { get; set; }
    public TipoProceso Tipo { get; set; }
    public Guid? IniciadoPorId { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}

public class NotificacionAdmin
{
    public Guid Id { get; set; }
    public string? Tipo { get; set; }
    public string? Mensaje { get; set; }
    public bool Leida { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string? DetallesJson { get; set; }
}
