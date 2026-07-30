using System.Net;
using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-02.07: las 6 reglas de deteccion. Cada test comprueba dos cosas: que la
// regla dispara cuando debe, y que NO dispara justo por debajo del umbral.
// Lo segundo importa igual: una regla que avisa de mas se acaba ignorando, y
// una alerta ignorada no defiende de nada.
// -----------------------------------------------------------------------
public sealed class SecurityAlertServiceTests
{
    private static readonly DateTime Ahora = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Regla1_Should_Alert_When_Login_Failures_Exceed_Threshold_For_One_Account()
    {
        await using var db = BuildDbContext();
        // 6 fallos con umbral 5.
        AddEventos(db, 6, AuditActions.LoginFailed, ip: "10.0.0.9", detalles: new { email = "victima@atlas.local" });
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().ContainSingle(a => a.Regla == SecurityAlertService.Reglas.LoginFallidosPorCuenta);
        alertas.Single(a => a.Regla == SecurityAlertService.Reglas.LoginFallidosPorCuenta)
            .Clave.Should().Be("victima@atlas.local");
    }

    [Fact]
    public async Task Regla1_Should_Not_Alert_At_The_Threshold()
    {
        await using var db = BuildDbContext();
        AddEventos(db, 5, AuditActions.LoginFailed, ip: "10.0.0.9", detalles: new { email = "victima@atlas.local" });
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().NotContain(a => a.Regla == SecurityAlertService.Reglas.LoginFallidosPorCuenta);
    }

    [Fact]
    public async Task Regla2_Should_Alert_When_One_Ip_Touches_Many_Accounts()
    {
        await using var db = BuildDbContext();
        // 11 cuentas distintas desde la misma IP, umbral 10. Es la firma de un
        // credential stuffing: ninguna llega a autenticarse, asi que contar solo
        // usuario_id no lo veria. Por eso la regla mira tambien el email probado.
        for (var i = 0; i < 11; i++)
        {
            AddEventos(db, 1, AuditActions.LoginFailed, ip: "10.0.0.66", detalles: new { email = $"cuenta{i}@atlas.local" });
        }
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().Contain(a =>
            a.Regla == SecurityAlertService.Reglas.IpMultiplesCuentas && a.Clave == "10.0.0.66");
    }

    [Fact]
    public async Task Regla3_Should_Alert_On_Bulk_Access_Events()
    {
        await using var db = BuildDbContext();
        var usuarioId = Guid.NewGuid();
        AddEventos(db, 1, AuditActions.AccesoBulk, ip: "10.0.0.4", usuarioId: usuarioId);
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().Contain(a => a.Regla == SecurityAlertService.Reglas.AccesoMasivo);
    }

    [Fact]
    public async Task Regla4_Should_Alert_On_Login_From_Ip_Never_Seen_For_That_User()
    {
        await using var db = BuildDbContext();
        var usuarioId = Guid.NewGuid();

        // Historico: siempre desde la misma IP, fuera de la ventana evaluada.
        db.Auditorias.Add(Evento(AuditActions.Login, Ahora.AddDays(-10), "10.0.0.20", usuarioId));
        db.Auditorias.Add(Evento(AuditActions.Login, Ahora.AddDays(-3), "10.0.0.20", usuarioId));
        // Ahora entra desde otra.
        db.Auditorias.Add(Evento(AuditActions.Login, Ahora.AddMinutes(-1), "203.0.113.7", usuarioId));
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().Contain(a =>
            a.Regla == SecurityAlertService.Reglas.IpNuevaParaUsuario && a.UsuarioId == usuarioId);
    }

    [Fact]
    public async Task Regla4_Should_Not_Alert_On_Known_Ip()
    {
        await using var db = BuildDbContext();
        var usuarioId = Guid.NewGuid();
        db.Auditorias.Add(Evento(AuditActions.Login, Ahora.AddDays(-3), "10.0.0.20", usuarioId));
        db.Auditorias.Add(Evento(AuditActions.Login, Ahora.AddMinutes(-1), "10.0.0.20", usuarioId));
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().NotContain(a => a.Regla == SecurityAlertService.Reglas.IpNuevaParaUsuario);
    }

    [Fact]
    public async Task Regla4_Should_Not_Alert_On_The_First_Ever_Login()
    {
        // Sin historico no hay nada que comparar. Alertar aqui seria ruido
        // garantizado en cada alta de usuario.
        await using var db = BuildDbContext();
        db.Auditorias.Add(Evento(AuditActions.Login, Ahora.AddMinutes(-1), "10.0.0.30", Guid.NewGuid()));
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().NotContain(a => a.Regla == SecurityAlertService.Reglas.IpNuevaParaUsuario);
    }

    [Fact]
    public async Task Regla5_Should_Alert_On_Repeated_Password_Changes()
    {
        await using var db = BuildDbContext();
        var usuarioId = Guid.NewGuid();
        AddEventos(db, 4, AuditActions.PasswordChanged, ip: "10.0.0.8", usuarioId: usuarioId);
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().Contain(a => a.Regla == SecurityAlertService.Reglas.PasswordResetsRepetidos);
    }

    [Fact]
    public async Task Regla6_Should_Alert_When_Auth_Errors_Exceed_Baseline()
    {
        await using var db = BuildDbContext();

        // Linea base: 1 error por ventana durante las 12 ventanas anteriores.
        for (var i = 1; i <= 12; i++)
        {
            db.Auditorias.Add(Evento(AuditActions.AuthzDenied, Ahora.AddMinutes(-5 * i - 1), "10.0.0.2", null));
        }

        // Ventana actual: 30 errores, muy por encima de la media y del minimo.
        AddEventos(db, 30, AuditActions.AuthzDenied, ip: "10.0.0.2");
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().Contain(a => a.Regla == SecurityAlertService.Reglas.ErroresAuthSobreLineaBase);
    }

