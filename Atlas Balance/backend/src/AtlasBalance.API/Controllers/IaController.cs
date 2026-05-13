using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize]
[Route("api/ia")]
public sealed class IaController : ControllerBase
{
    private readonly IAtlasAiService _atlasAiService;
    private readonly IUserAccessService _userAccessService;

    public IaController(
        IAtlasAiService atlasAiService,
        IUserAccessService userAccessService)
    {
        _atlasAiService = atlasAiService;
        _userAccessService = userAccessService;
    }

    [HttpGet("config")]
    public async Task<IActionResult> Config(CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        return Ok(await _atlasAiService.GetConfigAsync(scope, cancellationToken));
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] IaChatRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Escribe una pregunta para la IA." });
        }

        var pregunta = request.Pregunta?.Trim() ?? string.Empty;
        if (pregunta.Length == 0)
        {
            return BadRequest(new { error = "Escribe una pregunta." });
        }

        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        try
        {
            var response = await _atlasAiService.AskAsync(
                scope,
                pregunta,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken,
                request.Model);
            return Ok(response);
        }
        catch (IaAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (IaLimitExceededException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (IaOutOfScopeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (IaConfigurationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (IaProviderException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}
