using System.Globalization;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtlasBalance.API.Services;

/// <summary>Alerta detectada por una regla, antes de notificarse.</summary>
public sealed record SecurityAlert(
    string Regla,
    string Severidad,
    /// <summary>
    /// Identifica el sujeto de la alerta (cuenta, IP, sesion). Es la clave de
    /// deduplicacion: la misma regla sobre el mismo sujeto no vuelve a notificar
    /// hasta que pasa el enfriamiento.
    /// </summary>
    string Clave,
    string Resumen,
    IReadOnlyList<string> Detalles,
    Guid? UsuarioId);

public interface ISecurityAlertService
{
    /// <summary>
    /// Evalua las reglas sobre la ventana actual y notifica lo que se dispare.
    /// Devuelve las alertas notificadas (las silenciadas por enfriamiento no).
    /// </summary>
    Task<IReadOnlyList<SecurityAlert>> EvaluarYNotificarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// V-02.07: reglas de deteccion sobre AUDITORIAS.
///
/// Se evalua desde un job de Hangfire cada VentanaMinutos. Todas las reglas
/// trabajan sobre la misma foto de la ventana, que se trae de una vez: con 4-8
/// usuarios son decenas de filas, y hacerlo en memoria evita seis consultas con
/// agrupaciones que EF traduce regular.
///
/// La deduplicacion consulta AUDITORIAS (no una cache en memoria) a proposito:
/// asi sobrevive a un reinicio del servicio y no se puede perder por un reciclado
/// del proceso justo cuando empieza un ataque.
/// </summary>
public sealed class SecurityAlertService : ISecurityAlertService
{
    public static class Reglas
    {
        public const string LoginFallidosPorCuenta = "LOGIN_FALLIDOS_POR_CUENTA";
        public const string IpMultiplesCuentas = "IP_MULTIPLES_CUENTAS";
        public const string AccesoMasivo = "ACCESO_MASIVO";
        public const string IpNuevaParaUsuario = "IP_NUEVA_PARA_USUARIO";
        public const string PasswordResetsRepetidos = "PASSWORD_RESETS_REPETIDOS";
        public const string ErroresAuthSobreLineaBase = "ERRORES_AUTH_SOBRE_LINEA_BASE";
    }

    public const string SeveridadAlta = "ALTA";
    public const string SeveridadMedia = "MEDIA";

    private readonly AppDbContext _dbContext;
    private readonly IAlertDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly SecurityAlertOptions _options;

    public SecurityAlertService(
        AppDbContext dbContext,
        IAlertDispatcher dispatcher,
        IClock clock,
        IOptions<SecurityAlertOptions> options)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SecurityAlert>> EvaluarYNotificarAsync(CancellationToken cancellationToken)
    {
        if (!_options.Habilitado)
        {
            return Array.Empty<SecurityAlert>();
        }

        var ahora = _clock.UtcNow;
        var ventana = TimeSpan.FromMinutes(Math.Max(1, _options.VentanaMinutos));
        var desde = ahora - ventana;

        var eventos = await _dbContext.Auditorias
            .AsNoTracking()
            .Where(a => a.Timestamp >= desde && a.Timestamp <= ahora)
            .Select(a => new EventoVentana
            {
                UsuarioId = a.UsuarioId,
                TipoAccion = a.TipoAccion,
                Timestamp = a.Timestamp,
                Ip = a.IpAddress != null ? a.IpAddress.ToString() : null,
                SessionId = a.SessionId,
                DetallesJson = a.DetallesJson
            })
            .ToListAsync(cancellationToken);

        var candidatas = new List<SecurityAlert>();
        candidatas.AddRange(ReglaLoginFallidosPorCuenta(eventos));
        candidatas.AddRange(ReglaIpMultiplesCuentas(eventos));
        candidatas.AddRange(ReglaAccesoMasivo(eventos));
        candidatas.AddRange(await ReglaIpNuevaParaUsuarioAsync(eventos, desde, cancellationToken));
        candidatas.AddRange(ReglaPasswordResetsRepetidos(eventos));
        candidatas.AddRange(await ReglaErroresAuthSobreLineaBaseAsync(eventos, desde, ventana, cancellationToken));

        if (candidatas.Count == 0)
        {
            return Array.Empty<SecurityAlert>();
        }

        var yaAvisadas = await _dispatcher.ClavesEnEnfriamientoAsync(ahora, cancellationToken);
        var notificadas = new List<SecurityAlert>();

        foreach (var alerta in candidatas)
        {
            var clave = $"{alerta.Regla}|{alerta.Clave}";
            if (yaAvisadas.Contains(clave))
            {
                continue;
            }

            await _dispatcher.DespacharAsync(alerta, cancellationToken);
            // Se anade a la vez para que dos reglas que produzcan la misma clave
            // en la misma pasada no notifiquen dos veces.
            yaAvisadas.Add(clave);
            notificadas.Add(alerta);
        }

        return notificadas;
    }

