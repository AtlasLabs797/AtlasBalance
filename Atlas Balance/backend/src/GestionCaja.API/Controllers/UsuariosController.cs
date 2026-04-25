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
[Authorize(Roles = "ADMIN")]
[Route("api/usuarios")]
public sealed class UsuariosController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public UsuariosController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet("catalogos-permisos")]
    public async Task<IActionResult> CatalogosPermisos(CancellationToken cancellationToken)
    {
        var titulares = await _dbContext.Titulares
            .OrderBy(t => t.Nombre)
            .Select(t => new { id = t.Id, nombre = t.Nombre })
            .ToListAsync(cancellationToken);

        var cuentas = await (
            from c in _dbContext.Cuentas
            join t in _dbContext.Titulares on c.TitularId equals t.Id into titularesJoin
            from titular in titularesJoin.DefaultIfEmpty()
            orderby c.Nombre
            select new
            {
                id = c.Id,
                nombre = c.Nombre,
                titular_id = c.TitularId,
                titular_nombre = titular != null ? titular.Nombre : null
            })
            .ToListAsync(cancellationToken);

        return Ok(new { titulares, cuentas });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "fecha_creacion",
        [FromQuery] string sortDir = "desc",
        [FromQuery] string? search = null,
        [FromQuery] bool incluirEliminados = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<Usuario> query = incluirEliminados
            ? _dbContext.Usuarios.IgnoreQueryFilters()
            : _dbContext.Usuarios;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(term) || u.NombreCompleto.ToLower().Contains(term));
        }

        query = ApplySorting(query, sortBy, desc);

        var total = await query.CountAsync(cancellationToken);
        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UsuarioListItemResponse
            {
                Id = u.Id,
                Email = u.Email,
                NombreCompleto = u.NombreCompleto,
                Rol = u.Rol.ToString(),
                Activo = u.Activo,
                PrimerLogin = u.PrimerLogin,
                FechaCreacion = u.FechaCreacion,
                FechaUltimaLogin = u.FechaUltimaLogin,
                DeletedAt = u.DeletedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<UsuarioListItemResponse>
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
        var userQuery = incluirEliminados ? _dbContext.Usuarios.IgnoreQueryFilters() : _dbContext.Usuarios;
        var usuario = await userQuery.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var emails = await _dbContext.UsuarioEmails
            .Where(x => x.UsuarioId == id)
            .OrderByDescending(x => x.EsPrincipal)
            .ThenBy(x => x.Email)
            .Select(x => x.Email)
            .ToListAsync(cancellationToken);

        var permisos = await LoadPermisosAsync(id, cancellationToken);

        return Ok(new UsuarioDetalleResponse
        {
            Usuario = new UsuarioListItemResponse
            {
                Id = usuario.Id,
                Email = usuario.Email,
                NombreCompleto = usuario.NombreCompleto,
                Rol = usuario.Rol.ToString(),
                Activo = usuario.Activo,
                PrimerLogin = usuario.PrimerLogin,
                FechaCreacion = usuario.FechaCreacion,
                FechaUltimaLogin = usuario.FechaUltimaLogin,
                DeletedAt = usuario.DeletedAt
            },
            Emails = emails,
            Permisos = permisos
        });
    }

    [HttpGet("{id:guid}/permisos")]
    public async Task<IActionResult> ObtenerPermisos(Guid id, CancellationToken cancellationToken)
    {
        if (!await UsuarioExisteAsync(id, includeDeleted: true, cancellationToken))
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var permisos = await LoadPermisosAsync(id, cancellationToken);
        return Ok(permisos);
    }

    [HttpPut("{id:guid}/permisos")]
    public async Task<IActionResult> GuardarPermisos(Guid id, [FromBody] IReadOnlyList<SavePermisoUsuarioRequest> permisos, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var validation = await ValidatePermisosAsync(permisos, cancellationToken);
        if (!validation.Ok)
        {
            return BadRequest(new { error = validation.Error });
        }

        var before = await LoadPermisosAuditSnapshotAsync(id, cancellationToken);
        await UpsertPermisosAsync(id, permisos, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var after = await LoadPermisosAuditSnapshotAsync(id, cancellationToken);

        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.CambioPermisos,
            "USUARIOS",
            id,
            HttpContext,
            JsonSerializer.Serialize(new { before, after }),
            cancellationToken);

        return Ok(new { message = "Permisos actualizados" });
    }

    [HttpGet("{id:guid}/permisos/cuenta/{cuentaId:guid}")]
    public async Task<IActionResult> ObtenerPermisoCuenta(Guid id, Guid cuentaId, CancellationToken cancellationToken)
    {
        if (!await UsuarioExisteAsync(id, includeDeleted: true, cancellationToken))
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var permiso = await _dbContext.PermisosUsuario
            .Where(x => x.UsuarioId == id && x.CuentaId == cuentaId)
            .FirstOrDefaultAsync(cancellationToken);
        var preferencia = await _dbContext.PreferenciasUsuarioCuenta
            .Where(x => x.UsuarioId == id && x.CuentaId == cuentaId)
            .FirstOrDefaultAsync(cancellationToken);

        return permiso is null
            ? NotFound(new { error = "Permiso no encontrado para la cuenta indicada" })
            : Ok(MapPermiso(permiso, preferencia));
    }

    [HttpPut("{id:guid}/permisos/cuenta/{cuentaId:guid}")]
    public async Task<IActionResult> GuardarPermisoCuenta(Guid id, Guid cuentaId, [FromBody] SavePermisoUsuarioRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var normalizedRequest = new SavePermisoUsuarioRequest
        {
            CuentaId = cuentaId,
            TitularId = request.TitularId,
            PuedeVerCuentas = request.PuedeVerCuentas,
            PuedeAgregarLineas = request.PuedeAgregarLineas,
            PuedeEditarLineas = request.PuedeEditarLineas,
            PuedeEliminarLineas = request.PuedeEliminarLineas,
            PuedeImportar = request.PuedeImportar,
            PuedeVerDashboard = request.PuedeVerDashboard,
            ColumnasVisibles = request.ColumnasVisibles,
            ColumnasEditables = request.ColumnasEditables
        };

        var validation = await ValidatePermisosAsync([normalizedRequest], cancellationToken);
        if (!validation.Ok)
        {
            return BadRequest(new { error = validation.Error });
        }

        var before = await LoadPermisosAuditSnapshotAsync(id, cancellationToken);

        var existing = await _dbContext.PermisosUsuario
            .Where(x => x.UsuarioId == id && x.CuentaId == cuentaId)
            .ToListAsync(cancellationToken);
        _dbContext.PermisosUsuario.RemoveRange(existing);
        var existingPreference = await _dbContext.PreferenciasUsuarioCuenta
            .Where(x => x.UsuarioId == id && x.CuentaId == cuentaId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingPreference is not null)
        {
            _dbContext.PreferenciasUsuarioCuenta.Remove(existingPreference);
        }

        await AddPermisoAsync(id, normalizedRequest);
        AddPreferencia(id, normalizedRequest.CuentaId, normalizedRequest.ColumnasVisibles, normalizedRequest.ColumnasEditables);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = await LoadPermisosAuditSnapshotAsync(id, cancellationToken);
        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.CambioPermisos,
            "USUARIOS",
            id,
            HttpContext,
            JsonSerializer.Serialize(new { before, after, cuenta_id = cuentaId }),
            cancellationToken);

        return Ok(new { message = "Permiso de cuenta actualizado" });
    }

    [HttpGet("{id:guid}/emails")]
    public async Task<IActionResult> ObtenerEmails(Guid id, CancellationToken cancellationToken)
    {
        if (!await UsuarioExisteAsync(id, includeDeleted: true, cancellationToken))
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        return Ok(await LoadEmailsAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/emails")]
    public async Task<IActionResult> CrearEmail(Guid id, [FromBody] SaveUsuarioEmailRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email obligatorio" });
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var exists = await _dbContext.UsuarioEmails.AnyAsync(
            x => x.UsuarioId == id && x.Email.ToLower() == normalizedEmail,
            cancellationToken);
        if (exists)
        {
            return Conflict(new { error = "Ese email ya existe para el usuario" });
        }

        var before = await LoadEmailsAsync(id, cancellationToken);
        if (request.EsPrincipal)
        {
            var currentPrimary = await _dbContext.UsuarioEmails.Where(x => x.UsuarioId == id && x.EsPrincipal).ToListAsync(cancellationToken);
            foreach (var item in currentPrimary)
            {
                item.EsPrincipal = false;
            }
        }

        var shouldBePrimary = request.EsPrincipal || !await _dbContext.UsuarioEmails.AnyAsync(x => x.UsuarioId == id, cancellationToken);
        var email = new UsuarioEmail
        {
            Id = Guid.NewGuid(),
            UsuarioId = id,
            Email = normalizedEmail,
            EsPrincipal = shouldBePrimary
        };

        _dbContext.UsuarioEmails.Add(email);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = await LoadEmailsAsync(id, cancellationToken);
        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.UpdateUsuario,
            "USUARIOS",
            id,
            HttpContext,
            JsonSerializer.Serialize(new { before, after }),
            cancellationToken);

        return CreatedAtAction(nameof(ObtenerEmails), new { id }, new UsuarioEmailResponse
        {
            Id = email.Id,
            Email = email.Email,
            EsPrincipal = email.EsPrincipal
        });
    }

    [HttpDelete("{id:guid}/emails/{emailId:guid}")]
    public async Task<IActionResult> EliminarEmail(Guid id, Guid emailId, CancellationToken cancellationToken)
    {
        if (!await UsuarioExisteAsync(id, includeDeleted: true, cancellationToken))
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var email = await _dbContext.UsuarioEmails.FirstOrDefaultAsync(x => x.Id == emailId && x.UsuarioId == id, cancellationToken);
        if (email is null)
        {
            return NotFound(new { error = "Email no encontrado" });
        }

        var before = await LoadEmailsAsync(id, cancellationToken);
        var wasPrimary = email.EsPrincipal;
        _dbContext.UsuarioEmails.Remove(email);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (wasPrimary)
        {
            await PromoteFirstEmailAsync(id, cancellationToken);
        }

        var after = await LoadEmailsAsync(id, cancellationToken);
        await _auditService.LogAsync(
            GetCurrentUserId(),
            AuditActions.UpdateUsuario,
            "USUARIOS",
            id,
            HttpContext,
            JsonSerializer.Serialize(new { before, after }),
            cancellationToken);

        return Ok(new { message = "Email eliminado" });
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CreateUsuarioRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.NombreCompleto))
        {
            return BadRequest(new { error = "Email, nombre y password son obligatorios" });
        }

        if (!SecurityPolicy.TryValidatePassword(request.Password, out var passwordError))
        {
            return BadRequest(new { error = passwordError });
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var exists = await _dbContext.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (exists)
        {
            return Conflict(new { error = "Ya existe un usuario con ese email" });
        }

        var validation = await ValidatePermisosAsync(request.Permisos, cancellationToken);
        if (!validation.Ok)
        {
            return BadRequest(new { error = validation.Error });
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            NombreCompleto = request.NombreCompleto.Trim(),
            Rol = request.Rol,
            Activo = request.Activo,
            PrimerLogin = request.PrimerLogin,
            FechaCreacion = DateTime.UtcNow,
            SecurityStamp = UserSessionState.CreateSecurityStamp(),
            PasswordChangedAt = DateTime.UtcNow
        };

        _dbContext.Usuarios.Add(usuario);

        var normalizedEmails = NormalizeEmails(request.Emails);
        await UpsertEmailsAsync(usuario.Id, normalizedEmails, cancellationToken);
        await UpsertPermisosAsync(usuario.Id, request.Permisos, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(GetCurrentUserId(), AuditActions.CreateUsuario, "USUARIOS", usuario.Id, HttpContext,
            JsonSerializer.Serialize(new
            {
                nuevo = new
                {
                    usuario.Email,
                    usuario.NombreCompleto,
                    rol = usuario.Rol.ToString(),
                    usuario.Activo,
                    usuario.PrimerLogin,
                    emails = normalizedEmails,
                    permisos = request.Permisos
                }
            }), cancellationToken);

        if (request.Permisos.Count > 0)
        {
            await _auditService.LogAsync(
                GetCurrentUserId(),
                AuditActions.CambioPermisos,
                "USUARIOS",
                usuario.Id,
                HttpContext,
                JsonSerializer.Serialize(new { before = Array.Empty<object>(), after = request.Permisos }),
                cancellationToken);
        }

        return CreatedAtAction(nameof(Obtener), new { id = usuario.Id }, new { id = usuario.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] UpdateUsuarioRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.NombreCompleto))
        {
            return BadRequest(new { error = "Email y nombre son obligatorios" });
        }

        var validation = await ValidatePermisosAsync(request.Permisos, cancellationToken);
        if (!validation.Ok)
        {
            return BadRequest(new { error = validation.Error });
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var emailAlreadyExists = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id != id && u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailAlreadyExists)
        {
            return Conflict(new { error = "Ya existe un usuario con ese email" });
        }

        var before = new
        {
            usuario.Email,
            usuario.NombreCompleto,
            rol = usuario.Rol.ToString(),
            usuario.Activo,
            usuario.PrimerLogin,
            emails = await _dbContext.UsuarioEmails.Where(x => x.UsuarioId == id).OrderBy(x => x.Email).Select(x => x.Email).ToListAsync(cancellationToken),
            permisos = await LoadPermisosAuditSnapshotAsync(id, cancellationToken)
        };

        var shouldRevokeForDeactivation = usuario.Activo && !request.Activo;

        usuario.Email = normalizedEmail;
        usuario.NombreCompleto = request.NombreCompleto.Trim();
        usuario.Rol = request.Rol;
        usuario.Activo = request.Activo;
        usuario.PrimerLogin = request.PrimerLogin;

        var passwordChanged = false;
        var revokedRefreshTokens = 0;
        if (!string.IsNullOrWhiteSpace(request.PasswordNueva))
        {
            if (!SecurityPolicy.TryValidatePassword(request.PasswordNueva, out var resetPasswordError))
            {
                return BadRequest(new { error = resetPasswordError });
            }
            var now = DateTime.UtcNow;
            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.PasswordNueva, workFactor: 12);
            UserSessionState.RotateAfterPasswordChange(usuario, now);
            revokedRefreshTokens = await RevokeActiveRefreshTokensAsync(usuario.Id, now, cancellationToken);
            passwordChanged = true;
        }
        else if (shouldRevokeForDeactivation)
        {
            var now = DateTime.UtcNow;
            UserSessionState.RotateSecurityStamp(usuario);
            revokedRefreshTokens = await RevokeActiveRefreshTokensAsync(usuario.Id, now, cancellationToken);
        }

        var normalizedEmails = NormalizeEmails(request.Emails);
        await UpsertEmailsAsync(usuario.Id, normalizedEmails, cancellationToken);
        await UpsertPermisosAsync(usuario.Id, request.Permisos, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = new
        {
            usuario.Email,
            usuario.NombreCompleto,
            rol = usuario.Rol.ToString(),
            usuario.Activo,
            usuario.PrimerLogin,
            emails = normalizedEmails,
            permisos = request.Permisos
        };

        await _auditService.LogAsync(GetCurrentUserId(), AuditActions.UpdateUsuario, "USUARIOS", usuario.Id, HttpContext,
            JsonSerializer.Serialize(new { before, after }), cancellationToken);

        if (passwordChanged)
        {
            await _auditService.LogAsync(
                GetCurrentUserId(),
                AuditActions.PasswordReset,
                "USUARIOS",
                usuario.Id,
                HttpContext,
                JsonSerializer.Serialize(new { password_reset = true, refresh_tokens_revocados = revokedRefreshTokens }),
                cancellationToken);
        }

        if (JsonSerializer.Serialize(before.permisos) != JsonSerializer.Serialize(after.permisos))
        {
            await _auditService.LogAsync(
                GetCurrentUserId(),
                AuditActions.CambioPermisos,
                "USUARIOS",
                usuario.Id,
                HttpContext,
                JsonSerializer.Serialize(new { before = before.permisos, after = after.permisos }),
                cancellationToken);
        }

        return Ok(new { message = "Usuario actualizado" });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var actorId = GetCurrentUserId();
        if (actorId == id)
        {
            return BadRequest(new { error = "No puedes eliminar tu propio usuario" });
        }

        var before = new
        {
            usuario.Activo,
            usuario.DeletedAt,
            usuario.DeletedById
        };

        var deletedAt = DateTime.UtcNow;
        usuario.Activo = false;
        usuario.DeletedAt = deletedAt;
        usuario.DeletedById = actorId;
        UserSessionState.RotateSecurityStamp(usuario);
        var revokedRefreshTokens = await RevokeActiveRefreshTokensAsync(usuario.Id, deletedAt, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = new
        {
            usuario.Activo,
            usuario.DeletedAt,
            usuario.DeletedById
        };

        await _auditService.LogAsync(actorId, AuditActions.DeleteUsuario, "USUARIOS", usuario.Id, HttpContext,
            JsonSerializer.Serialize(new { before, after, refresh_tokens_revocados = revokedRefreshTokens }), cancellationToken);

        return Ok(new { message = "Usuario eliminado" });
    }

    [HttpPost("{id:guid}/restaurar")]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado" });
        }

        var before = new
        {
            usuario.Activo,
            usuario.DeletedAt,
            usuario.DeletedById
        };

        usuario.DeletedAt = null;
        usuario.DeletedById = null;
        usuario.Activo = true;
        UserSessionState.RotateSecurityStamp(usuario);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = new
        {
            usuario.Activo,
            usuario.DeletedAt,
            usuario.DeletedById
        };

        await _auditService.LogAsync(GetCurrentUserId(), AuditActions.RestoreUsuario, "USUARIOS", usuario.Id, HttpContext,
            JsonSerializer.Serialize(new { before, after }), cancellationToken);

        return Ok(new { message = "Usuario restaurado" });
    }

    private async Task<(bool Ok, string? Error)> ValidatePermisosAsync(IReadOnlyList<SavePermisoUsuarioRequest> permisos, CancellationToken cancellationToken)
    {
        foreach (var permiso in permisos)
        {
            if (permiso.CuentaId.HasValue)
            {
                var cuenta = await _dbContext.Cuentas
                    .Where(c => c.Id == permiso.CuentaId.Value)
                    .Select(c => new { c.Id, c.TitularId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (cuenta is null)
                {
                    return (false, $"Cuenta inválida: {permiso.CuentaId}");
                }

                if (permiso.TitularId.HasValue && cuenta.TitularId != permiso.TitularId.Value)
                {
                    return (false, "La cuenta indicada no pertenece al titular seleccionado");
                }
            }

            if (permiso.TitularId.HasValue)
            {
                var exists = await _dbContext.Titulares.AnyAsync(t => t.Id == permiso.TitularId.Value, cancellationToken);
                if (!exists)
                {
                    return (false, $"Titular inválido: {permiso.TitularId}");
                }
            }
        }

        return (true, null);
    }

    private async Task<bool> UsuarioExisteAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken)
    {
        var query = includeDeleted ? _dbContext.Usuarios.IgnoreQueryFilters() : _dbContext.Usuarios;
        return await query.AnyAsync(u => u.Id == id, cancellationToken);
    }

    private async Task<List<UsuarioEmailResponse>> LoadEmailsAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        return await _dbContext.UsuarioEmails
            .Where(x => x.UsuarioId == usuarioId)
            .OrderByDescending(x => x.EsPrincipal)
            .ThenBy(x => x.Email)
            .Select(x => new UsuarioEmailResponse
            {
                Id = x.Id,
                Email = x.Email,
                EsPrincipal = x.EsPrincipal
            })
            .ToListAsync(cancellationToken);
    }

    private async Task PromoteFirstEmailAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var nextPrimary = await _dbContext.UsuarioEmails
            .Where(x => x.UsuarioId == usuarioId)
            .OrderBy(x => x.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextPrimary is null)
        {
            return;
        }

        nextPrimary.EsPrincipal = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> RevokeActiveRefreshTokensAsync(Guid usuarioId, DateTime revokedAt, CancellationToken cancellationToken)
    {
        var activeRefreshTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UsuarioId == usuarioId && rt.RevocadoEn == null && rt.ExpiraEn > revokedAt)
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevocadoEn = revokedAt;
        }

        return activeRefreshTokens.Count;
    }

    private async Task<List<PermisoUsuarioResponse>> LoadPermisosAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var permisos = await _dbContext.PermisosUsuario
            .Where(x => x.UsuarioId == usuarioId)
            .OrderBy(x => x.TitularId)
            .ThenBy(x => x.CuentaId)
            .ToListAsync(cancellationToken);
        var preferencias = await _dbContext.PreferenciasUsuarioCuenta
            .Where(x => x.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        return permisos.Select(permiso =>
        {
            var preferencia = preferencias.FirstOrDefault(pref => pref.CuentaId == permiso.CuentaId);
            return MapPermiso(permiso, preferencia);
        }).ToList();
    }

    private async Task<List<object>> LoadPermisosAuditSnapshotAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var permisos = await _dbContext.PermisosUsuario
            .Where(x => x.UsuarioId == usuarioId)
            .OrderBy(x => x.TitularId)
            .ThenBy(x => x.CuentaId)
            .ToListAsync(cancellationToken);
        var preferencias = await _dbContext.PreferenciasUsuarioCuenta
            .Where(x => x.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        var snapshot = permisos.Select(permiso =>
        {
            var preferencia = preferencias.FirstOrDefault(pref => pref.CuentaId == permiso.CuentaId);
            return new
            {
                permiso.CuentaId,
                permiso.TitularId,
                permiso.PuedeVerCuentas,
                permiso.PuedeAgregarLineas,
                permiso.PuedeEditarLineas,
                permiso.PuedeEliminarLineas,
                permiso.PuedeImportar,
                permiso.PuedeVerDashboard,
                ColumnasVisibles = preferencia?.ColumnasVisibles,
                ColumnasEditables = preferencia?.ColumnasEditables
            };
        });

        return snapshot.Cast<object>().ToList();
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static List<string> NormalizeEmails(IReadOnlyList<string> emails)
    {
        return emails
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task UpsertEmailsAsync(Guid usuarioId, IReadOnlyList<string> normalizedEmails, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.UsuarioEmails.Where(x => x.UsuarioId == usuarioId).ToListAsync(cancellationToken);
        _dbContext.UsuarioEmails.RemoveRange(existing);

        for (var i = 0; i < normalizedEmails.Count; i++)
        {
            _dbContext.UsuarioEmails.Add(new UsuarioEmail
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                Email = normalizedEmails[i],
                EsPrincipal = i == 0
            });
        }
    }

    private async Task UpsertPermisosAsync(Guid usuarioId, IReadOnlyList<SavePermisoUsuarioRequest> permisos, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.PermisosUsuario.Where(x => x.UsuarioId == usuarioId).ToListAsync(cancellationToken);
        _dbContext.PermisosUsuario.RemoveRange(existing);
        var existingPreferences = await _dbContext.PreferenciasUsuarioCuenta.Where(x => x.UsuarioId == usuarioId).ToListAsync(cancellationToken);
        _dbContext.PreferenciasUsuarioCuenta.RemoveRange(existingPreferences);

        foreach (var item in permisos)
        {
            await AddPermisoAsync(usuarioId, item);
        }

        foreach (var item in permisos
                     .Where(x => x.ColumnasVisibles is not null || x.ColumnasEditables is not null)
                     .GroupBy(x => x.CuentaId)
                     .Select(x => x.Last()))
        {
            AddPreferencia(usuarioId, item.CuentaId, item.ColumnasVisibles, item.ColumnasEditables);
        }
    }

    private Task AddPermisoAsync(Guid usuarioId, SavePermisoUsuarioRequest item)
    {
        _dbContext.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            CuentaId = item.CuentaId,
            TitularId = item.TitularId,
            PuedeVerCuentas = item.PuedeVerCuentas,
            PuedeAgregarLineas = item.PuedeAgregarLineas,
            PuedeEditarLineas = item.PuedeEditarLineas,
            PuedeEliminarLineas = item.PuedeEliminarLineas,
            PuedeImportar = item.PuedeImportar,
            PuedeVerDashboard = item.PuedeVerDashboard
        });

        return Task.CompletedTask;
    }

    private void AddPreferencia(Guid usuarioId, Guid? cuentaId, IReadOnlyList<string>? columnasVisibles, IReadOnlyList<string>? columnasEditables)
    {
        _dbContext.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            CuentaId = cuentaId,
            ColumnasVisibles = columnasVisibles is null ? null : JsonSerializer.Serialize(columnasVisibles),
            ColumnasEditables = columnasEditables is null ? null : JsonSerializer.Serialize(columnasEditables),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static IReadOnlyList<string>? ParseJsonArray(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson);
        }
        catch
        {
            return null;
        }
    }

    private static PermisoUsuarioResponse MapPermiso(PermisoUsuario permiso, PreferenciaUsuarioCuenta? preferencia)
    {
        return new PermisoUsuarioResponse
        {
            Id = permiso.Id,
            UsuarioId = permiso.UsuarioId,
            CuentaId = permiso.CuentaId,
            TitularId = permiso.TitularId,
            PuedeVerCuentas = permiso.PuedeVerCuentas,
            PuedeAgregarLineas = permiso.PuedeAgregarLineas,
            PuedeEditarLineas = permiso.PuedeEditarLineas,
            PuedeEliminarLineas = permiso.PuedeEliminarLineas,
            PuedeImportar = permiso.PuedeImportar,
            PuedeVerDashboard = permiso.PuedeVerDashboard,
            ColumnasVisibles = ParseJsonArray(preferencia?.ColumnasVisibles),
            ColumnasEditables = ParseJsonArray(preferencia?.ColumnasEditables)
        };
    }

    private IQueryable<Usuario> ApplySorting(IQueryable<Usuario> query, string sortBy, bool desc)
    {
        return (sortBy.ToLowerInvariant(), desc) switch
        {
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("email", false) => query.OrderBy(u => u.Email),
            ("nombre_completo", true) => query.OrderByDescending(u => u.NombreCompleto),
            ("nombre_completo", false) => query.OrderBy(u => u.NombreCompleto),
            ("rol", true) => query.OrderByDescending(u => u.Rol),
            ("rol", false) => query.OrderBy(u => u.Rol),
            ("fecha_ultima_login", true) => query.OrderByDescending(u => u.FechaUltimaLogin),
            ("fecha_ultima_login", false) => query.OrderBy(u => u.FechaUltimaLogin),
            ("fecha_creacion", true) => query.OrderByDescending(u => u.FechaCreacion),
            _ => query.OrderBy(u => u.FechaCreacion)
        };
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
