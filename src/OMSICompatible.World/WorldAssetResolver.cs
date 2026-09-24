using OmsiCompat.Core;

namespace OMSICompatible.World;

public static class WorldAssetResolver
{
    public static WorldDependencyReport ResolvePrimaryDependencies(
        OmsiContentRoot contentRoot,
        IEnumerable<WorldObjectPlacement> objects,
        IEnumerable<WorldSplinePlacement> splines)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(splines);

        var dependencies = new List<WorldDependency>();

        foreach (var path in objects
                     .Select(static placement => placement.AssetPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            dependencies.Add(Resolve(
                contentRoot,
                WorldAssetKind.SceneryObject,
                path,
                "Sceneryobjects"));
        }

        foreach (var path in splines
                     .Select(static placement => placement.AssetPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            dependencies.Add(Resolve(
                contentRoot,
                WorldAssetKind.Spline,
                path,
                "Splines"));
        }

        return new WorldDependencyReport(dependencies.ToArray());
    }

    private static WorldDependency Resolve(
        OmsiContentRoot contentRoot,
        WorldAssetKind kind,
        string sourcePath,
        string conventionalFolder)
    {
        var normalizedSource = sourcePath
            .Trim()
            .Trim('"')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var candidateRelativePath =
            StartsWithDirectory(normalizedSource, conventionalFolder)
                ? normalizedSource
                : Path.Combine(conventionalFolder, normalizedSource);

        string? fullPath = null;
        var exists = false;

        try
        {
            var contentRootFullPath = EnsureTrailingSeparator(
                Path.GetFullPath(contentRoot.RootPath));

            var candidateFullPath = Path.GetFullPath(
                Path.Combine(contentRoot.RootPath, candidateRelativePath));

            if (candidateFullPath.StartsWith(
                    contentRootFullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                fullPath = candidateFullPath;
                exists = File.Exists(candidateFullPath);
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            fullPath = null;
            exists = false;
        }

        return new WorldDependency(
            kind,
            sourcePath,
            fullPath,
            exists);
    }

    private static bool StartsWithDirectory(string path, string directory)
    {
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == directory.Length ||
               path[directory.Length] == Path.DirectorySeparatorChar ||
               path[directory.Length] == Path.AltDirectorySeparatorChar;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ||
               path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
