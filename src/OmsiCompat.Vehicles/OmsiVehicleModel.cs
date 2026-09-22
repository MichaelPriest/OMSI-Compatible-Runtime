namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleMaterialOverride(
    string TextureName,
    int MaterialIndex,
    int? AlphaMode,
    string? TransMapSource);

public sealed record OmsiVehicleMeshReference(
    int Ordinal,
    string DeclaredPath,
    IReadOnlyList<OmsiVehicleMaterialOverride> MaterialOverrides);

public sealed record OmsiVehicleModel(
    string SourcePath,
    IReadOnlyList<OmsiVehicleMeshReference> Meshes);
