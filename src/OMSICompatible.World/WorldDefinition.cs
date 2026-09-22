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
    IReadOnlyList<string> ExtraValues,
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

public sealed record WorldTerrainMask(
    int LayerIndex,
    string Path);

public sealed record WorldGroundTexture(
    int LayerIndex,
    string? MainTexturePath,
    string? DetailTexturePath,
    double MainTextureRepeating,
    double DetailTextureRepeating);

public sealed record WorldTileResources(
    string? TerrainPath,
    string? LightmapPath,
    string? WaterPath,
    IReadOnlyList<string> ReadyMeshPaths,
    IReadOnlyList<string> TerrainTexturePaths,
    IReadOnlyList<WorldTerrainMask> TerrainMasks);

public sealed record WorldTerrainData(
    int CellCount,
    IReadOnlyList<float> Heights,
    float MinimumHeight,
    float MaximumHeight)
{
    public int SampleCount => CellCount + 1;
}

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
    WorldTerrainData? Terrain,
    string? TerrainErrorCode,
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
    int TotalTileCount,
    int? ActiveTileRadius,
    IReadOnlyList<WorldTile> Tiles,
    IReadOnlyList<WorldAssetReference> Assets,
    IReadOnlyList<WorldObjectPlacement> Objects,
    IReadOnlyList<WorldSplinePlacement> Splines,
    IReadOnlyDictionary<string, WorldSplineAsset> SplineAssets,
    IReadOnlyDictionary<string, WorldSceneryAsset> SceneryAssets,
    IReadOnlyList<WorldGroundTexture> GroundTextures,
    WorldDependencyReport Dependencies,
    int PlacementParseIssueCount,
    int TerrainParseIssueCount,
    WorldBounds? Bounds);
