namespace OmsiCompat.Scenery;

public sealed record OmsiSceneryMeshTransform(
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ)
{
    public static OmsiSceneryMeshTransform Identity { get; } =
        new(
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            1,
            1);
}

public sealed record OmsiSceneryMeshReference(
    string Path,
    double? LodThreshold,
    OmsiSceneryMeshTransform Transform);

public sealed record OmsiSceneryTreeDefinition(
    string TextureName,
    double MinimumHeight,
    double MaximumHeight,
    double MinimumAspect,
    double MaximumAspect);

public sealed record OmsiSceneryMaterialOverride(
    int MeshOrdinal,
    string TextureName,
    int MaterialIndex,
    int? AlphaMode,
    string? TransMapSource,
    bool NoZWrite,
    bool NoZCheck);

public sealed record OmsiSceneryDefinition(
    bool Exists,
    bool UsesAbsoluteHeight,
    string? RenderType,
    IReadOnlyList<OmsiSceneryMeshReference> Meshes,
    IReadOnlyList<OmsiSceneryMaterialOverride> MaterialOverrides,
    OmsiSceneryTreeDefinition? Tree)
{
    public static OmsiSceneryDefinition Missing { get; } =
        new(
            false,
            false,
            null,
            Array.Empty<OmsiSceneryMeshReference>(),
            Array.Empty<OmsiSceneryMaterialOverride>(),
            null);
}
