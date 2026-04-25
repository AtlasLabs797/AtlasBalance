namespace GestionCaja.API;

public static class ConfigurationDefaults
{
    public const string GitHubOwner = "AtlasLabs797";
    public const string GitHubRepository = "AtlasBalance";
    public const string UpdateCheckUrl = "https://github.com/AtlasLabs797/AtlasBalance";

    public static bool TryNormalizeUpdateCheckUrl(string? configuredUrl, out string normalizedUrl)
    {
        normalizedUrl = string.IsNullOrWhiteSpace(configuredUrl)
            ? UpdateCheckUrl
            : configuredUrl.Trim();

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return segments.Length >= 2 &&
                   segments[0].Equals(GitHubOwner, StringComparison.OrdinalIgnoreCase) &&
                   segments[1].Equals(GitHubRepository, StringComparison.OrdinalIgnoreCase);
        }

        if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            var expectedPrefix = $"/repos/{GitHubOwner}/{GitHubRepository}/";
            return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