    [Fact]
    public async Task Regla6_Should_Not_Alert_Below_The_Absolute_Minimum()
    {
        // Sin suelo absoluto, una media historica de 0 convierte 2 errores en
        // una alerta. El suelo es lo que evita ese falso positivo permanente.
        await using var db = BuildDbContext();
        AddEventos(db, 5, AuditActions.AuthzDenied, ip: "10.0.0.2");
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db);

        alertas.Should().NotContain(a => a.Regla == SecurityAlertService.Reglas.ErroresAuthSobreLineaBase);
    }

    [Fact]
    public async Task Should_Not_Repeat_An_Alert_Within_The_Cooldown()
    {
        // Un ataque sostenido dispara la misma regla cada pasada. Sin
        // enfriamiento serian correos cada 5 minutos durante horas, y el
        // operador acabaria filtrandolos a la papelera.
        await using var db = BuildDbContext();
        AddEventos(db, 6, AuditActions.LoginFailed, ip: "10.0.0.9", detalles: new { email = "victima@atlas.local" });
        await db.SaveChangesAsync();

        var primera = await Evaluar(db);
        var segunda = await Evaluar(db);

        primera.Should().Contain(a => a.Regla == SecurityAlertService.Reglas.LoginFallidosPorCuenta);
        segunda.Should().NotContain(a => a.Regla == SecurityAlertService.Reglas.LoginFallidosPorCuenta);
    }

    [Fact]
    public async Task Should_Return_Nothing_When_Disabled()
    {
        await using var db = BuildDbContext();
        AddEventos(db, 50, AuditActions.LoginFailed, ip: "10.0.0.9", detalles: new { email = "victima@atlas.local" });
        await db.SaveChangesAsync();

        var alertas = await Evaluar(db, opciones => opciones.Habilitado = false);

        alertas.Should().BeEmpty();
    }

    // --- helpers -----------------------------------------------------------

    private static async Task<IReadOnlyList<SecurityAlert>> Evaluar(
        AppDbContext db,
        Action<SecurityAlertOptions>? configurar = null)
    {
        var opciones = new SecurityAlertOptions
        {
            VentanaMinutos = 5,
            EnfriamientoMinutos = 60,
            MaxLoginFallidosPorCuenta = 5,
            MaxCuentasPorIp = 10,
            MaxPasswordResets = 3,
            MinErroresAuthParaAlertar = 20,
            FactorSobreLineaBase = 3.0,
            VentanasLineaBase = 12,
            DiasHistoricoIpConocida = 90
        };
        configurar?.Invoke(opciones);

        var reloj = new FakeClock(Ahora);
        var servicio = new SecurityAlertService(
            db,
            new AlertDispatcherEnMemoria(db, reloj),
            reloj,
            Options.Create(opciones));

        return await servicio.EvaluarYNotificarAsync(CancellationToken.None);
    }

    private static void AddEventos(
        AppDbContext db,
        int cantidad,
        string tipoAccion,
        string ip,
        Guid? usuarioId = null,
        object? detalles = null)
    {
        for (var i = 0; i < cantidad; i++)
        {
            db.Auditorias.Add(Evento(tipoAccion, Ahora.AddSeconds(-10 - i), ip, usuarioId, detalles));
        }
    }

    private static Auditoria Evento(
        string tipoAccion,
        DateTime timestamp,
        string ip,
        Guid? usuarioId,
        object? detalles = null) => new()
        {
            Id = Guid.NewGuid(),
            TipoAccion = tipoAccion,
            Timestamp = timestamp,
            IpAddress = IPAddress.Parse(ip),
            UsuarioId = usuarioId,
            Origen = AuditOrigenes.Ui,
            DetallesJson = detalles is null ? null : JsonSerializer.Serialize(detalles)
        };

    private static AppDbContext BuildDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    /// <summary>
    /// Despachador real en lo que importa para estos tests (escribe la fila
    /// ALERTA_SEGURIDAD_DISPARADA, que es el estado de deduplicacion) y sin
    /// canales externos, que no son lo que se esta probando aqui.
    /// </summary>
    private sealed class AlertDispatcherEnMemoria : IAlertDispatcher
    {
        private readonly AppDbContext _db;
        private readonly IClock _clock;

        public AlertDispatcherEnMemoria(AppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<HashSet<string>> ClavesEnEnfriamientoAsync(DateTime ahoraUtc, CancellationToken cancellationToken)
        {
            var desde = ahoraUtc.AddMinutes(-60);
            var recientes = await _db.Auditorias
                .Where(a => a.TipoAccion == AuditActions.AlertaSeguridadDisparada && a.Timestamp >= desde)
                .Select(a => a.DetallesJson)
                .ToListAsync(cancellationToken);

            var claves = new HashSet<string>(StringComparer.Ordinal);
            foreach (var detalle in recientes)
            {
                var regla = AlertDispatcher.LeerCampoTexto(detalle, "regla");
                var clave = AlertDispatcher.LeerCampoTexto(detalle, "clave");
                if (regla is not null && clave is not null)
                {
                    claves.Add($"{regla}|{clave}");
                }
            }

            return claves;
        }

        public async Task DespacharAsync(SecurityAlert alerta, CancellationToken cancellationToken)
        {
            _db.Auditorias.Add(new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = AuditActions.AlertaSeguridadDisparada,
                Timestamp = _clock.UtcNow,
                Origen = AuditOrigenes.Job,
                DetallesJson = JsonSerializer.Serialize(new { regla = alerta.Regla, clave = alerta.Clave })
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
