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
    private readonly IPlazoFijoService _plazoFijoService;

    public CuentasController(AppDbContext dbContext, IUserAccessService userAccessService, IAuditService auditService, IPlazoFijoService plazoFijoService)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
        _auditService = auditService;
        _plazoFijoService = plazoFijoService;
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
        [FromQuery] TipoTitular? tipoTitular = null,
        [FromQuery] TipoCuenta? tipoCuenta = null,
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

        if (tipoCuenta.HasValue)
        {
            query = query.Where(c => c.TipoCuenta == tipoCuenta.Value);
        }

        if (tipoTitular.HasValue)
        {
            query = query.Where(c => _dbContext.Titulares.Any(t => t.Id == c.TitularId && t.Tipo == tipoTitular.Value));
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
        var pageRows = await query
            .Join(
                _dbContext.Titulares.IgnoreQueryFilters(),
                c => c.TitularId,
                t => t.Id,
                (c, t) => new { Cuenta = c, Titular = t })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var plazoMap = await BuildPlazoFijoMapAsync(pageRows.Select(x => x.Cuenta.Id).ToList(), cancellationToken);
        var data = pageRows
            .Select(x => new CuentaListItemResponse
                {
                    Id = x.Cuenta.Id,
                    TitularId = x.Cuenta.TitularId,
                    TitularNombre = x.Titular.Nombre,
                    TitularTipo = x.Titular.Tipo.ToString(),
                    Nombre = x.Cuenta.Nombre,
                    NumeroCuenta = x.Cuenta.NumeroCuenta,
                    Iban = x.Cuenta.Iban,
                    BancoNombre = x.Cuenta.BancoNombre,
                    Divisa = x.Cuenta.Divisa,
                    FormatoId = x.Cuenta.FormatoId,
                    EsEfectivo = x.Cuenta.EsEfectivo,
                    TipoCuenta = ResolveTipoCuenta(x.Cuenta).ToString(),
                    PlazoFijo = plazoMap.GetValueOrDefault(x.Cuenta.Id),
                    Activa = x.Cuenta.Activa,
                    Notas = x.Cuenta.Notas,
                    FechaCreacion = x.Cuenta.FechaCreacion,
                    DeletedAt = x.Cuenta.DeletedAt
                })
            .ToList();

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
            .Select(t => new { t.Nombre, t.Tipo })
            .FirstOrDefaultAsync(cancellationToken);
        var plazoMap = await BuildPlazoFijoMapAsync([cuenta.Id], cancellationToken);

        return Ok(new CuentaListItemResponse
        {
            Id = cuenta.Id,
            TitularId = cuenta.TitularId,
            TitularNombre = titular?.Nombre ?? string.Empty,
            TitularTipo = titular?.Tipo.ToString() ?? string.Empty,
            Nombre = cuenta.Nombre,
            NumeroCuenta = cuenta.NumeroCuenta,
            Iban = cuenta.Iban,
            BancoNombre = cuenta.BancoNombre,
            Divisa = cuenta.Divisa,
            FormatoId = cuenta.FormatoId,
            EsEfectivo = cuenta.EsEfectivo,
            TipoCuenta = ResolveTipoCuenta(cuenta).ToString(),
            PlazoFijo = plazoMap.GetValueOrDefault(cuenta.Id),
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

        var cuenta = await _dbContext.Cuentas
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.Nombre,
                c.Divisa,
                c.EsEfectivo,
                c.TipoCuenta,
                c.TitularId,
                c.Notas
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (cuenta is null)
        {
            return NotFound(new { error = "Cuenta no encontrada" });
        }

        var titular = await _dbContext.Titulares
            .Where(t => t.Id == cuenta.TitularId)
            .Select(t => t.Nombre)
            .FirstOrDefaultAsync(cancellationToken);

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
        var last = await _dbContext.Extractos
            .Where(e => e.CuentaId == id)
            .OrderByDescending(e => e.FechaModificacion ?? e.FechaCreacion)
            .Select(e => (DateTime?)(e.FechaModificacion ?? e.FechaCreacion))
            .FirstOrDefaultAsync(cancellationToken);
        var tipoCuenta = ResolveTipoCuenta(new Cuenta { TipoCuenta = cuenta.TipoCuenta, EsEfectivo = cuenta.EsEfectivo });
        var plazoMap = await BuildPlazoFijoMapAsync([cuenta.Id], cancellationToken);

        return Ok(new CuentaResumenResponse
        {
            CuentaId = cuenta.Id,
            CuentaNombre = cuenta.Nombre,
            Divisa = cuenta.Divisa,
            TitularId = cuenta.TitularId,
            TitularNombre = titular ?? string.Empty,
            EsEfectivo = cuenta.EsEfectivo,
            TipoCuenta = tipoCuenta.ToString(),
            PlazoFijo = plazoMap.GetValueOrDefault(cuenta.Id),
            Notas = cuenta.Notas,
            SaldoActual = latest?.Saldo ?? 0m,
            IngresosMes = resumenMensual?.Ingresos ?? 0,
            EgresosMes = resumenMensual?.Egresos ?? 0,
            UltimaActualizacion = last
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
            FormatoId = validation.TipoCuenta == TipoCuenta.NORMAL ? request.FormatoId : null,
            TipoCuenta = validation.TipoCuenta,
            EsEfectivo = validation.TipoCuenta == TipoCuenta.EFECTIVO,
            Activa = request.Activa,
            Notas = NormalizeOptionalText(request.Notas),
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.Cuentas.Add(cuenta);
        if (validation.PlazoFijo is not null)
        {
            _dbContext.PlazosFijos.Add(new PlazoFijo
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                CuentaReferenciaId = validation.PlazoFijo.CuentaReferenciaId,
                FechaInicio = validation.PlazoFijo.FechaInicio!.Value,
                FechaVencimiento = validation.PlazoFijo.FechaVencimiento!.Value,
                InteresPrevisto = validation.PlazoFijo.InteresPrevisto,
                Renovable = validation.PlazoFijo.Renovable,
                Estado = EstadoPlazoFijo.ACTIVO,
                Notas = NormalizeOptionalText(validation.PlazoFijo.Notas),
                FechaCreacion = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            validation.TipoCuenta == TipoCuenta.PLAZO_FIJO ? "cuenta_plazo_fijo_creada" : "cuenta_creada",
            "CUENTAS",
            cuenta.Id,
            HttpContext,
            JsonSerializer.Serialize(new { cuenta.Nombre, cuenta.Divisa, tipo_cuenta = cuenta.TipoCuenta.ToString(), cuenta.Notas, plazo_fijo = validation.PlazoFijo }),
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

        var previousTipoCuenta = ResolveTipoCuenta(cuenta);
        if (previousTipoCuenta == TipoCuenta.PLAZO_FIJO && validation.TipoCuenta != TipoCuenta.PLAZO_FIJO)
        {
            return BadRequest(new { error = "No se puede convertir una cuenta de plazo fijo a otro tipo; crea una cuenta nueva" });
        }

        cuenta.TitularId = request.TitularId;
        cuenta.Nombre = request.Nombre.Trim();
        cuenta.NumeroCuenta = validation.NumeroCuenta;
        cuenta.Iban = validation.Iban;
        cuenta.BancoNombre = validation.BancoNombre;
        cuenta.Divisa = validation.Divisa!;
        cuenta.FormatoId = validation.TipoCuenta == TipoCuenta.NORMAL ? request.FormatoId : null;
        cuenta.TipoCuenta = validation.TipoCuenta;
        cuenta.EsEfectivo = validation.TipoCuenta == TipoCuenta.EFECTIVO;
        cuenta.Activa = request.Activa;
        cuenta.Notas = NormalizeOptionalText(request.Notas);

        if (validation.TipoCuenta == TipoCuenta.PLAZO_FIJO && validation.PlazoFijo is not null)
        {
            var plazo = await _dbContext.PlazosFijos.FirstOrDefaultAsync(p => p.CuentaId == cuenta.Id, cancellationToken);
            if (plazo is null)
            {
                plazo = new PlazoFijo
                {
                    Id = Guid.NewGuid(),
                    CuentaId = cuenta.Id,
                    FechaCreacion = DateTime.UtcNow
                };
                _dbContext.PlazosFijos.Add(plazo);
            }

            plazo.CuentaReferenciaId = validation.PlazoFijo.CuentaReferenciaId;
            plazo.FechaInicio = validation.PlazoFijo.FechaInicio!.Value;
            plazo.FechaVencimiento = validation.PlazoFijo.FechaVencimiento!.Value;
            plazo.InteresPrevisto = validation.PlazoFijo.InteresPrevisto;
            plazo.Renovable = validation.PlazoFijo.Renovable;
            plazo.Notas = NormalizeOptionalText(validation.PlazoFijo.Notas);
            plazo.FechaModificacion = DateTime.UtcNow;
            if (plazo.Estado == EstadoPlazoFijo.VENCIDO && plazo.FechaVencimiento > DateOnly.FromDateTime(DateTime.UtcNow.Date))
            {
                plazo.Estado = EstadoPlazoFijo.ACTIVO;
                plazo.FechaUltimaNotificacion = null;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            validation.TipoCuenta == TipoCuenta.PLAZO_FIJO ? "cuenta_plazo_fijo_actualizada" : "cuenta_actualizada",
            "CUENTAS",
            cuenta.Id,
            HttpContext,
            JsonSerializer.Serialize(new { cuenta.Nombre, cuenta.Divisa, tipo_cuenta = cuenta.TipoCuenta.ToString(), cuenta.Activa, cuenta.Notas, plazo_fijo = validation.PlazoFijo }),
            cancellationToken);

        return Ok(new { message = "Cuenta actualizada" });
    }

    [HttpPost("{id:guid}/plazo-fijo/renovar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> RenovarPlazoFijo(Guid id, [FromBody] RenovarPlazoFijoRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request invalido" });
        }

        try
        {
            var result = await _plazoFijoService.RenovarAsync(id, request, GetCurrentUserId(), HttpContext, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    private async Task<CuentaValidationResult> ValidateCuentaRequestAsync(
        SaveCuentaRequest request,
        Guid? currentId,
        CancellationToken cancellationToken)
    {
        var tipoCuenta = ResolveRequestedTipoCuenta(request);
        var plazoFijo = ResolvePlazoFijoRequest(request);

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return CuentaValidationResult.Fail("Nombre es obligatorio", tipoCuenta);
        }

        var titularExists = await _dbContext.Titulares.AnyAsync(t => t.Id == request.TitularId, cancellationToken);
        if (!titularExists)
        {
            return CuentaValidationResult.Fail("Titular invalido", tipoCuenta);
        }

        var divisa = request.Divisa?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(divisa))
        {
            return CuentaValidationResult.Fail("Divisa es obligatoria", tipoCuenta);
        }

        var divisaExists = await _dbContext.DivisasActivas.AnyAsync(d => d.Activa && d.Codigo == divisa, cancellationToken);
        if (!divisaExists)
        {
            return CuentaValidationResult.Fail("La divisa indicada no esta activa", tipoCuenta);
        }

        if (tipoCuenta == TipoCuenta.NORMAL && request.FormatoId.HasValue)
        {
            var formato = await _dbContext.FormatosImportacion.FirstOrDefaultAsync(f => f.Id == request.FormatoId.Value, cancellationToken);
            if (formato is null)
            {
                return CuentaValidationResult.Fail("Formato de importacion invalido", tipoCuenta);
            }

            if (!string.IsNullOrWhiteSpace(formato.Divisa) &&
                !string.Equals(formato.Divisa, divisa, StringComparison.OrdinalIgnoreCase))
            {
                return CuentaValidationResult.Fail("La divisa de la cuenta debe coincidir con la del formato de importacion", tipoCuenta);
            }
        }

        if (tipoCuenta == TipoCuenta.PLAZO_FIJO)
        {
            if (plazoFijo?.FechaInicio is null || plazoFijo.FechaVencimiento is null)
            {
                return CuentaValidationResult.Fail("Fecha de inicio y fecha de vencimiento son obligatorias para plazo fijo", tipoCuenta);
            }

            if (plazoFijo.FechaVencimiento < plazoFijo.FechaInicio)
            {
                return CuentaValidationResult.Fail("La fecha de vencimiento no puede ser anterior a la fecha de inicio", tipoCuenta);
            }

            if (plazoFijo.InteresPrevisto.HasValue && plazoFijo.InteresPrevisto.Value < 0)
            {
                return CuentaValidationResult.Fail("El interes previsto no puede ser negativo", tipoCuenta);
            }

            if (plazoFijo.CuentaReferenciaId.HasValue)
            {
                if (plazoFijo.CuentaReferenciaId == currentId)
                {
                    return CuentaValidationResult.Fail("La cuenta de referencia no puede ser la misma cuenta", tipoCuenta);
                }

                var referencia = await _dbContext.Cuentas.FirstOrDefaultAsync(
                    c => c.Id == plazoFijo.CuentaReferenciaId.Value && c.Activa,
                    cancellationToken);
                if (referencia is null)
                {
                    return CuentaValidationResult.Fail("Cuenta de referencia invalida o inactiva", tipoCuenta);
                }
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
            return CuentaValidationResult.Fail("Ya existe una cuenta con ese nombre para el titular indicado", tipoCuenta);
        }

        return new CuentaValidationResult
        {
            TipoCuenta = tipoCuenta,
            Divisa = divisa,
            NumeroCuenta = tipoCuenta == TipoCuenta.NORMAL ? request.NumeroCuenta?.Trim() : null,
            Iban = tipoCuenta == TipoCuenta.NORMAL ? request.Iban?.Trim() : null,
            BancoNombre = tipoCuenta == TipoCuenta.NORMAL ? request.BancoNombre?.Trim() : null,
            PlazoFijo = tipoCuenta == TipoCuenta.PLAZO_FIJO ? plazoFijo : null
        };
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

    private static TipoCuenta ResolveTipoCuenta(Cuenta cuenta)
    {
        if (cuenta.TipoCuenta == TipoCuenta.NORMAL && cuenta.EsEfectivo)
        {
            return TipoCuenta.EFECTIVO;
        }

        return cuenta.TipoCuenta;
    }

    private static TipoCuenta ResolveRequestedTipoCuenta(SaveCuentaRequest request)
    {
        if (request.TipoCuenta.HasValue)
        {
            return request.TipoCuenta.Value;
        }

        return request.EsEfectivo ? TipoCuenta.EFECTIVO : TipoCuenta.NORMAL;
    }

    private static SavePlazoFijoRequest? ResolvePlazoFijoRequest(SaveCuentaRequest request)
    {
        if (request.PlazoFijo is not null)
        {
            return request.PlazoFijo;
        }

        if (request.FechaInicio is null &&
            request.FechaVencimiento is null &&
            request.InteresPrevisto is null &&
            request.Renovable is null &&
            request.CuentaReferenciaId is null &&
            string.IsNullOrWhiteSpace(request.PlazoFijoNotas))
        {
            return null;
        }

        return new SavePlazoFijoRequest
        {
            FechaInicio = request.FechaInicio,
            FechaVencimiento = request.FechaVencimiento,
            InteresPrevisto = request.InteresPrevisto,
            Renovable = request.Renovable ?? false,
            CuentaReferenciaId = request.CuentaReferenciaId,
            Notas = request.PlazoFijoNotas
        };
    }

    private async Task<Dictionary<Guid, PlazoFijoResponse>> BuildPlazoFijoMapAsync(IReadOnlyList<Guid> cuentaIds, CancellationToken cancellationToken)
    {
        if (cuentaIds.Count == 0)
        {
            return [];
        }

        var rows = await (
                from plazo in _dbContext.PlazosFijos
                join refCuenta in _dbContext.Cuentas.IgnoreQueryFilters() on plazo.CuentaReferenciaId equals refCuenta.Id into refJoin
                from cuentaReferencia in refJoin.DefaultIfEmpty()
                where cuentaIds.Contains(plazo.CuentaId)
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
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.CuentaId);
    }

    private sealed class CuentaValidationResult
    {
        public string? Error { get; init; }
        public TipoCuenta TipoCuenta { get; init; }
        public string? Divisa { get; init; }
        public string? NumeroCuenta { get; init; }
        public string? Iban { get; init; }
        public string? BancoNombre { get; init; }
        public SavePlazoFijoRequest? PlazoFijo { get; init; }

        public static CuentaValidationResult Fail(string error, TipoCuenta tipoCuenta) => new()
        {
            Error = error,
            TipoCuenta = tipoCuenta
        };
    }
}
