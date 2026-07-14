using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IConfiguracionRepository
{
    Task<string?> GetAsync(string clave, CancellationToken cancellationToken);
    Task UpsertAsync(string clave, string? valor, bool esSecreto, string? tipo, string? descripcion, Guid? usuarioModificacionId, CancellationToken cancellationToken);
    IReadOnlyList<string> SecretKeys { get; }
}

public static class ConfiguracionSecretKeys
{
    public static readonly IReadOnlyList<string> List = new[]
    {
        "smtp_password",
        "exchange_rate_api_key",
        "openrouter_api_key",
        "openai_api_key",
        "minimax_api_key",
        "google_drive_oauth_client_secret",
        "backup_cloud_encryption_key",
        "github_update_token"
    };
}

public sealed class ConfiguracionRepository : IConfiguracionRepository
{
    private readonly AppDbContext _dbContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IClock _clock;

    public ConfiguracionRepository(AppDbContext dbContext, ISecretProtector secretProtector, IClock clock)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _clock = clock;
    }

    public IReadOnlyList<string> SecretKeys => ConfiguracionSecretKeys.List;

    public async Task<string?> GetAsync(string clave, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Configuraciones.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Clave == clave, cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (row.EsSecreto)
        {
            return string.IsNullOrEmpty(row.Valor) ? null : _secretProtector.UnprotectFromStorage(row.Valor);
        }

        return row.Valor;
    }

    public async Task UpsertAsync(string clave, string? valor, bool esSecreto, string? tipo, string? descripcion, Guid? usuarioModificacionId, CancellationToken cancellationToken)
    {
        var storedValue = valor;
        if (esSecreto && !string.IsNullOrEmpty(valor))
        {
            storedValue = _secretProtector.ProtectForStorage(valor);
        }

        var existing = await _dbContext.Configuraciones.FirstOrDefaultAsync(c => c.Clave == clave, cancellationToken);
        if (existing is null)
        {
            _dbContext.Configuraciones.Add(new Configuracion
            {
                Clave = clave,
                Valor = storedValue ?? string.Empty,
                EsSecreto = esSecreto,
                Tipo = tipo,
                Descripcion = descripcion,
                FechaModificacion = _clock.UtcNow,
                UsuarioModificacionId = usuarioModificacionId
            });
        }
        else
        {
            existing.Valor = storedValue ?? string.Empty;
            existing.EsSecreto = esSecreto;
            existing.Tipo = tipo;
            existing.Descripcion = descripcion;
            existing.FechaModificacion = _clock.UtcNow;
            existing.UsuarioModificacionId = usuarioModificacionId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
