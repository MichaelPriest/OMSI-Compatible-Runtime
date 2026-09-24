namespace OMSICompatible.World;

public sealed record WorldSplineProfilePoint(
    double X,
    double Z,
    double TextureX,
    double TextureScale);

public sealed record WorldSplineSurface(
    int TextureIndex,
    string? TextureName,
    string? TexturePath,
    int AlphaMode,
    WorldSplineProfilePoint From,
    WorldSplineProfilePoint To);

public sealed record WorldSplinePath(
    int Type,
    double X,
    double Z,
    double Width,
    int Direction);

public sealed record WorldSplineAsset(
    string DeclaredPath,
    string? ResolvedPath,
    bool Exists,
    IReadOnlyList<WorldSplineSurface> Surfaces,
    IReadOnlyList<WorldSplinePath> Paths)
{
    public bool IsRenderable => Exists && Surfaces.Count > 0;
}
