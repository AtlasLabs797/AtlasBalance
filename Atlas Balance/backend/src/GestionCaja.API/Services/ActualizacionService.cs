using System.Reflection;
using System.Text.Json;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

public interface IActualizacionService
{
    Task<VersionActualResponse> GetVersionActualAsync(CancellationToken cancellationToken);
    Task<VersionDisponibleResponse> CheckVersionDisponibleAsync(CancellationToken cancellationToken);
    Task<bool> IniciarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken);
}

public sealed class ActualizacionService : IActualizacionService
{
    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWatchdogClientService _watchdogClientService;
    private readonly ILogger<ActualizacionService> _logger;
    private readonly IConfiguration _configuration;

    public ActualizacionService(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IWatchdogClientService watchdogClientService,
        ILogger<ActualizacionService> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _watchdogClientService = watchdogClientService;
        _logger = logger;
        _configuration = configuration;
    }

    public Task<VersionActualResponse> GetVersionActualAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new VersionActualResponse
        {
            VersionActual = ResolveCurrentVersion()
        });
    }

    public async Task<VersionDisponibleResponse> CheckVersionDisponibleAsync(CancellationToken cancellationToken)
    {
        var versionActual = ResolveCurrentVersion();
        var checkUrl = await _dbContext.Configuraciones
            .Where(c => c.Clave == "app_update_check_url")
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        checkUrl = ResolveConfiguredUpdateUrl(checkUrl, _logger);

        try
        {
            var response = await GetUpdateCheckBodyAsync(checkUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new VersionDisponibleResponse
                {
                    VersionActual = versionActual,
                    ActualizacionDisponible = false,
                    Mensaje = $"No se pudo consultar actualizaciones ({response.StatusCode})."
                };
            }

            var payload = ParseUpdatePayload(response.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Version))
            {
                return new VersionDisponibleResponse
                {
                    VersionActual = versionActual,
                    ActualizacionDisponible = false,
                    Mensaje = "Respuesta de actualizacion invalida."
                };
            }

            var hasUpdate = CompareVersions(versionActual, payload.Version) < 0;
            return new VersionDisponibleResponse
            {
                VersionActual = versionActual,
                VersionDisponible = payload.Version,
                ActualizacionDisponible = hasUpdate,
                Mensaje = hasUpdate
                    ? (payload.Message ?? "Actualizacion disponible.")
                    : (payload.Message ?? "El sistema ya esta actualizado.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check de actualizacion fallo");
            return new VersionDisponibleResponse
            {
                VersionActual = versionActual,
                ActualizacionDisponible = false,
                Mensaje = "No se pudo verificar actualizacion."
            };
        }
    }

    public async Task<bool> IniciarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
    {
        var finalSourcePath = sourcePath;
        var finalTargetPath = ResolveConfiguredUpdateTargetPath();

        if (string.IsNullOrWhiteSpace(finalSourcePath))
        {
            var checkUrl = await _dbContext.Configuraciones
                .Where(c => c.Clave == "app_update_check_url")
                .Select(c => c.Valor)
                .FirstOrDefaultAsync(cancellationToken);

            checkUrl = ResolveConfiguredUpdateUrl(checkUrl, _logger);

            try
            {
                var response = await GetUpdateCheckBodyAsync(checkUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var payload = ParseUpdatePayload(response.Body);
                    if (payload is not null)
                    {
                        finalSourcePath ??= payload.SourcePath;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo resolver source/target path desde update_check_url");
            }
        }

        if (string.IsNullOrWhiteSpace(finalTargetPath))
        {
            _logger.LogWarning("No se puede iniciar actualizacion: WatchdogSettings:UpdateTargetPath no configurado");
            return false;
        }

        if (!IsAllowedSourcePath(finalSourcePath, _configuration["WatchdogSettings:UpdateSourceRoot"]))
        {
            _logger.LogWarning("No se puede iniciar actualizacion: source path fuera de UpdateSourceRoot");
            return false;
        }

        return await _watchdogClientService.SolicitarActualizacionAsync(finalSourcePath, finalTargetPath, cancellationToken);
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(NormalizeVersion(left), out var leftVersion) &&
            Version.TryParse(NormalizeVersion(right), out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version)
    {
        var core = version.Trim().TrimStart('v', 'V');
        var dashIndex = core.IndexOf('-');
        if (dashIndex >= 0)
        {
            core = core[..dashIndex];
        }

        return core;
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string ResolveConfiguredUpdateUrl(string? configuredUrl, ILogger logger)
    {
        if (ConfigurationDefaults.TryNormalizeUpdateCheckUrl(configuredUrl, out var normalizedUrl))
        {
            return normalizedUrl;
        }

        logger.LogWarning("Update check URL no permitida; se usara el endpoint oficial de Atlas Balance.");
        return ConfigurationDefaults.UpdateCheckUrl;
    }

    private async Task<UpdateCheckHttpResponse> GetUpdateCheckBodyAsync(string checkUrl, CancellationToken cancellationToken)
    {
        var endpoint = ResolveUpdateCheckEndpoint(checkUrl);
        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (IsGitHubApiEndpoint(endpoint))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd($"AtlasBalance/{ResolveCurrentVersion()}");

            var token = ResolveGitHubUpdateToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new UpdateCheckHttpResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
    }

    private static Uri ResolveUpdateCheckEndpoint(string checkUrl)
    {
        if (!Uri.TryCreate(checkUrl, UriKind.Absolute, out var uri))
        {
            return new Uri(checkUrl, UriKind.RelativeOrAbsolute);
        }

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return uri;
        }

        return new Uri($"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest");
    }

    private static bool IsGitHubApiEndpoint(Uri endpoint)
    {
        return endpoint.IsAbsoluteUri &&
            endpoint.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveGitHubUpdateToken()
    {
        return _configuration["GitHubSettings:UpdateToken"]
            ?? _configuration["GITHUB_UPDATE_TOKEN"];
    }

    private string? ResolveConfiguredUpdateTargetPath()
    {
        var configured = _configuration["WatchdogSettings:UpdateTargetPath"];
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    private static bool IsAllowedSourcePath(string? sourcePath, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        if (!IsExplicitlyRooted(sourcePath) || !IsExplicitlyRooted(sourceRoot))
        {
            return false;
        }

        try
        {
            var fullSource = EnsureTrailingSeparator(Path.GetFullPath(sourcePath));
            var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(sourceRoot));
            return fullSource.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }

    private static bool IsExplicitlyRooted(string path)
    {
        return Path.IsPathRooted(path) ||
               (path.Length >= 3 &&
                char.IsLetter(path[0]) &&
                path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'));
    }

    private readonly record struct UpdateCheckHttpResponse(int StatusCode, bool IsSuccessStatusCode, string Body);

    private sealed class UpdateCheckPayload
    {
        public string? Version { get; init; }
        public string? Message { get; init; }
        public string? SourcePath { get; init; }
        public string? TargetPath { get; init; }
    }

    private static UpdateCheckPayload? ParseUpdatePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new UpdateCheckPayload
            {
                Version = TryGetString(root, "version", "version_disponible", "latest_version", "tag_name"),
                Message = TryGetString(root, "message", "mensaje", "name", "body"),
                SourcePath = TryGetString(root, "source_path", "sourcePath", "package_path"),
                TargetPath = TryGetString(root, "target_path", "targetPath", "install_path")
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}
