using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AtlasBalance.API.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null, string? userAgent = null);
    Task<AuthResult> VerifyMfaAsync(string challengeId, string code, bool rememberDevice, string? ipAddress, CancellationToken cancellationToken, string? userAgent = null);
    Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);
    Task<Guid?> LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
    Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken);
    Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, string? currentRefreshToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrustedMfaDeviceResponse>> GetTrustedMfaDevicesAsync(Guid userId, string? currentTrustedMfaToken, CancellationToken cancellationToken);
    Task<bool> RevokeTrustedMfaDeviceAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken);
    Task<bool> RevokeCurrentTrustedMfaDeviceAsync(Guid userId, string? currentTrustedMfaToken, CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    /// <summary>
    /// Namespace de cache para el payload de <c>GET /api/auth/me</c>. El TTL
    /// se compone con <c>securityStamp</c> para que un cambio de contrasena
    /// o de permisos del usuario invalide la entrada sin necesidad de bump.
    /// Adicionalmente, el <c>DashboardCacheInvalidationInterceptor</c>
    /// invalida este namespace tras cambios en <c>USUARIOS</c>,
    /// <c>PERMISOS_USUARIO</c> o <c>PREFERENCIAS_USUARIO_CUENTA</c>.
    /// </summary>
    internal const string AuthCurrentNamespace = "auth_current";

    private const int PasswordWorkFactor = 12;
    private const int MaxMfaFailuresPerChallenge = 5;
    private const int MaxMfaFailuresPerUser = 5;
    private const string MfaIssuer = "Atlas Balance";
    private static readonly object LoginRateLimitLock = new();
    private static readonly object MfaRateLimitLock = new();
    private static readonly TimeSpan MfaChallengeDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MfaFailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MfaRememberDuration = TimeSpan.FromDays(SecurityConfigurationDefaults.MfaRememberDeviceDays);
    private static readonly IMemoryCache FallbackMemoryCache = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    /// V-02.07: hash señuelo para igualar el coste del login cuando el email no
    /// existe o la cuenta esta bloqueada. Sin el, esas dos ramas respondian sin
    /// ejecutar BCrypt (~250 ms menos que "password incorrecta") y la latencia
    /// permitia enumerar cuentas pese a que el mensaje de error ya era identico.
    /// Se deriva de bytes aleatorios en el arranque: no es un secreto ni debe
    /// coincidir con ninguna contrasena real.
    /// </summary>
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        workFactor: PasswordWorkFactor);

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly IMemoryCache _cache;
    private readonly ISecretProtector _secretProtector;
    private readonly ICacheService _cacheService;
    private readonly CachingOptions _cachingOptions;
    private readonly RateLimitingOptions _rateLimitingOptions;

    public AuthService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IAuditService auditService,
        ISecretProtector secretProtector,
        ICacheService cacheService,
        IOptions<CachingOptions> cachingOptions,
        IOptions<RateLimitingOptions> rateLimitingOptions,
        IMemoryCache? cache = null)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _auditService = auditService;
        _cache = cache ?? FallbackMemoryCache;
        _cacheService = cacheService;
        _cachingOptions = cachingOptions.Value;
        _rateLimitingOptions = rateLimitingOptions.Value;
        // V-02.05 (MED-1): el protector es obligatorio. Si DI no lo inyecta, el
        // constructor falla ruidosamente en lugar de degradar a PassthroughSecretProtector
        // (que almacena secretos en claro). Solo se permite explicitamente via constructor
        // con un protector de testing (en cuyo caso el caller debe responsabilizarse).
        if (secretProtector is null)
        {
            throw new InvalidOperationException("ISecretProtector es obligatorio. Configure DataProtectionSecretProtector en Program.cs.");
        }
        if (secretProtector is PassthroughSecretProtector && !AllowPassthroughSecretProtector)
        {
            throw new InvalidOperationException("PassthroughSecretProtector detectado. Esto almacenaria secretos en claro. Use DataProtectionSecretProtector.");
        }
