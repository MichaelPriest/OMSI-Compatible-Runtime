namespace OmsiCompat.Models;

public sealed record OmsiO3dGeometry(
    bool IsLoaded,
    string? ErrorCode,
    float[] Positions,
    float[] Normals,
    float[] Uvs,
    uint[] Indices,
    ushort[] TriangleMaterialIndices,
    IReadOnlyList<OmsiO3dMaterial> Materials)
{
    public static OmsiO3dGeometry Error(string errorCode) =>
        new(
            false,
            errorCode,
            Array.Empty<float>(),
            Array.Empty<float>(),
            Array.Empty<float>(),
            Array.Empty<uint>(),
            Array.Empty<ushort>(),
            Array.Empty<OmsiO3dMaterial>());
}