    // --- Regla 1: mas de N fallos de login sobre una misma cuenta -------------

    private IEnumerable<SecurityAlert> ReglaLoginFallidosPorCuenta(List<EventoVentana> eventos)
    {
        // Se agrupa por el email del detalle y no solo por usuario_id: los
        // intentos contra una cuenta inexistente (o antes de resolver el usuario)
        // llevan usuario_id nulo, y son justo los que interesa ver.
        var fallidos = eventos
            .Where(e => string.Equals(e.TipoAccion, AuditActions.LoginFailed, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => LeerCampoTexto(e.DetallesJson, "email") ?? e.UsuarioId?.ToString() ?? "(desconocida)");

        foreach (var grupo in fallidos)
        {
            if (grupo.Count() <= _options.MaxLoginFallidosPorCuenta)
            {
                continue;
            }

            var ips = grupo.Select(e => e.Ip).Where(ip => ip is not null).Distinct().ToList();
            yield return new SecurityAlert(
                Reglas.LoginFallidosPorCuenta,
                SeveridadAlta,
                grupo.Key,
                $"{grupo.Count()} intentos de login fallidos contra la cuenta {grupo.Key} en {_options.VentanaMinutos} minutos.",
                new[]
                {
                    $"Umbral: {_options.MaxLoginFallidosPorCuenta} intentos.",
                    $"IPs implicadas: {(ips.Count == 0 ? "sin registrar" : string.Join(", ", ips))}."
                },
                grupo.Select(e => e.UsuarioId).FirstOrDefault(id => id.HasValue));
        }
    }

    // --- Regla 2: una IP tocando muchas cuentas distintas --------------------

    private IEnumerable<SecurityAlert> ReglaIpMultiplesCuentas(List<EventoVentana> eventos)
    {
        var porIp = eventos
            .Where(e => e.Ip is not null)
            .GroupBy(e => e.Ip!);

        foreach (var grupo in porIp)
        {
            // Cuentas identificadas por usuario_id o, en los login fallidos, por
            // el email probado: un barrido de credenciales no llega a autenticarse
            // nunca, asi que contar solo usuario_id lo dejaria invisible.
            var cuentas = grupo
                .Select(e => e.UsuarioId?.ToString() ?? LeerCampoTexto(e.DetallesJson, "email"))
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();

            if (cuentas.Count <= _options.MaxCuentasPorIp)
            {
                continue;
            }

            yield return new SecurityAlert(
                Reglas.IpMultiplesCuentas,
                SeveridadAlta,
                grupo.Key,
                $"La IP {grupo.Key} ha tocado {cuentas.Count} cuentas distintas en {_options.VentanaMinutos} minutos.",
                new[]
                {
                    $"Umbral: {_options.MaxCuentasPorIp} cuentas.",
                    $"Eventos en la ventana: {grupo.Count()}."
                },
                null);
        }
    }

    // --- Regla 3: acceso masivo o secuencial rapido --------------------------

    private IEnumerable<SecurityAlert> ReglaAccesoMasivo(List<EventoVentana> eventos)
    {
        // 3a. Peticiones que pidieron mas filas que el umbral (las marca
        // SecurityAuditMiddleware).
        var bulk = eventos
            .Where(e => string.Equals(e.TipoAccion, AuditActions.AccesoBulk, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.UsuarioId?.ToString() ?? e.Ip ?? "(anonimo)");

        foreach (var grupo in bulk)
        {
            yield return new SecurityAlert(
                Reglas.AccesoMasivo,
                SeveridadMedia,
                $"bulk:{grupo.Key}",
                $"{grupo.Count()} peticiones de lectura masiva de {grupo.Key} en {_options.VentanaMinutos} minutos.",
                new[] { "Origen del evento: SecurityAuditMiddleware (pageSize por encima del umbral)." },
                grupo.Select(e => e.UsuarioId).FirstOrDefault(id => id.HasValue));
        }

        // 3b. Volumen secuencial: muchas acciones auditadas de la misma sesion en
        // la ventana. Es la firma de un scraping automatizado con una sesion
        // legitima, que ninguna regla de rate limiting por IP detecta.
        var porSesion = eventos
            .Where(e => !string.IsNullOrEmpty(e.SessionId))
            .GroupBy(e => e.SessionId!);

        foreach (var grupo in porSesion)
        {
            if (grupo.Count() <= _options.MaxPeticionesSecuenciales)
            {
                continue;
            }

            yield return new SecurityAlert(
                Reglas.AccesoMasivo,
                SeveridadMedia,
                $"sesion:{grupo.Key}",
                $"{grupo.Count()} acciones auditadas de una misma sesion en {_options.VentanaMinutos} minutos.",
                new[]
                {
                    $"Umbral: {_options.MaxPeticionesSecuenciales} acciones.",
                    $"Sesion: {grupo.Key}."
                },
                grupo.Select(e => e.UsuarioId).FirstOrDefault(id => id.HasValue));
        }
    }

    // --- Regla 4: login desde una IP nunca vista para ese usuario ------------

    private async Task<IEnumerable<SecurityAlert>> ReglaIpNuevaParaUsuarioAsync(
        List<EventoVentana> eventos,
        DateTime desdeVentana,
        CancellationToken cancellationToken)
    {
        // El requisito original pedia "login desde un pais nuevo". Aqui no hay
        // GeoIP: la app es on-premise, en LAN y sin salida garantizada a
        // internet, asi que una base de geolocalizacion seria una dependencia
        // externa que no se puede mantener actualizada. La senal equivalente y
        // honesta en este despliegue es la IP: si un usuario entra desde una IP
        // que nunca habia usado, es lo mismo que se pretendia detectar.
        var logins = eventos
            .Where(e => string.Equals(e.TipoAccion, AuditActions.Login, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.UsuarioId.HasValue && e.Ip is not null)
            .Select(e => new { UsuarioId = e.UsuarioId!.Value, Ip = e.Ip! })
            .Distinct()
            .ToList();

        if (logins.Count == 0)
        {
            return Array.Empty<SecurityAlert>();
        }

        var historicoDesde = desdeVentana.AddDays(-Math.Max(1, _options.DiasHistoricoIpConocida));
        var usuarios = logins.Select(l => l.UsuarioId).Distinct().ToList();

        var ipsConocidas = await _dbContext.Auditorias
            .AsNoTracking()
            .Where(a => a.TipoAccion == AuditActions.Login
                        && a.Timestamp >= historicoDesde
                        && a.Timestamp < desdeVentana
                        && a.UsuarioId.HasValue
                        && usuarios.Contains(a.UsuarioId.Value)
                        && a.IpAddress != null)
            .Select(a => new { a.UsuarioId, Ip = a.IpAddress!.ToString() })
            .Distinct()
            .ToListAsync(cancellationToken);

        var conocidasPorUsuario = ipsConocidas
            .GroupBy(x => x.UsuarioId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Ip).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var alertas = new List<SecurityAlert>();
        foreach (var login in logins)
        {
            if (!conocidasPorUsuario.TryGetValue(login.UsuarioId, out var conocidas))
            {
                // Sin historico: primer login registrado del usuario. Alertar
                // aqui seria ruido garantizado en cada alta.
                continue;
            }

            if (conocidas.Contains(login.Ip))
            {
                continue;
            }

            alertas.Add(new SecurityAlert(
                Reglas.IpNuevaParaUsuario,
                SeveridadMedia,
                $"{login.UsuarioId}:{login.Ip}",
                $"Login del usuario {login.UsuarioId} desde una IP nunca usada antes ({login.Ip}).",
                new[]
                {
                    $"IPs conocidas en los ultimos {_options.DiasHistoricoIpConocida} dias: {conocidas.Count}.",
                    "Confirma con el usuario si el acceso es suyo antes de dar por buena la sesion."
                },
                login.UsuarioId));
        }

        return alertas;
    }

    // --- Regla 5: reinicios de password repetidos ----------------------------

    private IEnumerable<SecurityAlert> ReglaPasswordResetsRepetidos(List<EventoVentana> eventos)
    {
        var resets = eventos
            .Where(e => string.Equals(e.TipoAccion, AuditActions.PasswordReset, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.TipoAccion, AuditActions.PasswordChanged, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.UsuarioId?.ToString() ?? e.Ip ?? "(desconocido)");

        foreach (var grupo in resets)
        {
            if (grupo.Count() <= _options.MaxPasswordResets)
            {
                continue;
            }

            yield return new SecurityAlert(
                Reglas.PasswordResetsRepetidos,
                SeveridadAlta,
                grupo.Key,
                $"{grupo.Count()} cambios o reinicios de password sobre {grupo.Key} en {_options.VentanaMinutos} minutos.",
                new[] { $"Umbral: {_options.MaxPasswordResets}." },
                grupo.Select(e => e.UsuarioId).FirstOrDefault(id => id.HasValue));
        }
    }

    // --- Regla 6: 401/403 por encima de la linea base ------------------------

    private async Task<IEnumerable<SecurityAlert>> ReglaErroresAuthSobreLineaBaseAsync(
        List<EventoVentana> eventos,
        DateTime desdeVentana,
        TimeSpan ventana,
        CancellationToken cancellationToken)
    {
        var actuales = eventos.Count(e =>
            string.Equals(e.TipoAccion, AuditActions.AuthnDenied, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.TipoAccion, AuditActions.AuthzDenied, StringComparison.OrdinalIgnoreCase));

        // Suelo absoluto primero: sin el, una media historica de 0 convierte
        // cualquier par de errores en una alerta.
        if (actuales < _options.MinErroresAuthParaAlertar)
        {
            return Array.Empty<SecurityAlert>();
        }

        var ventanasBase = Math.Max(1, _options.VentanasLineaBase);
        var inicioBase = desdeVentana - (ventana * ventanasBase);

        var totalBase = await _dbContext.Auditorias
            .AsNoTracking()
            .CountAsync(
                a => a.Timestamp >= inicioBase
                     && a.Timestamp < desdeVentana
                     && (a.TipoAccion == AuditActions.AuthnDenied || a.TipoAccion == AuditActions.AuthzDenied),
                cancellationToken);

        var media = totalBase / (double)ventanasBase;
        var umbral = media * _options.FactorSobreLineaBase;

        if (actuales <= umbral)
        {
            return Array.Empty<SecurityAlert>();
        }

        return new[]
        {
            new SecurityAlert(
                Reglas.ErroresAuthSobreLineaBase,
                SeveridadAlta,
                "global",
                $"{actuales} errores 401/403 en {_options.VentanaMinutos} minutos, frente a una media de {media.ToString("N1", CultureInfo.InvariantCulture)} por ventana.",
                new[]
                {
                    $"Linea base calculada sobre las {ventanasBase} ventanas anteriores.",
                    $"Umbral: {umbral.ToString("N1", CultureInfo.InvariantCulture)} (x{_options.FactorSobreLineaBase} la media) y minimo absoluto {_options.MinErroresAuthParaAlertar}."
                },
                null)
        };
    }

    private static string? LeerCampoTexto(string? detallesJson, string propiedad)
        => AlertDispatcher.LeerCampoTexto(detallesJson, propiedad);

    private sealed class EventoVentana
    {
        public Guid? UsuarioId { get; init; }
        public string TipoAccion { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string? Ip { get; init; }
        public string? SessionId { get; init; }
        public string? DetallesJson { get; init; }
    }
}