_secretProtector = secretProtector;
    }

    /// <summary>
    /// V-02.05 (MED-1): los tests unitarios pueden necesitar el passthrough. Lo activan
    /// explicitamente. En produccion esto queda siempre en false.
    /// </summary>
    public static bool AllowPassthroughSecretProtector { get; set; }

    public async Task<AuthResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken, string? trustedMfaToken = null, string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        // V-02.07: segundos honestos para el header Retry-After de los 429 de
        // login: es cuando caduca el contador de fallos, no un valor arbitrario.
        var loginRetryAfterSeconds = (int)Math.Round(_rateLimitingOptions.LoginFailureWindow.TotalSeconds);
        if (IsLoginEmailThrottled(normalizedEmail, ipAddress))
        {
            await _auditService.LogAsync(
                null,
                AuditActions.LoginFailed,
                "USUARIOS",
                null,
                ipAddress,
                JsonSerializer.Serialize(new { email = normalizedEmail, motivo = "rate_limited" }),
                cancellationToken);
            throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests, loginRetryAfterSeconds);
        }

        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.Activo, cancellationToken);

        if (usuario is null)
        {
            // Igualamos el coste con la rama de password incorrecta (ver DummyPasswordHash).
            BCrypt.Net.BCrypt.Verify(password, DummyPasswordHash);

            if (IsLoginClientThrottled(ipAddress))
            {
                await _auditService.LogAsync(
                    null,
                    AuditActions.LoginFailed,
                    "USUARIOS",
                    null,
                    ipAddress,
                    JsonSerializer.Serialize(new { email = normalizedEmail, motivo = "rate_limited" }),
                    cancellationToken);
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests, loginRetryAfterSeconds);
            }

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
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests, loginRetryAfterSeconds);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > now)
        {
            // Mismo motivo que en la rama de usuario inexistente: sin esto, una
            // cuenta bloqueada respondia mas rapido y se distinguia por latencia.
            BCrypt.Net.BCrypt.Verify(password, DummyPasswordHash);

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
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests, loginRetryAfterSeconds);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            var throttled = RecordLoginFailure(normalizedEmail, ipAddress);
            usuario.FailedLoginAttempts += 1;
            var lockTriggered = false;
            if (usuario.FailedLoginAttempts >= _rateLimitingOptions.LoginMaxFailedAttemptsPerAccount)
            {
                usuario.LockedUntil = now.Add(_rateLimitingOptions.LoginLockDuration);
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
                throw new AuthException("Demasiados intentos. Espera unos minutos.", StatusCodes.Status429TooManyRequests, loginRetryAfterSeconds);
            }

            throw new AuthException("Credenciales inválidas", StatusCodes.Status401Unauthorized);
        }

        usuario.FailedLoginAttempts = 0;
        usuario.LockedUntil = null;

        // V-02.07: rehash oportunista. Es el unico momento en que tenemos la
        // contrasena en claro y ya validada, asi que si algun dia sube
        // PasswordWorkFactor las cuentas existentes migran solas en su siguiente
        // login en vez de quedarse con el coste antiguo para siempre.
        if (BCrypt.Net.BCrypt.PasswordNeedsRehash(usuario.PasswordHash, PasswordWorkFactor))
        {
            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: PasswordWorkFactor);
        }

        UserSessionState.EnsureSecurityStamp(usuario);
        ClearLoginFailures(normalizedEmail, ipAddress);

        var mfaRequired = await RequiresMfaAsync(usuario, cancellationToken);
        var rememberDeviceEnabled = mfaRequired && await IsMfaRememberDeviceEnabledAsync(cancellationToken);
        var trustedMfaTokenValid = rememberDeviceEnabled &&
            await TryUseTrustedMfaDeviceAsync(usuario, trustedMfaToken, now, ipAddress, userAgent, cancellationToken);
        if (mfaRequired && !trustedMfaTokenValid)
        {
            var challenge = CreateMfaChallenge(usuario, ipAddress, mfaRequired);
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
                    setup_required = challenge.SetupRequired,
                    rol = usuario.Rol.ToString(),
                    policy_source = "configuration_or_admin"
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
                    : null,
                MfaRememberDeviceAllowed = rememberDeviceEnabled,
                MfaRememberDeviceDays = SecurityConfigurationDefaults.MfaRememberDeviceDays,
                ClearTrustedMfaToken = !string.IsNullOrWhiteSpace(trustedMfaToken)
            };
        }

        usuario.FechaUltimaLogin = now;
        ClearMfaFailures(usuario.Id);
        var tokens = await IssueTokensAsync(
            usuario,
            ipAddress,
            cancellationToken,
            mfaVerifiedAt: mfaRequired ? now : null);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            usuario.Id,
            AuditActions.Login,
            "USUARIOS",
            usuario.Id,
            ipAddress,
            JsonSerializer.Serialize(new { email = normalizedEmail }),
            cancellationToken);

        var result = await BuildAuthResultAsync(usuario, tokens.AccessToken, tokens.RefreshToken, cancellationToken);
        result.ClearTrustedMfaToken = !mfaRequired && !string.IsNullOrWhiteSpace(trustedMfaToken);
        return result;
    }

    public async Task<AuthResult> VerifyMfaAsync(string challengeId, string code, bool rememberDevice, string? ipAddress, CancellationToken cancellationToken, string? userAgent = null)
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

        // V-02.06: la politica MFA puede cambiar entre login y verificacion.
        // Re-evaluamos rol + estado del usuario y comparamos el security stamp
        // capturado en el challenge. Si cualquiera de los tres diverge, el
        // challenge queda invalidado.
        if (string.IsNullOrWhiteSpace(challenge.SecurityStamp) ||
            !string.Equals(challenge.SecurityStamp, usuario.SecurityStamp, StringComparison.Ordinal) ||
            challenge.Rol != usuario.Rol ||
            !usuario.Activo)
        {
            RemoveMfaChallenge(challengeId);
            throw new AuthException("Codigo MFA invalido o expirado", StatusCodes.Status401Unauthorized);
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
                usuario.FailedLoginAttempts = _rateLimitingOptions.LoginMaxFailedAttemptsPerAccount;
                usuario.LockedUntil = now.Add(_rateLimitingOptions.LoginLockDuration);
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

        var tokens = await IssueTokensAsync(usuario, ipAddress, cancellationToken, mfaVerifiedAt: now);
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
        var rememberDeviceEnabled = rememberDevice && await IsMfaRememberDeviceEnabledAsync(cancellationToken);
        if (rememberDeviceEnabled)
        {
            var trustedDevice = CreateTrustedMfaDevice(usuario, now, ipAddress, userAgent);
            _dbContext.MfaTrustedDevices.Add(trustedDevice.Device);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result.TrustedMfaTokenExpiresAt = trustedDevice.ExpiresAt;
            result.TrustedMfaToken = trustedDevice.Token;
        }
        else
        {
            result.ClearTrustedMfaToken = true;
        }

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

            if (!HasMatchingSecurityStamp(storedToken.SecurityStamp, usuario.SecurityStamp))
            {
                storedToken.RevocadoEn = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                throw new AuthException("Refresh token inválido o expirado", StatusCodes.Status401Unauthorized);
            }

            if (await RequiresMfaAsync(usuario, cancellationToken) && storedToken.MfaVerifiedAt is null)
            {
                storedToken.RevocadoEn = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                throw new AuthException("Se requiere MFA para renovar la sesión", StatusCodes.Status401Unauthorized);
            }

            // V-02.07: la sesion no se ata a la IP a proposito. Invalidar por
            // cambio de IP expulsaria a usuarios legitimos con VPN, DHCP o salto
            // de red, asi que solo dejamos rastro para poder investigarlo despues.
            var currentIp = ParseIpAddress(ipAddress);
            if (storedToken.IpAddress is not null && currentIp is not null &&
                !NormalizeIpForComparison(storedToken.IpAddress).Equals(NormalizeIpForComparison(currentIp)))
            {
                await _auditService.LogAsync(
                    usuario.Id,
                    AuditActions.SessionIpChanged,
                    "USUARIOS",
                    usuario.Id,
                    ipAddress,
                    JsonSerializer.Serialize(new
                    {
                        ip_anterior = storedToken.IpAddress.ToString(),
                        refresh_token_id = storedToken.Id
                    }),
                    cancellationToken);
            }

            var replacement = GenerateRefreshToken();
            var replacementHash = ComputeSha256(replacement);

            storedToken.RevocadoEn = now;
            storedToken.ReemplazadoPor = replacementHash;

            // V-02.07: el id de sesion sobrevive a la rotacion. Es lo que permite
            // que AUDITORIAS agrupe toda la actividad de una sesion de login
            // aunque el access token se renueve cada hora. Los tokens emitidos
            // antes de V-02.07 no lo llevan: se genera uno al rotar.
            var sessionId = storedToken.SessionId ?? GenerateSessionId();

            _dbContext.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                TokenHash = replacementHash,
                SecurityStamp = usuario.SecurityStamp,
                ExpiraEn = now.AddDays(GetRefreshTokenExpDays()),
                CreadoEn = now,
                MfaVerifiedAt = storedToken.MfaVerifiedAt,
                IpAddress = ParseIpAddress(ipAddress),
                SessionId = sessionId
            });

            var accessToken = GenerateAccessToken(usuario, sessionId, storedToken.MfaVerifiedAt);
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

        var now = DateTime.UtcNow;
        var refreshHash = ComputeSha256(refreshToken);
        var storedToken = await _dbContext.RefreshTokens
            .Include(rt => rt.Usuario)
            .FirstOrDefaultAsync(rt => rt.TokenHash == refreshHash, cancellationToken);

        // Solo un refresh token vivo autoriza la rotacion del stamp. Si aceptaramos
        // uno ya revocado o caducado, cualquiera con una copia antigua podria forzar
        // el cierre de sesion del usuario legitimo de forma repetida.
        if (storedToken is null || storedToken.RevocadoEn.HasValue || storedToken.ExpiraEn <= now)
        {
            return null;
        }

        // V-02.07: rotar el security stamp invalida tambien el access token JWT en
        // curso. UserStateMiddleware compara el stamp contra BD en cada request, asi
        // que sin esta rotacion el logout solo borraba cookies del navegador y el JWT
        // seguia siendo aceptado por la API hasta 1h. Como efecto buscado, el logout
        // cierra todas las sesiones del usuario ("cerrar sesion en todas partes").
        var usuario = storedToken.Usuario;
        if (usuario is not null)
        {
            var previousStamp = usuario.SecurityStamp;
            UserSessionState.RotateSecurityStamp(usuario);

            // El recuerdo MFA por dispositivo esta anclado al stamp (ver
            // TryUseTrustedMfaDeviceAsync). Logout cierra la sesion, no la confianza
            // del navegador: re-anclamos al stamp nuevo para no regresionar "logout
            // conserva la cookie mfa_trusted" (V-01.09).
            //
            // Solo se re-anclan los dispositivos que calzaban con el stamp anterior.
            // Un cambio de contrasena, un reset por admin o una deteccion de reuso
            // rotan el stamp SIN tocar esta tabla: los dispositivos que quedaron
            // huerfanos asi deben seguir exigiendo MFA. Re-anclarlos aqui resucitaria
            // el dispositivo de un atacante justo despues de que la victima cambio
            // la contrasena para expulsarlo.
            var trustedDevices = await _dbContext.MfaTrustedDevices
                .Where(x => x.UsuarioId == usuario.Id &&
                            x.RevokedAt == null &&
                            x.ExpiresAt > now &&
                            x.SecurityStamp == previousStamp)
                .ToListAsync(cancellationToken);
            foreach (var device in trustedDevices)
            {
                device.SecurityStamp = usuario.SecurityStamp;
            }
        }

        var activeRefreshTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UsuarioId == storedToken.UsuarioId && rt.RevocadoEn == null && rt.ExpiraEn > now)
            .ToListAsync(cancellationToken);
        foreach (var activeRefreshToken in activeRefreshTokens)
        {
            activeRefreshToken.RevocadoEn = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return storedToken.UsuarioId;
    }

    public async Task<IReadOnlyList<TrustedMfaDeviceResponse>> GetTrustedMfaDevicesAsync(Guid userId, string? currentTrustedMfaToken, CancellationToken cancellationToken)
    {
        var currentHash = TryHashTrustedMfaToken(currentTrustedMfaToken);
        var now = DateTime.UtcNow;
        var devices = await _dbContext.MfaTrustedDevices
            .AsNoTracking()
            .Where(x => x.UsuarioId == userId && x.ExpiresAt > now)
            .OrderByDescending(x => x.LastUsedAt ?? x.CreatedAt)
            .Select(x => new TrustedMfaDeviceResponse
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                ExpiresAt = x.ExpiresAt,
                LastUsedAt = x.LastUsedAt,
                RevokedAt = x.RevokedAt,
                UserAgentSummary = x.UserAgentSummary,
                IpAddressSummary = x.IpAddressSummary,
                Current = currentHash != null && x.TokenHash == currentHash
            })
            .ToListAsync(cancellationToken);

        return devices;
    }

    public async Task<bool> RevokeTrustedMfaDeviceAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await _dbContext.MfaTrustedDevices
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.UsuarioId == userId, cancellationToken);
        if (device is null)
        {
            return false;
        }

        if (device.RevokedAt is null)
        {
            device.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> RevokeCurrentTrustedMfaDeviceAsync(Guid userId, string? currentTrustedMfaToken, CancellationToken cancellationToken)
    {
        var currentHash = TryHashTrustedMfaToken(currentTrustedMfaToken);
        if (currentHash is null)
        {
            return false;
        }

        var device = await _dbContext.MfaTrustedDevices
            .FirstOrDefaultAsync(x => x.UsuarioId == userId && x.TokenHash == currentHash, cancellationToken);
        if (device is null)
        {
            return false;
        }

        if (device.RevokedAt is null)
        {
            device.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<AuthResult> GetCurrentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == userId && u.Activo, cancellationToken);
        if (usuario is null)
        {
            throw new AuthException("Usuario no encontrado", StatusCodes.Status404NotFound);
        }

        // Clave compuesta con securityStamp: un cambio de contrasena o
        // rotacion del stamp invalida la entrada cacheada sin pasar por el
        // interceptor. El interceptor anade una capa defensiva por si la
        // rotacion del stamp no ocurre (p.ej. solo cambian permisos).
        var cacheKey = $"{userId:N}|{usuario.SecurityStamp}";

        return await _cacheService.GetOrLoadAsync(
            new CacheNamespace(AuthCurrentNamespace),
            cacheKey,
            ct => BuildAuthResultAsync(usuario, accessToken: null, refreshToken: null, ct),
            _cachingOptions.AuthCurrentTtl,
            cancellationToken);
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, string passwordActual, string passwordNueva, string? ipAddress, string? currentRefreshToken, CancellationToken cancellationToken)
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

        var now = DateTime.UtcNow;

        // V-02.07: la verificacion de passwordActual comparte el lockout del login.
        // Antes no contaba intentos ni auditaba el fallo, asi que una sesion robada
        // permitia fuerza bruta ilimitada y silenciosa sobre la contrasena actual.
        if (usuario.LockedUntil.HasValue && usuario.LockedUntil.Value > now)
        {
            // Mismo señuelo que en LoginAsync: sin el, "cuenta bloqueada" respondia
            // al instante y "password actual incorrecta" tras ~250 ms de BCrypt, asi
            // que la latencia delataba el estado de la cuenta.
            BCrypt.Net.BCrypt.Verify(passwordActual, DummyPasswordHash);
            throw new AuthException("Usuario bloqueado temporalmente por intentos fallidos", StatusCodes.Status423Locked);
        }

        if (!BCrypt.Net.BCrypt.Verify(passwordActual, usuario.PasswordHash))
        {
            usuario.FailedLoginAttempts += 1;
            var lockTriggered = usuario.FailedLoginAttempts >= _rateLimitingOptions.LoginMaxFailedAttemptsPerAccount;
            if (lockTriggered)
            {
                usuario.LockedUntil = now.Add(_rateLimitingOptions.LoginLockDuration);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                userId,
                AuditActions.LoginFailed,
                "USUARIOS",
                userId,
                ipAddress,
                JsonSerializer.Serialize(new
                {
                    motivo = "password_actual_incorrecta",
                    failed_login_attempts = usuario.FailedLoginAttempts
                }),
                cancellationToken);

            if (lockTriggered)
            {
                await _auditService.LogAsync(
                    userId,
                    AuditActions.AccountLocked,
                    "USUARIOS",
                    userId,
                    ipAddress,
                    JsonSerializer.Serialize(new
                    {
                        motivo = "password_actual_incorrecta",
                        locked_until = usuario.LockedUntil
                    }),
                    cancellationToken);
            }

            throw new AuthException("Contraseña actual incorrecta", StatusCodes.Status400BadRequest);
        }

        usuario.FailedLoginAttempts = 0;
        usuario.LockedUntil = null;

        DateTime? currentSessionMfaVerifiedAt = null;
        if (await RequiresMfaAsync(usuario, cancellationToken))
        {
            currentSessionMfaVerifiedAt = await ResolveCurrentSessionMfaVerifiedAtAsync(userId, currentRefreshToken, usuario.SecurityStamp, now, cancellationToken);
            if (currentSessionMfaVerifiedAt is null)
            {
                throw new AuthException("Se requiere MFA para cambiar la contraseña", StatusCodes.Status401Unauthorized);
            }
        }

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: PasswordWorkFactor);
        usuario.PrimerLogin = false;
        UserSessionState.RotateAfterPasswordChange(usuario, now);

        var activeRefreshTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UsuarioId == userId && rt.RevocadoEn == null && rt.ExpiraEn > now)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevocadoEn = now;
        }

        // Sesion nueva: el cambio de password acaba de revocar todas las
        // anteriores, asi que no hay id de sesion que heredar.
        var sessionId = GenerateSessionId();
        var accessToken = GenerateAccessToken(usuario, sessionId, currentSessionMfaVerifiedAt);
        var newRefreshToken = GenerateRefreshToken();
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = ComputeSha256(newRefreshToken),
            SecurityStamp = usuario.SecurityStamp,
            ExpiraEn = now.AddDays(GetRefreshTokenExpDays()),
            CreadoEn = now,
            MfaVerifiedAt = currentSessionMfaVerifiedAt,
            IpAddress = ParseIpAddress(ipAddress),
            SessionId = sessionId
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

    private async Task<DateTime?> ResolveCurrentSessionMfaVerifiedAtAsync(Guid userId, string? refreshToken, string securityStamp, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var refreshHash = ComputeSha256(refreshToken);
        return await _dbContext.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.UsuarioId == userId && rt.TokenHash == refreshHash && rt.RevocadoEn == null && rt.ExpiraEn > now)
            .Where(rt => rt.SecurityStamp == securityStamp)
            .Select(rt => rt.MfaVerifiedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AuthResult> BuildAuthResultAsync(Usuario usuario, string? accessToken, string? refreshToken, CancellationToken cancellationToken)
    {
        var permisos = await _dbContext.PermisosUsuario
            .Where(p => p.UsuarioId == usuario.Id)
            .ToListAsync(cancellationToken);
        var preferencias = await _dbContext.PreferenciasUsuarioCuenta
            .Where(p => p.UsuarioId == usuario.Id)
            .ToListAsync(cancellationToken);

        var mfaRequiredForUser = await RequiresMfaAsync(usuario, cancellationToken);

        var permisosResponse = permisos.Select(p =>
        {
            var preferencia = preferencias.FirstOrDefault(pref =>
                pref.PaisId == p.PaisId &&
                pref.TitularId == p.TitularId &&
                pref.CuentaId == p.CuentaId);
            return new PermisoUsuarioResponse
            {
                Id = p.Id,
                UsuarioId = p.UsuarioId,
                CuentaId = p.CuentaId,
                TitularId = p.TitularId,
                PaisId = p.PaisId,
                PuedeVerCuentas = p.PuedeVerCuentas,
                PuedeAgregarLineas = p.PuedeAgregarLineas,
                PuedeEditarLineas = p.PuedeEditarLineas,
                PuedeEliminarLineas = p.PuedeEliminarLineas,
                PuedeImportar = p.PuedeImportar,
                PuedeVerDashboard = p.PuedeVerDashboard,
                PuedeRevisarLineas = p.PuedeRevisarLineas,
                PuedeAprobarImportaciones = p.PuedeAprobarImportaciones,
                PuedeConciliar = p.PuedeConciliar,
                PuedeCerrarConciliacion = p.PuedeCerrarConciliacion,
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
                PuedeUsarIa = usuario.PuedeUsarIa,
                MfaEnabled = usuario.MfaEnabled,
                MfaRequired = mfaRequiredForUser,
                FechaCreacion = usuario.FechaCreacion,
                FechaUltimaLogin = usuario.FechaUltimaLogin
            },
            Permisos = permisosResponse
        };
    }

    private async Task<(string AccessToken, string RefreshToken)> IssueTokensAsync(
        Usuario usuario,
        string? ipAddress,
        CancellationToken cancellationToken,
        DateTime? mfaVerifiedAt = null)
    {
        // V-02.07: aqui nace la sesion (login y verificacion MFA). El id viaja al
        // JWT como claim `sid` y se copia en cada rotacion del refresh token.
        var sessionId = GenerateSessionId();
        var accessToken = GenerateAccessToken(usuario, sessionId, mfaVerifiedAt);
        var refreshToken = GenerateRefreshToken();
        var now = DateTime.UtcNow;

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = ComputeSha256(refreshToken),
            SecurityStamp = usuario.SecurityStamp,
            ExpiraEn = now.AddDays(GetRefreshTokenExpDays()),
            CreadoEn = now,
            MfaVerifiedAt = mfaVerifiedAt,
            IpAddress = ParseIpAddress(ipAddress),
            SessionId = sessionId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (accessToken, refreshToken);
    }

    private async Task<bool> RequiresMfaAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        if (!usuario.Activo)
        {
            return false;
        }

        // V-02.06: los administradores siempre necesitan MFA, sin importar la
        // configuracion operativa. Esto protege la gestion de usuarios y la
        // configuracion ante cualquier intento de relajar la politica.
        if (usuario.Rol == RolUsuario.ADMIN)
        {
            return true;
        }

        var stored = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x => x.Clave == SecurityConfigurationDefaults.MfaRequireForNonAdminUsersKey)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stored) && bool.TryParse(stored, out var explicitValue))
        {
            return explicitValue;
        }

        // Fallback: si la BD no tiene la clave sembrada todavia, mantenemos el
        // comportamiento historico de appsettings.json. Asi una migracion en
        // marcha no desactiva MFA por accidente.
        return _configuration.GetValue("Security:RequireMfaForWebUsers", true);
    }

    private bool RequiresMfa(Usuario usuario)
    {
        // Sobrecarga sincrona usada por rutas que no requieren re-leer la BD
        // cuando el caller ya conoce la politica vigente.
        if (!usuario.Activo)
        {
            return false;
        }

        if (usuario.Rol == RolUsuario.ADMIN)
        {
            return true;
        }

        return _configuration.GetValue("Security:RequireMfaForWebUsers", true);
    }

    private MfaChallengeState CreateMfaChallenge(Usuario usuario, string? ipAddress, bool mfaRequired)
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
            FailedAttempts: 0,
            SecurityStamp: usuario.SecurityStamp,
            Rol: usuario.Rol,
            MfaRequired: mfaRequired);

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

    /// <summary>
    /// V-02.07: id de sesion de login. 128 bits en base64url: no es un secreto
    /// (viaja en el JWT y se guarda en AUDITORIAS en claro), solo tiene que ser
    /// no adivinable para que nadie pueda contaminar la auditoria de otro.
    /// </summary>
    private static string GenerateSessionId()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private string GenerateAccessToken(Usuario usuario, string sessionId, DateTime? mfaVerifiedAt = null)
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
            new Claim(AuthClaimNames.SecurityStamp, usuario.SecurityStamp),
            new Claim(AuditRequestContext.SessionClaim, sessionId)
        };

        if (usuario.PasswordChangedAt.HasValue)
        {
            claims.Add(new Claim(
                AuthClaimNames.PasswordChangedAt,
                new DateTimeOffset(usuario.PasswordChangedAt.Value).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        // V-02.06: si la sesion obtuvo garantia MFA, llevamos la marca al JWT
        // para que UserStateMiddleware pueda exigirla a administradores en cada
        // request sin esperar a un re-login. Tambien ancla la marca al security
        // stamp para invalidar garantias obsoletas tras una rotacion.
        if (mfaVerifiedAt.HasValue)
        {
            claims.Add(new Claim(
                AuthClaimNames.MfaVerifiedAt,
                new DateTimeOffset(mfaVerifiedAt.Value).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
            claims.Add(new Claim(AuthClaimNames.MfaSecurityStamp, usuario.SecurityStamp));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool IsLoginEmailThrottled(string normalizedEmail, string? ipAddress)
    {
        var emailKey = BuildLoginFailureCacheKey(normalizedEmail, ipAddress);
        lock (LoginRateLimitLock)
        {
            return _cache.TryGetValue<int>(emailKey, out var emailCount) &&
                   emailCount >= _rateLimitingOptions.LoginMaxFailuresPerIpAndEmail;
        }
    }

    private bool IsLoginClientThrottled(string? ipAddress)
    {
        var clientKey = BuildLoginClientFailureCacheKey(ipAddress);
        lock (LoginRateLimitLock)
        {
            return _cache.TryGetValue<int>(clientKey, out var clientCount) &&
                   clientCount >= _rateLimitingOptions.LoginMaxFailuresPerIp;
        }
    }

    private bool RecordLoginFailure(string normalizedEmail, string? ipAddress)
    {
        var emailKey = BuildLoginFailureCacheKey(normalizedEmail, ipAddress);
        var clientKey = BuildLoginClientFailureCacheKey(ipAddress);
        lock (LoginRateLimitLock)
        {
            var emailCount = _cache.Get<int>(emailKey) + 1;
            var clientCount = _cache.Get<int>(clientKey) + 1;
            _cache.Set(emailKey, emailCount, _rateLimitingOptions.LoginFailureWindow);
            _cache.Set(clientKey, clientCount, _rateLimitingOptions.LoginFailureWindow);
            return emailCount >= _rateLimitingOptions.LoginMaxFailuresPerIpAndEmail ||
                   clientCount >= _rateLimitingOptions.LoginMaxFailuresPerIp;
        }
    }

    private void ClearLoginFailures(string normalizedEmail, string? ipAddress)
    {
        _cache.Remove(BuildLoginFailureCacheKey(normalizedEmail, ipAddress));
        _cache.Remove(BuildLoginClientFailureCacheKey(ipAddress));
    }

    private static string BuildLoginFailureCacheKey(string normalizedEmail, string? ipAddress)
    {
        var client = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        return $"auth:login-failures:{ComputeSha256($"{client}|{normalizedEmail}")}";
    }

    private static string BuildLoginClientFailureCacheKey(string? ipAddress)
    {
        var client = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        return $"auth:login-failures-client:{ComputeSha256(client)}";
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

    private static bool HasMatchingSecurityStamp(string tokenStamp, string userStamp)
    {
        if (string.IsNullOrWhiteSpace(tokenStamp) || string.IsNullOrWhiteSpace(userStamp))
        {
            return false;
        }

        var tokenBytes = Encoding.UTF8.GetBytes(tokenStamp);
        var userBytes = Encoding.UTF8.GetBytes(userStamp);
        return tokenBytes.Length == userBytes.Length &&
               CryptographicOperations.FixedTimeEquals(tokenBytes, userBytes);
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

    private async Task<bool> IsMfaRememberDeviceEnabledAsync(CancellationToken cancellationToken)
    {
        var value = await _dbContext.Configuraciones
            .AsNoTracking()
            .Where(x => x.Clave == SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var enabled) && enabled;
    }

    private async Task<bool> TryUseTrustedMfaDeviceAsync(
        Usuario usuario,
        string? token,
        DateTime now,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (!usuario.MfaEnabled || string.IsNullOrWhiteSpace(usuario.MfaSecret))
        {
            return false;
        }

        var tokenHash = TryHashTrustedMfaToken(token);
        if (tokenHash is null)
        {
            return false;
        }

        var device = await _dbContext.MfaTrustedDevices
            .FirstOrDefaultAsync(x => x.UsuarioId == usuario.Id && x.TokenHash == tokenHash, cancellationToken);
        if (device is null ||
            device.RevokedAt.HasValue ||
            device.ExpiresAt <= now ||
            !HasMatchingSecurityStamp(device.SecurityStamp, usuario.SecurityStamp))
        {
            return false;
        }

        device.LastUsedAt = now;
        var ipSummary = SummarizeIp(ipAddress);
        if (ipSummary is not null)
        {
            device.IpAddressSummary = ipSummary;
        }

        var userAgentSummary = SummarizeUserAgent(userAgent);
        if (userAgentSummary is not null)
        {
            device.UserAgentSummary = userAgentSummary;
        }

        return true;
    }

    private TrustedMfaDeviceIssue CreateTrustedMfaDevice(Usuario usuario, DateTime now, string? ipAddress, string? userAgent)
    {
        var token = GenerateRefreshToken();
        var expiresAt = now.Add(MfaRememberDuration);
        return new TrustedMfaDeviceIssue(
            token,
            expiresAt,
            new MfaTrustedDevice
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                TokenHash = ComputeSha256(token),
                SecurityStamp = usuario.SecurityStamp,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                LastUsedAt = now,
                UserAgentSummary = SummarizeUserAgent(userAgent),
                IpAddressSummary = SummarizeIp(ipAddress)
            });
    }

    private static string? TryHashTrustedMfaToken(string? token)
    {
        var normalized = token?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 512)
        {
            return null;
        }

        return ComputeSha256(normalized);
    }

    private static string? SummarizeUserAgent(string? userAgent)
    {
        return TruncateForStorage(userAgent, 256);
    }

    private static string? SummarizeIp(string? ipAddress)
    {
        return TruncateForStorage(ipAddress, 128);
    }

    private static string? TruncateForStorage(string? value, int maxLength)
    {
        var normalized = value?.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
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

    /// <summary>
    /// V-02.07: la misma maquina puede llegar como <c>10.0.0.1</c> (via
    /// X-Forwarded-For, que esta habilitado) o como <c>::ffff:10.0.0.1</c> (socket
    /// dual-mode directo). <see cref="System.Net.IPAddress.Equals"/> los considera
    /// distintos porque cambia la familia de direcciones, asi que sin normalizar
    /// generariamos alertas de cambio de IP falsas. Una auditoria con ruido no
    /// sirve para investigar nada.
    /// </summary>
    private static System.Net.IPAddress NormalizeIpForComparison(System.Net.IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

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
    public bool MfaRememberDeviceAllowed { get; set; }
    public int MfaRememberDeviceDays { get; set; } = SecurityConfigurationDefaults.MfaRememberDeviceDays;
    public string? TrustedMfaToken { get; set; }
    public DateTime? TrustedMfaTokenExpiresAt { get; set; }
    public bool ClearTrustedMfaToken { get; set; }
}

public sealed class AuthException : Exception
{
    public int StatusCode { get; }
    public int? RetryAfterSeconds { get; }

    public AuthException(string message, int statusCode) : this(message, statusCode, null)
    {
    }

    public AuthException(string message, int statusCode, int? retryAfterSeconds) : base(message)
    {
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

internal sealed record MfaChallengeState(
    string ChallengeId,
    Guid UserId,
    string Secret,
    bool SetupRequired,
    string? IpAddress,
    int FailedAttempts,
    string SecurityStamp,
    RolUsuario Rol,
    bool MfaRequired);

internal sealed record TrustedMfaDeviceIssue(
    string Token,
    DateTime ExpiresAt,
    MfaTrustedDevice Device);

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
