using System.Text.Json;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

public interface IAlertaService
{
    Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, CancellationToken cancellationToken);
}

public sealed class AlertaService : IAlertaService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AlertaService> _logger;

    public AlertaService(AppDbContext dbContext, IEmailService emailService, IAuditService auditService, ILogger<AlertaService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var cuenta = await (
                from c in _dbContext.Cuentas
                join t in _dbContext.Titulares on c.TitularId equals t.Id
                where c.Id == cuentaId && c.Activa
                select new
                {
                    c.Id,
                    c.Nombre,
                    c.Divisa,
                    TitularId = t.Id,
                    TitularNombre = t.Nombre,
                    TitularTipo = t.Tipo
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (cuenta is null)
        {
            return;
        }

        var saldoActual = await _dbContext.Extractos
            .Where(x => x.CuentaId == cuentaId)
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.FilaNumero)
            .Select(x => (decimal?)x.Saldo)
            .FirstOrDefaultAsync(cancellationToken);

        if (!saldoActual.HasValue)
        {
            return;
        }

        var alertas = await _dbContext.AlertasSaldo
            .Where(x => x.Activa && (x.CuentaId == cuentaId || (x.CuentaId == null && (x.TipoTitular == null || x.TipoTitular == cuenta.TitularTipo))))
            .OrderByDescending(x => x.CuentaId == cuentaId)
            .ThenByDescending(x => x.TipoTitular == cuenta.TitularTipo)
            .ThenByDescending(x => x.FechaCreacion)
            .ToListAsync(cancellationToken);

        var alertaAplicable = alertas.FirstOrDefault();
        if (alertaAplicable is null)
        {
            return;
        }

        if (saldoActual.Value >= alertaAplicable.SaldoMinimo)
        {
            return;
        }

        var ultimoConcepto = await _dbContext.Extractos
            .Where(x => x.CuentaId == cuentaId)
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.FilaNumero)
            .Select(x => x.Concepto)
            .FirstOrDefaultAsync(cancellationToken);

        var destinatarioUserIds = await _dbContext.AlertaDestinatarios
            .Where(x => x.AlertaId == alertaAplicable.Id)
            .Select(x => x.UsuarioId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var recipients = await ResolveRecipientEmailsAsync(destinatarioUserIds, cancellationToken);
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "No se envia email de alerta por saldo bajo: alerta_id={AlertaId} sin destinatarios validos",
                alertaAplicable.Id);
        }
        else
        {
            try
            {
                await _emailService.SendSaldoBajoAlertAsync(
                    recipients,
                    cuenta.TitularNombre,
                    cuenta.Nombre,
                    cuenta.Id,
                    cuenta.Divisa,
                    saldoActual.Value,
                    alertaAplicable.SaldoMinimo,
                    ultimoConcepto,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Fallo al enviar email de alerta por saldo bajo. alerta_id={AlertaId}, cuenta_id={CuentaId}",
                    alertaAplicable.Id,
                    cuenta.Id);
            }
        }

        alertaAplicable.FechaUltimaAlerta = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            actorUserId,
            AuditActions.AlertaSaldoDisparada,
            "ALERTAS_SALDO",
            alertaAplicable.Id,
            ipAddress: null,
            detallesJson: JsonSerializer.Serialize(new
            {
                cuenta_id = cuenta.Id,
                cuenta_nombre = cuenta.Nombre,
                titular_id = cuenta.TitularId,
                titular_nombre = cuenta.TitularNombre,
                tipo_titular = cuenta.TitularTipo.ToString(),
                saldo_actual = saldoActual.Value,
                saldo_minimo = alertaAplicable.SaldoMinimo,
                alcance = alertaAplicable.CuentaId.HasValue ? "CUENTA" : alertaAplicable.TipoTitular.HasValue ? "TIPO_TITULAR" : "GLOBAL",
                destinatarios = recipients.Count
            }),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, CancellationToken cancellationToken)
    {
        var cuentasQuery = _dbContext.Cuentas.Where(c => c.Activa);
        if (!scope.IsAdmin)
        {
            if (!scope.HasPermissions)
            {
                return [];
            }

            if (!scope.HasGlobalAccess)
            {
                cuentasQuery = cuentasQuery.Where(c =>
                    scope.CuentaIds.Contains(c.Id) ||
                    scope.TitularIds.Contains(c.TitularId));
            }
        }

        var cuentas = await (
                from c in cuentasQuery
                join t in _dbContext.Titulares on c.TitularId equals t.Id
                select new
                {
                    c.Id,
                    c.Nombre,
                    c.Divisa,
                    TitularId = t.Id,
                    TitularNombre = t.Nombre,
                    TitularTipo = t.Tipo
                })
            .ToListAsync(cancellationToken);

        if (cuentas.Count == 0)
        {
            return [];
        }

        var cuentaIds = cuentas.Select(x => x.Id).ToList();
        var latestRows = await _dbContext.Extractos
            .Where(x => cuentaIds.Contains(x.CuentaId))
            .GroupBy(x => x.CuentaId)
            .Select(g => g
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FilaNumero)
                .Select(x => new { x.CuentaId, x.Saldo })
                .First())
            .ToListAsync(cancellationToken);

        var saldoByCuenta = latestRows.ToDictionary(x => x.CuentaId, x => x.Saldo);

        var alertas = await _dbContext.AlertasSaldo
            .Where(x => x.Activa && (x.CuentaId == null || cuentaIds.Contains(x.CuentaId.Value)))
            .OrderByDescending(x => x.CuentaId.HasValue)
            .ThenByDescending(x => x.TipoTitular.HasValue)
            .ThenByDescending(x => x.FechaCreacion)
            .ToListAsync(cancellationToken);

        var globalAlert = alertas.FirstOrDefault(x => x.CuentaId == null && x.TipoTitular == null);
        var alertByTipoTitular = alertas
            .Where(x => x.CuentaId == null && x.TipoTitular.HasValue)
            .GroupBy(x => x.TipoTitular!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var alertByCuenta = alertas
            .Where(x => x.CuentaId.HasValue)
            .GroupBy(x => x.CuentaId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<AlertaActivaItemResponse>();
        foreach (var cuenta in cuentas)
        {
            if (!saldoByCuenta.TryGetValue(cuenta.Id, out var saldoActual))
            {
                continue;
            }

            AlertaSaldo? alertaAplicable = null;
            if (alertByCuenta.TryGetValue(cuenta.Id, out var alertaCuenta))
            {
                alertaAplicable = alertaCuenta;
            }
            else if (alertByTipoTitular.TryGetValue(cuenta.TitularTipo, out var alertaTipo))
            {
                alertaAplicable = alertaTipo;
            }
            else if (globalAlert is not null)
            {
                alertaAplicable = globalAlert;
            }

            if (alertaAplicable is null || saldoActual >= alertaAplicable.SaldoMinimo)
            {
                continue;
            }

            result.Add(new AlertaActivaItemResponse
            {
                AlertaId = alertaAplicable.Id,
                CuentaId = cuenta.Id,
                TitularId = cuenta.TitularId,
                CuentaNombre = cuenta.Nombre,
                TitularNombre = cuenta.TitularNombre,
                TipoTitular = cuenta.TitularTipo.ToString(),
                Divisa = cuenta.Divisa,
                SaldoActual = saldoActual,
                SaldoMinimo = alertaAplicable.SaldoMinimo
            });
        }

        return result
            .OrderBy(x => x.TitularNombre)
            .ThenBy(x => x.CuentaNombre)
            .ToList();
    }

    private async Task<List<string>> ResolveRecipientEmailsAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var loginEmails = await _dbContext.Usuarios
            .Where(x => userIds.Contains(x.Id) && x.Activo)
            .Select(x => x.Email.ToLower())
            .ToListAsync(cancellationToken);

        var extraEmails = await _dbContext.UsuarioEmails
            .Where(x => userIds.Contains(x.UsuarioId))
            .Select(x => x.Email.ToLower())
            .ToListAsync(cancellationToken);

        return loginEmails
            .Concat(extraEmails)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }
}
