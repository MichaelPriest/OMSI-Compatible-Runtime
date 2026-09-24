namespace OmsiCompat.Splines;

public sealed record OmsiSplineProfilePoint(
    double X,
    double Z,
    double TextureX,
    double TextureScale);

public sealed record OmsiSplineSurface(
    int TextureIndex,
    string? TextureName,
    int AlphaMode,
    OmsiSplineProfilePoint From,
    OmsiSplineProfilePoint To);

public sealed record OmsiSplinePathDefinition(
    int Type,
    double X,
    double Z,
    double Width,
    int Direction);

public sealed record OmsiSplineDefinition(
    bool Exists,
    IReadOnlyList<string> Textures,
    IReadOnlyList<OmsiSplineSurface> Surfaces,
    IReadOnlyList<OmsiSplinePathDefinition> Paths)
{
    public static OmsiSplineDefinition Missing { get; } = new(
        false,
        Array.Empty<string>(),
        Array.Empty<OmsiSplineSurface>(),
        Array.Empty<OmsiSplinePathDefinition>());
}
