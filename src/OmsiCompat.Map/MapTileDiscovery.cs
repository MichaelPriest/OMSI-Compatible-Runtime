using System.Globalization;
using System.Text.RegularExpressions;

namespace OmsiCompat.Map;

public static class MapTileDiscovery
{
    private static readonly Regex TileFilePattern = new(
        "^tile_(-?\\d+)_(-?\\d+)\\.map$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<OmsiMapTileInfo> Discover(OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (!Directory.Exists(map.DirectoryPath))
        {
            return Array.Empty<OmsiMapTileInfo>();
        }

        var declared = GlobalConfigMapParser.ReadTileDeclarations(map);
        if (declared.Count > 0)
        {
            return DiscoverDeclared(map, declared);
        }

        return DiscoverByFileName(map);
    }

    private static IReadOnlyList<OmsiMapTileInfo> DiscoverDeclared(
        OmsiMapInfo map,
        IReadOnlyList<OmsiGlobalTileDeclaration> declarations)
    {
        var tiles = new List<OmsiMapTileInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapRoot = EnsureTrailingSeparator(Path.GetFullPath(map.DirectoryPath));

        foreach (var declaration in declarations)
        {
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(
                    Path.Combine(map.DirectoryPath, declaration.FileName));
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                continue;
            }

            if (!fullPath.StartsWith(mapRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath) ||
                !seenPaths.Add(fullPath))
            {
                continue;
            }

            var file = new FileInfo(fullPath);
            tiles.Add(new OmsiMapTileInfo(
                declaration.Coordinate,
                fullPath,
                file.Length));
        }

        return Sort(tiles);
    }

    private static IReadOnlyList<OmsiMapTileInfo> DiscoverByFileName(OmsiMapInfo map)
    {
        var tiles = new List<OmsiMapTileInfo>();

        foreach (var path in Directory.EnumerateFiles(map.DirectoryPath))
        {
            var match = TileFilePattern.Match(Path.GetFileName(path));
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            {
                continue;
            }

            var file = new FileInfo(path);
            tiles.Add(new OmsiMapTileInfo(
                new OmsiTileCoordinate(x, y),
                path,
                file.Exists ? file.Length : 0));
        }

        return Sort(tiles);
    }

    private static IReadOnlyList<OmsiMapTileInfo> Sort(IEnumerable<OmsiMapTileInfo> tiles)
    {
        return tiles
            .OrderBy(static tile => tile.Coordinate.Y)
            .ThenBy(static tile => tile.Coordinate.X)
            .ToArray();
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ||
               path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
