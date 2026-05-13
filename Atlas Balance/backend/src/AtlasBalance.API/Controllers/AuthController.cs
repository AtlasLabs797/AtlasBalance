using System.Security.Claims;
using AtlasBalance.API.Constants;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICsrfService _csrfService;
    private readonly IWebHostEnvironment _environment;
    private readonly IAuditService _auditService;

    public AuthController(IAuthService authService, ICsrfService csrfService, IWebHostEnvironment environment, IAuditService auditService)
    {
        _authService = authService;
        _csrfService = csrfService;
        _environment = environment;
        _auditService = auditService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "La verificacion de acceso esta incompleta." });
        }

        try
        {
            var trustedMfaToken = Request.Cookies["mfa_trusted"];
            var result = await _authService.LoginAsync(
                request.Email,
                request.Password,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken,
                trustedMfaToken);
            return Ok(AttachCookiesAndBuildAuthResponse(result));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        try
        {
            var refreshToken = Request.Cookies["refresh_token"];
            var result = await _authService.RefreshTokenAsync(refreshToken ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return Ok(AttachCookiesAndBuildAuthResponse(result));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyMfa([FromBody] VerifyMfaRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "La solicitud de inicio de sesion esta incompleta." });
        }

        try
        {
            var result = await _authService.VerifyMfaAsync(
                request.ChallengeId,
                request.Code,
                request.RememberDevice,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);
            return Ok(AttachCookiesAndBuildAuthResponse(result));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }


    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var revokedUserId = await _authService.LogoutAsync(refreshToken, cancellationToken);

        DeleteCookie("access_token");
        DeleteCookie("refresh_token");
        DeleteCookie("csrf_token");
        DeleteCookie("mfa_trusted");

        var actorUserId = TryGetUserId(out var authenticatedUserId)
            ? authenticatedUserId
            : revokedUserId;

        if (actorUserId.HasValue)
        {
            await _auditService.LogAsync(
                actorUserId,
                AuditActions.Logout,
                "USUARIOS",
                actorUserId,
                HttpContext,
                null,
                cancellationToken);
        }

        return Ok(new { message = "Sesión cerrada" });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _authService.GetCurrentAsync(userId, cancellationToken);
            return Ok(new
            {
                usuario = result.Usuario,
                permisos = result.Permisos
            });
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPut("cambiar-password")]
    [Authorize]
    public async Task<IActionResult> CambiarPassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "La solicitud de cambio de contrasena esta incompleta." });
        }

        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var result = await _authService.ChangePasswordAsync(
                userId,
                request.PasswordActual,
                request.PasswordNueva,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);
            return Ok(AttachCookiesAndBuildAuthResponse(result));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    private object AttachCookiesAndBuildAuthResponse(AuthResult result)
    {
        if (result.MfaRequired)
        {
            return new AuthResponse
            {
                MfaRequired = true,
                MfaSetupRequired = result.MfaSetupRequired,
                MfaChallengeId = result.MfaChallengeId,
                MfaSecret = result.MfaSecret,
                MfaOtpAuthUri = result.MfaOtpAuthUri
            };
        }

        if (!string.IsNullOrWhiteSpace(result.AccessToken))
        {
            Response.Cookies.Append("access_token", result.AccessToken, BuildCookieOptions(TimeSpan.FromHours(1), httpOnly: true, secure: ShouldUseSecureCookie()));
        }

        if (!string.IsNullOrWhiteSpace(result.RefreshToken))
        {
            Response.Cookies.Append("refresh_token", result.RefreshToken, BuildCookieOptions(TimeSpan.FromDays(7), httpOnly: true, secure: ShouldUseSecureCookie()));
        }

        if (!string.IsNullOrWhiteSpace(result.TrustedMfaToken) && result.TrustedMfaTokenExpiresAt.HasValue)
        {
            var maxAge = result.TrustedMfaTokenExpiresAt.Value - DateTime.UtcNow;
            if (maxAge > TimeSpan.Zero)
            {
                Response.Cookies.Append("mfa_trusted", result.TrustedMfaToken, BuildCookieOptions(maxAge, httpOnly: true, secure: ShouldUseSecureCookie()));
            }
        }

        var csrfToken = _csrfService.GenerateToken();
        Response.Cookies.Append("csrf_token", csrfToken, BuildCookieOptions(TimeSpan.FromDays(7), httpOnly: false, secure: ShouldUseSecureCookie()));

        return new AuthResponse
        {
            CsrfToken = csrfToken,
            Usuario = result.Usuario,
            Permisos = result.Permisos
        };
    }

    private static CookieOptions BuildCookieOptions(TimeSpan maxAge, bool httpOnly, bool secure)
    {
        return new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            MaxAge = maxAge,
            IsEssential = true
        };
    }

    private bool ShouldUseSecureCookie() => !_environment.IsDevelopment() || Request.IsHttps;

    private void DeleteCookie(string name)
    {
        Response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = name is "access_token" or "refresh_token" or "mfa_trusted",
            Secure = ShouldUseSecureCookie(),
            SameSite = SameSiteMode.Strict,
            IsEssential = true
        });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }
}
