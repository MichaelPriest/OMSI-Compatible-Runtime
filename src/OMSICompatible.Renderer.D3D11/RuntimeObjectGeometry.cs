using System.Globalization;
using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeObjectBatch(
    uint StartVertex,
    uint VertexCount,
    string? TexturePath,
    bool AlphaCutout);

internal sealed record RuntimeObjectGeometry(
    RuntimeObjectVertex[] Vertices,
    IReadOnlyList<RuntimeObjectBatch> Batches,
    int RenderedObjectCount,
    int RenderedMeshCount,
    int RenderedTreeCount,
    int TexturedBatchCount,
    int ProtectedMeshCount,
    int MissingMeshCount,
    bool HitVertexBudget)
{
    public static RuntimeObjectGeometry Empty { get; } =
        new(
            [],
            Array.Empty<RuntimeObjectBatch>(),
            0,
            0,
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

    private readonly record struct BatchKey(
        string? TexturePath,
        bool AlphaCutout);

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

        var batches =
            new Dictionary<
                BatchKey,
                List<RuntimeObjectVertex>>();

        var batchOrder =
            new List<BatchKey>();

        var renderedObjects = 0;
        var renderedMeshes = 0;
        var renderedTrees = 0;

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

        var totalVertices = 0;
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

                if (totalVertices +
                    mesh.Indices.Length >
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

                var appended =
                    AppendMesh(
                        mesh,
                        worldTransform,
                        batches,
                        batchOrder,
                        ref totalVertices);

                if (appended)
                {
                    renderedMeshes++;
                    objectContributed = true;
                }
            }

            if (hitBudget)
            {
                break;
            }

            if (asset.Tree is not null)
            {
                if (totalVertices + 12 >
                    MaximumVertices)
                {
                    hitBudget = true;
                }
                else if (AppendTree(
                    instance,
                    asset.Tree,
                    worldX,
                    worldZ,
                    (float)instance.Y +
                    terrainOffset +
                    renderLift,
                    batches,
                    batchOrder,
                    ref totalVertices))
                {
                    renderedTrees++;
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

        var vertices =
            new List<RuntimeObjectVertex>(
                totalVertices);

        var runtimeBatches =
            new List<RuntimeObjectBatch>(
                batchOrder.Count);

        foreach (var key in batchOrder)
        {
            var batchVertices =
                batches[key];

            if (batchVertices.Count == 0)
            {
                continue;
            }

            var start =
                (uint)vertices.Count;

            vertices.AddRange(
                batchVertices);

            runtimeBatches.Add(
                new RuntimeObjectBatch(
                    start,
                    (uint)batchVertices.Count,
                    key.TexturePath,
                    key.AlphaCutout));
        }

        return new RuntimeObjectGeometry(
            vertices.ToArray(),
            runtimeBatches.ToArray(),
            renderedObjects,
            renderedMeshes,
            renderedTrees,
            runtimeBatches.Count(
                static batch =>
                    !string.IsNullOrWhiteSpace(
                        batch.TexturePath)),
            protectedMeshes,
            missingMeshes,
            hitBudget);
    }

    private static bool AppendMesh(
        RuntimeObjectMeshInfo mesh,
        Matrix4x4 worldTransform,
        IDictionary<BatchKey, List<RuntimeObjectVertex>> batches,
        ICollection<BatchKey> batchOrder,
        ref int totalVertices)
    {
        var vertexCount =
            mesh.Positions.Length / 3;

        var triangleCount =
            mesh.Indices.Length / 3;

        var contributed = false;

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

            var material =
                ResolveMaterial(
                    mesh,
                    triangle);

            var color =
                MaterialColor(material);

            var key =
                new BatchKey(
                    material?.TexturePath,
                    false);

            var output =
                GetBatch(
                    key,
                    batches,
                    batchOrder);

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

            totalVertices += 3;
            contributed = true;
        }

        return contributed;
    }

    private static RuntimeO3dMaterialInfo? ResolveMaterial(
        RuntimeObjectMeshInfo mesh,
        int triangle)
    {
        if (triangle < 0 ||
            triangle >=
            mesh.TriangleMaterialIndices.Length)
        {
            return null;
        }

        var materialIndex =
            mesh.TriangleMaterialIndices[
                triangle];

        return materialIndex <
               mesh.Materials.Count
            ? mesh.Materials[
                materialIndex]
            : null;
    }

    private static Color4 MaterialColor(
        RuntimeO3dMaterialInfo? material)
    {
        if (material is null)
        {
            return new Color4(
                0.62f,
                0.68f,
                0.72f,
                1.0f);
        }

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
            Math.Clamp(
                material.DiffuseA,
                0.0f,
                1.0f));
    }

    private static void AddVertex(
        RuntimeObjectMeshInfo mesh,
        int vertexIndex,
        Matrix4x4 worldTransform,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var positionOffset =
            vertexIndex * 3;

        var source =
            new Vector3(
                mesh.Positions[positionOffset],
                mesh.Positions[positionOffset + 1],
                mesh.Positions[positionOffset + 2]);

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

        var uv =
            Vector2.Zero;

        var uvOffset =
            vertexIndex * 2;

        if (uvOffset >= 0 &&
            uvOffset + 1 <
            mesh.Uvs.Length)
        {
            uv =
                new Vector2(
                    mesh.Uvs[uvOffset],
                    mesh.Uvs[uvOffset + 1]);
        }

        output.Add(
            new RuntimeObjectVertex(
                world,
                color,
                uv));
    }

    private static bool AppendTree(
        RuntimeObjectInfo instance,
        RuntimeTreeInfo tree,
        double worldX,
        double worldZ,
        float baseY,
        IDictionary<BatchKey, List<RuntimeObjectVertex>> batches,
        ICollection<BatchKey> batchOrder,
        ref int totalVertices)
    {
        var height =
            ResolveTreePlacementValue(
                instance.ExtraValues,
                2,
                tree.MinimumHeight,
                tree.MaximumHeight);

        var aspect =
            ResolveTreePlacementValue(
                instance.ExtraValues,
                3,
                tree.MinimumAspect,
                tree.MaximumAspect);

        if (!double.IsFinite(height) ||
            !double.IsFinite(aspect) ||
            height <= 0 ||
            aspect <= 0)
        {
            return false;
        }

        var basePosition =
            new Vector3(
                (float)worldX,
                baseY,
                (float)worldZ);

        var heightVector =
            Vector3.UnitY *
            (float)height;

        var halfWidth =
            (float)(
                height *
                aspect *
                0.5);

        var rotation =
            Matrix4x4.CreateRotationY(
                DegreesToRadians(
                    instance.HeadingDegrees));

        var right =
            Vector3.TransformNormal(
                Vector3.UnitX,
                rotation);

        var forward =
            Vector3.TransformNormal(
                Vector3.UnitZ,
                rotation);

        var hasTexture =
            !string.IsNullOrWhiteSpace(
                tree.TexturePath);

        var key =
            new BatchKey(
                tree.TexturePath,
                hasTexture);

        var output =
            GetBatch(
                key,
                batches,
                batchOrder);

        var color =
            hasTexture
                ? new Color4(
                    1,
                    1,
                    1,
                    1)
                : new Color4(
                    0.18f,
                    0.48f,
                    0.20f,
                    1.0f);

        AppendTreeQuad(
            basePosition,
            heightVector,
            right * halfWidth,
            color,
            output);

        AppendTreeQuad(
            basePosition,
            heightVector,
            forward * halfWidth,
            color,
            output);

        totalVertices += 12;
        return true;
    }

    private static void AppendTreeQuad(
        Vector3 basePosition,
        Vector3 heightVector,
        Vector3 halfWidthVector,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var bottomLeft =
            basePosition -
            halfWidthVector;

        var bottomRight =
            basePosition +
            halfWidthVector;

        var topLeft =
            bottomLeft +
            heightVector;

        var topRight =
            bottomRight +
            heightVector;

        output.Add(
            new RuntimeObjectVertex(
                bottomLeft,
                color,
                new Vector2(0, 1)));
        output.Add(
            new RuntimeObjectVertex(
                topLeft,
                color,
                new Vector2(0, 0)));
        output.Add(
            new RuntimeObjectVertex(
                topRight,
                color,
                new Vector2(1, 0)));

        output.Add(
            new RuntimeObjectVertex(
                bottomLeft,
                color,
                new Vector2(0, 1)));
        output.Add(
            new RuntimeObjectVertex(
                topRight,
                color,
                new Vector2(1, 0)));
        output.Add(
            new RuntimeObjectVertex(
                bottomRight,
                color,
                new Vector2(1, 1)));
    }

    private static List<RuntimeObjectVertex> GetBatch(
        BatchKey key,
        IDictionary<BatchKey, List<RuntimeObjectVertex>> batches,
        ICollection<BatchKey> batchOrder)
    {
        if (batches.TryGetValue(
                key,
                out var existing))
        {
            return existing;
        }

        var created =
            new List<RuntimeObjectVertex>();

        batches[key] = created;
        batchOrder.Add(key);

        return created;
    }

    private static double ResolveTreePlacementValue(
        IReadOnlyList<string> extraValues,
        int index,
        double minimum,
        double maximum)
    {
        if (index >= 0 &&
            index < extraValues.Count &&
            double.TryParse(
                extraValues[index],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            double.IsFinite(parsed) &&
            parsed > 0)
        {
            return Math.Clamp(
                parsed,
                minimum,
                maximum);
        }

        return minimum +
               (maximum - minimum) *
               0.5;
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

internal readonly struct RuntimeObjectVertex
{
    public const uint SizeInBytes = 36;

    public RuntimeObjectVertex(
        Vector3 position,
        Color4 color,
        Vector2 uv)
    {
        Position = position;
        Color = color;
        Uv = uv;
    }

    public readonly Vector3 Position;
    public readonly Color4 Color;
    public readonly Vector2 Uv;
}
