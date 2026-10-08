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

    public static bool TryResolveVehicleTexture(
        string omsiRoot,
        string vehicleDirectory,
        string modelConfigPath,
        string meshFullPath,
        string textureName,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureName);

        fullPath = string.Empty;

        var vehiclesRoot =
            Path.GetFullPath(Path.Combine(omsiRoot, "Vehicles"));

        var modelDirectory =
            Path.GetDirectoryName(Path.GetFullPath(modelConfigPath))
            ?? vehicleDirectory;

        var meshDirectory =
            Path.GetDirectoryName(Path.GetFullPath(meshFullPath))
            ?? modelDirectory;

        string[] textureSearchDirectories =
        [
            vehicleDirectory,
            Path.Combine(vehicleDirectory, "Texture"),
            modelDirectory,
            Path.Combine(modelDirectory, "Texture"),
            meshDirectory,
            Path.Combine(meshDirectory, "Texture"),
            Path.GetFullPath(Path.Combine(meshDirectory, "..", "Texture"))
        ];

        if (TryResolve(
                vehiclesRoot,
                textureName,
                textureSearchDirectories,
                out fullPath))
        {
            return true;
        }

        // Some OMSI add-ons author freetex names relative to an old model
        // layout ("..\\..\\Texture\\panel.bmp"). Follow openOMSI's
        // Texture-component fallback, but only for relative paths and only
        // inside the permitted Vehicles tree.
        var rawName = textureName.Trim().Trim('"');
        if (Path.IsPathRooted(rawName) ||
            rawName.Contains(':', StringComparison.Ordinal) ||
            rawName.StartsWith(@"\", StringComparison.Ordinal) ||
            rawName.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var components = rawName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var textureComponent = Array.FindIndex(
            components,
            static component => string.Equals(
                component,
                "Texture",
                StringComparison.OrdinalIgnoreCase));

        if (textureComponent < 0 ||
            textureComponent == components.Length - 1)
        {
            return false;
        }

        // The common resolver enforces extension validation and the allowed
        // root on this second lookup too; it never accepts escaped paths.
        var textureRelativeName = string.Join(
            Path.DirectorySeparatorChar,
            components[(textureComponent + 1)..]);

        return TryResolve(
            vehiclesRoot,
            textureRelativeName,
            textureSearchDirectories,
            out fullPath);
    }

    public static bool TryResolveGroundTexture(
        string omsiRoot,
        string mapDirectory,
        string textureName,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            mapDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            textureName);

        fullPath = string.Empty;

        var root =
            Path.GetFullPath(
                omsiRoot);

        var mapRoot =
            Path.GetFullPath(
                mapDirectory);

        var requiredRootPrefix =
            root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!mapRoot.StartsWith(
                requiredRootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryResolve(
            root,
            textureName,
            [
                mapRoot,
                root
            ],
            out fullPath);
    }

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
                        (char)92,
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

            // openOMSI resolves authored paths such as
            // "Splines\OtherPack\Texture\road.bmp" relative to the OMSI
            // content root. This resolver is deliberately scoped to its
            // asset category, so strip only that exact category prefix and
            // resolve against the category root. Do not search outside it.
            var categoryName = Path.GetFileName(root);
            var categoryPrefix =
                categoryName +
                Path.DirectorySeparatorChar;

            if ((string.Equals(categoryName, "Splines", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(categoryName, "Sceneryobjects", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(categoryName, "Vehicles", StringComparison.OrdinalIgnoreCase)) &&
                normalized.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase) &&
                TryResolveFromBaseDirectory(
                    root,
                    requiredPrefix,
                    root,
                    normalized[categoryPrefix.Length..],
                    out fullPath))
            {
                return true;
            }

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

        // OMSI (and openOMSI's find_texture_in_dir) prefers a same-stem
        // optimized DDS in this very directory before the authored BMP,
        // TGA or PNG. Never let a DDS in a later/global directory override
        // a local texture.
        var ddsCandidate =
            Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    Path.ChangeExtension(normalizedTextureName, ".dds")));

        if (TryAcceptCandidate(
                root,
                requiredPrefix,
                ddsCandidate,
                out fullPath))
        {
            return true;
        }

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

        // OMSI add-ons frequently ship an optimized DDS next to a model
        // whose material still names BMP/TGA/PNG, and some repaints do the
        // inverse. Prefer DDS after the exact name, then try the remaining
        // supported raster extensions before declaring the texture missing.
        var fallbackExtensions =
            new[]
            {
                ".dds",
                ".png",
                ".tga",
                ".bmp",
                ".jpg",
                ".jpeg",
                ".webp",
                ".gif"
            };

        foreach (var fallbackExtension in
                 fallbackExtensions)
        {
            if (string.Equals(
                    extension,
                    fallbackExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fallbackName =
                Path.ChangeExtension(
                    normalizedTextureName,
                    fallbackExtension);

            var fallbackCandidate =
                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        fallbackName));

            if (TryAcceptCandidate(
                    root,
                    requiredPrefix,
                    fallbackCandidate,
                    out fullPath))
            {
                return true;
            }
        }

        return false;
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
