using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Models;
using OmsiCompat.Scenery;
using OmsiCompat.Splines;

namespace OMSICompatible.World;

public static class WorldLoader
{
    public static WorldDefinition Load(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map,
        IProgress<WorldLoadProgress>? progress = null,
        WorldLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(map);

        var allSourceTiles =
            MapTileDiscovery.Discover(
                map);

        options ??=
            new WorldLoadOptions();

        var sourceTiles =
            SelectSourceTiles(
                allSourceTiles,
                options);

        progress?.Report(
            new WorldLoadProgress(
                8,
                "Lendo mapa",
                options.LoadEntireMap
                    ? $"Carregando mapa completo · {allSourceTiles.Count:N0} tiles..."
                    : $"Preparando streaming · {sourceTiles.Count:N0}/{allSourceTiles.Count:N0} tiles ativos..."));

        var tiles = new List<WorldTile>(sourceTiles.Count);
        var tileIndex = 0;

        foreach (var sourceTile in sourceTiles)
        {
            tileIndex++;

            var tilePercent =
                sourceTiles.Count == 0
                    ? 45
                    : 10 +
                      (int)Math.Round(
                          45.0 *
                          tileIndex /
                          sourceTiles.Count);

            progress?.Report(
                new WorldLoadProgress(
                    tilePercent,
                    "Carregando mundo",
                    $"Tile {tileIndex:N0}/{sourceTiles.Count:N0} · {sourceTile.Coordinate.X},{sourceTile.Coordinate.Y}"));
            var coordinate = new WorldTileCoordinate(
                sourceTile.Coordinate.X,
                sourceTile.Coordinate.Y);

            var summary = MapTileProbe.ReadSummary(sourceTile);
            var placements = MapTilePlacementParser.Parse(sourceTile);
            var companions = MapTileCompanionDiscovery.Discover(sourceTile);

            var assetReferences = AssetReferenceScanner.Scan(sourceTile)
                .Select(static reference => new WorldAssetReference(
                    Classify(reference.Extension),
                    NormalizePath(reference.RawPath),
                    reference.SectionName,
                    reference.LineNumber))
                .ToArray();

            var tileObjects = placements.Objects
                .Select(source => new WorldObjectPlacement(
                    coordinate,
                    source.Id,
                    NormalizePath(source.AssetPath),
                    ToWorldVector(source.Position),
                    source.HeadingDegrees,
                    source.PitchDegrees,
                    source.BankDegrees,
                    source.ExtraValues,
                    source.SourceLineNumber))
                .ToArray();

            var tileSplines = placements.Splines
                .Select(source => new WorldSplinePlacement(
                    coordinate,
                    source.Id,
                    source.PreviousId,
                    source.NextId,
                    NormalizePath(source.AssetPath),
                    ToWorldVector(source.Position),
                    source.HeadingDegrees,
                    source.LengthMeters,
                    source.RadiusMeters,
                    source.GradientStartPercent,
                    source.GradientEndPercent,
                    source.UsesHeightProfile,
                    source.SourceLineNumber))
                .ToArray();

            var resources = new WorldTileResources(
                companions.TerrainPath,
                companions.LightmapPath,
                companions.WaterPath,
                companions.ReadyMeshPaths,
                companions.TerrainTexturePaths,
                BuildTerrainMasks(
                    sourceTile.FilePath,
                    companions.TerrainTexturePaths));

            var (terrain, terrainErrorCode) =
                LoadTerrain(resources.TerrainPath);

            tiles.Add(new WorldTile(
                coordinate,
                sourceTile.FilePath,
                sourceTile.Bytes,
                summary.SectionCount,
                summary.SectionCounts,
                assetReferences,
                tileObjects,
                tileSplines,
                resources,
                terrain,
                terrainErrorCode,
                placements.Issues.Count));
        }

        WorldBounds? bounds = null;
        if (tiles.Count > 0)
        {
            bounds = new WorldBounds(
                tiles.Min(static tile => tile.Coordinate.X),
                tiles.Min(static tile => tile.Coordinate.Y),
                tiles.Max(static tile => tile.Coordinate.X),
                tiles.Max(static tile => tile.Coordinate.Y));
        }

        var assets = tiles
            .SelectMany(static tile => tile.AssetReferences)
            .GroupBy(
                static asset => (asset.Kind, asset.SourcePath),
                AssetKeyComparer.Instance)
            .Select(static group => group.First())
            .OrderBy(static asset => asset.Kind)
            .ThenBy(static asset => asset.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allObjects = tiles
            .SelectMany(static tile => tile.Objects)
            .ToArray();

        var allSplines = tiles
            .SelectMany(static tile => tile.Splines)
            .ToArray();

        progress?.Report(
            new WorldLoadProgress(
                62,
                "Resolvendo conteúdo",
                $"{allObjects.Length:N0} objetos · {allSplines.Length:N0} splines"));

        var dependencies = WorldAssetResolver.ResolvePrimaryDependencies(
            contentRoot,
            allObjects,
            allSplines);

        progress?.Report(
            new WorldLoadProgress(
                74,
                "Preparando vias",
                $"Lendo {allSplines.Select(static spline => spline.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} arquivos de spline..."));

        var splineAssets = LoadSplineAssets(
            contentRoot,
            allSplines,
            dependencies);

        var trafficPaths =
            WorldTrafficPathNetworkBuilder.Build(
                allSplines,
                splineAssets);

        progress?.Report(
            new WorldLoadProgress(
                82,
                "Preparando cenário",
                $"Lendo {allObjects.Select(static item => item.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} tipos de Sceneryobjects/O3D..."));

        var sceneryAssets = LoadSceneryAssets(
            contentRoot,
            allObjects,
            dependencies);

        var groundTextures =
            LoadGroundTextures(
                contentRoot,
                map);

        var aiCatalog =
            OmsiMapAiCatalogReader.Read(
                contentRoot,
                map);

        progress?.Report(
            new WorldLoadProgress(
                90,
                "Montando mundo",
                $"Cenário: {sceneryAssets.Values.Count(static asset => asset.IsRenderable):N0} assets renderizáveis · paths {trafficPaths.Segments.Count:N0} · terreno {groundTextures.Count:N0} camada(s) · finalizando modelo x64..."));

        return new WorldDefinition(
            map.FolderName,
            map.DirectoryPath,
            allSourceTiles.Count,
            options.LoadEntireMap
                ? null
                : options.SafeActiveTileRadius,
            tiles.ToArray(),
            assets,
            allObjects,
            allSplines,
            splineAssets,
            sceneryAssets,
            groundTextures,
            trafficPaths,
            aiCatalog,
            dependencies,
            tiles.Sum(static tile => tile.PlacementParseIssueCount),
            tiles.Count(static tile => tile.TerrainErrorCode is not null),
            bounds);
    }

    private static IReadOnlyList<OmsiMapTileInfo>
        SelectSourceTiles(
            IReadOnlyList<OmsiMapTileInfo> allSourceTiles,
            WorldLoadOptions options)
    {
        if (options.LoadEntireMap ||
            !options.HasCenter)
        {
            return allSourceTiles;
        }

        var radius =
            options.SafeActiveTileRadius;

        var centerX =
            options.CenterTileX!.Value;

        var centerY =
            options.CenterTileY!.Value;

        return allSourceTiles
            .Where(
                tile =>
                    Math.Abs(
                        tile.Coordinate.X -
                        centerX) <= radius &&
                    Math.Abs(
                        tile.Coordinate.Y -
                        centerY) <= radius)
            .OrderBy(
                tile =>
                    Math.Max(
                        Math.Abs(
                            tile.Coordinate.X -
                            centerX),
                        Math.Abs(
                            tile.Coordinate.Y -
                            centerY)))
            .ThenBy(static tile => tile.Coordinate.Y)
            .ThenBy(static tile => tile.Coordinate.X)
            .ToArray();
    }

    private static IReadOnlyList<WorldGroundTexture>
        LoadGroundTextures(
            OmsiContentRoot contentRoot,
            OmsiMapInfo map)
    {
        var source =
            OmsiGroundTextureReader.ReadFile(
                map.GlobalConfigPath);

        return source
            .Select(
                (ground, index) =>
                    new WorldGroundTexture(
                        index,
                        ResolveGroundTexturePath(
                            contentRoot.RootPath,
                            map.DirectoryPath,
                            ground.MainTexturePath),
                        ResolveGroundTexturePath(
                            contentRoot.RootPath,
                            map.DirectoryPath,
                            ground.DetailTexturePath),
                        ground.MainTextureRepeating,
                        ground.DetailTextureRepeating))
            .ToArray();
    }

    private static string? ResolveGroundTexturePath(
        string contentRoot,
        string mapDirectory,
        string textureName)
    {
        return OmsiTextureAssetPathResolver
            .TryResolveGroundTexture(
                contentRoot,
                mapDirectory,
                textureName,
                out var resolved)
            ? resolved
            : null;
    }

    private static IReadOnlyList<WorldTerrainMask>
        BuildTerrainMasks(
            string tileFilePath,
            IReadOnlyList<string> paths)
    {
        var tileFileName =
            Path.GetFileName(
                tileFilePath);

        var prefix =
            tileFileName + ".";

        var masks =
            new List<WorldTerrainMask>();

        foreach (var path in paths)
        {
            var fileName =
                Path.GetFileName(path);

            if (!fileName.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(
                    ".dds",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var layerText =
                fileName[
                    prefix.Length..
                    ^4];

            if (!int.TryParse(
                    layerText,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var layerIndex) ||
                layerIndex <= 0)
            {
                continue;
            }

            masks.Add(
                new WorldTerrainMask(
                    layerIndex,
                    path));
        }

        return masks
            .OrderBy(
                static mask =>
                    mask.LayerIndex)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, WorldSplineAsset>
        LoadSplineAssets(
            OmsiContentRoot contentRoot,
            IReadOnlyList<WorldSplinePlacement> splines,
            WorldDependencyReport dependencies)
    {
        var result = new Dictionary<string, WorldSplineAsset>(
            StringComparer.OrdinalIgnoreCase);

        var dependencyByPath = dependencies.Dependencies
            .Where(static dependency =>
                dependency.Kind == WorldAssetKind.Spline)
            .ToDictionary(
                static dependency => dependency.SourcePath,
                StringComparer.OrdinalIgnoreCase);

        foreach (var declaredPath in splines
                     .Select(static spline => spline.AssetPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dependencyByPath.TryGetValue(
                declaredPath,
                out var dependency);

            if (dependency is null ||
                !dependency.Exists ||
                string.IsNullOrWhiteSpace(dependency.ResolvedPath))
            {
                result[declaredPath] = new WorldSplineAsset(
                    declaredPath,
                    dependency?.ResolvedPath,
                    false,
                    Array.Empty<WorldSplineSurface>(),
                    Array.Empty<WorldSplinePath>());
                continue;
            }

            try
            {
                var definition = OmsiSplineDefinitionReader.ReadFile(
                    dependency.ResolvedPath);

                var surfaces = definition.Surfaces
                    .Select(surface =>
                    {
                        string? texturePath = null;

                        if (!string.IsNullOrWhiteSpace(
                                surface.TextureName) &&
                            OmsiTextureAssetPathResolver
                                .TryResolveSplineTexture(
                                    contentRoot.RootPath,
                                    dependency.ResolvedPath,
                                    surface.TextureName,
                                    out var resolvedTexture))
                        {
                            texturePath = resolvedTexture;
                        }

                        return new WorldSplineSurface(
                            surface.TextureIndex,
                            surface.TextureName,
                            texturePath,
                            surface.AlphaMode,
                            new WorldSplineProfilePoint(
                                surface.From.X,
                                surface.From.Z,
                                surface.From.TextureX,
                                surface.From.TextureScale),
                            new WorldSplineProfilePoint(
                                surface.To.X,
                                surface.To.Z,
                                surface.To.TextureX,
                                surface.To.TextureScale));
                    })
                    .ToArray();

                var paths =
                    definition.Paths
                        .Select(
                            static path =>
                                new WorldSplinePath(
                                    path.Type,
                                    path.X,
                                    path.Z,
                                    path.Width,
                                    path.Direction))
                        .ToArray();

                result[declaredPath] = new WorldSplineAsset(
                    declaredPath,
                    dependency.ResolvedPath,
                    definition.Exists,
                    surfaces,
                    paths);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException)
            {
                result[declaredPath] = new WorldSplineAsset(
                    declaredPath,
                    dependency.ResolvedPath,
                    false,
                    Array.Empty<WorldSplineSurface>(),
                    Array.Empty<WorldSplinePath>());
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, WorldSceneryAsset>
        LoadSceneryAssets(
            OmsiContentRoot contentRoot,
            IReadOnlyList<WorldObjectPlacement> objects,
            WorldDependencyReport dependencies)
    {
        var result =
            new Dictionary<string, WorldSceneryAsset>(
                StringComparer.OrdinalIgnoreCase);

        var dependencyByPath =
            dependencies.Dependencies
                .Where(
                    static dependency =>
                        dependency.Kind ==
                        WorldAssetKind.SceneryObject)
                .ToDictionary(
                    static dependency =>
                        dependency.SourcePath,
                    StringComparer.OrdinalIgnoreCase);

        foreach (var declaredPath in objects
                     .Select(static item => item.AssetPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dependencyByPath.TryGetValue(
                declaredPath,
                out var dependency);

            if (dependency is null ||
                !dependency.Exists ||
                string.IsNullOrWhiteSpace(
                    dependency.ResolvedPath))
            {
                result[declaredPath] =
                    new WorldSceneryAsset(
                        declaredPath,
                        dependency?.ResolvedPath,
                        false,
                        false,
                        false,
                        null,
                        Array.Empty<WorldSceneryMeshAsset>(),
                        null);

                continue;
            }

            try
            {
                var definition =
                    OmsiSceneryObjectReader.ReadFile(
                        dependency.ResolvedPath);

                var lodThresholds =
                    definition.Meshes
                        .Where(
                            static mesh =>
                                mesh.LodThreshold.HasValue)
                        .Select(
                            static mesh =>
                                mesh.LodThreshold!.Value)
                        .Distinct()
                        .OrderByDescending(
                            static value => value)
                        .ToArray();

                var selectedLod =
                    lodThresholds.FirstOrDefault();

                var meshes =
                    new List<WorldSceneryMeshAsset>();

                for (var meshOrdinal = 0;
                     meshOrdinal < definition.Meshes.Count;
                     meshOrdinal++)
                {
                    var mesh =
                        definition.Meshes[meshOrdinal];
                    if (mesh.LodThreshold.HasValue &&
                        lodThresholds.Length > 0 &&
                        Math.Abs(
                            mesh.LodThreshold.Value -
                            selectedLod) >
                        0.000001)
                    {
                        continue;
                    }

                    var meshPath =
                        ResolveMeshPath(
                            contentRoot.RootPath,
                            dependency.ResolvedPath,
                            mesh.Path);

                    if (meshPath is null)
                    {
                        meshes.Add(
                            CreateMissingMesh(
                                mesh.Path,
                                mesh.Transform,
                                "missingO3d"));

                        continue;
                    }

                    var geometry =
                        string.Equals(
                            Path.GetExtension(meshPath),
                            ".x",
                            StringComparison.OrdinalIgnoreCase)
                            ? new OmsiDirectXTextGeometryReader()
                                .Read(meshPath)
                            : OmsiO3dGeometryReader.ReadFile(
                                meshPath);

                    meshes.Add(
                        new WorldSceneryMeshAsset(
                            mesh.Path,
                            meshPath,
                            File.Exists(meshPath),
                            geometry.ErrorCode,
                            ConvertTransform(
                                mesh.Transform),
                            geometry.Positions,
                            geometry.Normals,
                            geometry.Uvs,
                            geometry.Indices,
                            geometry.TriangleMaterialIndices,
                            geometry.Materials
                                .Select(
                                    (material, materialIndex) =>
                                    {
                                        string? texturePath = null;

                                        if (!string.IsNullOrWhiteSpace(
                                                material.TextureName) &&
                                            OmsiTextureAssetPathResolver
                                                .TryResolveSceneryTexture(
                                                    contentRoot.RootPath,
                                                    dependency.ResolvedPath,
                                                    meshPath,
                                                    material.TextureName,
                                                    out var resolvedTexture))
                                        {
                                            texturePath = resolvedTexture;
                                        }

                                        var textureOccurrence =
                                            GetTextureOccurrenceIndex(
                                                geometry.Materials,
                                                materialIndex);

                                        var materialOverride =
                                            definition.MaterialOverrides
                                                .Where(
                                                    item =>
                                                        item.MeshOrdinal ==
                                                            meshOrdinal &&
                                                        item.MaterialIndex ==
                                                            textureOccurrence &&
                                                        MaterialTextureMatches(
                                                            item.TextureName,
                                                            material.TextureName))
                                                .FirstOrDefault();

                                        materialOverride ??=
                                            definition.MaterialOverrides
                                                .Where(
                                                    item =>
                                                        item.MeshOrdinal ==
                                                            meshOrdinal &&
                                                        item.MaterialIndex ==
                                                            materialIndex &&
                                                        MaterialTextureMatches(
                                                            item.TextureName,
                                                            material.TextureName))
                                                .FirstOrDefault();

                                        string? transMapTexturePath =
                                            null;

                                        if (materialOverride?.TransMapSource is
                                                { Length: > 0 } transMapSource &&
                                            !transMapSource.StartsWith(
                                                "\\",
                                                StringComparison.Ordinal) &&
                                            OmsiTextureAssetPathResolver
                                                .TryResolveSceneryTexture(
                                                    contentRoot.RootPath,
                                                    dependency.ResolvedPath,
                                                    meshPath,
                                                    transMapSource,
                                                    out var resolvedTransMap))
                                        {
                                            transMapTexturePath =
                                                resolvedTransMap;
                                        }

                                        return new WorldO3dMaterial(
                                            material.DiffuseR,
                                            material.DiffuseG,
                                            material.DiffuseB,
                                            material.DiffuseA,
                                            material.TextureName,
                                            texturePath,
                                            materialOverride?.AlphaMode ??
                                                (material.DiffuseA < 0.999f
                                                    ? 2
                                                    : 0),
                                            transMapTexturePath,
                                            materialOverride?.NoZWrite ??
                                                false,
                                            materialOverride?.NoZCheck ??
                                                false);
                                    })
                                .ToArray()));
                }

                result[declaredPath] =
                    new WorldSceneryAsset(
                        declaredPath,
                        dependency.ResolvedPath,
                        definition.Exists,
                        definition.UsesAbsoluteHeight,
                        definition.OnlyEditor,
                        definition.RenderType,
                        meshes.ToArray(),
                        definition.Tree is null
                            ? null
                            : new WorldSceneryTreeDefinition(
                                definition.Tree.TextureName,
                                ResolveTreeTexturePath(
                                    contentRoot.RootPath,
                                    dependency.ResolvedPath,
                                    definition.Tree.TextureName),
                                definition.Tree.MinimumHeight,
                                definition.Tree.MaximumHeight,
                                definition.Tree.MinimumAspect,
                                definition.Tree.MaximumAspect));
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException or
                OverflowException)
            {
                result[declaredPath] =
                    new WorldSceneryAsset(
                        declaredPath,
                        dependency.ResolvedPath,
                        false,
                        false,
                        false,
                        null,
                        Array.Empty<WorldSceneryMeshAsset>(),
                        null);
            }
        }

        return result;
    }

    private static int GetTextureOccurrenceIndex(
        IReadOnlyList<OmsiO3dMaterial> materials,
        int materialIndex)
    {
        if (materialIndex <= 0 ||
            materialIndex >= materials.Count)
        {
            return 0;
        }

        var textureName =
            Path.GetFileName(
                materials[materialIndex]
                    .TextureName);

        var occurrence =
            0;

        for (var index = 0;
             index < materialIndex;
             index++)
        {
            if (string.Equals(
                    Path.GetFileName(
                        materials[index]
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

    private static WorldSceneryMeshAsset CreateMissingMesh(
        string declaredPath,
        OmsiSceneryMeshTransform transform,
        string errorCode) =>
        new(
            declaredPath,
            null,
            false,
            errorCode,
            ConvertTransform(transform),
            Array.Empty<float>(),
            Array.Empty<float>(),
            Array.Empty<float>(),
            Array.Empty<uint>(),
            Array.Empty<ushort>(),
            Array.Empty<WorldO3dMaterial>());

    private static string? ResolveTreeTexturePath(
        string contentRoot,
        string sceneryObjectPath,
        string textureName)
    {
        return OmsiTextureAssetPathResolver
            .TryResolveSceneryObjectTexture(
                contentRoot,
                sceneryObjectPath,
                textureName,
                out var resolved)
            ? resolved
            : null;
    }

    private static WorldSceneryMeshTransform ConvertTransform(
        OmsiSceneryMeshTransform transform) =>
        new(
            transform.PositionX,
            transform.PositionY,
            transform.PositionZ,
            transform.RotationX,
            transform.RotationY,
            transform.RotationZ,
            transform.ScaleX,
            transform.ScaleY,
            transform.ScaleZ);

    private static string? ResolveMeshPath(
        string contentRoot,
        string sceneryObjectPath,
        string declaredMeshPath)
    {
        if (string.IsNullOrWhiteSpace(
                declaredMeshPath))
        {
            return null;
        }

        var normalized =
            declaredMeshPath
                .Trim()
                .Trim('"')
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            return null;
        }

        var sceneryDirectory =
            Path.GetDirectoryName(
                sceneryObjectPath);

        if (string.IsNullOrWhiteSpace(
                sceneryDirectory))
        {
            return null;
        }

        var candidates =
            new List<string>
            {
                Path.Combine(
                    sceneryDirectory,
                    normalized)
            };

        if (!normalized.StartsWith(
                "model" +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(
                Path.Combine(
                    sceneryDirectory,
                    "model",
                    normalized));
        }

        if (normalized.StartsWith(
                "Sceneryobjects" +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(
                Path.Combine(
                    contentRoot,
                    normalized));
        }

        var root =
            EnsureTrailingSeparator(
                Path.GetFullPath(
                    contentRoot));

        foreach (var candidate in candidates)
        {
            try
            {
                var fullPath =
                    Path.GetFullPath(
                        candidate);

                if (!fullPath.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
            }
        }

        return null;
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

    private static (WorldTerrainData? Terrain, string? ErrorCode)
        LoadTerrain(string? terrainPath)
    {
        if (terrainPath is null)
        {
            return (null, null);
        }

        try
        {
            var source = OmsiTerrainReader.ReadFile(terrainPath);

            if (source.Heights.Count == 0)
            {
                return (null, "emptyTerrain");
            }

            return (
                new WorldTerrainData(
                    source.CellCount,
                    source.Heights,
                    source.Heights.Min(),
                    source.Heights.Max()),
                null);
        }
        catch (InvalidDataException)
        {
            return (null, "invalidTerrain");
        }
        catch (IOException)
        {
            return (null, "terrainIoError");
        }
        catch (UnauthorizedAccessException)
        {
            return (null, "terrainAccessDenied");
        }
    }

    private static WorldVector3 ToWorldVector(OmsiSourceVector3 source)
    {
        // OMSI placement files use X/Y on the ground plane and Z as height.
        // The renderer uses X/Z on the ground plane and Y as height.
        return new WorldVector3(
            source.X,
            source.Z,
            source.Y);
    }

    private static WorldAssetKind Classify(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".sco" => WorldAssetKind.SceneryObject,
            ".sli" => WorldAssetKind.Spline,
            ".o3d" => WorldAssetKind.Mesh,
            ".dds" or ".bmp" or ".png" or ".tga" or ".jpg" or ".jpeg" => WorldAssetKind.Texture,
            ".wav" => WorldAssetKind.Sound,
            _ => WorldAssetKind.Unknown
        };
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('/', Path.DirectorySeparatorChar);
    }

    private sealed class AssetKeyComparer :
        IEqualityComparer<(WorldAssetKind Kind, string SourcePath)>
    {
        public static AssetKeyComparer Instance { get; } = new();

        public bool Equals(
            (WorldAssetKind Kind, string SourcePath) x,
            (WorldAssetKind Kind, string SourcePath) y)
        {
            return x.Kind == y.Kind &&
                   string.Equals(
                       x.SourcePath,
                       y.SourcePath,
                       StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(
            (WorldAssetKind Kind, string SourcePath) obj)
        {
            return HashCode.Combine(
                obj.Kind,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourcePath));
        }
    }
}
