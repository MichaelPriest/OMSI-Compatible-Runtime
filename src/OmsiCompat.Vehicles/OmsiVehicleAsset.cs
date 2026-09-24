using System.Numerics;

namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleMaterialChangeItem(
    int ItemIndex,
    int? AlphaMode,
    string? TransMapTexturePath,
    bool HasTransMapDirective,
    bool NoZWrite,
    bool NoZCheck,
    string? AlphaScaleVariable,
    string? LightMapTexturePath,
    string? LightMapVariable,
    string? MaterialChangeTexturePath,
    OmsiVehicleMaterialColor? AllColor,
    string? EnvMapTexturePath,
    double EnvMapStrength,
    string? EnvMapMaskTexturePath,
    string? BumpMapTexturePath,
    double BumpMapStrength,
    IReadOnlyList<OmsiVehicleFreeTexture> FreeTextures,
    int? TextTextureIndex);

public sealed record OmsiVehicleMaterialChangeSet(
    string VariableName,
    int GroupIndex,
    IReadOnlyList<OmsiVehicleMaterialChangeItem> Items);

public sealed record OmsiVehicleMaterial(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA,
    string? TexturePath,
    int AlphaMode,
    string? TransMapTexturePath,
    bool NoZWrite,
    bool NoZCheck,
    string? AlphaScaleVariable,
    string? LightMapTexturePath,
    string? LightMapVariable,
    string? MaterialChangeTexturePath,
    string? MaterialChangeVariable,
    OmsiVehicleMaterialColor? BaseAllColor,
    OmsiVehicleMaterialColor? MaterialChangeAllColor,
    string? EnvMapTexturePath,
    double EnvMapStrength,
    string? EnvMapMaskTexturePath,
    string? BumpMapTexturePath,
    double BumpMapStrength,
    IReadOnlyList<OmsiVehicleFreeTexture> FreeTextures,
    int? TextTextureIndex,
    IReadOnlyList<OmsiVehicleMaterialChangeSet>? MaterialChangeSets = null,
    bool HasTransMapDirective = false);

public sealed record OmsiVehicleMeshAsset(
    string DeclaredPath,
    string? ResolvedPath,
    string? ErrorCode,
    OmsiVehicleMeshTransform Transform,
    int ViewpointFlag,
    double? LodThreshold,
    IReadOnlyList<OmsiVehicleVisibilityCondition> VisibilityConditions,
    IReadOnlyList<OmsiVehicleAnimation> Animations,
    Matrix4x4 SourceTransform,
    float[] Positions,
    float[] Normals,
    float[] Uvs,
    uint[] Indices,
    ushort[] TriangleMaterialIndices,
    IReadOnlyList<OmsiVehicleMaterial> Materials,
    IReadOnlyList<OmsiVehicleLightEffect>? LightEffects = null,
    string? MeshIdentifier = null,
    string? AnimationParent = null)
{
    public bool IsRenderable =>
        ErrorCode is null &&
        Positions.Length >= 3 &&
        Indices.Length >= 3;
}

public sealed record OmsiVehicleAsset(
    OmsiBusInfo Bus,
    IReadOnlyList<OmsiVehicleMeshAsset> Meshes,
    OmsiDriverPosition? DriverPosition,
    IReadOnlyList<OmsiVehicleTextTexture> TextTextures)
{
    public int RenderableMeshCount =>
        Meshes.Count(static mesh => mesh.IsRenderable);

    public int ProtectedMeshCount =>
        Meshes.Count(static mesh =>
            string.Equals(
                mesh.ErrorCode,
                "encryptedO3dUnsupported",
                StringComparison.OrdinalIgnoreCase));

    public int FailedMeshCount =>
        Meshes.Count(static mesh =>
            !mesh.IsRenderable &&
            !string.Equals(
                mesh.ErrorCode,
                "encryptedO3dUnsupported",
                StringComparison.OrdinalIgnoreCase));
}
