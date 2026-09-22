using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeObjectGeometry(
    RuntimeTerrainVertex[] Vertices,
    int RenderedObjectCount,
    int RenderedMeshCount,
    int ProtectedMeshCount,
    int MissingMeshCount,
    bool HitVertexBudget)
{
    public static RuntimeObjectGeometry Empty { get; } =
        new(
            [],
            0,
            0,
            0,
            0,
            false);
}

internal static class RuntimeObjectGeometryBuilder
{
    private const int MaximumVertices = 4_000_000;
    private const double TileSizeMeters = 300.0;

    private static readonly Matrix4x4 SourceToRendererBasis =
        new(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);

    public static RuntimeObjectGeometry Build(
        IReadOnlyList<RuntimeTileInfo> tiles,
        IReadOnlyList<RuntimeObjectInfo> objects,
        IReadOnlyDictionary<string, RuntimeSceneryAssetInfo> assets)
    {
        if (objects.Count == 0 ||
            assets.Count == 0)
        {
            return RuntimeObjectGeometry.Empty;
        }

        var terrain =
            new RuntimeTerrainSampler(tiles);

        var vertices =
            new List<RuntimeTerrainVertex>(
                Math.Min(
                    256_000,
                    MaximumVertices));

        var renderedObjects = 0;
        var renderedMeshes = 0;
        var protectedMeshes =
            assets.Values.Sum(
                static asset =>
                    asset.Meshes.Count(
                        static mesh =>
                            string.Equals(
                                mesh.ErrorCode,
                                "protectedO3dUnsupported",
                                StringComparison.OrdinalIgnoreCase)));
        var missingMeshes =
            assets.Values.Sum(
                static asset =>
                    asset.Meshes.Count(
                        static mesh =>
                            !string.IsNullOrWhiteSpace(
                                mesh.ErrorCode) &&
                            !string.Equals(
                                mesh.ErrorCode,
                                "protectedO3dUnsupported",
                                StringComparison.OrdinalIgnoreCase)));

        var hitBudget = false;

        foreach (var instance in objects)
        {
            if (!assets.TryGetValue(
                    instance.AssetPath,
                    out var asset))
            {
                continue;
            }

            var worldX =
                instance.TileX * TileSizeMeters +
                instance.X;

            var worldZ =
                instance.TileY * TileSizeMeters +
                instance.Z;

            var terrainOffset = 0.0f;

            if (!asset.UsesAbsoluteHeight &&
                terrain.TrySample(
                    worldX,
                    worldZ,
                    out var groundHeight))
            {
                terrainOffset =
                    groundHeight;
            }

            var renderLift =
                string.Equals(
                    asset.RenderType,
                    "on_surface",
                    StringComparison.OrdinalIgnoreCase)
                    ? 0.015f
                    : 0.0f;

            var objectTransform =
                Matrix4x4.CreateFromYawPitchRoll(
                    DegreesToRadians(
                        instance.HeadingDegrees),
                    DegreesToRadians(
                        instance.PitchDegrees),
                    DegreesToRadians(
                        instance.BankDegrees)) *
                Matrix4x4.CreateTranslation(
                    (float)worldX,
                    (float)instance.Y +
                    terrainOffset +
                    renderLift,
                    (float)worldZ);

            var objectContributed = false;

            foreach (var mesh in asset.Meshes)
            {
                if (!string.IsNullOrWhiteSpace(
                        mesh.ErrorCode) ||
                    mesh.Positions.Length < 3 ||
                    mesh.Indices.Length < 3)
                {
                    continue;
                }

                var needed =
                    mesh.Indices.Length;

                if (vertices.Count + needed >
                    MaximumVertices)
                {
                    hitBudget = true;
                    break;
                }

                var localTransform =
                    CreateMeshTransform(
                        mesh.Transform);

                var worldTransform =
                    localTransform *
                    objectTransform;

                if (AppendMesh(
                        mesh,
                        worldTransform,
                        vertices))
                {
                    renderedMeshes++;
                    objectContributed = true;
                }
            }

            if (objectContributed)
            {
                renderedObjects++;
            }

            if (hitBudget)
            {
                break;
            }
        }

        return new RuntimeObjectGeometry(
            vertices.ToArray(),
            renderedObjects,
            renderedMeshes,
            protectedMeshes,
            missingMeshes,
            hitBudget);
    }

