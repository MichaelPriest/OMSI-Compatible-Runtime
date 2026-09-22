using OmsiCompat.Map;

namespace OMSICompatible.World;

public static class WorldLoader
{
    public static WorldDefinition Load(OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var sourceTiles = MapTileDiscovery.Discover(map);
        var tiles = new List<WorldTile>(sourceTiles.Count);

        foreach (var sourceTile in sourceTiles)
        {
            var summary = MapTileProbe.ReadSummary(sourceTile);
            tiles.Add(new WorldTile(
                new WorldTileCoordinate(sourceTile.Coordinate.X, sourceTile.Coordinate.Y),
                sourceTile.FilePath,
                sourceTile.Bytes,
                summary.SectionCount,
                summary.SectionCounts));
        }

        WorldBounds? bounds = null;
        if (tiles.Count > 0)
        {
            bounds = new WorldBounds(
                tiles.Min(static tile => tile.Coordinate.X),
                tiles.Min(static tile => tile.Coordinate.Y),
                tiles.Max(static tile => tile.Coordinate.X),
                tiles.Max(static tile => tile.Coordinate.Y));
        }

        return new WorldDefinition(
            map.FolderName,
            map.DirectoryPath,
            tiles.ToArray(),
            bounds);
    }
}
