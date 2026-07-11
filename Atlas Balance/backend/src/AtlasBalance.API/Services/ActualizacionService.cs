using System.IO.Compression;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IActualizacionService
{
    Task<VersionActualResponse> GetVersionActualAsync(CancellationToken cancellationToken);
    Task<VersionDisponibleResponse> CheckVersionDisponibleAsync(CancellationToken cancellationToken);
    Task<bool> IniciarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken);
}

public sealed class ActualizacionService : IActualizacionService
{
    private const long MaxUpdatePackageBytes = 300L * 1024L * 1024L;
    private const long MaxExtractedPackageBytes = 1024L * 1024L * 1024L;
    private const long MaxArchiveEntryBytes = 512L * 1024L * 1024L;
    private const int MaxArchiveEntries = 10000;

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
                    Mensaje = "Respuesta de actualización no válida."
                };
            }

            var hasUpdate = CompareVersions(versionActual, payload.Version) < 0;
            var preflight = hasUpdate
                ? await BuildUpdatePreflightAsync(payload, cancellationToken)
                : UpdatePreflight.Empty;
            return new VersionDisponibleResponse
            {
                VersionActual = versionActual,
                VersionDisponible = payload.Version,
                ActualizacionDisponible = hasUpdate,
                Instalable = hasUpdate && preflight.Instalable,
                Bloqueos = preflight.Bloqueos,
                AssetZipNombre = payload.AssetName,
                AssetZipDetectado = preflight.AssetZipDetectado,
                FirmaDetectada = preflight.FirmaDetectada,
                DigestPresente = preflight.DigestPresente,
                ClavePublicaConfigurada = preflight.ClavePublicaConfigurada,
                WatchdogDisponible = preflight.WatchdogDisponible,
                Mensaje = hasUpdate
                    ? $"Actualización disponible: {payload.Version}. Revisa el paquete antes de instalar."
                    : "El sistema ya está actualizado."
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
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            _logger.LogWarning("No se puede iniciar actualizacion: sourcePath manual rechazado; la app solo instala assets descargados y firmados.");
            return false;
        }

        var available = await CheckVersionDisponibleAsync(cancellationToken);
        if (!available.ActualizacionDisponible || !available.Instalable)
        {
            _logger.LogWarning("No se puede iniciar actualizacion: preflight no instalable. Bloqueos: {Bloqueos}", string.Join(" | ", available.Bloqueos));
            return false;
        }

        string? finalSourcePath = null;
        string? finalZipPath = null;
        var finalTargetPath = ResolveConfiguredUpdateInstallPath();

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
                if (payload is not null && !string.IsNullOrWhiteSpace(payload.AssetDownloadUrl))
                {
                    var download = await DownloadAndPreparePackageAsync(payload, cancellationToken);
                    finalSourcePath = download.PackageRoot;
                    finalZipPath = download.ZipPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo resolver asset firmado desde update_check_url");
        }

        if (string.IsNullOrWhiteSpace(finalTargetPath))
        {
            _logger.LogWarning("No se puede iniciar actualizacion: ruta de instalacion no configurada");
            return false;
        }

        if (!IsAllowedSourcePath(finalSourcePath, _configuration["WatchdogSettings:UpdateSourceRoot"]))
        {
            _logger.LogWarning("No se puede iniciar actualizacion: source path fuera de UpdateSourceRoot");
            return false;
        }

        return await _watchdogClientService.SolicitarActualizacionAsync(finalSourcePath, finalTargetPath, finalZipPath, cancellationToken);
    }

    private async Task<UpdatePreflight> BuildUpdatePreflightAsync(UpdateCheckPayload payload, CancellationToken cancellationToken)
    {
        var bloqueos = new List<string>();
        var assetZipDetectado = IsOfficialReleaseAssetUrl(payload.AssetDownloadUrl);
        if (!assetZipDetectado)
        {
            bloqueos.Add("No se detecto un asset ZIP oficial win-x64 del repositorio Atlas Balance.");
        }

        var firmaDetectada = IsOfficialReleaseSignatureUrl(payload.AssetSignatureDownloadUrl);
        if (!firmaDetectada)
        {
            bloqueos.Add("No se detecto la firma .zip.sig oficial del asset.");
        }

        var digestPresente = HasValidSha256Digest(payload.AssetDigest);
        if (!digestPresente)
        {
            bloqueos.Add("El asset no incluye digest SHA-256 valido.");
        }

        var clavePublicaConfigurada = !string.IsNullOrWhiteSpace(ResolveReleaseSigningPublicKeyPem());
        if (!clavePublicaConfigurada)
        {
            bloqueos.Add("Falta configurar UpdateSecurity:ReleaseSigningPublicKeyPem.");
        }

        var sourceRoot = ResolveConfiguredUpdateSourceRoot();
        if (string.IsNullOrWhiteSpace(sourceRoot) || !IsExplicitlyRooted(sourceRoot))
        {
            bloqueos.Add("Falta configurar WatchdogSettings:UpdateSourceRoot como ruta absoluta.");
        }

        var installPath = ResolveConfiguredUpdateInstallPath();
        if (string.IsNullOrWhiteSpace(installPath) || !IsExplicitlyRooted(installPath))
        {
            bloqueos.Add("Falta configurar WatchdogSettings:UpdateInstallPath como ruta absoluta.");
        }

        var watchdogDisponible = await _watchdogClientService.EstaDisponibleAsync(cancellationToken);
        if (!watchdogDisponible)
        {
            bloqueos.Add("Watchdog local no disponible o sin secreto configurado.");
        }

        return new UpdatePreflight(
            bloqueos.Count == 0,
            bloqueos,
            assetZipDetectado,
            firmaDetectada,
            digestPresente,
            clavePublicaConfigurada,
            watchdogDisponible);
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
        var match = Regex.Match(version.Trim(), @"(?i)v?[-_]?(\d+(?:[.-]\d+){0,3})");
        return match.Success
            ? match.Groups[1].Value.Replace('-', '.')
            : version.Trim().TrimStart('v', 'V');
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

    private async Task<(string? PackageRoot, string? ZipPath)> DownloadAndPreparePackageAsync(UpdateCheckPayload payload, CancellationToken cancellationToken)
    {
        var assetUrl = payload.AssetDownloadUrl;
        if (!IsOfficialReleaseAssetUrl(assetUrl))
        {
            _logger.LogWarning("Asset de actualizacion rechazado por no pertenecer al repo oficial.");
            return (null, null);
        }

        var sourceRoot = ResolveConfiguredUpdateSourceRoot();
        if (string.IsNullOrWhiteSpace(sourceRoot) || !IsExplicitlyRooted(sourceRoot))
        {
            _logger.LogWarning("No se puede descargar actualizacion: WatchdogSettings:UpdateSourceRoot no configurado");
            return (null, null);
        }

        var packageVersion = string.IsNullOrWhiteSpace(payload.Version) ? "latest" : payload.Version;
        var safeVersion = ToSafePathSegment(packageVersion);
        var packageRoot = Path.Combine(sourceRoot, safeVersion);
        var zipPath = Path.Combine(sourceRoot, $"{safeVersion}.zip");

        Directory.CreateDirectory(sourceRoot);
        EnsurePathWithinRoot(packageRoot, sourceRoot);
        EnsurePathWithinRoot(zipPath, sourceRoot);

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
        request.Headers.UserAgent.ParseAdd($"AtlasBalance/{ResolveCurrentVersion()}");
        var token = ResolveGitHubUpdateToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("No se pudo descargar asset de actualizacion: {StatusCode}", (int)response.StatusCode);
            return (null, null);
        }

        var maxUpdatePackageBytes = ResolveMaxUpdatePackageBytes();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxUpdatePackageBytes)
        {
            _logger.LogWarning("Asset de actualizacion rechazado por tamano declarado superior al limite.");
            return (null, null);
        }

        if (!await CopyContentToFileWithLimitAsync(response.Content, zipPath, maxUpdatePackageBytes, cancellationToken))
        {
            _logger.LogWarning("Asset de actualizacion rechazado por tamano descargado invalido.");
            TryDeleteFile(zipPath);
            return (null, null);
        }

        var downloadedSize = new FileInfo(zipPath).Length;
        if (downloadedSize is 0 || downloadedSize > maxUpdatePackageBytes)
        {
            _logger.LogWarning("Asset de actualizacion rechazado por tamano descargado invalido.");
            TryDeleteFile(zipPath);
            return (null, null);
        }

        if (!VerifyAssetDigest(zipPath, payload.AssetDigest))
        {
            _logger.LogWarning("Asset de actualizacion rechazado por digest SHA-256 ausente o invalido.");
            TryDeleteFile(zipPath);
            return (null, null);
        }

        if (!await VerifyAssetSignatureAsync(http, zipPath, payload.AssetSignatureDownloadUrl, token, cancellationToken))
        {
            _logger.LogWarning("Asset de actualizacion rechazado por firma ausente o invalida.");
            TryDeleteFile(zipPath);
            return (null, null);
        }

        if (Directory.Exists(packageRoot))
        {
            Directory.Delete(packageRoot, recursive: true);
        }

        if (!TryExtractPackageSafely(zipPath, packageRoot))
        {
            _logger.LogWarning("Asset de actualizacion rechazado por tamano, cantidad de entradas o rutas invalidas.");
            return (null, null);
        }
        var resolvedPackageRoot = ResolveExtractedPackageRoot(packageRoot);
        if (!IsValidReleasePackage(resolvedPackageRoot))
        {
            _logger.LogWarning("Paquete de actualizacion descargado invalido.");
            return (null, null);
        }

        return (resolvedPackageRoot, zipPath);
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

    private string? ResolveConfiguredUpdateSourceRoot()
    {
        var configured = _configuration["WatchdogSettings:UpdateSourceRoot"];
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    private string? ResolveConfiguredUpdateInstallPath()
    {
        var installPath = _configuration["WatchdogSettings:UpdateInstallPath"];
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            return installPath.Trim();
        }

        var configured = _configuration["WatchdogSettings:UpdateTargetPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return TryDeriveInstallPathFromLegacyTarget(configured.Trim());
    }

    private static string TryDeriveInstallPathFromLegacyTarget(string configuredTarget)
    {
        try
        {
            var fullPath = Path.GetFullPath(configuredTarget);
            var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
            if (leaf.Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath)) ?? configuredTarget;
            }
        }
        catch
        {
            return configuredTarget;
        }

        return configuredTarget;
    }

    private long ResolveMaxUpdatePackageBytes()
    {
        var configured = _configuration["UpdateSecurity:MaxUpdatePackageBytes"];
        if (long.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return Math.Min(value, MaxUpdatePackageBytes);
        }

        return MaxUpdatePackageBytes;
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
        public string? AssetName { get; init; }
        public string? AssetDownloadUrl { get; init; }
        public string? AssetDigest { get; init; }
        public string? AssetSignatureDownloadUrl { get; init; }
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
            var asset = TryGetReleaseAsset(root);
            return new UpdateCheckPayload
            {
                Version = TryGetString(root, "version", "version_disponible", "latest_version", "tag_name"),
                Message = TryGetString(root, "message", "mensaje", "name", "body"),
                SourcePath = TryGetString(root, "source_path", "sourcePath", "package_path"),
                TargetPath = TryGetString(root, "target_path", "targetPath", "install_path"),
                AssetName = asset.Name,
                AssetDownloadUrl = asset.DownloadUrl,
                AssetDigest = asset.Digest,
                AssetSignatureDownloadUrl = asset.SignatureDownloadUrl
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

    private static ReleaseAssetRef TryGetReleaseAsset(JsonElement root)
    {
        var direct = TryGetString(root, "asset_download_url", "assetDownloadUrl", "download_url", "browser_download_url");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return new ReleaseAssetRef(
                TryGetString(root, "asset_name", "assetName", "name"),
                direct,
                TryGetString(root, "asset_digest", "assetDigest", "digest"),
                TryGetString(root, "asset_signature_url", "assetSignatureUrl", "signature_download_url", "signatureDownloadUrl"));
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return new ReleaseAssetRef(null, null, null, null);
        }

        string? zipName = null;
        string? zipDownloadUrl = null;
        string? zipDigest = null;
        var signatureAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets.EnumerateArray())
        {
            var name = TryGetString(asset, "name") ?? string.Empty;
            var downloadUrl = TryGetString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            if (name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            {
                signatureAssets[name] = downloadUrl;
                continue;
            }

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("AtlasBalance", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            {
                zipName = name;
                zipDownloadUrl = downloadUrl;
                zipDigest = TryGetString(asset, "digest");
            }
        }

        var signatureDownloadUrl = zipName is not null &&
            signatureAssets.TryGetValue($"{zipName}.sig", out var matchingSignature)
                ? matchingSignature
                : null;

        return new ReleaseAssetRef(zipName, zipDownloadUrl, zipDigest, signatureDownloadUrl);
    }

    private static bool HasValidSha256Digest(string? expectedDigest)
    {
        if (string.IsNullOrWhiteSpace(expectedDigest))
        {
            return false;
        }

        var normalized = expectedDigest.Trim();
        const string prefix = "sha256:";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedHash = normalized[prefix.Length..].Trim();
        return expectedHash.Length == 64 && expectedHash.All(Uri.IsHexDigit);
    }

    private static bool VerifyAssetDigest(string zipPath, string? expectedDigest)
    {
        if (string.IsNullOrWhiteSpace(expectedDigest))
        {
            return false;
        }

        var normalized = expectedDigest.Trim();
        const string prefix = "sha256:";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedHash = normalized[prefix.Length..].Trim();
        if (expectedHash.Length != 64 || expectedHash.Any(ch => !Uri.IsHexDigit(ch)))
        {
            return false;
        }

        using var stream = File.OpenRead(zipPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actualHash, expectedHash.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private async Task<bool> VerifyAssetSignatureAsync(HttpClient http, string zipPath, string? signatureUrl, string? githubToken, CancellationToken cancellationToken)
    {
        var publicKeyPem = ResolveReleaseSigningPublicKeyPem();
        if (string.IsNullOrWhiteSpace(publicKeyPem) || !IsOfficialReleaseSignatureUrl(signatureUrl))
        {
            return false;
        }

        byte[] signature;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, signatureUrl);
            request.Headers.UserAgent.ParseAdd($"AtlasBalance/{ResolveCurrentVersion()}");
            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 8192)
            {
                return false;
            }

            var rawSignature = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (rawSignature.Length is 0 or > 8192)
            {
                return false;
            }

            signature = NormalizeSignatureBytes(rawSignature);
        }
        catch
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.AsSpan());
            using var stream = File.OpenRead(zipPath);
            return rsa.VerifyData(stream, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private string? ResolveReleaseSigningPublicKeyPem()
    {
        var pem = _configuration["UpdateSecurity:ReleaseSigningPublicKeyPem"]
            ?? _configuration["ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM"];
        return string.IsNullOrWhiteSpace(pem)
            ? null
            : pem.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static byte[] NormalizeSignatureBytes(byte[] rawSignature)
    {
        var text = System.Text.Encoding.ASCII.GetString(rawSignature).Trim();
        if (text.Length > 0 && text.All(ch => char.IsLetterOrDigit(ch) || ch is '+' or '/' or '=' or '\r' or '\n'))
        {
            try
            {
                return Convert.FromBase64String(text);
            }
            catch
            {
                return rawSignature;
            }
        }

        return rawSignature;
    }

    private static bool TryExtractPackageSafely(string zipPath, string packageRoot)
    {
        Directory.CreateDirectory(packageRoot);
        var rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var rootFullPathWithSeparator = EnsureTrailingSeparator(rootFullPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var entryCount = 0;
        var totalUncompressedBytes = 0L;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName))
            {
                continue;
            }

            entryCount++;
            if (entryCount > MaxArchiveEntries ||
                entry.Length < 0 ||
                entry.Length > MaxArchiveEntryBytes)
            {
                Directory.Delete(packageRoot, recursive: true);
                return false;
            }

            totalUncompressedBytes += entry.Length;
            if (totalUncompressedBytes > MaxExtractedPackageBytes)
            {
                Directory.Delete(packageRoot, recursive: true);
                return false;
            }

            var destinationFullPath = Path.GetFullPath(Path.Combine(packageRoot, entry.FullName));
            var isDirectoryEntry = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var destinationNormalized = Path.TrimEndingDirectorySeparator(destinationFullPath);

            if (string.Equals(destinationNormalized, rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCurrentDirectoryEntry(entry.FullName, isDirectoryEntry, entry.Length))
                {
                    Directory.Delete(packageRoot, recursive: true);
                    return false;
                }

                Directory.CreateDirectory(rootFullPath);
                continue;
            }

            if (!destinationFullPath.StartsWith(rootFullPathWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(packageRoot, recursive: true);
                return false;
            }

            if (isDirectoryEntry)
            {
                Directory.CreateDirectory(destinationFullPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationFullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationFullPath, overwrite: true);
        }

        return true;
    }

    private static bool IsCurrentDirectoryEntry(string entryName, bool isDirectoryEntry, long entryLength)
    {
        var normalizedName = entryName.Replace('\\', '/').TrimEnd('/');
        return string.Equals(normalizedName, ".", StringComparison.Ordinal) &&
               (isDirectoryEntry || entryLength == 0);
    }

    private static async Task<bool> CopyContentToFileWithLimitAsync(HttpContent content, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        await using var output = File.Create(destinationPath);
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                return total > 0;
            }

            total += read;
            if (total > maxBytes)
            {
                return false;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only; the update is already rejected.
        }
    }

    private static bool IsOfficialReleaseAssetUrl(string? assetUrl)
    {
        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedPrefix = $"/{ConfigurationDefaults.GitHubOwner}/{ConfigurationDefaults.GitHubRepository}/releases/download/";
        return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOfficialReleaseSignatureUrl(string? signatureUrl)
    {
        if (!Uri.TryCreate(signatureUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedPrefix = $"/{ConfigurationDefaults.GitHubOwner}/{ConfigurationDefaults.GitHubRepository}/releases/download/";
        return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.EndsWith(".zip.sig", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSafePathSegment(string value)
    {
        var cleaned = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "latest" : cleaned;
    }

    private static string ResolveExtractedPackageRoot(string extractionRoot)
    {
        if (IsValidReleasePackage(extractionRoot))
        {
            return extractionRoot;
        }

        var children = Directory.GetDirectories(extractionRoot);
        return children.Length == 1 ? children[0] : extractionRoot;
    }

    private static bool IsValidReleasePackage(string packageRoot)
    {
        return File.Exists(Path.Combine(packageRoot, "VERSION")) &&
               File.Exists(Path.Combine(packageRoot, "api", "AtlasBalance.API.exe")) &&
               File.Exists(Path.Combine(packageRoot, "watchdog", "AtlasBalance.Watchdog.exe")) &&
               File.Exists(Path.Combine(packageRoot, "scripts", "Actualizar-AtlasBalance.ps1"));
    }

    private static void EnsurePathWithinRoot(string path, string root)
    {
        if (!IsAllowedSourcePath(path, root))
        {
            throw new InvalidOperationException("Ruta de actualizacion fuera de UpdateSourceRoot.");
        }
    }

    private sealed record UpdatePreflight(
        bool Instalable,
        IReadOnlyList<string> Bloqueos,
        bool AssetZipDetectado,
        bool FirmaDetectada,
        bool DigestPresente,
        bool ClavePublicaConfigurada,
        bool WatchdogDisponible)
    {
        public static UpdatePreflight Empty { get; } = new(false, [], false, false, false, false, false);
    }

    private readonly record struct ReleaseAssetRef(string? Name, string? DownloadUrl, string? Digest, string? SignatureDownloadUrl);
}
