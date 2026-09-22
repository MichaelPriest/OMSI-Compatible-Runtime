namespace OMSICompatible.Renderer.D3D11;

public sealed record RuntimeTerrainInfo(
    int CellCount,
    IReadOnlyList<float> Heights,
    float MinimumHeight,
    float MaximumHeight);

public sealed record RuntimeTileInfo(
    int X,
    int Y,
    int ObjectCount,
    int SplineCount,
    RuntimeTerrainInfo? Terrain);

public sealed record RuntimeSplineProfilePointInfo(
    double X,
    double Z);

public sealed record RuntimeSplineSurfaceInfo(
    RuntimeSplineProfilePointInfo From,
    RuntimeSplineProfilePointInfo To);

public sealed record RuntimeSplineInfo(
    int TileX,
    int TileY,
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double LengthMeters,
    double RadiusMeters,
    double GradientStartPercent,
    double GradientEndPercent,
    IReadOnlyList<RuntimeSplineSurfaceInfo> Surfaces);

public sealed record RuntimeObjectInfo(
    int TileX,
    int TileY,
    string AssetPath,
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double PitchDegrees,
    double BankDegrees);

public sealed record RuntimeObjectMeshTransformInfo(
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ);

public sealed record RuntimeO3dMaterialInfo(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA);

public sealed record RuntimeObjectMeshInfo(
    string DeclaredPath,
    string? ResolvedPath,
    string? ErrorCode,
    RuntimeObjectMeshTransformInfo Transform,
    float[] Positions,
    uint[] Indices,
    ushort[] TriangleMaterialIndices,
    IReadOnlyList<RuntimeO3dMaterialInfo> Materials);

public sealed record RuntimeSceneryAssetInfo(
    bool UsesAbsoluteHeight,
    string? RenderType,
    IReadOnlyList<RuntimeObjectMeshInfo> Meshes);

public sealed record RuntimeWindowInfo(
    string WorldName,
    int TileCount,
    int ObjectCount,
    int SplineCount,
    string ContentRoot,
    IReadOnlyList<RuntimeTileInfo> Tiles,
    IReadOnlyList<RuntimeSplineInfo> Splines,
    IReadOnlyList<RuntimeObjectInfo> Objects,
    IReadOnlyDictionary<string, RuntimeSceneryAssetInfo> SceneryAssets);
