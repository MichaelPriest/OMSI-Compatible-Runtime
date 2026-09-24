namespace OmsiCompat.Core;

public static class ContentLocator
{
    public static IReadOnlyList<string> GetCandidates(string? explicitPath = null)
    {
        var candidates = new List<string>();

        Add(candidates, explicitPath);
        Add(candidates, Environment.GetEnvironmentVariable("OMSI_CONTENT_ROOT"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Add(candidates, CombineIfPresent(programFilesX86, "Steam", "steamapps", "common", "OMSI 2"));
        Add(candidates, CombineIfPresent(programFiles, "Steam", "steamapps", "common", "OMSI 2"));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static OmsiContentRoot? FindFirstValid(string? explicitPath, out IReadOnlyList<string> checkedPaths)
    {
        var paths = GetCandidates(explicitPath);
        checkedPaths = paths;

        foreach (var path in paths)
        {
            if (OmsiContentRoot.TryCreate(path, out var contentRoot, out _))
            {
                return contentRoot;
            }
        }

        return null;
    }

    private static string? CombineIfPresent(string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    private static void Add(ICollection<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value);
        }
    }
}
