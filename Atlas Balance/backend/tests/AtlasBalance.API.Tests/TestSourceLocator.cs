namespace AtlasBalance.API.Tests;

internal static class TestSourceLocator
{
    public static string Find(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var backendPath = Path.Combine([directory.FullName, "src", .. relativePath]);
            if (File.Exists(backendPath))
            {
                return backendPath;
            }

            var workspacePath = Path.Combine([directory.FullName, "Atlas Balance", "backend", "src", .. relativePath]);
            if (File.Exists(workspacePath))
            {
                return workspacePath;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"No se encontró el archivo fuente desde '{AppContext.BaseDirectory}'.",
            Path.Combine(relativePath));
    }
}
