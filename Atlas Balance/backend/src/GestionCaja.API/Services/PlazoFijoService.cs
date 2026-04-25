using System.Text.Json;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

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

        var cambios = 0;
        foreach (var item in plazos)
        {
            var nuevoEstado = ResolveEstado(item.Plazo.FechaVencimiento, hoy);
            if (nuevoEstado is null)
            {
                continue;
            }

            var debeNotificar = item.Plazo.FechaUltimaNotificacion != hoy &&
                (item.Plazo.Estado != nuevoEstado.Value || nuevoEstado.Value == EstadoPlazoFijo.VENCIDO);

            item.Plazo.Estado = nuevoEstado.Value;
            item.Plazo.FechaModificacion = DateTime.UtcNow;
            if (debeNotificar)
            {
                item.Plazo.FechaUltimaNotificacion = hoy;
                _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
                {
                    Id = Guid.NewGuid(),
                    Tipo = "PLAZO_FIJO",
                    Mensaje = BuildNotificationMessage(item.Cuenta.Nombre, item.Plazo.FechaVencimiento, nuevoEstado.Value),
                    Leida = false,
                    Fecha = DateTime.UtcNow,
                    DetallesJson = JsonSerializer.Serialize(new
                    {
                        cuenta_id = item.Cuenta.Id,
                        cuenta_nombre = item.Cuenta.Nombre,
                        titular_id = item.Titular.Id,
                        titular_nombre = item.Titular.Nombre,
                        fecha_vencimiento = item.Plazo.FechaVencimiento,
                        estado = nuevoEstado.Value.ToString()
                    })
                });
            }

            await _auditService.LogAsync(
                null,
                nuevoEstado.Value == EstadoPlazoFijo.VENCIDO ? AuditActions.PlazoFijoVencido : AuditActions.PlazoFijoProximoVencer,
                "PLAZOS_FIJOS",
                item.Plazo.Id,
                ipAddress: null,
                detallesJson: JsonSerializer.Serialize(new
                {
                    cuenta_id = item.Cuenta.Id,
                    fecha_vencimiento = item.Plazo.FechaVencimiento,
                    estado = nuevoEstado.Value.ToString()
                }),
                cancellationToken);

            if (debeNotificar)
            {
                await TrySendEmailAsync(item.Cuenta, item.Titular, item.Plazo.FechaVencimiento, nuevoEstado.Value, cancellationToken);
            }

            cambios++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return cambios;
    }

    public async Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (request.NuevaFechaVencimiento < request.NuevaFechaInicio)
        {
            throw new InvalidOperationException("La fecha de vencimiento no puede ser anterior a la fecha de inicio");
        }

        if (request.InteresPrevisto.HasValue && request.InteresPrevisto.Value < 0)
        {
            throw new InvalidOperationException("El interes previsto no puede ser negativo");
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
            throw new InvalidOperationException("No se puede renovar un plazo fijo cancelado");
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

    private async Task TrySendEmailAsync(Cuenta cuenta, Titular titular, DateOnly fechaVencimiento, EstadoPlazoFijo estado, CancellationToken cancellationToken)
    {
        var recipients = await _dbContext.Usuarios
            .Where(u => u.Activo && u.Rol == RolUsuario.ADMIN)
            .Select(u => u.Email.ToLower())
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            return;
        }

        try
        {
            await _emailService.SendPlazoFijoVencimientoAsync(
                recipients,
                titular.Nombre,
                cuenta.Nombre,
                cuenta.Id,
                fechaVencimiento,
                estado,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al enviar email de plazo fijo. cuenta_id={CuentaId}", cuenta.Id);
        }
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

    private static string BuildNotificationMessage(string cuentaNombre, DateOnly fechaVencimiento, EstadoPlazoFijo estado) =>
        estado == EstadoPlazoFijo.VENCIDO
            ? $"El plazo fijo {cuentaNombre} vencio el {fechaVencimiento:yyyy-MM-dd}."
            : $"El plazo fijo {cuentaNombre} vence el {fechaVencimiento:yyyy-MM-dd}.";

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
