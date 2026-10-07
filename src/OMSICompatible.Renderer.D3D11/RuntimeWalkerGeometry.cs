using System.Diagnostics;
using System.Numerics;
using OmsiCompat.Models;
using OmsiCompat.Vehicles;
using OMSICompatible.Renderer.Common;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal static class RuntimeWalkerGeometry
{
    private const string HumanAssetPrefix =
        "__runtime_human__:";

    private static readonly object HumanAssetCacheGate =
        new();

    private static readonly Dictionary<string, RuntimeHumanAsset?>
        HumanAssetCache =
            new(
                StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, RuntimeHumanAnimationState>
        HumanAnimationStates =
            new(
                StringComparer.OrdinalIgnoreCase);

    private sealed record RuntimeHumanMeshSkin(
        byte[] Slots,
        float[] Weights);

    private sealed record RuntimeHumanAsset(
        string SourcePath,
        OmsiHumanDefinition Definition,
        OmsiHumanRig? Rig,
        RuntimeSceneryAssetInfo Scenery,
        IReadOnlyList<RuntimeHumanMeshSkin?> Skins);

    private sealed class RuntimeHumanAnimationState(
        OmsiHumanAnimator animator)
    {
        public OmsiHumanAnimator Animator { get; } =
            animator;

        public long LastTick { get; set; } =
            Stopwatch.GetTimestamp();

        public long LastSeen { get; set; } =
            Stopwatch.GetTimestamp();
    }

    private static readonly string[] TextureFallbackExtensions =
    [
        ".dds",
        ".png",
        ".tga",
        ".bmp",
        ".jpg",
        ".jpeg",
        ".webp",
        ".gif"
    ];

    public static RuntimeObjectGeometry Build(
        string contentRoot,
        IReadOnlyList<RuntimeRemoteWalkerInfo> walkers,
        string? fallbackFigurePath = null)
    {
        if (walkers.Count == 0)
        {
            return RuntimeObjectGeometry.Empty;
        }

        var assets =
            new Dictionary<string, RuntimeSceneryAssetInfo>(
                StringComparer.OrdinalIgnoreCase);

        var instances =
            new List<RuntimeObjectInfo>(
                walkers.Count);

        var fallback =
            new List<RuntimeRemoteWalkerInfo>();

        var now =
            Stopwatch.GetTimestamp();

        foreach (var stale in
                 HumanAnimationStates
                     .Where(
                         pair =>
                             Stopwatch.GetElapsedTime(
                                 pair.Value.LastSeen,
                                 now) >
                             TimeSpan.FromSeconds(
                                 30))
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            HumanAnimationStates.Remove(
                stale);
        }

        foreach (var walker in walkers)
        {
            if (!TryResolveHumanAsset(
                    contentRoot,
                    walker.FigurePath,
                    fallbackFigurePath,
                    out var assetKey,
                    out var human))
            {
                fallback.Add(
                    walker);
                continue;
            }

            var posedKey =
                string.Concat(
                    assetKey,
                    "#",
                    walker.PlayerId);

            assets[posedKey] =
                PoseHumanAsset(
                    walker,
                    human,
                    now);

            var originY =
                walker.Y;

            if (walker.Seated &&
                double.IsFinite(
                    human.Definition.SeatHeight))
            {
                // openOMSI/OMSI store a seated WORLD/passpos position at
                // the hip. Human meshes are rooted at the feet, so move
                // the visual origin down by the .hum seat_height.
                originY -=
                    Math.Max(
                        0.0,
                        human.Definition.SeatHeight);
            }

            instances.Add(
                new RuntimeObjectInfo(
                    0,
                    0,
                    posedKey,
                    walker.X,
                    originY,
                    walker.Z,
                    walker.HeadingDegrees,
                    0.0,
                    0.0,
                    Array.Empty<string>()));
        }

        var humanGeometry =
            instances.Count == 0
                ? RuntimeObjectGeometry.Empty
                : RuntimeObjectGeometryBuilder.Build(
                    Array.Empty<RuntimeTileInfo>(),
                    instances,
                    assets,
                    useNativeOmsiModelSpace:
                        true);

        var fallbackVertices =
            BuildFallback(
                fallback);

        if (fallbackVertices.Length == 0)
        {
            return humanGeometry;
        }

        if (humanGeometry.Vertices.Length == 0)
        {
            return new RuntimeObjectGeometry(
                fallbackVertices,
                [
                    new RuntimeObjectBatch(
                        0,
                        (uint)fallbackVertices.Length,
                        null,
                        false,
                        false)
                ],
                fallback.Count,
                0,
                0,
                0,
                0,
                0,
                false);
        }

        var vertices =
            new RuntimeObjectVertex[
                checked(
                    humanGeometry.Vertices.Length +
                    fallbackVertices.Length)];

        Array.Copy(
            humanGeometry.Vertices,
            vertices,
            humanGeometry.Vertices.Length);

        Array.Copy(
            fallbackVertices,
            0,
            vertices,
            humanGeometry.Vertices.Length,
            fallbackVertices.Length);

        var batches =
            humanGeometry.Batches
                .ToList();

        batches.Add(
            new RuntimeObjectBatch(
                (uint)humanGeometry.Vertices.Length,
                (uint)fallbackVertices.Length,
                null,
                false,
                false));

        return new RuntimeObjectGeometry(
            vertices,
            batches,
            humanGeometry.RenderedObjectCount +
                fallback.Count,
            humanGeometry.RenderedMeshCount,
            humanGeometry.RenderedTreeCount,
            humanGeometry.TexturedBatchCount,
            humanGeometry.ProtectedMeshCount,
            humanGeometry.MissingMeshCount,
            humanGeometry.HitVertexBudget);
    }

    private static bool TryResolveHumanAsset(
        string contentRoot,
        string? figurePath,
        string? fallbackFigurePath,
        out string assetKey,
        out RuntimeHumanAsset asset)
    {
        assetKey =
            string.Empty;
        asset =
            null!;

        var selectedPath =
            string.IsNullOrWhiteSpace(
                figurePath)
                ? fallbackFigurePath
                : figurePath;

        if (!TryResolveHumanPath(
                contentRoot,
                selectedPath,
                out var humanPath))
        {
            return false;
        }

        RuntimeHumanAsset?
            cached;

        lock (HumanAssetCacheGate)
        {
            if (HumanAssetCache.TryGetValue(
                    humanPath,
                    out cached))
            {
                if (cached is null)
                {
                    return false;
                }

                assetKey =
                    HumanAssetPrefix +
                    humanPath;
                asset =
                    cached;
                return true;
            }
        }

        cached =
            LoadHumanAsset(
                contentRoot,
                humanPath);

        lock (HumanAssetCacheGate)
        {
            HumanAssetCache[
                humanPath] =
                cached;
        }

        if (cached is null)
        {
            return false;
        }

        assetKey =
            HumanAssetPrefix +
            humanPath;
        asset =
            cached;
        return true;
    }

    private static RuntimeHumanAsset? LoadHumanAsset(
        string contentRoot,
        string humanPath)
    {
        try
        {
            var definition =
                OmsiHumanDefinitionReader.ReadFile(
                    humanPath);

            if (string.IsNullOrWhiteSpace(
                    definition.ModelPath) ||
                !TryResolveRelativeContentPath(
                    contentRoot,
                    Path.GetDirectoryName(
                        humanPath),
                    definition.ModelPath,
                    out var modelPath))
            {
                Console.Error.WriteLine(
                    $"[human] .hum without usable [model]: {humanPath}");
                return null;
            }

            var model =
                OmsiVehicleModelReader.ReadFile(
                    modelPath);

            var selectedMeshes =
                SelectDetailedMeshes(
                    model.Meshes);

            var runtimeMeshes =
                new List<RuntimeObjectMeshInfo>(
                    selectedMeshes.Length);

            var skins =
                new List<RuntimeHumanMeshSkin?>(
                    selectedMeshes.Length);

            foreach (var mesh in
                     selectedMeshes)
            {
                if (!TryResolveHumanMeshPath(
                        contentRoot,
                        humanPath,
                        modelPath,
                        mesh.DeclaredPath,
                        out var meshPath))
                {
                    continue;
                }

                var geometry =
                    string.Equals(
                        Path.GetExtension(
                            meshPath),
                        ".x",
                        StringComparison.OrdinalIgnoreCase)
                        ? new OmsiDirectXTextGeometryReader()
                            .Read(
                                meshPath)
                        : OmsiO3dGeometryReader.ReadFile(
                            meshPath);

                if (!geometry.IsLoaded ||
                    !string.IsNullOrWhiteSpace(
                        geometry.ErrorCode) ||
                    geometry.Positions.Length <
                        3 ||
                    geometry.Indices.Length <
                        3)
                {
                    continue;
                }

                var materials =
                    BuildHumanMaterials(
                        contentRoot,
                        humanPath,
                        modelPath,
                        meshPath,
                        geometry.Materials,
                        mesh.MaterialOverrides);

                runtimeMeshes.Add(
                    new RuntimeObjectMeshInfo(
                        mesh.DeclaredPath,
                        meshPath,
                        null,
                        new RuntimeObjectMeshTransformInfo(
                            mesh.Transform.PositionX,
                            mesh.Transform.PositionY,
                            mesh.Transform.PositionZ,
                            mesh.Transform.RotationX,
                            mesh.Transform.RotationY,
                            mesh.Transform.RotationZ,
                            mesh.Transform.ScaleX,
                            mesh.Transform.ScaleY,
                            mesh.Transform.ScaleZ),
                        geometry.Positions,
                        geometry.Normals,
                        geometry.Uvs,
                        geometry.Indices,
                        geometry.TriangleMaterialIndices,
                        materials,
                        // OMSI's O3D matrix is the authored pivot frame,
                        // not a transform that moves human vertices.
                        SourceTransform:
                            Matrix4x4.Identity,
                        ModelOrdinal:
                            mesh.Ordinal));

                skins.Add(
                    BuildHumanSkin(
                        mesh,
                        geometry));
            }

            if (runtimeMeshes.Count ==
                0)
            {
                Console.Error.WriteLine(
                    $"[human] no renderable meshes: {humanPath}");
                return null;
            }

            var scenery =
                new RuntimeSceneryAssetInfo(
                    UsesAbsoluteHeight:
                        true,
                    OnlyEditor:
                        false,
                    RenderType:
                        null,
                    Meshes:
                        runtimeMeshes,
                    Tree:
                        null,
                    NoCollision:
                        true);

            var rig =
                definition.HasCompleteLinks
                    ? OmsiHumanRig.FromDefinition(
                        definition)
                    : null;

            Console.WriteLine(
                $"[human] loaded {Path.GetRelativePath(contentRoot, humanPath)} · meshes={runtimeMeshes.Count} · rig={(rig is null ? "fallback" : "omsi")}");

            return new RuntimeHumanAsset(
                humanPath,
                definition,
                rig,
                scenery,
                skins);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            OverflowException)
        {
            Console.Error.WriteLine(
                $"[human] failed {humanPath}: {exception.Message}");
            return null;
        }
    }

    private static RuntimeHumanMeshSkin? BuildHumanSkin(
        OmsiVehicleMeshReference mesh,
        OmsiO3dGeometry geometry)
    {
        var vertexCount =
            geometry.Positions.Length /
            3;

        if (vertexCount <=
                0 ||
            geometry.Bones is not
                { Count: > 0 })
        {
            return null;
        }

        var perVertex =
            Enumerable.Range(
                    0,
                    vertexCount)
                .Select(
                    static _ =>
                        new List<(byte Slot, float Weight)>())
                .ToArray();

        foreach (var bone in
                 geometry.Bones)
        {
            var binding =
                mesh.SkinBoneBindings?
                    .FirstOrDefault(
                        item =>
                            string.Equals(
                                item.BoneName,
                                bone.Name,
                                StringComparison.OrdinalIgnoreCase));

            var boneId =
                binding?
                    .TargetMeshOrdinal ??
                HumanBoneIdByName(
                    bone.Name);

            var slot =
                HumanBoneSlot(
                    boneId);

            if (!slot.HasValue)
            {
                continue;
            }

            foreach (var influence in
                     bone.Weights)
            {
                if (influence.VertexIndex <
                        0 ||
                    influence.VertexIndex >=
                        vertexCount ||
                    !float.IsFinite(
                        influence.Weight) ||
                    influence.Weight <=
                        0.0f)
                {
                    continue;
                }

                var list =
                    perVertex[
                        influence.VertexIndex];

                var existing =
                    list.FindIndex(
                        item =>
                            item.Slot ==
                            slot.Value);

                if (existing >=
                    0)
                {
                    var current =
                        list[
                            existing];

                    list[
                        existing] =
                        (
                            current.Slot,
                            Math.Max(
                                current.Weight,
                                influence.Weight)
                        );
                }
                else
                {
                    list.Add(
                        (
                            slot.Value,
                            influence.Weight
                        ));
                }
            }
        }

        var slots =
            new byte[
                checked(
                    vertexCount *
                    4)];

        var weights =
            new float[
                checked(
                    vertexCount *
                    4)];

        for (var vertex = 0;
             vertex <
                 vertexCount;
             vertex++)
        {
            var influences =
                perVertex[
                    vertex]
                    .OrderByDescending(
                        static item =>
                            item.Weight)
                    .Take(
                        4)
                    .ToArray();

            if (influences.Length ==
                0)
            {
                // openOMSI/OMSI behavior: unclaimed vertices follow MAIN.
                slots[
                    vertex *
                    4] =
                    9;

                weights[
                    vertex *
                    4] =
                    1.0f;
                continue;
            }

            var total =
                influences.Sum(
                    static item =>
                        item.Weight);

            if (!float.IsFinite(
                    total) ||
                total <
                    0.0001f)
            {
                slots[
                    vertex *
                    4] =
                    9;

                weights[
                    vertex *
                    4] =
                    1.0f;
                continue;
            }

            for (var index = 0;
                 index <
                     influences.Length;
                 index++)
            {
                var offset =
                    vertex *
                    4 +
                    index;

                slots[
                    offset] =
                    influences[
                        index]
                        .Slot;

                weights[
                    offset] =
                    influences[
                        index]
                        .Weight /
                    total;
            }
        }

        return new RuntimeHumanMeshSkin(
            slots,
            weights);
    }

    private static byte? HumanBoneSlot(
        int boneId) =>
        boneId is >=
            -14 and <=
            -2
            ? (byte)(
                -boneId -
                2)
            : null;

    private static int HumanBoneIdByName(
        string? name)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            return 0;
        }

        var normalized =
            name
                .Trim()
                .Replace(
                    "_",
                    string.Empty,
                    StringComparison.Ordinal)
                .ToLowerInvariant();

        return normalized switch
        {
            "osl" =>
                -2,
            "osr" =>
                -3,
            "usl" =>
                -4,
            "usr" =>
                -5,
            "oal" =>
                -6,
            "oar" =>
                -7,
            "ual" =>
                -8,
            "uar" =>
                -9,
            "hip" =>
                -10,
            "main" =>
                -11,
            "head" =>
                -12,
            "handl" =>
                -13,
            "handr" =>
                -14,
            _ =>
                0
        };
    }

    private static RuntimeSceneryAssetInfo PoseHumanAsset(
        RuntimeRemoteWalkerInfo walker,
        RuntimeHumanAsset human,
        long now)
    {
        if (human.Rig is null ||
            human.Skins.Count !=
                human.Scenery.Meshes.Count ||
            !human.Skins.Any(
                static skin =>
                    skin is not null))
        {
            return human.Scenery;
        }

        var animationKey =
            string.Concat(
                walker.PlayerId,
                "|",
                human.SourcePath);

        if (!HumanAnimationStates.TryGetValue(
                animationKey,
                out var state))
        {
            state =
                new RuntimeHumanAnimationState(
                    new OmsiHumanAnimator(
                        human.Rig));

            state.LastTick =
                now;

            HumanAnimationStates[
                animationKey] =
                state;
        }

        var elapsed =
            Stopwatch.GetElapsedTime(
                state.LastTick,
                now);

        state.LastTick =
            now;

        state.LastSeen =
            now;

        var deltaSeconds =
            (float)Math.Clamp(
                elapsed.TotalSeconds >
                    0.000001
                    ? elapsed.TotalSeconds
                    : 1.0 /
                      60.0,
                1.0 /
                240.0,
                0.1);

        var speed =
            float.IsFinite(
                    walker.SpeedMetersPerSecond)
                ? Math.Abs(
                    walker.SpeedMetersPerSecond)
                : 0.0f;

        var activity =
            walker.Seated
                ? OmsiHumanActivity.Sit
                : speed >
                      0.05f
                    ? speed >
                          2.2f
                        ? OmsiHumanActivity.Run
                        : OmsiHumanActivity.Walk
                    : OmsiHumanActivity.Stand;

        var bones =
            state.Animator.Advance(
                activity,
                speed,
                speed *
                deltaSeconds,
                deltaSeconds);

        var meshes =
            new RuntimeObjectMeshInfo[
                human.Scenery.Meshes.Count];

        for (var meshIndex = 0;
             meshIndex <
                 meshes.Length;
             meshIndex++)
        {
            var mesh =
                human.Scenery.Meshes[
                    meshIndex];

            var skin =
                human.Skins[
                    meshIndex];

            if (skin is null)
            {
                meshes[
                    meshIndex] =
                    mesh;
                continue;
            }

            var vertexCount =
                mesh.Positions.Length /
                3;

            if (skin.Slots.Length <
                    vertexCount *
                    4 ||
                skin.Weights.Length <
                    vertexCount *
                    4)
            {
                meshes[
                    meshIndex] =
                    mesh;
                continue;
            }

            var positions =
                new float[
                    mesh.Positions.Length];

            var normals =
                new float[
                    mesh.Normals.Length];

            for (var vertex = 0;
                 vertex <
                     vertexCount;
                 vertex++)
            {
                var positionOffset =
                    vertex *
                    3;

                var sourcePosition =
                    new Vector3(
                        mesh.Positions[
                            positionOffset],
                        mesh.Positions[
                            positionOffset +
                            1],
                        mesh.Positions[
                            positionOffset +
                            2]);

                var hasNormal =
                    positionOffset +
                        2 <
                    mesh.Normals.Length;

                var sourceNormal =
                    hasNormal
                        ? new Vector3(
                            mesh.Normals[
                                positionOffset],
                            mesh.Normals[
                                positionOffset +
                                1],
                            mesh.Normals[
                                positionOffset +
                                2])
                        : Vector3.UnitY;

                var posedPosition =
                    Vector3.Zero;

                var posedNormal =
                    Vector3.Zero;

                var total =
                    0.0f;

                for (var influence = 0;
                     influence <
                         4;
                     influence++)
                {
                    var skinOffset =
                        vertex *
                        4 +
                        influence;

                    var weight =
                        skin.Weights[
                            skinOffset];

                    if (!float.IsFinite(
                            weight) ||
                        weight <=
                            0.0f)
                    {
                        continue;
                    }

                    var slot =
                        skin.Slots[
                            skinOffset];

                    if (slot >=
                        bones.Length)
                    {
                        continue;
                    }

                    posedPosition +=
                        Vector3.Transform(
                            sourcePosition,
                            bones[
                                slot]) *
                        weight;

                    posedNormal +=
                        Vector3.TransformNormal(
                            sourceNormal,
                            bones[
                                slot]) *
                        weight;

                    total +=
                        weight;
                }

                if (total <
                    0.0001f)
                {
                    posedPosition =
                        sourcePosition;

                    posedNormal =
                        sourceNormal;
                }
                else if (Math.Abs(
                             total -
                             1.0f) >
                         0.0001f)
                {
                    posedPosition /=
                        total;

                    posedNormal /=
                        total;
                }

                positions[
                    positionOffset] =
                    posedPosition.X;

                positions[
                    positionOffset +
                    1] =
                    posedPosition.Y;

                positions[
                    positionOffset +
                    2] =
                    posedPosition.Z;

                if (hasNormal)
                {
                    if (posedNormal.LengthSquared() >
                        0.000001f)
                    {
                        posedNormal =
                            Vector3.Normalize(
                                posedNormal);
                    }
                    else
                    {
                        posedNormal =
                            sourceNormal;
                    }

                    normals[
                        positionOffset] =
                        posedNormal.X;

                    normals[
                        positionOffset +
                        1] =
                        posedNormal.Y;

                    normals[
                        positionOffset +
                        2] =
                        posedNormal.Z;
                }
            }

            meshes[
                meshIndex] =
                mesh with
                {
                    Positions =
                        positions,
                    Normals =
                        normals
                };
        }

        return human.Scenery with
        {
            Meshes =
                meshes
        };
    }

    private static OmsiVehicleMeshReference[] SelectDetailedMeshes(
        IReadOnlyList<OmsiVehicleMeshReference> meshes)
    {
        if (meshes.Count == 0)
        {
            return [];
        }

        var detailedLod =
            meshes
                .Where(
                    static mesh =>
                        mesh.LodThreshold.HasValue)
                .Select(
                    static mesh =>
                        mesh.LodThreshold!.Value)
                .DefaultIfEmpty(
                    double.NaN)
                .Max();

        if (double.IsNaN(
                detailedLod))
        {
            return meshes.ToArray();
        }

        var selected =
            meshes
                .Where(
                    mesh =>
                        !mesh.LodThreshold.HasValue ||
                        Math.Abs(
                            mesh.LodThreshold.Value -
                            detailedLod) <
                        0.000001)
                .ToArray();

        return selected.Length > 0
            ? selected
            : meshes.ToArray();
    }

    private static RuntimeO3dMaterialInfo[] BuildHumanMaterials(
        string contentRoot,
        string humanPath,
        string modelPath,
        string meshPath,
        IReadOnlyList<OmsiO3dMaterial> source,
        IReadOnlyList<OmsiVehicleMaterialOverride> overrides)
    {
        var result =
            new RuntimeO3dMaterialInfo[
                source.Count];

        for (var materialIndex = 0;
             materialIndex <
                 source.Count;
             materialIndex++)
        {
            var material =
                source[
                    materialIndex];

            var occurrence =
                GetTextureOccurrenceIndex(
                    source,
                    materialIndex);

            var materialOverride =
                overrides
                    .FirstOrDefault(
                        item =>
                            item.MaterialIndex ==
                                occurrence &&
                            MaterialTextureMatches(
                                item.TextureName,
                                material.TextureName) &&
                            string.IsNullOrWhiteSpace(
                                item.MaterialChangeVariable));

            if (materialOverride is null)
            {
                materialOverride =
                    overrides
                        .FirstOrDefault(
                            item =>
                                item.MaterialIndex ==
                                    materialIndex &&
                                MaterialTextureMatches(
                                    item.TextureName,
                                    material.TextureName) &&
                                string.IsNullOrWhiteSpace(
                                    item.MaterialChangeVariable));
            }

            string? texturePath =
                null;

            if (!string.IsNullOrWhiteSpace(
                    material.TextureName))
            {
                TryResolveHumanTexture(
                    contentRoot,
                    humanPath,
                    modelPath,
                    meshPath,
                    material.TextureName,
                    out texturePath);
            }

            string? transMapPath =
                null;

            if (!string.IsNullOrWhiteSpace(
                    materialOverride?
                        .TransMapSource))
            {
                TryResolveHumanTexture(
                    contentRoot,
                    humanPath,
                    modelPath,
                    meshPath,
                    materialOverride!
                        .TransMapSource!,
                    out transMapPath);
            }

            var alphaMode =
                materialOverride?
                    .AlphaMode ??
                (materialOverride?
                     .HasTransMapDirective ==
                 true &&
                 !string.IsNullOrWhiteSpace(
                     transMapPath)
                    ? 1
                    : 0);

            result[
                materialIndex] =
                new RuntimeO3dMaterialInfo(
                    material.DiffuseR,
                    material.DiffuseG,
                    material.DiffuseB,
                    material.DiffuseA,
                    texturePath,
                    alphaMode,
                    transMapPath,
                    materialOverride?
                        .NoZWrite ??
                    false,
                    materialOverride?
                        .NoZCheck ??
                    false,
                    HasTransMapDirective:
                        materialOverride?
                            .HasTransMapDirective ??
                        false,
                    RequiresExternalTransMap:
                        materialOverride?
                            .HasTransMapDirective ==
                        true &&
                        !string.IsNullOrWhiteSpace(
                            transMapPath));
        }

        return result;
    }

    private static bool TryReadHumanModelPath(
        string humanPath,
        out string modelPath)
    {
        modelPath =
            string.Empty;

        var lines =
            File.ReadAllLines(
                humanPath);

        var enabled =
            true;
        var expectingModel =
            false;

        foreach (var raw in lines)
        {
            var trimmed =
                raw
                    .Trim()
                    .TrimStart(
                        '\uFEFF');

            if (trimmed.Equals(
                    "-<DISABLED>-",
                    StringComparison.OrdinalIgnoreCase))
            {
                enabled =
                    false;
                expectingModel =
                    false;
                continue;
            }

            if (trimmed.Equals(
                    "-<ENABLED>-",
                    StringComparison.OrdinalIgnoreCase))
            {
                enabled =
                    true;
                continue;
            }

            if (!enabled)
            {
                continue;
            }

            if (expectingModel)
            {
                if (trimmed.Length == 0 ||
                    trimmed.StartsWith(
                        ";",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.StartsWith(
                        "[",
                        StringComparison.Ordinal))
                {
                    return false;
                }

                modelPath =
                    trimmed
                        .Trim('"');

                return !string.IsNullOrWhiteSpace(
                    modelPath);
            }

            if (trimmed.Equals(
                    "[model]",
                    StringComparison.OrdinalIgnoreCase))
            {
                expectingModel =
                    true;
            }
        }

        return false;
    }

    private static bool TryResolveHumanPath(
        string contentRoot,
        string? declaredPath,
        out string fullPath)
    {
        fullPath =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                contentRoot) ||
            string.IsNullOrWhiteSpace(
                declaredPath))
        {
            return false;
        }

        try
        {
            var root =
                EnsureTrailingSeparator(
                    Path.GetFullPath(
                        contentRoot));

            var normalized =
                declaredPath
                    .Trim()
                    .Trim('"')
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            var candidates =
                new List<string>();

            if (Path.IsPathRooted(
                    normalized))
            {
                candidates.Add(
                    normalized);
            }
            else
            {
                candidates.Add(
                    Path.Combine(
                        contentRoot,
                        normalized));

                if (!normalized.StartsWith(
                        "Humans" +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(
                        Path.Combine(
                            contentRoot,
                            "Humans",
                            normalized));
                }
            }

            foreach (var candidate in candidates)
            {
                var resolved =
                    Path.GetFullPath(
                        candidate);

                if (IsInsideRoot(
                        resolved,
                        root) &&
                    resolved.EndsWith(
                        ".hum",
                        StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(
                        resolved))
                {
                    fullPath =
                        resolved;
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static bool TryResolveHumanMeshPath(
        string contentRoot,
        string humanPath,
        string modelPath,
        string declaredMeshPath,
        out string fullPath)
    {
        fullPath =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                declaredMeshPath))
        {
            return false;
        }

        var modelDirectory =
            Path.GetDirectoryName(
                modelPath);

        var humanDirectory =
            Path.GetDirectoryName(
                humanPath);

        if (string.IsNullOrWhiteSpace(
                modelDirectory) ||
            string.IsNullOrWhiteSpace(
                humanDirectory))
        {
            return false;
        }

        return TryResolveRelativeContentPath(
            contentRoot,
            modelDirectory,
            declaredMeshPath,
            out fullPath) ||
               TryResolveRelativeContentPath(
                   contentRoot,
                   humanDirectory,
                   declaredMeshPath,
                   out fullPath);
    }

    private static bool TryResolveRelativeContentPath(
        string contentRoot,
        string? baseDirectory,
        string declaredPath,
        out string fullPath)
    {
        fullPath =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                baseDirectory) ||
            string.IsNullOrWhiteSpace(
                declaredPath))
        {
            return false;
        }

        try
        {
            var normalized =
                declaredPath
                    .Trim()
                    .Trim('"')
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(
                    normalized) ||
                normalized.Contains(
                    ':',
                    StringComparison.Ordinal))
            {
                return false;
            }

            var root =
                EnsureTrailingSeparator(
                    Path.GetFullPath(
                        contentRoot));

            var candidate =
                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        normalized));

            if (!IsInsideRoot(
                    candidate,
                    root) ||
                !File.Exists(
                    candidate))
            {
                return false;
            }

            fullPath =
                candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryResolveHumanTexture(
        string contentRoot,
        string humanPath,
        string modelPath,
        string meshPath,
        string declaredTexture,
        out string? fullPath)
    {
        fullPath =
            null;

        if (string.IsNullOrWhiteSpace(
                declaredTexture))
        {
            return false;
        }

        try
        {
            var normalized =
                declaredTexture
                    .Trim()
                    .Trim('"')
                    .TrimStart(
                        '\\',
                        '/')
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            if (normalized.Length == 0 ||
                Path.IsPathRooted(
                    normalized) ||
                normalized.Contains(
                    ':',
                    StringComparison.Ordinal))
            {
                return false;
            }

            var humanDirectory =
                Path.GetDirectoryName(
                    humanPath);

            var modelDirectory =
                Path.GetDirectoryName(
                    modelPath);

            var meshDirectory =
                Path.GetDirectoryName(
                    meshPath);

            var bases =
                new List<string>();

            static void AddBase(
                ICollection<string> target,
                string? directory)
            {
                if (!string.IsNullOrWhiteSpace(
                        directory))
                {
                    target.Add(
                        directory);
                    target.Add(
                        Path.Combine(
                            directory,
                            "Texture"));
                    target.Add(
                        Path.Combine(
                            directory,
                            "texture"));
                }
            }

            AddBase(
                bases,
                humanDirectory);
            AddBase(
                bases,
                modelDirectory);
            AddBase(
                bases,
                meshDirectory);

            var root =
                EnsureTrailingSeparator(
                    Path.GetFullPath(
                        contentRoot));

            foreach (var baseDirectory in
                     bases.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                if (TryResolveTextureCandidate(
                        root,
                        baseDirectory,
                        normalized,
                        out var resolved))
                {
                    fullPath =
                        resolved;
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static bool TryResolveTextureCandidate(
        string root,
        string baseDirectory,
        string normalizedTexture,
        out string fullPath)
    {
        fullPath =
            string.Empty;

        var exact =
            Path.GetFullPath(
                Path.Combine(
                    baseDirectory,
                    normalizedTexture));

        if (IsInsideRoot(
                exact,
                root) &&
            File.Exists(
                exact))
        {
            fullPath =
                exact;
            return true;
        }

        foreach (var extension in
                 TextureFallbackExtensions)
        {
            var alternate =
                Path.ChangeExtension(
                    normalizedTexture,
                    extension);

            var candidate =
                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        alternate));

            if (IsInsideRoot(
                    candidate,
                    root) &&
                File.Exists(
                    candidate))
            {
                fullPath =
                    candidate;
                return true;
            }
        }

        return false;
    }

    private static int GetTextureOccurrenceIndex(
        IReadOnlyList<OmsiO3dMaterial> materials,
        int materialIndex)
    {
        if (materialIndex <= 0 ||
            materialIndex >=
                materials.Count)
        {
            return 0;
        }

        var textureName =
            Path.GetFileName(
                materials[
                    materialIndex]
                    .TextureName);

        var occurrence =
            0;

        for (var index = 0;
             index <
                 materialIndex;
             index++)
        {
            if (string.Equals(
                    Path.GetFileName(
                        materials[
                            index]
                            .TextureName),
                    textureName,
                    StringComparison.OrdinalIgnoreCase))
            {
                occurrence++;
            }
        }

        return occurrence;
    }

    private static bool MaterialTextureMatches(
        string? declaredTexture,
        string? o3dTexture) =>
        string.Equals(
            Path.GetFileName(
                declaredTexture),
            Path.GetFileName(
                o3dTexture),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideRoot(
        string fullPath,
        string rootWithSeparator)
    {
        var normalized =
            Path.GetFullPath(
                fullPath);

        return normalized.StartsWith(
                   rootWithSeparator,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized.TrimEnd(
                       Path.DirectorySeparatorChar,
                       Path.AltDirectorySeparatorChar),
                   rootWithSeparator.TrimEnd(
                       Path.DirectorySeparatorChar,
                       Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(
        string path) =>
        path.EndsWith(
            Path.DirectorySeparatorChar) ||
        path.EndsWith(
            Path.AltDirectorySeparatorChar)
            ? path
            : path +
              Path.DirectorySeparatorChar;

    private static RuntimeObjectVertex[] BuildFallback(
        IReadOnlyList<RuntimeRemoteWalkerInfo> walkers)
    {
        if (walkers.Count == 0)
        {
            return [];
        }

        var vertices =
            new List<RuntimeObjectVertex>(
                walkers.Count *
                108);

        foreach (var walker in walkers)
        {
            AppendFallbackWalker(
                walker,
                vertices);
        }

        return vertices.ToArray();
    }

    private static void AppendFallbackWalker(
        RuntimeRemoteWalkerInfo walker,
        ICollection<RuntimeObjectVertex> output)
    {
        var yaw =
            -walker.HeadingDegrees *
            MathF.PI /
            180.0f;

        var rotation =
            Matrix4x4.CreateRotationY(
                yaw);

        var feet =
            new Vector3(
                (float)walker.X,
                (float)walker.Y,
                (float)walker.Z);

        var bodyColor =
            new Color4(
                0.16f,
                0.52f,
                0.92f,
                0.92f);

        var skinColor =
            new Color4(
                0.82f,
                0.66f,
                0.52f,
                0.95f);

        if (walker.Seated)
        {
            AppendBox(
                feet +
                new Vector3(
                    0.0f,
                    0.72f,
                    0.0f),
                new Vector3(
                    0.48f,
                    0.72f,
                    0.34f),
                rotation,
                bodyColor,
                output);

            AppendBox(
                feet +
                new Vector3(
                    0.0f,
                    1.32f,
                    0.02f),
                new Vector3(
                    0.30f,
                    0.30f,
                    0.30f),
                rotation,
                skinColor,
                output);

            return;
        }

        AppendBox(
            feet +
            new Vector3(
                0.0f,
                0.88f,
                0.0f),
            new Vector3(
                0.46f,
                1.10f,
                0.32f),
            rotation,
            bodyColor,
            output);

        AppendBox(
            feet +
            new Vector3(
                0.0f,
                1.62f,
                0.0f),
            new Vector3(
                0.31f,
                0.31f,
                0.31f),
            rotation,
            skinColor,
            output);

        AppendBox(
            feet +
            TransformOffset(
                new Vector3(
                    -0.13f,
                    0.30f,
                    0.0f),
                rotation),
            new Vector3(
                0.16f,
                0.58f,
                0.20f),
            rotation,
            bodyColor,
            output);

        AppendBox(
            feet +
            TransformOffset(
                new Vector3(
                    0.13f,
                    0.30f,
                    0.0f),
                rotation),
            new Vector3(
                0.16f,
                0.58f,
                0.20f),
            rotation,
            bodyColor,
            output);
    }

    private static Vector3 TransformOffset(
        Vector3 value,
        Matrix4x4 rotation) =>
        Vector3.TransformNormal(
            value,
            rotation);

    private static void AppendBox(
        Vector3 center,
        Vector3 size,
        Matrix4x4 rotation,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var half =
            size *
            0.5f;

        Span<Vector3> p =
            stackalloc Vector3[8]
            {
                new(-half.X, -half.Y, -half.Z),
                new( half.X, -half.Y, -half.Z),
                new( half.X,  half.Y, -half.Z),
                new(-half.X,  half.Y, -half.Z),
                new(-half.X, -half.Y,  half.Z),
                new( half.X, -half.Y,  half.Z),
                new( half.X,  half.Y,  half.Z),
                new(-half.X,  half.Y,  half.Z)
            };

        for (var index = 0;
             index <
                 p.Length;
             index++)
        {
            p[index] =
                Vector3.TransformNormal(
                    p[index],
                    rotation) +
                center;
        }

        AppendQuad(p[0], p[1], p[2], p[3], color, output);
        AppendQuad(p[5], p[4], p[7], p[6], color, output);
        AppendQuad(p[4], p[0], p[3], p[7], color, output);
        AppendQuad(p[1], p[5], p[6], p[2], color, output);
        AppendQuad(p[3], p[2], p[6], p[7], color, output);
        AppendQuad(p[4], p[5], p[1], p[0], color, output);
    }

    private static void AppendQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        AppendTriangle(a, b, c, color, output);
        AppendTriangle(a, c, d, color, output);
    }

    private static void AppendTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var uv =
            Vector2.Zero;

        output.Add(new RuntimeObjectVertex(a, color, uv));
        output.Add(new RuntimeObjectVertex(b, color, uv));
        output.Add(new RuntimeObjectVertex(c, color, uv));
    }
}
