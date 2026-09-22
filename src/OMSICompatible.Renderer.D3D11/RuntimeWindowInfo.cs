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

public sealed record RuntimeWindowInfo(
    string WorldName,
    int TileCount,
    int ObjectCount,
    int SplineCount,
    string ContentRoot,
    IReadOnlyList<RuntimeTileInfo> Tiles,
    IReadOnlyList<RuntimeSplineInfo> Splines);
