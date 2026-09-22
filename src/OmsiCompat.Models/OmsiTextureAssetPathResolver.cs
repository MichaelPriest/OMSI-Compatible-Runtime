namespace OmsiCompat.Models;

public static class OmsiTextureAssetPathResolver
{
    private static readonly HashSet<string> SupportedExtensions =
        new(
            [
                ".bmp",
                ".dds",
                ".gif",
                ".jpeg",
                ".jpg",
                ".png",
                ".tga",
                ".webp"
            ],
            StringComparer.OrdinalIgnoreCase);

    public static bool TryResolveSceneryTexture(
        string omsiRoot,
        string sceneryObjectFullPath,
        string meshFullPath,
        string textureName,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneryObjectFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureName);

        fullPath = string.Empty;

        var sceneryRoot =
            Path.GetFullPath(
                Path.Combine(
                    omsiRoot,
                    "Sceneryobjects"));

        var objectDirectory =
            Path.GetDirectoryName(
                Path.GetFullPath(
                    sceneryObjectFullPath));

        var meshDirectory =
            Path.GetDirectoryName(
                Path.GetFullPath(
                    meshFullPath));

        if (string.IsNullOrWhiteSpace(objectDirectory) ||
            string.IsNullOrWhiteSpace(meshDirectory))
        {
            return false;
        }

        return TryResolve(
            sceneryRoot,
            textureName,
            [
                objectDirectory,
                Path.Combine(
                    objectDirectory,
                    "Texture"),
                meshDirectory,
                Path.Combine(
                    meshDirectory,
                    "Texture"),
                Path.GetFullPath(
                    Path.Combine(
                        meshDirectory,
                        "..",
                        "Texture"))
            ],
            out fullPath);
    }

    public static bool TryResolveSceneryObjectTexture(
        string omsiRoot,
        string sceneryObjectFullPath,
        string textureName,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneryObjectFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureName);

        fullPath = string.Empty;

        var sceneryRoot =
            Path.GetFullPath(
                Path.Combine(
                    omsiRoot,
                    "Sceneryobjects"));

        var objectDirectory =
            Path.GetDirectoryName(
                Path.GetFullPath(
                    sceneryObjectFullPath));

        if (string.IsNullOrWhiteSpace(objectDirectory))
        {
            return false;
        }

        return TryResolve(
            sceneryRoot,
            textureName,
            [
                objectDirectory,
                Path.Combine(
                    objectDirectory,
                    "Texture")
            ],
            out fullPath);
    }

    public static bool TryResolveSplineTexture(
        string omsiRoot,
        string splineFullPath,
        string textureName,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(splineFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureName);

        fullPath = string.Empty;

        var splinesRoot =
            Path.GetFullPath(
                Path.Combine(
                    omsiRoot,
                    "Splines"));

        var splineDirectory =
            Path.GetDirectoryName(
                Path.GetFullPath(
                    splineFullPath));

        if (string.IsNullOrWhiteSpace(splineDirectory))
        {
            return false;
        }

        return TryResolve(
            splinesRoot,
            textureName,
            [
                splineDirectory,
                Path.Combine(
                    splineDirectory,
                    "Texture")
            ],
            out fullPath);
    }

    private static bool TryResolve(
        string allowedRoot,
        string textureName,
        IReadOnlyList<string> baseDirectories,
        out string fullPath)
    {
        fullPath = string.Empty;

        var trimmed =
            textureName.Trim().Trim('"');

        if (Path.IsPathRooted(trimmed) ||
            trimmed.Contains(
                ':',
                StringComparison.Ordinal) ||
            trimmed.StartsWith(
                @"\",
                StringComparison.Ordinal) ||
            trimmed.StartsWith(
                "//",
                StringComparison.Ordinal) ||
            !SupportedExtensions.Contains(
                Path.GetExtension(trimmed)))
        {
            return false;
        }

        try
        {
            var normalized =
                trimmed
                    .Replace(
                        '\',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            var root =
                Path.GetFullPath(
                    allowedRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            var requiredPrefix =
                root +
                Path.DirectorySeparatorChar;

            foreach (var baseDirectory in baseDirectories)
            {
                if (TryResolveFromBaseDirectory(
                        root,
                        requiredPrefix,
                        baseDirectory,
                        normalized,
                        out fullPath))
                {
                    return true;
                }
            }

            foreach (var baseDirectory in baseDirectories)
            {
                var current =
                    Path.GetFullPath(
                        baseDirectory);

                while (IsInsideRoot(
                           current,
                           root,
                           requiredPrefix))
                {
                    if (TryResolveFromBaseDirectory(
                            root,
                            requiredPrefix,
                            current,
                            normalized,
                            out fullPath) ||
                        TryResolveFromBaseDirectory(
                            root,
                            requiredPrefix,
                            Path.Combine(
                                current,
                                "Texture"),
                            normalized,
                            out fullPath))
                    {
                        return true;
                    }

                    var parent =
                        Directory.GetParent(
                            current);

                    if (parent is null ||
                        string.Equals(
                            parent.FullName,
                            current,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    current =
                        parent.FullName;
                }
            }

            return false;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryResolveFromBaseDirectory(
        string root,
        string requiredPrefix,
        string baseDirectory,
        string normalizedTextureName,
        out string fullPath)
    {
        fullPath = string.Empty;

        var exactCandidate =
            Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    normalizedTextureName));

        if (TryAcceptCandidate(
                root,
                requiredPrefix,
                exactCandidate,
                out fullPath))
        {
            return true;
        }

        var extension =
            Path.GetExtension(
                normalizedTextureName);

        if (string.Equals(
                extension,
                ".dds",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ddsName =
            Path.ChangeExtension(
                normalizedTextureName,
                ".dds");

        var ddsCandidate =
            Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    ddsName));

        return TryAcceptCandidate(
            root,
            requiredPrefix,
            ddsCandidate,
            out fullPath);
    }

    private static bool TryAcceptCandidate(
        string root,
        string requiredPrefix,
        string candidate,
        out string fullPath)
    {
        fullPath = string.Empty;

        if (!IsInsideRoot(
                candidate,
                root,
                requiredPrefix) ||
            !File.Exists(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static bool IsInsideRoot(
        string candidate,
        string root,
        string requiredPrefix) =>
        string.Equals(
            candidate,
            root,
            StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(
            requiredPrefix,
            StringComparison.OrdinalIgnoreCase);
}
