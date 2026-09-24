using System.Globalization;

namespace OmsiCompat.Map;

public sealed record OmsiMapEntryPoint(
    string Name,
    OmsiTileCoordinate Tile,
    long ObjectId,
    double WorldX,
    double WorldY,
    double WorldZ,
    double HeadingDegrees);

public sealed record OmsiMapEntryPointGroup(
    string Name,
    IReadOnlyList<OmsiMapEntryPoint> Alternatives)
{
    public override string ToString() => Name;
}

public static class MapEntryPointDiscovery
{
    private const double TileSizeMeters = 300.0;

    public static IReadOnlyList<OmsiMapEntryPointGroup> Discover(
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var points = new List<OmsiMapEntryPoint>();
        var unnamed = 0;

        foreach (var tile in MapTileDiscovery.Discover(map))
        {
            OmsiTilePlacements placements;

            try
            {
                placements = MapTilePlacementParser.Parse(tile);
            }
            catch
            {
                continue;
            }

            foreach (var placement in placements.Objects)
            {
                if (!IsBusEntryPoint(placement.AssetPath))
                {
                    continue;
                }

                var name = ExtractLabel(placement.ExtraValues);

                if (string.IsNullOrWhiteSpace(name))
                {
                    unnamed++;
                    name = $"Entrypoint {unnamed}";
                }

                points.Add(new OmsiMapEntryPoint(
                    name,
                    tile.Coordinate,
                    placement.Id,
                    tile.Coordinate.X * TileSizeMeters + placement.Position.X,
                    placement.Position.Z,
                    tile.Coordinate.Y * TileSizeMeters + placement.Position.Y,
                    placement.HeadingDegrees));
            }
        }

        return points
            .GroupBy(static point => point.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                new OmsiMapEntryPointGroup(
                    group.Key,
                    group
                        .OrderBy(static point => point.Tile.Y)
                        .ThenBy(static point => point.Tile.X)
                        .ThenBy(static point => point.ObjectId)
                        .ToArray()))
            .OrderBy(static group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool IsBusEntryPoint(string assetPath)
    {
        var fileName = Path.GetFileName(assetPath);

        return fileName.Equals(
                   "entrypoint_bus.sco",
                   StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(
                   "entrypoint_bus ",
                   StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(
                   "entrypoint_bus(",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractLabel(
        IReadOnlyList<string> extraValues)
    {
        for (var index = extraValues.Count - 1;
             index >= 0;
             index--)
        {
            var value = extraValues[index]
                .Trim()
                .Trim('"');

            if (value.Length == 0 ||
                value.Length > 160)
            {
                continue;
            }

            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                continue;
            }

            return value;
        }

        return null;
    }
}
