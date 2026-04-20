using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;

namespace GestionCaja.API.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken);
    Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);
    Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
    Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken);
    Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;

    public AuthService(AppDbContext dbContext, IConfiguration configuration, IAuditService auditService)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _auditService = auditService;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.Activo, cancellationToken);

        if (usuario is null)
        {
            await _auditService.LogAsync(
                null,
                AuditActions.LoginFailed,
                "USUARIOS",
                null,
                ipAddress,
                JsonSerializer.Serialize(new { email = normalizedEmail, motivo = "usuario_no_encontrado" }),
                cancellationToken);
            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > now)
        {
            await _auditService.LogAsync(
                usuario.Id,
                AuditActions.AccountLocked,
                "USUARIOS",
                usuario.Id,
                ipAddress,
                JsonSerializer.Serialize(new { email = normalizedEmail, locked_until = usuario.LockedUntil }),
                cancellationToken);
            throw new AuthException("Usuario bloqueado temporalmente por intentos fallidos", StatusCodes.Status423Locked);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            usuario.FailedLoginAttempts += 1;
            var lockTriggered = false;
            if (usuario.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                usuario.LockedUntil = now.Add(LockDuration);
                lockTriggered = true;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                usuario.Id,
                AuditActions.LoginFailed,
                "USUARIOS",
                usuario.Id,
                ipAddress,
                JsonSerializer.Serialize(new
                {
                    email = normalizedEmail,
                    failed_login_attempts = usuario.FailedLoginAttempts
                }),
                cancellationToken);

            if (usuario.LockedUntil.HasValue)
            {
                await _auditService.LogAsync(
                    usuario.Id,
                    AuditActions.AccountLocked,
                    "USUARIOS",
                    usuario.Id,
                    ipAddress,
                    JsonSerializer.Serialize(new
                    {
                        email = normalizedEmail,
                        locked_until = usuario.LockedUntil
                    }),
                    cancellationToken);
            }

            if (lockTriggered)
            {
                throw new AuthException("Usuario bloqueado temporalmente por intentos fallidos", StatusCodes.Status423Locked);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        usuario.FailedLoginAttempts = 0;
        usuario.LockedUntil = null;
        usuario.FechaUltimaLogin = now;

        var tokens = await IssueTokensAsync(usuario, ipAddress, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuario.Id,
            AuditActions.Login,
            "USUARIOS",
            usuario.Id,
            ipAddress,
            JsonSerializer.Serialize(new { email = normalizedEmail }),
            cancellationToken);

        return await BuildAuthResultAsync(usuario, tokens.AccessToken, tokens.RefreshToken, cancellationToken);
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AuthException("Refresh token requerido", StatusCodes.Status401Unauthorized);
        }

        var now = DateTime.UtcNow;
        var refreshHash = ComputeSha256(refreshToken);

        IDbContextTransaction? tx = null;
        if (_dbContext.Database.IsRelational())
        {
            tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await AcquireRefreshTokenLockAsync(refreshHash, cancellationToken);
        }

        try
        {
            var storedToken = await _dbContext.RefreshTokens
                .Include(rt => rt.Usuario)
                .FirstOrDefaultAsync(rt => rt.TokenHash == refreshHash, cancellationToken);

            if (storedToken is null || storedToken.RevocadoEn.HasValue || storedToken.ExpiraEn <= now)
            {
                throw new AuthException("Refresh token inválido o expirado", StatusCodes.Status401Unauthorized);
            }

            var usuario = storedToken.Usuario;
            if (usuario is null || !usuario.Activo || usuario.DeletedAt.HasValue)
            {
                throw new AuthException("Usuario no válido", StatusCodes.Status401Unauthorized);
            }

            if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > now)
            {
                throw new AuthException("Usuario bloqueado temporalmente por intentos fallidos", StatusCodes.Status423Locked);
            }

            var replacement = GenerateRefreshToken();
            var replacementHash = ComputeSha256(replacement);

            storedToken.RevocadoEn = now;
            storedToken.ReemplazadoPor = replacementHash;

            _dbContext.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                TokenHash = replacementHash,
                ExpiraEn = now.AddDays(GetRefreshTokenExpDays()),
                CreadoEn = now,
                IpAddress = ParseIpAddress(ipAddress)
            });

            var accessToken = GenerateAccessToken(usuario);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return await BuildAuthResultAsync(usuario, accessToken, replacement, cancellationToken);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    public async Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var refreshHash = ComputeSha256(refreshToken);
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == refreshHash, cancellationToken);

        if (storedToken is null || storedToken.RevocadoEn.HasValue)
        {
            return null;
        }

        storedToken.RevocadoEn = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return storedToken.UsuarioId;
    }

    public async Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == userId && u.Activo, cancellationToken);
        if (usuario is null)
        {
            throw new AuthException("Usuario no encontrado", StatusCodes.Status404NotFound);
        }

        return await BuildAuthResultAsync(usuario, accessToken: null, refreshToken: null, cancellationToken);
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(passwordActual))
        {
            throw new AuthException("Contraseña actual requerida", StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(passwordNueva) || passwordNueva.Length < 8)
        {
            throw new AuthException("La nueva contraseña debe tener al menos 8 caracteres", StatusCodes.Status400BadRequest);
        }

        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == userId && u.Activo, cancellationToken);
        if (usuario is null)
        {
            throw new AuthException("Usuario no encontrado", StatusCodes.Status404NotFound);
        }

        if (!BCrypt.Net.BCrypt.Verify(passwordActual, usuario.PasswordHash))
        {
            throw new AuthException("Contraseña actual incorrecta", StatusCodes.Status400BadRequest);
        }

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: 12);
        usuario.PrimerLogin = false;

        var now = DateTime.UtcNow;
        var activeRefreshTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UsuarioId == userId && rt.RevocadoEn == null && rt.ExpiraEn > now)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevocadoEn = now;
        }

        var accessToken = GenerateAccessToken(usuario);
        var newRefreshToken = GenerateRefreshToken();
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = ComputeSha256(newRefreshToken),
            ExpiraEn = now.AddDays(GetRefreshTokenExpDays()),
            CreadoEn = now,
            IpAddress = ParseIpAddress(ipAddress)
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            userId,
            AuditActions.UpdateUsuario,
            "USUARIOS",
            userId,
            ipAddress: ipAddress,
            detallesJson: JsonSerializer.Serialize(new { cambio_password = true, usuario.PrimerLogin }),
            cancellationToken: cancellationToken);

        return await BuildAuthResultAsync(usuario, accessToken, newRefreshToken, cancellationToken);
    }

    private async Task<AuthResult> BuildAuthResultAsync(Usuario usuario, string? accessToken, string? refreshToken, CancellationToken cancellationToken)
    {
        var permisos = await _dbContext.PermisosUsuario
            .Where(p => p.UsuarioId == usuario.Id)
            .ToListAsync(cancellationToken);
        var preferencias = await _dbContext.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == usuario.Id)
            .ToListAsync(cancellationToken);
        var permisosResponse = permisos.Select(p =>
        {
            var preferencia = preferencias.FirstOrDefault(pref => pref.CuentaId == p.CuentaId);
            return new PermisoUsuarioResponse
            {
                Id = p.Id,
                UsuarioId = p.UsuarioId,
                CuentaId = p.CuentaId,
                TitularId = p.TitularId,
                PuedeAgregarLineas = p.PuedeAgregarLineas,
                PuedeEditarLineas = p.PuedeEditarLineas,
                PuedeEliminarLineas = p.PuedeEliminarLineas,
                PuedeImportar = p.PuedeImportar,
                PuedeVerDashboard = p.PuedeVerDashboard,
                ColumnasVisibles = ParseJsonArray(preferencia?.ColumnasVisibles),
                ColumnasEditables = ParseJsonArray(preferencia?.ColumnasEditables)
            };
        }).ToList();

        return new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Usuario = new AuthUsuarioResponse
            {
                Id = usuario.Id,
                Email = usuario.Email,
                NombreCompleto = usuario.NombreCompleto,
                Rol = usuario.Rol.ToString(),
                Activo = usuario.Activo,
                PrimerLogin = usuario.PrimerLogin,
                FechaCreacion = usuario.FechaCreacion,
                FechaUltimaLogin = usuario.FechaUltimaLogin
            },
            Permisos = permisosResponse
        };
    }

    private async Task<(string AccessToken, string RefreshToken)> IssueTokensAsync(Usuario usuario, string? ipAddress, CancellationToken cancellationToken)
    {
        var accessToken = GenerateAccessToken(usuario);
        var refreshToken = GenerateRefreshToken();

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = ComputeSha256(refreshToken),
            ExpiraEn = DateTime.UtcNow.AddDays(GetRefreshTokenExpDays()),
            CreadoEn = DateTime.UtcNow,
            IpAddress = ParseIpAddress(ipAddress)
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (accessToken, refreshToken);
    }

    private string GenerateAccessToken(Usuario usuario)
    {
        var jwtSecret = _configuration["JwtSettings:Secret"]
            ?? throw new InvalidOperationException("JwtSettings:Secret is required");
        var issuer = _configuration["JwtSettings:Issuer"] ?? "atlas-balance-api";
        var audience = _configuration["JwtSettings:Audience"] ?? "atlas-balance-app";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(GetAccessTokenExpMinutes());

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Email, usuario.Email),
            new Claim(ClaimTypes.Name, usuario.NombreCompleto),
            new Claim(ClaimTypes.Role, usuario.Rol.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task AcquireRefreshTokenLockAsync(string refreshHash, CancellationToken cancellationToken)
    {
        var bytes = Convert.FromHexString(refreshHash);
        var lockKey = BitConverter.ToInt64(bytes, 0) ^
                      BitConverter.ToInt64(bytes, 8) ^
                      BitConverter.ToInt64(bytes, 16) ^
                      BitConverter.ToInt64(bytes, 24);
        await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], cancellationToken);
    }

    private int GetAccessTokenExpMinutes() => _configuration.GetValue("JwtSettings:AccessTokenExpMinutes", 60);

    private int GetRefreshTokenExpDays() => _configuration.GetValue("JwtSettings:RefreshTokenExpDays", 7);

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

    private static System.Net.IPAddress? ParseIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        return System.Net.IPAddress.TryParse(ipAddress, out var parsed) ? parsed : null;
    }
}

public sealed class AuthResult
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public AuthUsuarioResponse Usuario { get; set; } = new();
    public IReadOnlyList<PermisoUsuarioResponse> Permisos { get; set; } = [];
}

public sealed class AuthException : Exception
{
    public int StatusCode { get; }

    public AuthException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
