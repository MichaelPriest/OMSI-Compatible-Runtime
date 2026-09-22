namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleMaterialOverride(
    string TextureName,
    int MaterialIndex,
    int? AlphaMode,
    string? TransMapSource,
    bool HasTransMapDirective,
    bool NoZWrite,
    bool NoZCheck);

public sealed record OmsiVehicleMeshTransform(
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
    public static OmsiVehicleMeshTransform Identity { get; } =
        new(
            0, 0, 0,
            0, 0, 0,
            1, 1, 1);
}

public sealed record OmsiVehicleMeshReference(
    int Ordinal,
    string DeclaredPath,
    OmsiVehicleMeshTransform Transform,
    int ViewpointFlag,
    double? LodThreshold,
    IReadOnlyList<OmsiVehicleMaterialOverride> MaterialOverrides);

public sealed record OmsiVehicleModel(
    string SourcePath,
    IReadOnlyList<OmsiVehicleMeshReference> Meshes);
