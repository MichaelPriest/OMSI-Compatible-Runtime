namespace OMSICompatible.World;

public sealed record WorldSplineProfilePoint(
    double X,
    double Z,
    double TextureX,
    double TextureScale);

public sealed record WorldSplineSurface(
    int TextureIndex,
    string? TextureName,
    int AlphaMode,
    WorldSplineProfilePoint From,
    WorldSplineProfilePoint To);

public sealed record WorldSplineAsset(
    string DeclaredPath,
    string? ResolvedPath,
    bool Exists,
    IReadOnlyList<WorldSplineSurface> Surfaces)
{
    public bool IsRenderable => Exists && Surfaces.Count > 0;
}
