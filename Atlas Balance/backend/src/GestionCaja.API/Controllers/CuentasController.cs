using System.Security.Claims;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize]
[Route("api/cuentas")]
public sealed class CuentasController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IUserAccessService _userAccessService;
    private readonly IAuditService _auditService;

    public CuentasController(AppDbContext dbContext, IUserAccessService userAccessService, IAuditService auditService)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
        _auditService = auditService;
    }

    [HttpGet("divisas-activas")]
    public async Task<IActionResult> DivisasActivas(CancellationToken cancellationToken)
    {
        var divisas = await _dbContext.DivisasActivas
            .Where(d => d.Activa)
            .OrderByDescending(d => d.EsBase)
            .ThenBy(d => d.Codigo)
            .Select(d => new
            {
                d.Codigo,
                d.Nombre
            })
            .ToListAsync(cancellationToken);

        return Ok(divisas);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "fecha_creacion",
        [FromQuery] string sortDir = "desc",
        [FromQuery] string? search = null,
        [FromQuery] Guid? titularId = null,
        [FromQuery] bool incluirEliminados = false,
        CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (!scope.IsAdmin)
        {
            incluirEliminados = false;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<Cuenta> query = incluirEliminados
            ? _dbContext.Cuentas.IgnoreQueryFilters()
            : _dbContext.Cuentas;

        query = _userAccessService.ApplyCuentaScope(query, scope);

        if (titularId.HasValue)
        {
            query = query.Where(c => c.TitularId == titularId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.Nombre.ToLower().Contains(term) ||
                (c.BancoNombre != null && c.BancoNombre.ToLower().Contains(term)) ||
                (c.NumeroCuenta != null && c.NumeroCuenta.ToLower().Contains(term)) ||
                (c.Iban != null && c.Iban.ToLower().Contains(term)) ||
                (c.Notas != null && c.Notas.ToLower().Contains(term)));
        }

        query = ApplySorting(query, sortBy, desc);

        var total = await query.CountAsync(cancellationToken);
        var data = await query
            .Join(
                _dbContext.Titulares.IgnoreQueryFilters(),
                c => c.TitularId,
                t => t.Id,
                (c, t) => new CuentaListItemResponse
                {
                    Id = c.Id,
                    TitularId = c.TitularId,
                    TitularNombre = t.Nombre,
                    Nombre = c.Nombre,
                    NumeroCuenta = c.NumeroCuenta,
                    Iban = c.Iban,
                    BancoNombre = c.BancoNombre,
                    Divisa = c.Divisa,
                    FormatoId = c.FormatoId,
                    EsEfectivo = c.EsEfectivo,
                    Activa = c.Activa,
                    Notas = c.Notas,
                    FechaCreacion = c.FechaCreacion,
                    DeletedAt = c.DeletedAt
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<CuentaListItemResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id, [FromQuery] bool incluirEliminados = false, CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (!scope.IsAdmin)
        {
            incluirEliminados = false;
        }

        var allowed = await _userAccessService.CanAccessCuentaAsync(id, scope, cancellationToken);
        if (!allowed)
        {
            return Forbid();
        }

        IQueryable<Cuenta> query = incluirEliminados
            ? _dbContext.Cuentas.IgnoreQueryFilters()
            : _dbContext.Cuentas;

        var cuenta = await query.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cuenta is null)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        var titular = await _dbContext.Titulares.IgnoreQueryFilters()
            .Where(t => t.Id == cuenta.TitularId)
            .Select(t => t.Nombre)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new CuentaListItemResponse
        {
            Id = cuenta.Id,
            TitularId = cuenta.TitularId,
            TitularNombre = titular ?? string.Empty,
            Nombre = cuenta.Nombre,
            NumeroCuenta = cuenta.NumeroCuenta,
            Iban = cuenta.Iban,
            BancoNombre = cuenta.BancoNombre,
            Divisa = cuenta.Divisa,
            FormatoId = cuenta.FormatoId,
            EsEfectivo = cuenta.EsEfectivo,
            Activa = cuenta.Activa,
            Notas = cuenta.Notas,
            FechaCreacion = cuenta.FechaCreacion,
            DeletedAt = cuenta.DeletedAt
        });
    }

    [HttpGet("{id:guid}/resumen")]
    public async Task<IActionResult> Resumen(Guid id, [FromQuery] string periodo = "1m", CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        var allowed = await _userAccessService.CanAccessCuentaAsync(id, scope, cancellationToken);
        if (!allowed)
        {
            return Forbid();
        }

        var exists = await _dbContext.Cuentas.AnyAsync(c => c.Id == id, cancellationToken);
        if (!exists)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        var latest = await _dbContext.Extractos
            .Where(e => e.CuentaId == id)
            .OrderByDescending(e => e.Fecha)
            .ThenByDescending(e => e.FilaNumero)
            .Select(e => new { e.Fecha, e.Saldo })
            .FirstOrDefaultAsync(cancellationToken);
        var periodEnd = latest?.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var periodStart = GetPeriodStart(NormalizePeriodo(periodo), periodEnd);

        var resumenMensual = await _dbContext.Extractos
            .Where(e => e.CuentaId == id && e.Fecha >= periodStart && e.Fecha <= periodEnd)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Ingresos = g.Sum(x => x.Monto > 0 ? x.Monto : 0),
                Egresos = g.Sum(x => x.Monto < 0 ? -x.Monto : 0)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new CuentaResumenResponse
        {
            CuentaId = id,
            SaldoActual = latest?.Saldo ?? 0m,
            IngresosMes = resumenMensual?.Ingresos ?? 0,
            EgresosMes = resumenMensual?.Egresos ?? 0
        });
    }

    private static string NormalizePeriodo(string? periodo)
    {
        var normalized = (periodo ?? "1m").Trim().ToLowerInvariant();
        return normalized switch
        {
            "1m" => "1m",
            "3m" => "3m",
            "6m" => "6m",
            "9m" => "9m",
            "12m" => "12m",
            "18m" => "18m",
            "24m" => "24m",
            _ => "1m"
        };
    }

    private static DateOnly GetPeriodStart(string periodo, DateOnly today)
    {
        var months = periodo switch
        {
            "1m" => 1,
            "3m" => 3,
            "6m" => 6,
            "9m" => 9,
            "12m" => 12,
            "18m" => 18,
            "24m" => 24,
            _ => 1
        };

        return today.AddMonths(-months);
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Crear([FromBody] SaveCuentaRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateCuentaRequestAsync(request, null, cancellationToken);
        if (validation.Error is not null)
        {
            return BadRequest(new { error = validation.Error });
        }

        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = request.TitularId,
            Nombre = request.Nombre.Trim(),
            NumeroCuenta = validation.NumeroCuenta,
            Iban = validation.Iban,
            BancoNombre = validation.BancoNombre,
            Divisa = validation.Divisa!,
            FormatoId = request.EsEfectivo ? null : request.FormatoId,
            EsEfectivo = request.EsEfectivo,
            Activa = request.Activa,
            Notas = NormalizeOptionalText(request.Notas),
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.Cuentas.Add(cuenta);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            "cuenta_creada",
            "CUENTAS",
            cuenta.Id,
            HttpContext,
            JsonSerializer.Serialize(new { cuenta.Nombre, cuenta.Divisa, cuenta.EsEfectivo, cuenta.Notas }),
            cancellationToken);

        return CreatedAtAction(nameof(Obtener), new { id = cuenta.Id }, new { id = cuenta.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] SaveCuentaRequest request, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cuenta is null)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        var validation = await ValidateCuentaRequestAsync(request, id, cancellationToken);
        if (validation.Error is not null)
        {
            return BadRequest(new { error = validation.Error });
        }

        cuenta.TitularId = request.TitularId;
        cuenta.Nombre = request.Nombre.Trim();
        cuenta.NumeroCuenta = validation.NumeroCuenta;
        cuenta.Iban = validation.Iban;
        cuenta.BancoNombre = validation.BancoNombre;
        cuenta.Divisa = validation.Divisa!;
        cuenta.FormatoId = request.EsEfectivo ? null : request.FormatoId;
        cuenta.EsEfectivo = request.EsEfectivo;
        cuenta.Activa = request.Activa;
        cuenta.Notas = NormalizeOptionalText(request.Notas);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            "cuenta_actualizada",
            "CUENTAS",
            cuenta.Id,
            HttpContext,
            JsonSerializer.Serialize(new { cuenta.Nombre, cuenta.Divisa, cuenta.EsEfectivo, cuenta.Activa, cuenta.Notas }),
            cancellationToken);

        return Ok(new { message = "Cuenta actualizada" });
    }

    [HttpPatch("{id:guid}/notas")]
    public async Task<IActionResult> ActualizarNotas(Guid id, [FromBody] UpdateCuentaNotasRequest request, CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        var cuenta = await _dbContext.Cuentas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cuenta is null)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        if (!await _userAccessService.CanAccessCuentaAsync(id, scope, cancellationToken))
        {
            return Forbid();
        }

        if (!await CanEditCuentaAsync(scope, cuenta, cancellationToken))
        {
            return Forbid();
        }

        var previous = cuenta.Notas;
        cuenta.Notas = NormalizeOptionalText(request.Notas);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            scope.UserId,
            "cuenta_notas_actualizadas",
            "CUENTAS",
            cuenta.Id,
            HttpContext,
            JsonSerializer.Serialize(new { valor_anterior = previous, valor_nuevo = cuenta.Notas }),
            cancellationToken);

        return Ok(new { message = "Notas guardadas", notas = cuenta.Notas });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cuenta is null)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        cuenta.Activa = false;
        cuenta.DeletedAt = DateTime.UtcNow;
        cuenta.DeletedById = GetCurrentUserId();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "cuenta_eliminada", "CUENTAS", cuenta.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Cuenta eliminada" });
    }

    [HttpPost("{id:guid}/restaurar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cuenta is null)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        cuenta.DeletedAt = null;
        cuenta.DeletedById = null;
        cuenta.Activa = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), "cuenta_restaurada", "CUENTAS", cuenta.Id, HttpContext, null, cancellationToken);

        return Ok(new { message = "Cuenta restaurada" });
    }

    private static IQueryable<Cuenta> ApplySorting(IQueryable<Cuenta> query, string sortBy, bool desc)
    {
        return (sortBy.ToLowerInvariant(), desc) switch
        {
            ("nombre", true) => query.OrderByDescending(c => c.Nombre),
            ("nombre", false) => query.OrderBy(c => c.Nombre),
            ("divisa", true) => query.OrderByDescending(c => c.Divisa),
            ("divisa", false) => query.OrderBy(c => c.Divisa),
            ("fecha_creacion", true) => query.OrderByDescending(c => c.FechaCreacion),
            ("fecha_creacion", false) => query.OrderBy(c => c.FechaCreacion),
            ("activa", true) => query.OrderByDescending(c => c.Activa),
            ("activa", false) => query.OrderBy(c => c.Activa),
            _ => query.OrderByDescending(c => c.FechaCreacion)
        };
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private async Task<(string? Error, string? Divisa, string? NumeroCuenta, string? Iban, string? BancoNombre)> ValidateCuentaRequestAsync(
        SaveCuentaRequest request,
        Guid? currentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return ("Nombre es obligatorio", null, null, null, null);
        }

        var titularExists = await _dbContext.Titulares.AnyAsync(t => t.Id == request.TitularId, cancellationToken);
        if (!titularExists)
        {
            return ("Titular invalido", null, null, null, null);
        }

        var divisa = request.Divisa?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(divisa))
        {
            return ("Divisa es obligatoria", null, null, null, null);
        }

        var divisaExists = await _dbContext.DivisasActivas.AnyAsync(d => d.Activa && d.Codigo == divisa, cancellationToken);
        if (!divisaExists)
        {
            return ("La divisa indicada no esta activa", null, null, null, null);
        }

        if (!request.EsEfectivo && request.FormatoId.HasValue)
        {
            var formato = await _dbContext.FormatosImportacion.FirstOrDefaultAsync(f => f.Id == request.FormatoId.Value, cancellationToken);
            if (formato is null)
            {
                return ("Formato de importacion invalido", null, null, null, null);
            }

            if (!string.IsNullOrWhiteSpace(formato.Divisa) &&
                !string.Equals(formato.Divisa, divisa, StringComparison.OrdinalIgnoreCase))
            {
                return ("La divisa de la cuenta debe coincidir con la del formato de importacion", null, null, null, null);
            }
        }

        var duplicateName = await _dbContext.Cuentas
            .IgnoreQueryFilters()
            .AnyAsync(
                c => c.Id != currentId &&
                     c.TitularId == request.TitularId &&
                     c.Nombre.ToLower() == request.Nombre.Trim().ToLower(),
                cancellationToken);

        if (duplicateName)
        {
            return ("Ya existe una cuenta con ese nombre para el titular indicado", null, null, null, null);
        }

        return (
            null,
            divisa,
            request.EsEfectivo ? null : request.NumeroCuenta?.Trim(),
            request.EsEfectivo ? null : request.Iban?.Trim(),
            request.EsEfectivo ? null : request.BancoNombre?.Trim());
    }

    private async Task<bool> CanEditCuentaAsync(UserAccessScope scope, Cuenta cuenta, CancellationToken cancellationToken)
    {
        if (scope.IsAdmin)
        {
            return true;
        }

        if (scope.UserId == Guid.Empty)
        {
            return false;
        }

        return await _dbContext.PermisosUsuario.AnyAsync(
            p => p.UsuarioId == scope.UserId &&
                 p.PuedeEditarLineas &&
                 (p.CuentaId == null || p.CuentaId == cuenta.Id) &&
                 (p.TitularId == null || p.TitularId == cuenta.TitularId),
            cancellationToken);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
