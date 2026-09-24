using OmsiCompat.Core;

namespace OmsiCompat.Map;

public static class MapDiscovery
{
    public static IReadOnlyList<OmsiMapInfo> Discover(OmsiContentRoot contentRoot)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);

        if (!Directory.Exists(contentRoot.MapsPath))
        {
            return Array.Empty<OmsiMapInfo>();
        }

        var maps = new List<OmsiMapInfo>();

        foreach (var directory in Directory.EnumerateDirectories(contentRoot.MapsPath))
        {
            var globalConfig = FindFileCaseInsensitive(directory, "global.cfg");
            if (globalConfig is null)
            {
                continue;
            }

            var info = new FileInfo(globalConfig);
            maps.Add(new OmsiMapInfo(
                FolderName: Path.GetFileName(directory),
                DirectoryPath: directory,
                GlobalConfigPath: globalConfig,
                GlobalConfigBytes: info.Exists ? info.Length : 0));
        }

        return maps
            .OrderBy(static map => map.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static OmsiMapInfo? FindByFolderName(OmsiContentRoot contentRoot, string folderName)
    {
        return Discover(contentRoot)
            .FirstOrDefault(map => string.Equals(map.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindFileCaseInsensitive(string directory, string fileName)
    {
        return Directory
            .EnumerateFiles(directory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }
}
