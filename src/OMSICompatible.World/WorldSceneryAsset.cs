namespace OMSICompatible.World;

public sealed record WorldO3dMaterial(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA,
    string? TextureName);

public sealed record WorldSceneryMeshTransform(
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ);

public sealed record WorldSceneryMeshAsset(
    string DeclaredPath,
    string? ResolvedPath,
    bool Exists,
    string? ErrorCode,
    WorldSceneryMeshTransform Transform,
    float[] Positions,
    uint[] Indices,
    ushort[] TriangleMaterialIndices,
    IReadOnlyList<WorldO3dMaterial> Materials)
{
    public bool IsRenderable =>
        Exists &&
        ErrorCode is null &&
        Positions.Length >= 3 &&
        Indices.Length >= 3;
}

public sealed record WorldSceneryAsset(
    string DeclaredPath,
    string? ResolvedPath,
    bool Exists,
    bool UsesAbsoluteHeight,
    string? RenderType,
    IReadOnlyList<WorldSceneryMeshAsset> Meshes)
{
    public int RenderableMeshCount =>
        Meshes.Count(
            static mesh =>
                mesh.IsRenderable);

    public int ProtectedMeshCount =>
        Meshes.Count(
            static mesh =>
                string.Equals(
                    mesh.ErrorCode,
                    "protectedO3dUnsupported",
                    StringComparison.OrdinalIgnoreCase));

    public bool IsRenderable =>
        Exists &&
        RenderableMeshCount > 0;
}
