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

public sealed record RuntimeWindowInfo(
    string WorldName,
    int TileCount,
    int ObjectCount,
    int SplineCount,
    string ContentRoot,
    IReadOnlyList<RuntimeTileInfo> Tiles);
