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
            // Seated people still use the compact fallback until the WORLD
            // seat origin is converted from [passpos] hip height to the
            // human's visual feet origin.
            if (walker.Seated ||
                !TryResolveHumanAsset(
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

            instances.Add(
                new RuntimeObjectInfo(
                    0,
                    0,
                    posedKey,
                    walker.X,
                    walker.Y,
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

    private static RuntimeSceneryAssetInfo? LoadHumanAsset(
        string contentRoot,
        string humanPath)
    {
        try
        {
            if (!TryReadHumanModelPath(
                    humanPath,
                    out var declaredModelPath) ||
                !TryResolveRelativeContentPath(
                    contentRoot,
                    Path.GetDirectoryName(
                        humanPath),
                    declaredModelPath,
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

            foreach (var mesh in selectedMeshes)
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
                        SourceTransform:
                            geometry.SourceTransform,
                        ModelOrdinal:
                            mesh.Ordinal));
            }

            if (runtimeMeshes.Count == 0)
            {
                Console.Error.WriteLine(
                    $"[human] no renderable meshes: {humanPath}");
                return null;
            }

            Console.WriteLine(
                $"[human] loaded {Path.GetRelativePath(contentRoot, humanPath)} · meshes={runtimeMeshes.Count}");

            return new RuntimeSceneryAssetInfo(
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
