using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Services;
using AtlasBalance.API.Services.IaPlanner;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Controllers;

[ApiController]
[Authorize]
[Route("api/ia")]
public sealed class IaController : ControllerBase
{
    private readonly IAtlasAiService _atlasAiService;
    private readonly IUserAccessService _userAccessService;
    private readonly IConversationMemory _conversationMemory;
    private readonly AppDbContext _dbContext;

    public IaController(
        IAtlasAiService atlasAiService,
        IUserAccessService userAccessService,
        IConversationMemory conversationMemory,
        AppDbContext dbContext)
    {
        _atlasAiService = atlasAiService;
        _userAccessService = userAccessService;
        _conversationMemory = conversationMemory;
        _dbContext = dbContext;
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

    [HttpGet("modelos")]
    public async Task<IActionResult> Modelos([FromQuery] string? provider, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        // SEC V-02.09: era el unico endpoint autenticado de /api/ia sin check
        // de PuedeUsarIa; exponia el catalogo del proveedor a usuarios sin el
        // permiso. Mismo criterio que Config y Chat.
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        var puedeUsarIa = await _dbContext.Usuarios
            .AsNoTracking()
            .AnyAsync(x => x.Id == scope.UserId && x.Activo && x.PuedeUsarIa, cancellationToken);
        if (!puedeUsarIa)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permiso para usar la IA." });
        }

        // SEC V-02.09: misma cota de busqueda que el resto de listados.
        if (search is { Length: > 200 })
        {
            search = search[..200];
        }

        try
        {
            return Ok(await _atlasAiService.GetModelsAsync(provider, search, cancellationToken));
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
                request.Model,
                request.PaisId,
                request.ThinkingMode);
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

    [HttpPost("conversacion/nueva")]
    public async Task<IActionResult> NuevaConversacion(CancellationToken cancellationToken)
    {
        var scope = await _userAccessService.GetScopeAsync(User, cancellationToken);
        if (scope.UserId == Guid.Empty)
        {
            return Unauthorized(new { error = "Usuario no autenticado" });
        }

        _conversationMemory.Invalidar(scope.UserId);
        return NoContent();
    }
}