    private static bool AppendMesh(
        RuntimeObjectMeshInfo mesh,
        Matrix4x4 worldTransform,
        ICollection<RuntimeTerrainVertex> output)
    {
        var vertexCount =
            mesh.Positions.Length / 3;

        var triangleCount =
            mesh.Indices.Length / 3;

        var before =
            output.Count;

        for (var triangle = 0;
             triangle < triangleCount;
             triangle++)
        {
            var baseIndex =
                triangle * 3;

            var index0 =
                checked(
                    (int)mesh.Indices[
                        baseIndex]);

            // Swapping OMSI model Y/Z changes handedness.
            var index1 =
                checked(
                    (int)mesh.Indices[
                        baseIndex + 2]);

            var index2 =
                checked(
                    (int)mesh.Indices[
                        baseIndex + 1]);

            if (index0 < 0 ||
                index0 >= vertexCount ||
                index1 < 0 ||
                index1 >= vertexCount ||
                index2 < 0 ||
                index2 >= vertexCount)
            {
                continue;
            }

            var color =
                TriangleColor(
                    mesh,
                    triangle);

            AddVertex(
                mesh,
                index0,
                worldTransform,
                color,
                output);

            AddVertex(
                mesh,
                index1,
                worldTransform,
                color,
                output);

            AddVertex(
                mesh,
                index2,
                worldTransform,
                color,
                output);
        }

        return output.Count > before;
    }

    private static void AddVertex(
        RuntimeObjectMeshInfo mesh,
        int vertexIndex,
        Matrix4x4 worldTransform,
        Color4 color,
        ICollection<RuntimeTerrainVertex> output)
    {
        var offset =
            vertexIndex * 3;

        var source =
            new Vector3(
                mesh.Positions[offset],
                mesh.Positions[offset + 1],
                mesh.Positions[offset + 2]);

        var rendererLocal =
            new Vector3(
                source.X,
                source.Z,
                source.Y);

        var world =
            Vector3.Transform(
                rendererLocal,
                worldTransform);

        if (!float.IsFinite(world.X) ||
            !float.IsFinite(world.Y) ||
            !float.IsFinite(world.Z))
        {
            return;
        }

        output.Add(
            new RuntimeTerrainVertex(
                world,
                color));
    }

    private static Color4 TriangleColor(
        RuntimeObjectMeshInfo mesh,
        int triangle)
    {
        if (triangle >= 0 &&
            triangle <
            mesh.TriangleMaterialIndices.Length)
        {
            var materialIndex =
                mesh.TriangleMaterialIndices[
                    triangle];

            if (materialIndex <
                mesh.Materials.Count)
            {
                var material =
                    mesh.Materials[
                        materialIndex];

                return new Color4(
                    Math.Clamp(
                        material.DiffuseR,
                        0.04f,
                        1.0f),
                    Math.Clamp(
                        material.DiffuseG,
                        0.04f,
                        1.0f),
                    Math.Clamp(
                        material.DiffuseB,
                        0.04f,
                        1.0f),
                    1.0f);
            }
        }

        return new Color4(
            0.62f,
            0.68f,
            0.72f,
            1.0f);
    }

    private static Matrix4x4 CreateMeshTransform(
        RuntimeObjectMeshTransformInfo transform)
    {
        var sourceTransform =
            Matrix4x4.CreateScale(
                (float)transform.ScaleX,
                (float)transform.ScaleY,
                (float)transform.ScaleZ) *
            Matrix4x4.CreateFromYawPitchRoll(
                DegreesToRadians(
                    transform.RotationY),
                DegreesToRadians(
                    transform.RotationX),
                DegreesToRadians(
                    transform.RotationZ)) *
            Matrix4x4.CreateTranslation(
                (float)transform.PositionX,
                (float)transform.PositionY,
                (float)transform.PositionZ);

        return
            SourceToRendererBasis *
            sourceTransform *
            SourceToRendererBasis;
    }

    private static float DegreesToRadians(
        double value) =>
        (float)(
            value *
            Math.PI /
            180.0);
}
