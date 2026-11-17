using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Jobs;

// V-03.02 (J-5): AppDbContext declara un filtro global sobre RefreshToken
// (e.Usuario != null && e.Usuario.DeletedAt == null), asi que los tokens de
// los usuarios con soft delete eran invisibles para este job: no entraban ni
// por caducidad ni por revocacion y se quedaban vivos hasta su expiracion
// natural. Aqui se ignora el filtro global y se purgan tambien los tokens
// cuyo usuario esta borrado, con independencia de su caducidad.
//
// V-03.02 (J-1): bloqueo concurrente y reintento automatico.
[DisableConcurrentExecution(timeoutInSeconds: 900)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class LimpiezaRefreshTokensJob
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<LimpiezaRefreshTokensJob> _logger;

    public LimpiezaRefreshTokensJob(AppDbContext dbContext, ILogger<LimpiezaRefreshTokensJob> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // V-03.01 (#4): trabajo de servidor sin HttpContext (worker de Hangfire).
        using var _rlsScope = AtlasBalance.API.Data.RlsDbCommandInterceptor.SystemContextScope.Enter();
        var now = DateTime.UtcNow;

        // J-5: los ids de los usuarios con soft delete se cargan ignorando el
        // filtro global de AppDbContext; con el filtro puesto no se ven.
        var usuariosBorradosIds = await GetUsuariosBorradosIdsAsync(CancellationToken.None);

        var rows = await BuildPurgeQuery(now, usuariosBorradosIds).ExecuteDeleteAsync();

        _logger.LogInformation("LimpiezaRefreshTokensJob eliminó {Count} refresh tokens", rows);
    }

    internal async Task<List<Guid>> GetUsuariosBorradosIdsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.DeletedAt != null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    // J-5: la seleccion se extrae a un metodo testeable porque el proveedor
    // InMemory de EF no soporta ExecuteDeleteAsync: el test comprueba que
    // filas seleccionaria la purga, no que se borren.
    internal IQueryable<RefreshToken> BuildPurgeQuery(DateTime now, List<Guid> usuariosBorradosIds)
    {
        var revokedCutoff = now.AddDays(-1);

        // J-5: se ignora el filtro global de AppDbContext (que oculta los tokens
        // de los usuarios con soft delete) y se purgan tambien los tokens de
        // esos usuarios, con independencia de su caducidad: un usuario borrado
        // ya no puede autenticarse y sus tokens son basura.
        return _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(x => x.ExpiraEn <= now
                || (x.RevocadoEn.HasValue && x.RevocadoEn.Value <= revokedCutoff)
                || (usuariosBorradosIds.Count > 0 && usuariosBorradosIds.Contains(x.UsuarioId)));
    }
}
