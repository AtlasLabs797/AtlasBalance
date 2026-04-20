using System.Security.Claims;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Constants;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize]
[Route("api/alertas")]
public sealed class AlertasController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IUserAccessService _userAccessService;
    private readonly IAlertaService _alertaService;

    public AlertasController(
        AppDbContext dbContext,
        IAuditService auditService,
        IUserAccessService userAccessService,
        IAlertaService alertaService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _userAccessService = userAccessService;
        _alertaService = alertaService;
    }

    [HttpGet]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var data = await (
                from a in _dbContext.AlertasSaldo
                join c in _dbContext.Cuentas on a.CuentaId equals c.Id into cuentaJoin
                from cuenta in cuentaJoin.DefaultIfEmpty()
                join t in _dbContext.Titulares on cuenta.TitularId equals t.Id into titularJoin
                from titular in titularJoin.DefaultIfEmpty()
                orderby a.CuentaId == null descending, titular.Nombre, cuenta.Nombre
                select new
                {
                    Alerta = a,
                    Cuenta = cuenta,
                    Titular = titular
                })
            .ToListAsync(cancellationToken);

        var alertaIds = data.Select(x => x.Alerta.Id).ToList();
        var destinatarios = await (
                from d in _dbContext.AlertaDestinatarios
                join u in _dbContext.Usuarios on d.UsuarioId equals u.Id
                where alertaIds.Contains(d.AlertaId)
                select new
                {
                    d.AlertaId,
                    d.UsuarioId,
                    u.NombreCompleto,
                    EmailLogin = u.Email
                })
            .ToListAsync(cancellationToken);

        var destinatarioMap = destinatarios
            .GroupBy(x => x.AlertaId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AlertaDestinatarioItemResponse>)g
                    .OrderBy(x => x.NombreCompleto)
                    .Select(x => new AlertaDestinatarioItemResponse
                    {
                        UsuarioId = x.UsuarioId,
                        NombreCompleto = x.NombreCompleto,
                        EmailLogin = x.EmailLogin
                    })
                    .ToList());

        var response = data.Select(x => new AlertaSaldoItemResponse
        {
            Id = x.Alerta.Id,
            CuentaId = x.Alerta.CuentaId,
            CuentaNombre = x.Cuenta?.Nombre,
            TitularId = x.Cuenta?.TitularId,
            TitularNombre = x.Titular?.Nombre,
            Divisa = x.Cuenta?.Divisa,
            SaldoMinimo = x.Alerta.SaldoMinimo,
            Activa = x.Alerta.Activa,
            FechaCreacion = x.Alerta.FechaCreacion,
            FechaUltimaAlerta = x.Alerta.FechaUltimaAlerta,
            Destinatarios = destinatarioMap.GetValueOrDefault(x.Alerta.Id, [])
        }).ToList();

        return Ok(response);
    }

    [HttpGet("contexto")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Contexto(CancellationToken cancellationToken)
    {
        var cuentas = await (
                from c in _dbContext.Cuentas
                join t in _dbContext.Titulares on c.TitularId equals t.Id
                where c.Activa
                orderby t.Nombre, c.Nombre
                select new AlertaContextoCuentaResponse
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    TitularId = t.Id,
                    TitularNombre = t.Nombre,
                    Divisa = c.Divisa
                })
            .ToListAsync(cancellationToken);

        var usuarios = await _dbContext.Usuarios
            .Where(x => x.Activo)
            .OrderBy(x => x.NombreCompleto)
            .Select(x => new AlertaContextoUsuarioResponse
            {
                Id = x.Id,
                NombreCompleto = x.NombreCompleto,
                Email = x.Email
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            cuentas,
            usuarios
        });
    }

    [HttpGet("activas")]
    public async Task<IActionResult> Activas(CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        var data = await _alertaService.GetAlertasActivasAsync(scope, cancellationToken);
        return Ok(data);
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Crear([FromBody] SaveAlertaSaldoRequest request, CancellationToken cancellationToken)
    {
        if (request.SaldoMinimo < 0)
        {
            return BadRequest(new { error = "Saldo mínimo inválido" });
        }

        if (request.CuentaId.HasValue)
        {
            var cuentaExists = await _dbContext.Cuentas.AnyAsync(
                x => x.Id == request.CuentaId.Value && x.Activa,
                cancellationToken);
            if (!cuentaExists)
            {
                return BadRequest(new { error = "Cuenta inválida o inactiva" });
            }
        }

        var duplicate = await _dbContext.AlertasSaldo.AnyAsync(
            x => x.CuentaId == request.CuentaId,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new { error = "Ya existe una alerta para esa cuenta (o global)" });
        }

        var invalidUsers = await ValidateDestinatariosAsync(request.DestinatarioUsuarioIds, cancellationToken);
        if (invalidUsers.Count > 0)
        {
            return BadRequest(new { error = "Hay destinatarios inválidos" });
        }

        var alerta = new AlertaSaldo
        {
            Id = Guid.NewGuid(),
            CuentaId = request.CuentaId,
            SaldoMinimo = request.SaldoMinimo,
            Activa = request.Activa,
            FechaCreacion = DateTime.UtcNow
        };
        _dbContext.AlertasSaldo.Add(alerta);
        await UpsertDestinatariosAsync(alerta.Id, request.DestinatarioUsuarioIds, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await LogAlertaAuditAsync(AuditActions.ConfigAlerta, alerta.Id, before: null, after: request, cancellationToken);
        return CreatedAtAction(nameof(Listar), new { id = alerta.Id }, new { id = alerta.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] SaveAlertaSaldoRequest request, CancellationToken cancellationToken)
    {
        var alerta = await _dbContext.AlertasSaldo.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (alerta is null)
        {
            return NotFound(new { error = "Alerta no encontrada" });
        }

        if (request.SaldoMinimo < 0)
        {
            return BadRequest(new { error = "Saldo mínimo inválido" });
        }

        if (request.CuentaId.HasValue)
        {
            var cuentaExists = await _dbContext.Cuentas.AnyAsync(
                x => x.Id == request.CuentaId.Value && x.Activa,
                cancellationToken);
            if (!cuentaExists)
            {
                return BadRequest(new { error = "Cuenta inválida o inactiva" });
            }
        }

        var duplicate = await _dbContext.AlertasSaldo.AnyAsync(
            x => x.Id != id && x.CuentaId == request.CuentaId,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new { error = "Ya existe una alerta para esa cuenta (o global)" });
        }

        var invalidUsers = await ValidateDestinatariosAsync(request.DestinatarioUsuarioIds, cancellationToken);
        if (invalidUsers.Count > 0)
        {
            return BadRequest(new { error = "Hay destinatarios inválidos" });
        }

        var before = new
        {
            alerta.CuentaId,
            alerta.SaldoMinimo,
            alerta.Activa,
            destinatarios = await _dbContext.AlertaDestinatarios.Where(x => x.AlertaId == id).Select(x => x.UsuarioId).ToListAsync(cancellationToken)
        };

        alerta.CuentaId = request.CuentaId;
        alerta.SaldoMinimo = request.SaldoMinimo;
        alerta.Activa = request.Activa;
        await UpsertDestinatariosAsync(alerta.Id, request.DestinatarioUsuarioIds, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await LogAlertaAuditAsync(AuditActions.ConfigAlerta, alerta.Id, before, request, cancellationToken);
        return Ok(new { message = "Alerta actualizada" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var alerta = await _dbContext.AlertasSaldo.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (alerta is null)
        {
            return NotFound(new { error = "Alerta no encontrada" });
        }

        var before = new
        {
            alerta.CuentaId,
            alerta.SaldoMinimo,
            alerta.Activa,
            destinatarios = await _dbContext.AlertaDestinatarios.Where(x => x.AlertaId == id).Select(x => x.UsuarioId).ToListAsync(cancellationToken)
        };

        var destinatarios = await _dbContext.AlertaDestinatarios.Where(x => x.AlertaId == id).ToListAsync(cancellationToken);
        _dbContext.AlertaDestinatarios.RemoveRange(destinatarios);
        _dbContext.AlertasSaldo.Remove(alerta);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await LogAlertaAuditAsync(AuditActions.ConfigAlerta, id, before, null, cancellationToken);
        return Ok(new { message = "Alerta eliminada" });
    }

    private async Task<List<Guid>> ValidateDestinatariosAsync(IReadOnlyList<Guid> destinatarioUsuarioIds, CancellationToken cancellationToken)
    {
        if (destinatarioUsuarioIds.Count == 0)
        {
            return [];
        }

        var unique = destinatarioUsuarioIds.Distinct().ToList();
        var existing = await _dbContext.Usuarios
            .Where(x => unique.Contains(x.Id) && x.Activo)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return unique.Except(existing).ToList();
    }

    private async Task UpsertDestinatariosAsync(Guid alertaId, IReadOnlyList<Guid> destinatarioUsuarioIds, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.AlertaDestinatarios
            .Where(x => x.AlertaId == alertaId)
            .ToListAsync(cancellationToken);

        _dbContext.AlertaDestinatarios.RemoveRange(existing);
        foreach (var userId in destinatarioUsuarioIds.Distinct())
        {
            _dbContext.AlertaDestinatarios.Add(new AlertaDestinatario
            {
                Id = Guid.NewGuid(),
                AlertaId = alertaId,
                UsuarioId = userId
            });
        }
    }

    private async Task LogAlertaAuditAsync(string action, Guid alertaId, object? before, object? after, CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        await _auditService.LogAsync(
            actorUserId,
            action,
            "ALERTAS_SALDO",
            alertaId,
            HttpContext,
            JsonSerializer.Serialize(new { before, after }),
            cancellationToken);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
