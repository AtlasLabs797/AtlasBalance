using System.Security.Claims;
using System.Text.Json;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/integraciones/tokens")]
public sealed class IntegracionesController : ControllerBase
{
    private static readonly string[] DefaultOpenClawScopes =
    [
        "titulares",
        "saldos",
        "extractos",
        "evolucion",
        "alertas",
        "vencimientos",
        "auditoria"
    ];

    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IIntegrationTokenService _integrationTokenService;

    public IntegracionesController(AppDbContext dbContext, IAuditService auditService, IIntegrationTokenService integrationTokenService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _integrationTokenService = integrationTokenService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool incluirEliminados = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<IntegrationToken> query = incluirEliminados
            ? _dbContext.IntegrationTokens.IgnoreQueryFilters()
            : _dbContext.IntegrationTokens;

        query = query.OrderByDescending(x => x.FechaCreacion);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<IntegrationTokenListItemResponse>
        {
            Data = rows.Select(MapToken).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id, [FromQuery] bool incluirEliminados = false, CancellationToken cancellationToken = default)
    {
        IQueryable<IntegrationToken> query = incluirEliminados
            ? _dbContext.IntegrationTokens.IgnoreQueryFilters()
            : _dbContext.IntegrationTokens;

        var token = await query.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (token is null)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        var permisos = await _dbContext.IntegrationPermissions
            .Where(x => x.TokenId == id)
            .OrderBy(x => x.PaisId)
            .ThenBy(x => x.TitularId)
            .ThenBy(x => x.CuentaId)
            .Select(x => new IntegrationPermissionItemResponse
            {
                Id = x.Id,
                TitularId = x.TitularId,
                CuentaId = x.CuentaId,
                PaisId = x.PaisId,
                AccesoTipo = x.AccesoTipo
            })
            .ToListAsync(cancellationToken);

        return Ok(new IntegrationTokenDetailResponse
        {
            Token = MapToken(token),
            Permisos = permisos
        });
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CreateIntegrationTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return BadRequest(new { error = "Nombre obligatorio." });
        }

        var validation = await ValidatePermissionsAsync(request.PermisoLectura, request.PermisoEscritura, request.Permisos, cancellationToken);
        if (validation is not null)
        {
            return BadRequest(new { error = validation });
        }

        var creatorId = GetCurrentUserId();
        if (!creatorId.HasValue)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        var tokenPlano = _integrationTokenService.GeneratePlainToken();
        var expiration = _integrationTokenService.ResolveExpiration(request.FechaExpiracion, request.SinExpiracionConfirmada, request.SinExpiracionTextoConfirmacion);
        var scopes = NormalizeEndpointScopes(request.Scopes);
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = request.Nombre.Trim(),
            Descripcion = request.Descripcion?.Trim(),
            Tipo = "openclaw",
            PermisoLectura = request.PermisoLectura,
            PermisoEscritura = request.PermisoEscritura,
            Estado = EstadoTokenIntegracion.Activo,
            TokenHash = _integrationTokenService.ComputeSha256(tokenPlano),
            FechaCreacion = DateTime.UtcNow,
            FechaExpiracion = expiration,
            EndpointScopesJson = JsonSerializer.Serialize(scopes),
            UsuarioCreadorId = creatorId.Value
        };

