namespace OMSICompatible.Renderer.D3D11;

public sealed record RuntimeWindowInfo(
    string WorldName,
    int TileCount,
    int ObjectCount,
    int SplineCount,
    string ContentRoot);
