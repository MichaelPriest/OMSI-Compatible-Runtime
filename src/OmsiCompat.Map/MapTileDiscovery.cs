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

        return tiles
            .OrderBy(static tile => tile.Coordinate.Y)
            .ThenBy(static tile => tile.Coordinate.X)
            .ToArray();
    }
}
