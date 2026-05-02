using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace GestionCaja.API.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null);
    Task<AuthResult> VerifyMfaAsync(string challengeId, string code, string? ipAddress, CancellationToken cancellationToken);
    Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);
    Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
    Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken);
    Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private const int MaxLoginFailuresPerClientAndEmail = 5;
    private const int MaxMfaFailuresPerChallenge = 5;
    private const int MaxMfaFailuresPerUser = 5;
    private const string MfaIssuer = "Atlas Balance";
    private const string MfaRememberTokenVersion = "v1";
    private static readonly object LoginRateLimitLock = new();
    private static readonly object MfaRateLimitLock = new();
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MfaChallengeDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MfaFailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MfaRememberDuration = TimeSpan.FromDays(90);
    private static readonly IMemoryCache FallbackMemoryCache = new MemoryCache(new MemoryCacheOptions());

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly IMemoryCache _cache;
    private readonly ISecretProtector _secretProtector;

    public AuthService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IAuditService auditService,
        IMemoryCache? cache = null,
        ISecretProtector? secretProtector = null)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _auditService = auditService;
        _cache = cache ?? FallbackMemoryCache;
        _secretProtector = secretProtector ?? PassthroughSecretProtector.Instance;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        if (IsLoginThrottled(normalizedEmail, ipAddress))
        {
            await _auditService.LogAsync(
                null,
                AuditActions.LoginFailed,
                "USUARIOS",
                null,
                ipAddress,
                JsonSerializer.Serialize(new { email = normalizedEmail, motivo = "rate_limited" }),
                cancellationToken);
            throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests);
        }

        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.Activo, cancellationToken);

        if (usuario is null)
        {
            var throttled = RecordLoginFailure(normalizedEmail, ipAddress);
            await _auditService.LogAsync(
                null,
                AuditActions.LoginFailed,
                "USUARIOS",
                null,
                ipAddress,
                JsonSerializer.Serialize(new { email = normalizedEmail, motivo = throttled ? "rate_limited" : "usuario_no_encontrado" }),
                cancellationToken);
            if (throttled)
            {
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > now)
        {
            var throttled = RecordLoginFailure(normalizedEmail, ipAddress);
            await _auditService.LogAsync(
                usuario.Id,
                AuditActions.AccountLocked,
                "USUARIOS",
                usuario.Id,
                ipAddress,
                JsonSerializer.Serialize(new { email = normalizedEmail, locked_until = usuario.LockedUntil }),
                cancellationToken);
            if (throttled)
            {
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            var throttled = RecordLoginFailure(normalizedEmail, ipAddress);
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
                throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
            }

            if (throttled)
            {
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        usuario.FailedLoginAttempts = 0;
        usuario.LockedUntil = null;
        UserSessionState.EnsureSecurityStamp(usuario);
        ClearLoginFailures(normalizedEmail, ipAddress);

        if (RequiresMfa(usuario) && !IsTrustedMfaTokenValid(usuario, trustedMfaToken, now))
        {
            var challenge = CreateMfaChallenge(usuario, ipAddress);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                usuario.Id,
                AuditActions.LoginMfaRequired,
                "USUARIOS",
                usuario.Id,
                ipAddress,
                JsonSerializer.Serialize(new
                {
                    email = normalizedEmail,
                    setup_required = challenge.SetupRequired
                }),
                cancellationToken);

            return new AuthResult
            {
                MfaRequired = true,
                MfaSetupRequired = challenge.SetupRequired,
                MfaChallengeId = challenge.ChallengeId,
                MfaSecret = challenge.SetupRequired ? challenge.Secret : null,
                MfaOtpAuthUri = challenge.SetupRequired
                    ? TotpService.BuildOtpAuthUri(MfaIssuer, usuario.Email, challenge.Secret)
                    : null
            };
        }

        usuario.FechaUltimaLogin = now;
        ClearMfaFailures(usuario.Id);
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

    public async Task<AuthResult> VerifyMfaAsync(string challengeId, string code, string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(code))
        {
            throw new AuthException("Codigo MFA invalido", StatusCodes.Status401Unauthorized);
        }

        if (!_cache.TryGetValue<MfaChallengeState>(BuildMfaChallengeCacheKey(challengeId), out var challenge) ||
            challenge is null)
        {
            throw new AuthException("Codigo MFA invalido o expirado", StatusCodes.Status401Unauthorized);
        }

        if (!string.IsNullOrWhiteSpace(challenge.IpAddress) &&
            !string.IsNullOrWhiteSpace(ipAddress) &&
            !string.Equals(challenge.IpAddress, ipAddress, StringComparison.Ordinal))
        {
            RemoveMfaChallenge(challengeId);
            throw new AuthException("Codigo MFA invalido o expirado", StatusCodes.Status401Unauthorized);
        }

        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Id == challenge.UserId && u.Activo, cancellationToken);
        if (usuario is null)
        {
            RemoveMfaChallenge(challengeId);
            throw new AuthException("Usuario no valido", StatusCodes.Status401Unauthorized);
        }

        var now = DateTime.UtcNow;
        if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > now)
        {
            RemoveMfaChallenge(challengeId);
            throw new AuthException("Codigo MFA invalido o expirado", StatusCodes.Status401Unauthorized);
        }

        var secret = challenge.Secret;
        if (!TotpService.TryValidateCode(secret, code, DateTime.UtcNow, out var matchedStep) ||
            (usuario.MfaLastAcceptedStep.HasValue && matchedStep <= usuario.MfaLastAcceptedStep.Value))
        {
            var userMfaFailures = RecordMfaFailure(usuario.Id);
            challenge = challenge with { FailedAttempts = challenge.FailedAttempts + 1 };
            var lockTriggered = userMfaFailures >= MaxMfaFailuresPerUser;
            if (lockTriggered)
            {
                usuario.FailedLoginAttempts = MaxFailedLoginAttempts;
                usuario.LockedUntil = now.Add(LockDuration);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (lockTriggered || challenge.FailedAttempts >= MaxMfaFailuresPerChallenge)
            {
                RemoveMfaChallenge(challengeId);
            }
            else
            {
                StoreMfaChallenge(challenge);
            }

            await _auditService.LogAsync(
                usuario.Id,
                AuditActions.LoginFailed,
                "USUARIOS",
                usuario.Id,
                ipAddress,
                JsonSerializer.Serialize(new { email = usuario.Email, motivo = "mfa_invalido" }),
                cancellationToken);

            if (lockTriggered)
            {
                await _auditService.LogAsync(
                    usuario.Id,
                    AuditActions.AccountLocked,
                    "USUARIOS",
                    usuario.Id,
                    ipAddress,
                    JsonSerializer.Serialize(new
                    {
                        email = usuario.Email,
                        motivo = "mfa_invalido",
                        locked_until = usuario.LockedUntil
                    }),
                    cancellationToken);
            }

            throw new AuthException("Codigo MFA invalido", StatusCodes.Status401Unauthorized);
        }

        if (challenge.SetupRequired)
        {
            usuario.MfaSecret = _secretProtector.ProtectForStorage(secret);
            usuario.MfaEnabled = true;
            usuario.MfaEnabledAt = now;
            await _auditService.LogAsync(
                usuario.Id,
                AuditActions.MfaEnabled,
                "USUARIOS",
                usuario.Id,
                ipAddress,
                JsonSerializer.Serialize(new { email = usuario.Email }),
                cancellationToken);
        }

        usuario.MfaLastAcceptedStep = matchedStep;
        usuario.FailedLoginAttempts = 0;
        usuario.LockedUntil = null;
        usuario.FechaUltimaLogin = now;
        UserSessionState.EnsureSecurityStamp(usuario);
        ClearMfaFailures(usuario.Id);

        var tokens = await IssueTokensAsync(usuario, ipAddress, cancellationToken);
        await _auditService.LogAsync(
            usuario.Id,
            AuditActions.MfaVerified,
            "USUARIOS",
            usuario.Id,
            ipAddress,
            JsonSerializer.Serialize(new { email = usuario.Email }),
            cancellationToken);
        await _auditService.LogAsync(
            usuario.Id,
            AuditActions.Login,
            "USUARIOS",
            usuario.Id,
            ipAddress,
            JsonSerializer.Serialize(new { email = usuario.Email }),
            cancellationToken);

        RemoveMfaChallenge(challengeId);
        var result = await BuildAuthResultAsync(usuario, tokens.AccessToken, tokens.RefreshToken, cancellationToken);
        result.TrustedMfaTokenExpiresAt = now.Add(MfaRememberDuration);
        result.TrustedMfaToken = GenerateTrustedMfaToken(usuario, result.TrustedMfaTokenExpiresAt.Value);
        return result;
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

            if (storedToken is null || storedToken.ExpiraEn <= now)
            {
                throw new AuthException("Refresh token inválido o expirado", StatusCodes.Status401Unauthorized);
            }

            if (storedToken.RevocadoEn.HasValue)
            {
                if (!string.IsNullOrWhiteSpace(storedToken.ReemplazadoPor))
                {
                    await RevokeSessionsAfterRefreshReuseAsync(storedToken, now, ipAddress, cancellationToken);
                    if (tx is not null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }
                }

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

            UserSessionState.EnsureSecurityStamp(usuario);

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

        if (!SecurityPolicy.TryValidatePassword(passwordNueva, out var passwordError))
        {
            throw new AuthException(passwordError, StatusCodes.Status400BadRequest);
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

        var now = DateTime.UtcNow;
        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: 12);
        usuario.PrimerLogin = false;
        UserSessionState.RotateAfterPasswordChange(usuario, now);

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
            AuditActions.PasswordChanged,
            "USUARIOS",
            userId,
            ipAddress: ipAddress,
            detallesJson: JsonSerializer.Serialize(new { cambio_password = true, usuario.PrimerLogin, refresh_tokens_revocados = activeRefreshTokens.Count }),
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
                PuedeVerCuentas = p.PuedeVerCuentas,
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
                MfaEnabled = usuario.MfaEnabled,
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

    private bool RequiresMfa(Usuario usuario)
    {
        return usuario.Activo &&
               _configuration.GetValue("Security:RequireMfaForWebUsers", true);
    }

    private MfaChallengeState CreateMfaChallenge(Usuario usuario, string? ipAddress)
    {
        var setupRequired = !usuario.MfaEnabled || string.IsNullOrWhiteSpace(usuario.MfaSecret);
        var secret = setupRequired
            ? TotpService.GenerateSecret()
            : _secretProtector.UnprotectFromStorage(usuario.MfaSecret) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(secret))
        {
            setupRequired = true;
            secret = TotpService.GenerateSecret();
        }

        var challenge = new MfaChallengeState(
            ChallengeId: GenerateChallengeId(),
            UserId: usuario.Id,
            Secret: secret,
            SetupRequired: setupRequired,
            IpAddress: ipAddress,
            FailedAttempts: 0);

        StoreMfaChallenge(challenge);
        return challenge;
    }

    private void StoreMfaChallenge(MfaChallengeState challenge)
    {
        _cache.Set(
            BuildMfaChallengeCacheKey(challenge.ChallengeId),
            challenge,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = MfaChallengeDuration
            });
    }

    private void RemoveMfaChallenge(string challengeId)
    {
        _cache.Remove(BuildMfaChallengeCacheKey(challengeId));
    }

    private static string BuildMfaChallengeCacheKey(string challengeId)
    {
        return $"auth:mfa-challenge:{challengeId}";
    }

    private int RecordMfaFailure(Guid userId)
    {
        var key = BuildMfaFailureCacheKey(userId);
        lock (MfaRateLimitLock)
        {
            var count = _cache.Get<int>(key) + 1;
            _cache.Set(key, count, MfaFailureWindow);
            return count;
        }
    }

    private void ClearMfaFailures(Guid userId)
    {
        _cache.Remove(BuildMfaFailureCacheKey(userId));
    }

    private static string BuildMfaFailureCacheKey(Guid userId)
    {
        return $"auth:mfa-failures:{userId:N}";
    }

    private static string GenerateChallengeId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private string GenerateAccessToken(Usuario usuario)
    {
        UserSessionState.EnsureSecurityStamp(usuario);
        var jwtSecret = _configuration["JwtSettings:Secret"]
            ?? throw new InvalidOperationException("JwtSettings:Secret is required");
        var issuer = _configuration["JwtSettings:Issuer"] ?? "atlas-balance-api";
        var audience = _configuration["JwtSettings:Audience"] ?? "atlas-balance-app";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(GetAccessTokenExpMinutes());

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Email, usuario.Email),
            new Claim(ClaimTypes.Name, usuario.NombreCompleto),
            new Claim(ClaimTypes.Role, usuario.Rol.ToString()),
            new Claim(AuthClaimNames.SecurityStamp, usuario.SecurityStamp)
        };

        if (usuario.PasswordChangedAt.HasValue)
        {
            claims.Add(new Claim(
                AuthClaimNames.PasswordChangedAt,
                new DateTimeOffset(usuario.PasswordChangedAt.Value).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool IsLoginThrottled(string normalizedEmail, string? ipAddress)
    {
        var key = BuildLoginFailureCacheKey(normalizedEmail, ipAddress);
        lock (LoginRateLimitLock)
        {
            return _cache.TryGetValue<int>(key, out var count) &&
                   count >= MaxLoginFailuresPerClientAndEmail;
        }
    }

    private bool RecordLoginFailure(string normalizedEmail, string? ipAddress)
    {
        var key = BuildLoginFailureCacheKey(normalizedEmail, ipAddress);
        lock (LoginRateLimitLock)
        {
            var count = _cache.Get<int>(key) + 1;
            _cache.Set(key, count, LoginFailureWindow);
            return count >= MaxLoginFailuresPerClientAndEmail;
        }
    }

    private void ClearLoginFailures(string normalizedEmail, string? ipAddress)
    {
        _cache.Remove(BuildLoginFailureCacheKey(normalizedEmail, ipAddress));
    }

    private static string BuildLoginFailureCacheKey(string normalizedEmail, string? ipAddress)
    {
        var client = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        return $"auth:login-failures:{ComputeSha256($"{client}|{normalizedEmail}")}";
    }

    private async Task RevokeSessionsAfterRefreshReuseAsync(
        RefreshToken reusedToken,
        DateTime now,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var usuario = reusedToken.Usuario ?? await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == reusedToken.UsuarioId, cancellationToken);
        if (usuario is null)
        {
            return;
        }

        UserSessionState.RotateSecurityStamp(usuario);
        var activeRefreshTokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UsuarioId == usuario.Id && rt.RevocadoEn == null && rt.ExpiraEn > now)
            .ToListAsync(cancellationToken);

        foreach (var activeRefreshToken in activeRefreshTokens)
        {
            activeRefreshToken.RevocadoEn = now;
        }

        await _auditService.LogAsync(
            usuario.Id,
            AuditActions.RefreshTokenReuseDetected,
            "USUARIOS",
            usuario.Id,
            ipAddress,
            JsonSerializer.Serialize(new
            {
                refresh_token_id = reusedToken.Id,
                refresh_tokens_revocados = activeRefreshTokens.Count
            }),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
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

    private bool IsTrustedMfaTokenValid(Usuario usuario, string? token, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(token) || !usuario.MfaEnabled || string.IsNullOrWhiteSpace(usuario.MfaSecret))
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            !FixedTimeEquals(parts[1], ComputeMfaRememberSignature(parts[0])))
        {
            return false;
        }

        MfaRememberPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MfaRememberPayload>(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
        }
        catch
        {
            return false;
        }

        return payload is not null &&
               payload.Version == MfaRememberTokenVersion &&
               payload.UserId == usuario.Id &&
               payload.SecurityStamp == usuario.SecurityStamp &&
               payload.ExpiresAtUnix > new DateTimeOffset(now).ToUnixTimeSeconds();
    }

    private string GenerateTrustedMfaToken(Usuario usuario, DateTime expiresAtUtc)
    {
        var payload = new MfaRememberPayload(
            MfaRememberTokenVersion,
            usuario.Id,
            usuario.SecurityStamp,
            new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds());
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return $"{payloadBase64}.{ComputeMfaRememberSignature(payloadBase64)}";
    }

    private string ComputeMfaRememberSignature(string payloadBase64)
    {
        var jwtSecret = _configuration["JwtSettings:Secret"]
            ?? throw new InvalidOperationException("JwtSettings:Secret is required");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtSecret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64)));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
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
    public bool MfaRequired { get; set; }
    public bool MfaSetupRequired { get; set; }
    public string? MfaChallengeId { get; set; }
    public string? MfaSecret { get; set; }
    public string? MfaOtpAuthUri { get; set; }
    public string? TrustedMfaToken { get; set; }
    public DateTime? TrustedMfaTokenExpiresAt { get; set; }
}

public sealed class AuthException : Exception
{
    public int StatusCode { get; }

    public AuthException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}

internal sealed record MfaChallengeState(
    string ChallengeId,
    Guid UserId,
    string Secret,
    bool SetupRequired,
    string? IpAddress,
    int FailedAttempts);

internal sealed record MfaRememberPayload(
    string Version,
    Guid UserId,
    string SecurityStamp,
    long ExpiresAtUnix);

internal sealed class PassthroughSecretProtector : ISecretProtector
{
    public static readonly PassthroughSecretProtector Instance = new();

    private PassthroughSecretProtector()
    {
    }

    public string ProtectForStorage(string? value) => value?.Trim() ?? string.Empty;
    public string? UnprotectFromStorage(string? storedValue) => storedValue;
    public bool IsProtected(string? storedValue) => false;
}
