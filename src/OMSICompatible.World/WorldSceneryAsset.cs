namespace OMSICompatible.World;

public sealed record WorldO3dMaterial(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA,
    string? TextureName,
    string? TexturePath,
    int AlphaMode,
    string? TransMapTexturePath,
    bool NoZWrite,
    bool NoZCheck);

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
    float[] Normals,
    float[] Uvs,
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

public sealed record WorldSceneryTreeDefinition(
    string TextureName,
    string? TexturePath,
    double MinimumHeight,
    double MaximumHeight,
    double MinimumAspect,
    double MaximumAspect);

public sealed record WorldSceneryAsset(
    string DeclaredPath,
    string? ResolvedPath,
    bool Exists,
    bool UsesAbsoluteHeight,
    bool OnlyEditor,
    string? RenderType,
    IReadOnlyList<WorldSceneryMeshAsset> Meshes,
    WorldSceneryTreeDefinition? Tree)
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
                    "encryptedO3dUnsupported",
                    StringComparison.OrdinalIgnoreCase));

    public bool IsRenderable =>
        Exists &&
        ((!OnlyEditor &&
          RenderableMeshCount > 0) ||
         Tree is not null);
}