        _dbContext.IntegrationTokens.Add(token);
        AddPermissions(token.Id, request.Permisos);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.CreateIntegrationToken,
            "INTEGRATION_TOKENS",
            token.Id,
            HttpContext,
            JsonSerializer.Serialize(new
            {
                token.Nombre,
                token.Estado,
                token.PermisoLectura,
                token.PermisoEscritura,
                token.FechaExpiracion,
                scopes,
                sin_expiracion = expiration is null,
                permisos = request.Permisos
            }),
            cancellationToken);

        var detail = await BuildDetailAsync(token, cancellationToken);
        return CreatedAtAction(nameof(Obtener), new { id = token.Id }, new CreateIntegrationTokenResponse
        {
            Token = detail,
            TokenPlano = tokenPlano,
            Advertencias = expiration is null
                ? ["Token sin expiracion. Revisa que esto sea intencionado."]
                : []
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] SaveIntegrationTokenRequest request, CancellationToken cancellationToken)
    {
        var token = await _dbContext.IntegrationTokens.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (token is null)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return BadRequest(new { error = "Nombre obligatorio." });
        }

        var validation = await ValidatePermissionsAsync(request.PermisoLectura, request.PermisoEscritura, request.Permisos, cancellationToken);
        if (validation is not null)
        {
            return BadRequest(new { error = validation });
        }

        var before = new
        {
            token.Nombre,
            token.Descripcion,
            token.Estado,
            token.PermisoLectura,
            token.PermisoEscritura,
            token.FechaExpiracion,
            token.EndpointScopesJson
        };

        var expiration = _integrationTokenService.ResolveExpiration(request.FechaExpiracion, request.SinExpiracionConfirmada, request.SinExpiracionTextoConfirmacion);
        var scopes = NormalizeEndpointScopes(request.Scopes);
        token.Nombre = request.Nombre.Trim();
        token.Descripcion = request.Descripcion?.Trim();
        token.PermisoLectura = request.PermisoLectura;
        token.PermisoEscritura = request.PermisoEscritura;
        token.FechaExpiracion = expiration;
        token.EndpointScopesJson = JsonSerializer.Serialize(scopes);

        var currentPermissions = await _dbContext.IntegrationPermissions.Where(x => x.TokenId == id).ToListAsync(cancellationToken);
        _dbContext.IntegrationPermissions.RemoveRange(currentPermissions);
        AddPermissions(token.Id, request.Permisos);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.UpdateIntegrationToken,
            "INTEGRATION_TOKENS",
            token.Id,
            HttpContext,
            JsonSerializer.Serialize(new { before, after = request }),
            cancellationToken);

        return Ok(await BuildDetailAsync(token, cancellationToken));
    }

    [HttpPost("{id:guid}/revocar")]
    public async Task<IActionResult> Revocar(Guid id, CancellationToken cancellationToken)
    {
        var revoked = await _integrationTokenService.RevokeAsync(id, cancellationToken);
        if (!revoked)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.RevokeIntegrationToken,
            "INTEGRATION_TOKENS",
            id,
            HttpContext,
            null,
            cancellationToken);

        return Ok(new { message = "Token revocado" });
    }

    [HttpPost("{id:guid}/rotar")]
    public async Task<IActionResult> Rotar(Guid id, [FromBody] RotarIntegrationTokenRequest request, CancellationToken cancellationToken)
    {
        var oldToken = await _dbContext.IntegrationTokens.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (oldToken is null)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        var creatorId = GetCurrentUserId();
        if (!creatorId.HasValue)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        var tokenPlano = _integrationTokenService.GeneratePlainToken();
        var expiration = _integrationTokenService.ResolveExpiration(request.FechaExpiracion, request.SinExpiracionConfirmada, request.SinExpiracionTextoConfirmacion);
        var now = DateTime.UtcNow;
        oldToken.Estado = EstadoTokenIntegracion.Revocado;
        oldToken.FechaRevocacion = now;

        var newToken = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = $"{oldToken.Nombre} (rotado)",
            Descripcion = oldToken.Descripcion,
            Tipo = oldToken.Tipo,
            PermisoLectura = oldToken.PermisoLectura,
            PermisoEscritura = oldToken.PermisoEscritura,
            Estado = EstadoTokenIntegracion.Activo,
            TokenHash = _integrationTokenService.ComputeSha256(tokenPlano),
            FechaCreacion = now,
            FechaExpiracion = expiration,
            RotatedFromTokenId = oldToken.Id,
            EndpointScopesJson = string.IsNullOrWhiteSpace(oldToken.EndpointScopesJson)
                ? JsonSerializer.Serialize(DefaultOpenClawScopes)
                : oldToken.EndpointScopesJson,
            UsuarioCreadorId = creatorId.Value
        };

        _dbContext.IntegrationTokens.Add(newToken);
        var oldPermissions = await _dbContext.IntegrationPermissions
            .AsNoTracking()
            .Where(x => x.TokenId == oldToken.Id)
            .ToListAsync(cancellationToken);
        foreach (var permiso in oldPermissions)
        {
            _dbContext.IntegrationPermissions.Add(new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = newToken.Id,
                TitularId = permiso.TitularId,
                CuentaId = permiso.CuentaId,
                PaisId = permiso.PaisId,
                AccesoTipo = permiso.AccesoTipo,
                FechaCreacion = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            creatorId,
            "ROTATE_INTEGRATION_TOKEN",
            "INTEGRATION_TOKENS",
            newToken.Id,
            HttpContext,
            JsonSerializer.Serialize(new
            {
                token_anterior_id = oldToken.Id,
                token_nuevo_id = newToken.Id,
                fecha_expiracion = newToken.FechaExpiracion,
                sin_expiracion = newToken.FechaExpiracion is null
            }),
            cancellationToken);

        return Ok(new CreateIntegrationTokenResponse
        {
            Token = await BuildDetailAsync(newToken, cancellationToken),
            TokenPlano = tokenPlano,
            Advertencias = expiration is null
                ? ["Token rotado sin expiracion. Revisa que esto sea intencionado."]
                : []
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var token = await _dbContext.IntegrationTokens.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (token is null)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        token.DeletedAt = DateTime.UtcNow;
        token.DeletedById = GetCurrentUserId();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.DeleteIntegrationToken,
            "INTEGRATION_TOKENS",
            token.Id,
            HttpContext,
            null,
            cancellationToken);

        return Ok(new { message = "Token eliminado" });
    }

    [HttpGet("{id:guid}/auditoria")]
    public async Task<IActionResult> Auditoria(Guid id, [FromQuery] int top = 100, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.IntegrationTokens.IgnoreQueryFilters().AnyAsync(x => x.Id == id, cancellationToken);
        if (!exists)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        var data = await _dbContext.AuditoriaIntegraciones
            .Where(x => x.TokenId == id)
            .OrderByDescending(x => x.Timestamp)
            .Take(Math.Clamp(top, 1, 500))
            .Select(x => new
            {
                x.Id,
                x.Endpoint,
                x.Metodo,
                x.CodigoRespuesta,
                x.Timestamp,
                x.TiempoEjecucionMs,
                x.IpAddress
            })
            .ToListAsync(cancellationToken);

        return Ok(data);
    }

    [HttpGet("{id:guid}/metricas")]
    public async Task<IActionResult> Metricas(Guid id, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.IntegrationTokens.IgnoreQueryFilters().AnyAsync(x => x.Id == id, cancellationToken);
        if (!exists)
        {
            return NotFound(new { error = "Token no encontrado" });
        }

        var metrics = await _dbContext.AuditoriaIntegraciones
            .Where(x => x.TokenId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                total_requests = g.Count(),
                exitosos = g.Count(x => x.CodigoRespuesta.HasValue && x.CodigoRespuesta.Value >= 200 && x.CodigoRespuesta.Value < 300),
                tiempo_promedio_ms = g.Average(x => (double?)x.TiempoEjecucionMs) ?? 0d
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (metrics is null)
        {
            return Ok(new
            {
                total_requests = 0,
                porcentaje_exito = 0d,
                tiempo_promedio_ms = 0d
            });
        }

        var porcentajeExito = metrics.total_requests == 0
            ? 0d
            : Math.Round((metrics.exitosos / (double)metrics.total_requests) * 100d, 2);

        return Ok(new
        {
            metrics.total_requests,
            porcentaje_exito = porcentajeExito,
            tiempo_promedio_ms = Math.Round(metrics.tiempo_promedio_ms, 2)
        });
    }

    [HttpGet("auditoria")]
    public async Task<IActionResult> AuditoriaGlobal(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? tokenId = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbContext.AuditoriaIntegraciones.AsNoTracking().AsQueryable();
        if (tokenId.HasValue)
        {
            query = query.Where(x => x.TokenId == tokenId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var tokenIds = rows.Select(x => x.TokenId).Distinct().ToList();
        var tokensById = await _dbContext.IntegrationTokens
            .IgnoreQueryFilters()
            .Where(x => tokenIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);

        return Ok(new PaginatedResponse<IntegrationAuditItemResponse>
        {
            Data = rows.Select(x => new IntegrationAuditItemResponse
            {
                Id = x.Id,
                TokenId = x.TokenId,
                TokenNombre = tokensById.GetValueOrDefault(x.TokenId),
                Endpoint = x.Endpoint,
                Metodo = x.Metodo,
                CodigoRespuesta = x.CodigoRespuesta,
                Timestamp = x.Timestamp,
                TiempoEjecucionMs = x.TiempoEjecucionMs,
                IpAddress = x.IpAddress?.ToString()
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    private async Task<string?> ValidatePermissionsAsync(
        bool permisoLectura,
        bool permisoEscritura,
        IReadOnlyList<SaveIntegrationPermissionRequest> permisos,
        CancellationToken cancellationToken)
    {
        if (!permisoLectura && !permisoEscritura)
        {
            return "Debe habilitar al menos lectura o escritura para el token.";
        }

        if (permisos.Count == 0)
        {
            return "Debes definir al menos un permiso de alcance para el token.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var permiso in permisos)
        {
            var accesoTipo = NormalizeAccessType(permiso.AccesoTipo);
            if (accesoTipo is null)
            {
                return "El tipo de acceso debe ser 'lectura' o 'escritura'.";
            }

            if (accesoTipo == "lectura" && !permisoLectura)
            {
                return "No puede asignar permisos de lectura si el token no permite lectura.";
            }

            if (accesoTipo == "escritura" && !permisoEscritura)
            {
                return "No puede asignar permisos de escritura si el token no permite escritura.";
            }

            permiso.AccesoTipo = accesoTipo;

            var duplicateKey = $"{permiso.PaisId?.ToString() ?? "global"}|{permiso.TitularId?.ToString() ?? "global"}|{permiso.CuentaId?.ToString() ?? "global"}|{accesoTipo}";
            if (!seen.Add(duplicateKey))
            {
                return "No repitas permisos idénticos.";
            }

            if (permiso.CuentaId.HasValue)
            {
                var cuenta = await _dbContext.Cuentas
                    .Where(x => x.Id == permiso.CuentaId.Value)
                    .Select(x => new { x.Id, x.TitularId, x.PaisId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (cuenta is null)
                {
                    return $"Cuenta inválida: {permiso.CuentaId}";
                }

                if (permiso.TitularId.HasValue && permiso.TitularId.Value != cuenta.TitularId)
                {
                    return "La cuenta indicada no pertenece al titular seleccionado.";
                }

                if (permiso.PaisId.HasValue && permiso.PaisId.Value != cuenta.PaisId)
                {
                    return "La cuenta indicada no pertenece al país seleccionado.";
                }
            }

            if (permiso.TitularId.HasValue)
            {
                var exists = await _dbContext.Titulares.AnyAsync(x => x.Id == permiso.TitularId.Value, cancellationToken);
                if (!exists)
                {
                    return $"Titular inválido: {permiso.TitularId}";
                }
            }

            if (permiso.PaisId.HasValue)
            {
                var exists = await _dbContext.Paises.AnyAsync(x => x.Id == permiso.PaisId.Value && x.Activo, cancellationToken);
                if (!exists)
                {
                    return $"País inválido: {permiso.PaisId}";
                }
            }
        }

        return null;
    }

    private void AddPermissions(Guid tokenId, IReadOnlyList<SaveIntegrationPermissionRequest> permisos)
    {
        foreach (var permiso in permisos)
        {
            _dbContext.IntegrationPermissions.Add(new IntegrationPermission
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                TitularId = permiso.TitularId,
                CuentaId = permiso.CuentaId,
                PaisId = permiso.PaisId,
                AccesoTipo = NormalizeAccessType(permiso.AccesoTipo) ?? "lectura",
                FechaCreacion = DateTime.UtcNow
            });
        }
    }

    private static string? NormalizeAccessType(string? accessType)
    {
        var normalized = accessType?.Trim().ToLowerInvariant();
        return normalized is "lectura" or "escritura" ? normalized : null;
    }

    private static IReadOnlyList<string> NormalizeEndpointScopes(IReadOnlyList<string>? scopes)
    {
        var allowed = DefaultOpenClawScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = (scopes is null || scopes.Count == 0 ? DefaultOpenClawScopes : scopes)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim().ToLowerInvariant())
            .Where(scope => allowed.Contains(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? DefaultOpenClawScopes : normalized;
    }

    private static IReadOnlyList<string> ParseEndpointScopes(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<IntegrationTokenDetailResponse> BuildDetailAsync(IntegrationToken token, CancellationToken cancellationToken)
    {
        var permisos = await _dbContext.IntegrationPermissions
            .Where(x => x.TokenId == token.Id)
            .OrderBy(x => x.PaisId)
            .ThenBy(x => x.TitularId)
            .ThenBy(x => x.CuentaId)
            .Select(x => new IntegrationPermissionItemResponse
            {
                Id = x.Id,
                TitularId = x.TitularId,
                CuentaId = x.CuentaId,
                PaisId = x.PaisId,
                AccesoTipo = x.AccesoTipo
            })
            .ToListAsync(cancellationToken);

        return new IntegrationTokenDetailResponse
        {
            Token = MapToken(token),
            Permisos = permisos
        };
    }

    private static IntegrationTokenListItemResponse MapToken(IntegrationToken token)
    {
        return new IntegrationTokenListItemResponse
        {
            Id = token.Id,
            Nombre = token.Nombre,
            Descripcion = token.Descripcion,
            Tipo = token.Tipo,
            Estado = token.Estado.ToString().ToLowerInvariant(),
            PermisoLectura = token.PermisoLectura,
            PermisoEscritura = token.PermisoEscritura,
            FechaCreacion = token.FechaCreacion,
            FechaExpiracion = token.FechaExpiracion,
            FechaUltimaUso = token.FechaUltimaUso,
            FechaRevocacion = token.FechaRevocacion,
            RotatedFromTokenId = token.RotatedFromTokenId,
            Scopes = ParseEndpointScopes(token.EndpointScopesJson),
            LastUsedIpAddress = token.LastUsedIpAddress,
            UsuarioCreadorId = token.UsuarioCreadorId,
            DeletedAt = token.DeletedAt
        };
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
