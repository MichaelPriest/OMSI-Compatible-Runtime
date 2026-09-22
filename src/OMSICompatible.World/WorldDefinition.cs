namespace OMSICompatible.World;

public readonly record struct WorldTileCoordinate(int X, int Y);

public readonly record struct WorldVector3(double X, double Y, double Z);

public enum WorldAssetKind
{
    Unknown,
    SceneryObject,
    Spline,
    Mesh,
    Texture,
    Sound
}

public sealed record WorldAssetReference(
    WorldAssetKind Kind,
    string SourcePath,
    string SourceSection,
    int SourceLineNumber);

public sealed record WorldObjectPlacement(
    WorldTileCoordinate Tile,
    long Id,
    string AssetPath,
    WorldVector3 Position,
    double HeadingDegrees,
    double PitchDegrees,
    double BankDegrees,
    int SourceLineNumber);

public sealed record WorldSplinePlacement(
    WorldTileCoordinate Tile,
    long Id,
    long PreviousId,
    long NextId,
    string AssetPath,
    WorldVector3 Position,
    double HeadingDegrees,
    double LengthMeters,
    double RadiusMeters,
    double GradientStartPercent,
    double GradientEndPercent,
    bool UsesHeightProfile,
    int SourceLineNumber);

public sealed record WorldTileResources(
    string? TerrainPath,
    string? LightmapPath,
    string? WaterPath,
    IReadOnlyList<string> ReadyMeshPaths,
    IReadOnlyList<string> TerrainTexturePaths);

public sealed record WorldTile(
    WorldTileCoordinate Coordinate,
    string SourcePath,
    long SourceBytes,
    int SourceSectionCount,
    IReadOnlyDictionary<string, int> SourceSectionCounts,
    IReadOnlyList<WorldAssetReference> AssetReferences,
    IReadOnlyList<WorldObjectPlacement> Objects,
    IReadOnlyList<WorldSplinePlacement> Splines,
    WorldTileResources Resources,
    int PlacementParseIssueCount);

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
    IReadOnlyList<WorldAssetReference> Assets,
    IReadOnlyList<WorldObjectPlacement> Objects,
    IReadOnlyList<WorldSplinePlacement> Splines,
    WorldDependencyReport Dependencies,
    int PlacementParseIssueCount,
    WorldBounds? Bounds);
