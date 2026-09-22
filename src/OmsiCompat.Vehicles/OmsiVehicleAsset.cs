namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleMaterial(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA,
    string? TexturePath,
    int AlphaMode,
    string? TransMapTexturePath,
    bool NoZWrite,
    bool NoZCheck);

public sealed record OmsiVehicleMeshAsset(
    string DeclaredPath,
    string? ResolvedPath,
    string? ErrorCode,
    OmsiVehicleMeshTransform Transform,
    int ViewpointFlag,
    double? LodThreshold,
    float[] Positions,
    float[] Uvs,
    uint[] Indices,
    ushort[] TriangleMaterialIndices,
    IReadOnlyList<OmsiVehicleMaterial> Materials)
{
    public bool IsRenderable =>
        ErrorCode is null &&
        Positions.Length >= 3 &&
        Indices.Length >= 3;
}

public sealed record OmsiVehicleAsset(
    OmsiBusInfo Bus,
    IReadOnlyList<OmsiVehicleMeshAsset> Meshes,
    OmsiDriverPosition? DriverPosition)
{
    public int RenderableMeshCount =>
        Meshes.Count(static mesh => mesh.IsRenderable);

    public int ProtectedMeshCount =>
        Meshes.Count(static mesh =>
            string.Equals(
                mesh.ErrorCode,
                "protectedO3dUnsupported",
                StringComparison.OrdinalIgnoreCase));

    public int FailedMeshCount =>
        Meshes.Count(static mesh =>
            !mesh.IsRenderable &&
            !string.Equals(
                mesh.ErrorCode,
                "protectedO3dUnsupported",
                StringComparison.OrdinalIgnoreCase));
}
