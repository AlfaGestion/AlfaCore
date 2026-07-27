namespace AlfaCore.Configuration;

public static class DotEnvLoader
{
    public static void LoadIfPresent(string contentRootPath)
    {
        var filePath = FindDotEnvFile(contentRootPath);
        if (filePath is null)
        {
            return;
        }

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindDotEnvFile(string contentRootPath)
    {
        var candidates = EnumerateDotEnvCandidates(contentRootPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .ToList();

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDotEnvCandidates(string contentRootPath)
    {
        foreach (var basePath in EnumerateSearchRoots(contentRootPath))
        {
            var current = new DirectoryInfo(basePath);
            while (current is not null)
            {
                yield return Path.Combine(current.FullName, ".env");
                current = current.Parent;
            }
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots(string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(contentRootPath))
            yield return contentRootPath;

        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            yield return AppContext.BaseDirectory;

        var currentDirectory = Environment.CurrentDirectory;
        if (!string.IsNullOrWhiteSpace(currentDirectory))
            yield return currentDirectory;
    }
}
