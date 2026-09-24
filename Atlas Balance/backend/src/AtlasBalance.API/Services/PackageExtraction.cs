using System.IO.Compression;

namespace AtlasBalance.Shared.Packaging;

/// <summary>
/// Extraccion de paquetes ZIP de actualizacion con proteccion frente a
/// zip-slip y limites de tamano/numero de entradas. Fisicamente vive en
/// AtlasBalance.API (lo usa ActualizacionService al descargar y preparar el
/// paquete) y se enlaza tambien, via &lt;Compile Include Link&gt; en
/// AtlasBalance.Watchdog.csproj, dentro de ElevatedUpdateRunner: el runner
/// elevado vuelve a verificar y extraer su propia copia aislada del ZIP
/// antes de ejecutar el actualizador con privilegios de SYSTEM, y debe
/// reusar exactamente esta misma logica de extraccion segura en vez de
/// duplicarla.
/// </summary>
internal static class PackageExtraction
{
    public const long MaxArchiveEntryBytes = 512L * 1024L * 1024L;
    public const long MaxExtractedPackageBytes = 1024L * 1024L * 1024L;
    public const int MaxArchiveEntries = 10000;

    public static bool TryExtractSafely(string zipPath, string packageRoot)
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

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }
}
