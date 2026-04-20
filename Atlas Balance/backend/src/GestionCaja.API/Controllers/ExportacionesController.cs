using System.Security.Claims;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCaja.API.Controllers;

[ApiController]
[Authorize]
[Route("api/exportaciones")]
public sealed class ExportacionesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IExportacionService _exportacionService;
    private readonly IUserAccessService _userAccessService;
    private readonly ILogger<ExportacionesController> _logger;

    public ExportacionesController(
        AppDbContext dbContext,
        IExportacionService exportacionService,
        IUserAccessService userAccessService,
        ILogger<ExportacionesController>? logger = null)
    {
        _dbContext = dbContext;
        _exportacionService = exportacionService;
        _userAccessService = userAccessService;
        _logger = logger ?? NullLogger<ExportacionesController>.Instance;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "fecha_exportacion",
        [FromQuery] string sortDir = "desc",
        [FromQuery] Guid? cuentaId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        var cuentasPermitidas = _userAccessService.ApplyCuentaScope(_dbContext.Cuentas.AsNoTracking(), scope).Select(c => c.Id);
        var query = _dbContext.Exportaciones
            .AsNoTracking()
            .Where(e => cuentasPermitidas.Contains(e.CuentaId));

        if (cuentaId.HasValue)
        {
            query = query.Where(e => e.CuentaId == cuentaId.Value);
        }

        query = (sortBy.ToLowerInvariant(), desc) switch
        {
            ("estado", true) => query.OrderByDescending(e => e.Estado).ThenByDescending(e => e.FechaExportacion),
            ("estado", false) => query.OrderBy(e => e.Estado).ThenByDescending(e => e.FechaExportacion),
            ("tipo", true) => query.OrderByDescending(e => e.Tipo).ThenByDescending(e => e.FechaExportacion),
            ("tipo", false) => query.OrderBy(e => e.Tipo).ThenByDescending(e => e.FechaExportacion),
            ("fecha_exportacion", false) => query.OrderBy(e => e.FechaExportacion),
            _ => query.OrderByDescending(e => e.FechaExportacion)
        };

        var total = await query.CountAsync(cancellationToken);
        var pageItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var cuentaIds = pageItems.Select(e => e.CuentaId).Distinct().ToList();
        var cuentas = await _dbContext.Cuentas.IgnoreQueryFilters()
            .Where(c => cuentaIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Nombre, c.TitularId })
            .ToListAsync(cancellationToken);
        var titularIds = cuentas.Select(c => c.TitularId).Distinct().ToList();
        var titulares = await _dbContext.Titulares.IgnoreQueryFilters()
            .Where(t => titularIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Nombre })
            .ToListAsync(cancellationToken);
        var usuariosIds = pageItems.Where(x => x.IniciadoPorId.HasValue).Select(x => x.IniciadoPorId!.Value).Distinct().ToList();
        var usuarios = await _dbContext.Usuarios.IgnoreQueryFilters()
            .Where(u => usuariosIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NombreCompleto })
            .ToListAsync(cancellationToken);

        var cuentasMap = cuentas.ToDictionary(x => x.Id, x => x);
        var titularesMap = titulares.ToDictionary(x => x.Id, x => x.Nombre);
        var usuariosMap = usuarios.ToDictionary(x => x.Id, x => x.NombreCompleto);

        var data = pageItems.Select(e =>
        {
            var cuenta = cuentasMap.GetValueOrDefault(e.CuentaId);
            var titularNombre = cuenta is not null ? titularesMap.GetValueOrDefault(cuenta.TitularId) : null;
            return new ExportacionListItemResponse
            {
                Id = e.Id,
                CuentaId = e.CuentaId,
                CuentaNombre = cuenta?.Nombre ?? string.Empty,
                TitularNombre = titularNombre ?? string.Empty,
                FechaExportacion = e.FechaExportacion,
                RutaArchivo = string.IsNullOrWhiteSpace(e.RutaArchivo) ? null : Path.GetFileName(e.RutaArchivo),
                TamanioBytes = e.TamanioBytes,
                Estado = e.Estado.ToString(),
                Tipo = e.Tipo.ToString(),
                IniciadoPorId = e.IniciadoPorId,
                IniciadoPorNombre = e.IniciadoPorId.HasValue ? usuariosMap.GetValueOrDefault(e.IniciadoPorId.Value) : null
            };
        }).ToList();

        return Ok(new PaginatedResponse<ExportacionListItemResponse>
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpPost("manual")]
    [Authorize(Roles = "ADMIN,GERENTE")]
    public async Task<IActionResult> Manual([FromBody] ExportacionManualRequest request, CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        var canAccess = await _userAccessService.CanAccessCuentaAsync(request.CuentaId, scope, cancellationToken);
        if (!canAccess)
        {
            return Forbid();
        }

        try
        {
            var exportacion = await _exportacionService.ExportarCuentaAsync(
                request.CuentaId,
                TipoProceso.MANUAL,
                GetCurrentUserId(),
                cancellationToken);

            return Ok(new
            {
                exportacion.Id,
                Estado = exportacion.Estado.ToString(),
                RutaArchivo = string.IsNullOrWhiteSpace(exportacion.RutaArchivo) ? null : Path.GetFileName(exportacion.RutaArchivo),
                exportacion.TamanioBytes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al generar exportacion manual para cuenta {CuentaId}", request.CuentaId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "No se pudo generar la exportacion. Revise los logs del servidor." });
        }
    }

    [HttpGet("{id:guid}/descargar")]
    public async Task<IActionResult> Descargar(Guid id, CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        var exportacion = await _dbContext.Exportaciones
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (exportacion is null)
        {
            return NotFound(new { error = "Exportación no encontrada" });
        }

        var canAccess = await _userAccessService.CanAccessCuentaAsync(exportacion.CuentaId, scope, cancellationToken);
        if (!canAccess)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(exportacion.RutaArchivo) || !System.IO.File.Exists(exportacion.RutaArchivo))
        {
            return NotFound(new { error = "Archivo de exportación no encontrado" });
        }

        var exportRoot = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(c => c.Clave == "export_path")
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken) ?? @"C:\atlas-balance\exports";

        if (!IsAllowedExportFile(exportacion.RutaArchivo, exportRoot))
        {
            _logger.LogWarning("Exportacion {ExportacionId} bloqueada por ruta no permitida", id);
            return NotFound(new { error = "Archivo de exportacion no encontrado" });
        }

        var stream = new FileStream(exportacion.RutaArchivo, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = Path.GetFileName(exportacion.RutaArchivo);
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private static bool IsAllowedExportFile(string filePath, string exportRoot)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(exportRoot));
            return fullFilePath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }
}
