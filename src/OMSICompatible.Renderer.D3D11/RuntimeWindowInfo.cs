namespace OMSICompatible.Renderer.D3D11;

public sealed record RuntimeTileInfo(
    int X,
    int Y,
    int ObjectCount,
    int SplineCount);

public sealed record RuntimeWindowInfo(
    string WorldName,
    int TileCount,
    int ObjectCount,
    int SplineCount,
    string ContentRoot,
    IReadOnlyList<RuntimeTileInfo> Tiles);
