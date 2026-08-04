using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AtlasBalance.API.Models;

namespace AtlasBalance.API.Services;

// V-02-07: contenedor DI de la clave de firma de auditoria, resuelta y validada
// una sola vez en Program.cs. Mismo patron que RlsContextSecret: internal para
// no exponer el valor a otros ensamblados.
internal sealed class AuditSigningKey
{
    public AuditSigningKey(string secret)
    {
        Secret = secret;
    }

    public string Secret { get; }
}

public interface IAuditSigner
{
    /// <summary>Calcula la firma de la fila. No la asigna.</summary>
    string Firmar(Auditoria auditoria);

    /// <summary>true si la firma almacenada corresponde al contenido actual.</summary>
    bool Verificar(Auditoria auditoria);
}

/// <summary>
/// Firma HMAC-SHA256 por fila de AUDITORIAS.
///
/// Modelo de amenaza (el que pide el requisito "logs que un atacante que
/// compromete la app no pueda modificar"): el atacante tiene la aplicacion y
/// con ella el connection string de PostgreSQL. En ese escenario PREVENIR la
/// escritura es imposible, asi que el objetivo real es DETECTAR:
///
/// - Modificar una fila     -> la firma deja de validar (no tiene la clave).
/// - Insertar una fila falsa -> no puede producir una firma valida.
/// - Borrar filas            -> hueco en SECUENCIA (bigserial de Postgres).
/// - Escritura casual/bug    -> trigger append-only la rechaza de entrada.
///
/// La firma NO cubre Secuencia a proposito: Postgres la asigna durante el
/// INSERT, y firmarla obligaria a un UPDATE posterior que el propio trigger
/// append-only bloquea. La continuidad de la secuencia ya cubre el borrado.
///
/// Limite conocido y aceptado: borrar la COLA de la tabla (las N filas mas
/// recientes) no deja hueco detectable. Eso lo cubre el espejo a Windows Event
/// Log (SecurityEventLog), que vive fuera del alcance del connection string.
///
/// La clave debe ser distinta de JwtSettings:Secret en produccion: si no,
/// comprometer el JWT permitiria forjar auditoria.
/// </summary>
internal sealed class AuditSigner : IAuditSigner
{
    // Postgres timestamptz almacena microsegundos; DateTime tiene resolucion de
    // 100 ns. Sin truncar, la firma calculada antes del INSERT no validaria al
    // releer la fila, y toda la auditoria pareceria manipulada.
    private const long TicksPorMicrosegundo = TimeSpan.TicksPerMillisecond / 1000;

    private readonly byte[] _key;

    public AuditSigner(AuditSigningKey key)
    {
        _key = Encoding.UTF8.GetBytes(key.Secret);
    }

    public string Firmar(Auditoria auditoria)
    {
        ArgumentNullException.ThrowIfNull(auditoria);

        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(Canonicalizar(auditoria)));
        return Convert.ToBase64String(hash);
    }

    public bool Verificar(Auditoria auditoria)
    {
        ArgumentNullException.ThrowIfNull(auditoria);

        if (string.IsNullOrEmpty(auditoria.Firma))
        {
            return false;
        }

        // FixedTimeEquals para no filtrar por latencia cuantos bytes coinciden.
        // Devuelve false si las longitudes difieren, que es lo que queremos.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Firmar(auditoria)),
            Encoding.UTF8.GetBytes(auditoria.Firma));
    }

    /// <summary>
    /// Serializacion canonica con prefijo de longitud por campo. El prefijo
    /// importa: sin el, ("ab", "c") y ("a", "bc") producirian el mismo payload
    /// y se podria mover contenido de un campo a otro sin invalidar la firma.
    /// </summary>
    internal static string Canonicalizar(Auditoria a)
    {
        var sb = new StringBuilder();
        Append(sb, a.Id.ToString("D", CultureInfo.InvariantCulture));
        Append(sb, a.UsuarioId?.ToString("D", CultureInfo.InvariantCulture));
        Append(sb, a.TipoAccion);
        Append(sb, a.EntidadTipo);
        Append(sb, a.EntidadId?.ToString("D", CultureInfo.InvariantCulture));
        Append(sb, a.CeldaReferencia);
        Append(sb, a.ColumnaNombre);
        Append(sb, a.ValorAnterior);
        Append(sb, a.ValorNuevo);
        Append(sb, TruncarAMicrosegundos(a.Timestamp).ToString("O", CultureInfo.InvariantCulture));
        Append(sb, NormalizarIp(a.IpAddress));
        Append(sb, a.UserAgent);
        Append(sb, a.SessionId);
        Append(sb, a.Origen);
        Append(sb, a.DetallesJson);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            // Longitud imposible: distingue null de cadena vacia sin ambiguedad.
            sb.Append("-1|");
            return;
        }

        sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append('|').Append(value);
    }

    internal static DateTime TruncarAMicrosegundos(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified: lo tratamos como UTC porque todo el codigo escribe
            // DateTime.UtcNow. Convertirlo asumiendo hora local cambiaria la
            // firma segun la zona del servidor.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTime(utc.Ticks - (utc.Ticks % TicksPorMicrosegundo), DateTimeKind.Utc);
    }

    /// <summary>
    /// Postgres inet puede devolver una IPv4 como IPv6 mapeada (::ffff:10.0.0.1)
    /// segun como se inserto. Normalizamos a IPv4 para que la firma no dependa
    /// de la representacion.
    /// </summary>
    internal static string? NormalizarIp(IPAddress? ip)
    {
        if (ip is null)
        {
            return null;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv4MappedToIPv6)
        {
            return ip.MapToIPv4().ToString();
        }

        return ip.ToString();
    }
}
