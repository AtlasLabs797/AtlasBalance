namespace AtlasBalance.Watchdog.Logging;

internal static class LogScrubber
{
    private const int MaxLength = 256;

    public static string? Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sanitized = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");

        return sanitized.Length > MaxLength ? sanitized[..MaxLength] : sanitized;
    }
}