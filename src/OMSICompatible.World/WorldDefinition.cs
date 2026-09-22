namespace OMSICompatible.World;

public readonly record struct WorldTileCoordinate(int X, int Y);

public sealed record WorldTile(
    WorldTileCoordinate Coordinate,
    string SourcePath,
    long SourceBytes,
    int SourceSectionCount,
    IReadOnlyDictionary<string, int> SourceSectionCounts);

public sealed record WorldBounds(
    int MinimumX,
    int MinimumY,
    int MaximumX,
    int MaximumY)
{
    public int WidthInTiles => MaximumX - MinimumX + 1;

    public int HeightInTiles => MaximumY - MinimumY + 1;
}

public sealed record WorldDefinition(
    string Name,
    string SourceDirectory,
    IReadOnlyList<WorldTile> Tiles,
    WorldBounds? Bounds);
