using System.Text.Json;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IPlazoFijoService
{
    Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken);
    Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken);
}

public sealed class PlazoFijoService : IPlazoFijoService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly ILogger<PlazoFijoService> _logger;

    public PlazoFijoService(AppDbContext dbContext, IEmailService emailService, IAuditService auditService, ILogger<PlazoFijoService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
    {
        var plazos = await (
            from plazo in _dbContext.PlazosFijos
            join cuenta in _dbContext.Cuentas on plazo.CuentaId equals cuenta.Id
            join titular in _dbContext.Titulares on cuenta.TitularId equals titular.Id
            where cuenta.Activa && plazo.Estado != EstadoPlazoFijo.CANCELADO && plazo.Estado != EstadoPlazoFijo.RENOVADO
            select new { Plazo = plazo, Cuenta = cuenta, Titular = titular })
            .ToListAsync(cancellationToken);

        // V-02-05 (HIGH-7): side effects materializados DESPUES del commit de los
        // cambios de estado. Email y notificacion admin solo se emiten si el commit
        // principal se aplico. Si la notificacion falla tras el commit, se loguea
        // y queda como deuda para el siguiente job (el PlazoFijo.FechaUltimaNotificacion
        // solo se actualiza si email y notificacion se completaron).
        var sideEffects = new List<(Cuenta Cuenta, Titular Titular, PlazoFijo Plazo, EstadoPlazoFijo NuevoEstado, bool EstadoCambio, bool DebeNotificar)>();

        var cambios = 0;
        foreach (var item in plazos)
        {
            var nuevoEstado = ResolveEstado(item.Plazo.FechaVencimiento, hoy);
            if (nuevoEstado is null)
            {
                continue;
            }

            var estadoCambio = item.Plazo.Estado != nuevoEstado.Value;
            var debeNotificar = item.Plazo.FechaUltimaNotificacion != hoy &&
                (estadoCambio || nuevoEstado.Value == EstadoPlazoFijo.VENCIDO);

            item.Plazo.Estado = nuevoEstado.Value;
            item.Plazo.FechaModificacion = DateTime.UtcNow;

            if (estadoCambio || debeNotificar)
            {
                sideEffects.Add((item.Cuenta, item.Titular, item.Plazo, nuevoEstado.Value, estadoCambio, debeNotificar));
            }

            if (estadoCambio || debeNotificar)
            {
                cambios++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // V-02-05 (MED-19): agrupar side effects por (titular, estado) y enviar UN
        // digest por destinatario. Antes: N emails (uno por plazo).
        var groupedByTitularEstado = sideEffects
            .Where(se => se.DebeNotificar)
            .GroupBy(se => (se.Titular.Id, se.NuevoEstado));

        foreach (var grupo in groupedByTitularEstado)
        {
            var representante = grupo.First();
            var items = grupo
                .Select(se => $"{se.Cuenta.Nombre} (vence {se.Plazo.FechaVencimiento:dd/MM/yyyy})")
                .ToList();
            var notificacionAdminCreada = await TryAddAdminNotificationAsync(
                representante.Cuenta, representante.Titular, representante.Plazo, representante.NuevoEstado, cancellationToken);
            var emailEnviado = await TrySendDigestEmailAsync(
                representante.Titular.Nombre, items, representante.NuevoEstado, cancellationToken);
            if (notificacionAdminCreada && emailEnviado)
            {
                foreach (var se in grupo)
                {
                    se.Plazo.FechaUltimaNotificacion = hoy;
                }
            }
        }

        foreach (var se in sideEffects)
        {
            await _auditService.LogAsync(
                null,
                se.NuevoEstado == EstadoPlazoFijo.VENCIDO ? AuditActions.PlazoFijoVencido : AuditActions.PlazoFijoProximoVencer,
                "PLAZOS_FIJOS",
                se.Plazo.Id,
                ipAddress: null,
                detallesJson: JsonSerializer.Serialize(new
                {
                    cuenta_id = se.Cuenta.Id,
                    fecha_vencimiento = se.Plazo.FechaVencimiento,
                    estado = se.NuevoEstado.ToString()
                }),
                cancellationToken);
        }

        if (sideEffects.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return cambios;
    }

    public async Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (request.NuevaFechaVencimiento < request.NuevaFechaInicio)
        {
            throw new BusinessRuleException("La fecha de vencimiento no puede ser anterior a la fecha de inicio");
        }

        if (request.InteresPrevisto.HasValue && request.InteresPrevisto.Value < 0)
        {
            throw new BusinessRuleException("El interes previsto no puede ser negativo");
        }

        var plazo = await _dbContext.PlazosFijos
            .Include(p => p.Cuenta)
            .FirstOrDefaultAsync(p => p.CuentaId == cuentaId, cancellationToken);

        if (plazo?.Cuenta is null || plazo.Cuenta.TipoCuenta != TipoCuenta.PLAZO_FIJO)
        {
            throw new KeyNotFoundException("Cuenta de plazo fijo no encontrada");
        }

        if (plazo.Estado == EstadoPlazoFijo.CANCELADO)
        {
            throw new BusinessRuleException("No se puede renovar un plazo fijo cancelado");
        }

        var before = new
        {
            plazo.FechaInicio,
            plazo.FechaVencimiento,
            plazo.InteresPrevisto,
            plazo.Renovable,
            Estado = plazo.Estado.ToString(),
            plazo.Notas
        };

        plazo.FechaInicio = request.NuevaFechaInicio;
        plazo.FechaVencimiento = request.NuevaFechaVencimiento;
        plazo.InteresPrevisto = request.InteresPrevisto;
        plazo.Renovable = request.Renovable;
        plazo.Notas = NormalizeOptionalText(request.Notas);
        plazo.Estado = EstadoPlazoFijo.ACTIVO;
        plazo.FechaUltimaNotificacion = null;
        plazo.FechaRenovacion = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        plazo.FechaModificacion = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            actorUserId,
            AuditActions.PlazoFijoRenovado,
            "PLAZOS_FIJOS",
            plazo.Id,
            httpContext,
            JsonSerializer.Serialize(new { before, after = request }),
            cancellationToken);

        return await BuildResponseAsync(plazo.CuentaId, cancellationToken)
            ?? throw new KeyNotFoundException("Cuenta de plazo fijo no encontrada");
    }

    private async Task<bool> TryAddAdminNotificationAsync(
        Cuenta cuenta,
        Titular titular,
        PlazoFijo plazo,
        EstadoPlazoFijo estado,
        CancellationToken cancellationToken)
    {
        var cuentaToken = cuenta.Id.ToString();
        var estadoToken = estado.ToString();
        var vencimientoToken = plazo.FechaVencimiento.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var alreadyExists = await _dbContext.NotificacionesAdmin.AnyAsync(
            x => x.Tipo == "PLAZO_FIJO" &&
                 x.DetallesJson != null &&
                 x.DetallesJson.Contains(cuentaToken) &&
                 x.DetallesJson.Contains(estadoToken) &&
                 x.DetallesJson.Contains(vencimientoToken),
            cancellationToken);

        if (alreadyExists)
        {
            return true;
        }

        _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
        {
            Id = Guid.NewGuid(),
            Tipo = "PLAZO_FIJO",
            Mensaje = BuildNotificationMessage(cuenta.Nombre, plazo.FechaVencimiento, estado),
            Leida = false,
            Fecha = DateTime.UtcNow,
            DetallesJson = JsonSerializer.Serialize(new
            {
                cuenta_id = cuenta.Id,
                cuenta_nombre = cuenta.Nombre,
                titular_id = titular.Id,
                titular_nombre = titular.Nombre,
                fecha_vencimiento = plazo.FechaVencimiento,
                estado = estado.ToString()
            })
        });

        return true;
    }

    private static EstadoPlazoFijo? ResolveEstado(DateOnly fechaVencimiento, DateOnly hoy)
    {
        if (fechaVencimiento <= hoy)
        {
            return EstadoPlazoFijo.VENCIDO;
        }

        return fechaVencimiento.DayNumber - hoy.DayNumber <= 14
            ? EstadoPlazoFijo.PROXIMO_VENCER
            : null;
    }

    // V-02-05 (MED-19): envio en formato digest. Un email por (titular, estado) con
    // la lista de plazos que tocan hoy, en lugar de un email por plazo.
    private async Task<bool> TrySendDigestEmailAsync(
        string titularNombre,
        IReadOnlyList<string> items,
        EstadoPlazoFijo estado,
        CancellationToken cancellationToken)
    {
        var recipients = await _dbContext.Usuarios
            .Where(u => u.Activo && u.Rol == RolUsuario.ADMIN)
            .Select(u => u.Email.ToLower())
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            _logger.LogWarning("No se envia email digest de plazos fijos: sin destinatarios admin activos");
            return false;
        }

        try
        {
            var digestSummary = string.Join("; ", items);
            await _emailService.SendPlazoFijoVencimientoAsync(
                recipients,
                titularNombre,
                $"Digest: {digestSummary}",
                Guid.Empty,
                DateOnly.MinValue,
                estado,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al enviar email digest de plazos fijos");
            return false;
        }
    }

    private static string BuildNotificationMessage(string cuentaNombre, DateOnly fechaVencimiento, EstadoPlazoFijo estado) =>
        estado == EstadoPlazoFijo.VENCIDO
            ? $"El plazo fijo {cuentaNombre} venció el {fechaVencimiento:dd/MM/yyyy}."
            : $"El plazo fijo {cuentaNombre} vence el {fechaVencimiento:dd/MM/yyyy}.";

    private async Task<PlazoFijoResponse?> BuildResponseAsync(Guid cuentaId, CancellationToken cancellationToken)
    {
        return await (
                from plazo in _dbContext.PlazosFijos
                join refCuenta in _dbContext.Cuentas on plazo.CuentaReferenciaId equals refCuenta.Id into refJoin
                from cuentaReferencia in refJoin.DefaultIfEmpty()
                where plazo.CuentaId == cuentaId
                select new PlazoFijoResponse
                {
                    Id = plazo.Id,
                    CuentaId = plazo.CuentaId,
                    CuentaReferenciaId = plazo.CuentaReferenciaId,
                    CuentaReferenciaNombre = cuentaReferencia != null ? cuentaReferencia.Nombre : null,
                    FechaInicio = plazo.FechaInicio,
                    FechaVencimiento = plazo.FechaVencimiento,
                    InteresPrevisto = plazo.InteresPrevisto,
                    Renovable = plazo.Renovable,
                    Estado = plazo.Estado.ToString(),
                    FechaUltimaNotificacion = plazo.FechaUltimaNotificacion,
                    FechaRenovacion = plazo.FechaRenovacion,
                    Notas = plazo.Notas
                })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
