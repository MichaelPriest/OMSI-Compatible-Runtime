using System.Numerics;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Models;
using OmsiCompat.Scenery;
using OmsiCompat.Splines;
using OmsiCompat.Vehicles;

namespace OMSICompatible.World;

public static class WorldLoader
{
    private static readonly object TileCacheGate =
        new();
    private static readonly Dictionary<string, CachedWorldTile>
        TileCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private static long _tileCacheGeneration;
    private const int MaximumTileCacheEntries =
        256;

    private sealed record CachedWorldTile(
        long SourceBytes,
        DateTime SourceLastWriteUtc,
        string? TerrainPath,
        long TerrainBytes,
        DateTime TerrainLastWriteUtc,
        long LastUsedGeneration,
        WorldTile Tile);

    private static readonly object SplineCacheGate =
        new();
    private static readonly Dictionary<string, CachedSplineAsset>
        SplineCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private static long _splineCacheGeneration;
    private const int MaximumSplineCacheEntries =
        512;

    private sealed record CachedSplineAsset(
        long SourceBytes,
        DateTime SourceLastWriteUtc,
        long LastUsedGeneration,
        WorldSplineAsset Asset);

    private static readonly object SceneryCacheGate =
        new();
    private static readonly Dictionary<string, CachedSceneryAsset>
        SceneryCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private static long _sceneryCacheGeneration;
    private const int MaximumSceneryCacheEntries =
        512;

    private sealed record CachedFileStamp(
        string Path,
        long Bytes,
        DateTime LastWriteUtc);

    private sealed record CachedSceneryAsset(
        long SourceBytes,
        DateTime SourceLastWriteUtc,
        IReadOnlyList<CachedFileStamp> FileStamps,
        long LastUsedGeneration,
        WorldSceneryAsset Asset);

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
        var tileCacheHits = 0;
        var tileCacheMisses = 0;

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
            var tile =
                LoadWorldTileCached(
                    sourceTile,
                    out var cacheHit);

            tiles.Add(
                tile);

            if (cacheHit)
            {
                tileCacheHits++;
            }
            else
            {
                tileCacheMisses++;
            }
        }

        Console.WriteLine(
            $"[world-cache] tile hits={tileCacheHits}; misses={tileCacheMisses}; active={tiles.Count}; retained={GetTileCacheCount()}");

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

        progress?.Report(
            new WorldLoadProgress(
                82,
                "Preparando cenário",
                $"Lendo {allObjects.Select(static item => item.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} tipos de Sceneryobjects/O3D..."));

        var sceneryAssets = LoadSceneryAssets(
            contentRoot,
            allObjects,
            dependencies);

        progress?.Report(
            new WorldLoadProgress(
                86,
                "Conectando tráfego",
                "Integrando paths de splines e crossings/scenery..."));

        var trafficPaths =
            WorldTrafficPathNetworkBuilder.Build(
                allSplines,
                splineAssets,
                allObjects,
                sceneryAssets,
                tiles);

        var groundTextures =
            LoadGroundTextures(
                contentRoot,
                map);

        var aiCatalog =
            OmsiMapAiCatalogReader.Read(
                contentRoot,
                map);

        var signalRoutes =
            OmsiSignalRoutesReader.ReadMap(
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
            bounds,
            signalRoutes);
    }

    private static WorldTile LoadWorldTileCached(
        OmsiMapTileInfo sourceTile,
        out bool cacheHit)
    {
        var sourceLastWriteUtc =
            SafeLastWriteUtc(
                sourceTile.FilePath);

        lock (TileCacheGate)
        {
            _tileCacheGeneration++;

            if (TileCache.TryGetValue(
                    sourceTile.FilePath,
                    out var cached) &&
                cached.SourceBytes ==
                    sourceTile.Bytes &&
                cached.SourceLastWriteUtc ==
                    sourceLastWriteUtc &&
                FileStampMatches(
                    cached.TerrainPath,
                    cached.TerrainBytes,
                    cached.TerrainLastWriteUtc))
            {
                TileCache[
                    sourceTile.FilePath] =
                    cached with
                    {
                        LastUsedGeneration =
                            _tileCacheGeneration
                    };

                cacheHit =
                    true;

                return cached.Tile;
            }
        }

        var coordinate =
            new WorldTileCoordinate(
                sourceTile.Coordinate.X,
                sourceTile.Coordinate.Y);

        var summary =
            MapTileProbe.ReadSummary(
                sourceTile);

        var placements =
            MapTilePlacementParser.Parse(
                sourceTile);

        var companions =
            MapTileCompanionDiscovery.Discover(
                sourceTile);

        var assetReferences =
            AssetReferenceScanner.Scan(
                    sourceTile)
                .Select(
                    static reference =>
                        new WorldAssetReference(
                            Classify(
                                reference.Extension),
                            NormalizePath(
                                reference.RawPath),
                            reference.SectionName,
                            reference.LineNumber))
                .ToArray();

        var tileObjects =
            placements.Objects
                .Select(
                    source =>
                        new WorldObjectPlacement(
                            coordinate,
                            source.Id,
                            NormalizePath(
                                source.AssetPath),
                            ToWorldVector(
                                source.Position),
                            source.HeadingDegrees,
                            source.PitchDegrees,
                            source.BankDegrees,
                            source.ExtraValues,
                            source.SourceLineNumber,
                            ConvertTrafficRules(
                                source.TrafficRules)))
                .ToArray();

        var tileSplines =
            placements.Splines
                .Select(
                    source =>
                        new WorldSplinePlacement(
                            coordinate,
                            source.Id,
                            source.PreviousId,
                            source.NextId,
                            NormalizePath(
                                source.AssetPath),
                            ToWorldVector(
                                source.Position),
                            source.HeadingDegrees,
                            source.LengthMeters,
                            source.RadiusMeters,
                            source.GradientStartPercent,
                            source.GradientEndPercent,
                            source.UsesHeightProfile,
                            source.DeltaHeightMeters,
                            source.CantStartPercent,
                            source.CantEndPercent,
                            source.SkewStart,
                            source.SkewEnd,
                            source.Mirror,
                            source.SourceLineNumber,
                            ConvertTrafficRules(
                                source.TrafficRules),
                            source.TerrainAlignMode))
                .ToArray();

        var resources =
            new WorldTileResources(
                companions.TerrainPath,
                companions.LightmapPath,
                companions.WaterPath,
                companions.ReadyMeshPaths,
                companions.TerrainTexturePaths,
                BuildTerrainMasks(
                    sourceTile.FilePath,
                    companions.TerrainTexturePaths));

        var (terrain, terrainErrorCode) =
            LoadTerrain(
                resources.TerrainPath);

        var tile =
            new WorldTile(
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
                placements.Issues.Count);

        var (terrainBytes, terrainLastWriteUtc) =
            GetFileStamp(
                resources.TerrainPath);

        lock (TileCacheGate)
        {
            _tileCacheGeneration++;

            TileCache[
                sourceTile.FilePath] =
                new CachedWorldTile(
                    sourceTile.Bytes,
                    sourceLastWriteUtc,
                    resources.TerrainPath,
                    terrainBytes,
                    terrainLastWriteUtc,
                    _tileCacheGeneration,
                    tile);

            while (TileCache.Count >
                   MaximumTileCacheEntries)
            {
                var oldest =
                    TileCache
                        .OrderBy(
                            static pair =>
                                pair.Value
                                    .LastUsedGeneration)
                        .First();

                TileCache.Remove(
                    oldest.Key);
            }
        }

        cacheHit =
            false;

        return tile;
    }

    private static bool FileStampMatches(
        string? path,
        long expectedBytes,
        DateTime expectedLastWriteUtc)
    {
        var (bytes, lastWriteUtc) =
            GetFileStamp(
                path);

        return bytes ==
                   expectedBytes &&
               lastWriteUtc ==
                   expectedLastWriteUtc;
    }

    private static (long Bytes, DateTime LastWriteUtc)
        GetFileStamp(
            string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return (
                0,
                DateTime.MinValue);
        }

        try
        {
            var info =
                new FileInfo(
                    path);

            return (
                info.Length,
                info.LastWriteTimeUtc);
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
        {
            return (
                -1,
                DateTime.MinValue);
        }
    }

    private static DateTime SafeLastWriteUtc(
        string path) =>
        GetFileStamp(
            path)
            .LastWriteUtc;

    private static int GetTileCacheCount()
    {
        lock (TileCacheGate)
        {
            return TileCache.Count;
        }
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

        var cacheHits =
            0;

        var cacheMisses =
            0;

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

            var resolvedPath =
                dependency.ResolvedPath;

            var (sourceBytes, sourceLastWriteUtc) =
                GetFileStamp(
                    resolvedPath);

            lock (SplineCacheGate)
            {
                _splineCacheGeneration++;

                if (SplineCache.TryGetValue(
                        resolvedPath,
                        out var cached) &&
                    cached.SourceBytes ==
                        sourceBytes &&
                    cached.SourceLastWriteUtc ==
                        sourceLastWriteUtc)
                {
                    SplineCache[
                        resolvedPath] =
                        cached with
                        {
                            LastUsedGeneration =
                                _splineCacheGeneration
                        };

                    result[
                        declaredPath] =
                        cached.Asset with
                        {
                            DeclaredPath =
                                declaredPath
                        };

                    cacheHits++;

                    continue;
                }
            }

            try
            {
                var definition = OmsiSplineDefinitionReader.ReadFile(
                    resolvedPath);

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

                var asset =
                    new WorldSplineAsset(
                        declaredPath,
                        resolvedPath,
                        definition.Exists,
                        surfaces,
                        paths);

                result[
                    declaredPath] =
                    asset;

                cacheMisses++;

                lock (SplineCacheGate)
                {
                    _splineCacheGeneration++;

                    SplineCache[
                        resolvedPath] =
                        new CachedSplineAsset(
                            sourceBytes,
                            sourceLastWriteUtc,
                            _splineCacheGeneration,
                            asset);

                    while (SplineCache.Count >
                           MaximumSplineCacheEntries)
                    {
                        var oldest =
                            SplineCache
                                .OrderBy(
                                    static pair =>
                                        pair.Value
                                            .LastUsedGeneration)
                                .First();

                        SplineCache.Remove(
                            oldest.Key);
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException)
            {
                result[declaredPath] = new WorldSplineAsset(
                    declaredPath,
                    resolvedPath,
                    false,
                    Array.Empty<WorldSplineSurface>(),
                    Array.Empty<WorldSplinePath>());

                cacheMisses++;
            }
        }

        Console.WriteLine(
            $"[world-cache] spline hits={cacheHits}; misses={cacheMisses}; retained={GetSplineCacheCount()}");

        return result;
    }

    private static int GetSplineCacheCount()
    {
        lock (SplineCacheGate)
        {
            return SplineCache.Count;
        }
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

        var cacheHits =
            0;

        var cacheMisses =
            0;

        var cacheSkipped =
            0;

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
                        null,
                        Array.Empty<WorldSceneryPath>());

                continue;
            }

            var resolvedPath =
                resolvedPath;

            var (sourceBytes, sourceLastWriteUtc) =
                GetFileStamp(
                    resolvedPath);

            lock (SceneryCacheGate)
            {
                _sceneryCacheGeneration++;

                if (SceneryCache.TryGetValue(
                        resolvedPath,
                        out var cached) &&
                    IsSceneryCacheEntryValid(
                        cached,
                        sourceBytes,
                        sourceLastWriteUtc))
                {
                    SceneryCache[
                        resolvedPath] =
                        cached with
                        {
                            LastUsedGeneration =
                                _sceneryCacheGeneration
                        };

                    result[
                        declaredPath] =
                        cached.Asset with
                        {
                            DeclaredPath =
                                declaredPath
                        };

                    cacheHits++;

                    continue;
                }
            }

            try
            {
                var definition =
                    OmsiSceneryObjectReader.ReadFile(
                        resolvedPath);

                var collisionGeometry =
                    ResolveCollisionGeometry(
                        contentRoot.RootPath,
                        resolvedPath,
                        definition.CollisionMeshSource);

                var dynamicModel =
                    OmsiVehicleModelReader.ReadFile(
                        resolvedPath);

                var dynamicMeshesByOrdinal =
                    dynamicModel.Meshes
                        .ToDictionary(
                            static mesh =>
                                mesh.Ordinal);

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

                    dynamicMeshesByOrdinal.TryGetValue(
                        meshOrdinal,
                        out var dynamicMesh);
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
                            resolvedPath,
                            mesh.Path);

                    if (meshPath is null)
                    {
                        meshes.Add(
                            CreateMissingMesh(
                                mesh.Path,
                                mesh.Transform,
                                "missingO3d",
                                dynamicMesh));

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
                                                    resolvedPath,
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

                                        var requiresExternalTransMap =
                                            materialOverride?.TransMapSource is
                                                { Length: > 0 };

                                        if (materialOverride?.TransMapSource is
                                                { Length: > 0 } transMapSource)
                                        {
                                            var normalizedTransMapSource =
                                                transMapSource
                                                    .Trim()
                                                    .Trim('"')
                                                    .TrimStart(
                                                        '\\',
                                                        '/');

                                            if (normalizedTransMapSource.Length >
                                                0)
                                            {
                                                if (OmsiTextureAssetPathResolver
                                                    .TryResolveSceneryTexture(
                                                        contentRoot.RootPath,
                                                        resolvedPath,
                                                        meshPath,
                                                        normalizedTransMapSource,
                                                        out var resolvedTransMap))
                                                {
                                                    transMapTexturePath =
                                                        resolvedTransMap;
                                                }
                                                else
                                                {
                                                    // Legacy OMSI scenery often stores
                                                    // [matl_transmap] with a rooted or
                                                    // stale subdirectory while the mask
                                                    // itself lives beside the diffuse
                                                    // texture / in the object's Texture
                                                    // folder. Retry with the leaf name
                                                    // before declaring the mask missing.
                                                    var transMapLeafName =
                                                        Path.GetFileName(
                                                            normalizedTransMapSource);

                                                    if (!string.IsNullOrWhiteSpace(
                                                            transMapLeafName) &&
                                                        !transMapLeafName.Equals(
                                                            normalizedTransMapSource,
                                                            StringComparison.OrdinalIgnoreCase) &&
                                                        OmsiTextureAssetPathResolver
                                                            .TryResolveSceneryTexture(
                                                                contentRoot.RootPath,
                                                                resolvedPath,
                                                                meshPath,
                                                                transMapLeafName,
                                                                out resolvedTransMap))
                                                    {
                                                        transMapTexturePath =
                                                            resolvedTransMap;
                                                    }
                                                    else if (!string.IsNullOrWhiteSpace(
                                                                 transMapLeafName) &&
                                                             !string.IsNullOrWhiteSpace(
                                                                 texturePath))
                                                    {
                                                        var diffuseDirectory =
                                                            Path.GetDirectoryName(
                                                                texturePath);

                                                        if (!string.IsNullOrWhiteSpace(
                                                                diffuseDirectory))
                                                        {
                                                            var siblingMaskPath =
                                                                Path.Combine(
                                                                    diffuseDirectory,
                                                                    transMapLeafName);

                                                            if (File.Exists(
                                                                    siblingMaskPath))
                                                            {
                                                                transMapTexturePath =
                                                                    siblingMaskPath;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        return new WorldO3dMaterial(
                                            material.DiffuseR,
                                            material.DiffuseG,
                                            material.DiffuseB,
                                            material.DiffuseA,
                                            material.TextureName,
                                            texturePath,
                                            materialOverride?.AlphaMode ??
                                                (requiresExternalTransMap
                                                    ? 1
                                                    : material.DiffuseA < 0.999f
                                                        ? 2
                                                        : 0),
                                            transMapTexturePath,
                                            materialOverride?.NoZWrite ??
                                                false,
                                            materialOverride?.NoZCheck ??
                                                false,
                                            requiresExternalTransMap);
                                    })
                                .ToArray(),
                            dynamicMesh?.VisibilityConditions,
                            dynamicMesh?.Animations,
                            dynamicMesh?.LightEffects,
                            dynamicMesh?.MeshIdentifier,
                            dynamicMesh?.AnimationParent,
                            dynamicMesh?.Ordinal ??
                                meshOrdinal,
                            geometry.SourceTransform));
                }

                var asset =
                    new WorldSceneryAsset(
                        declaredPath,
                        resolvedPath,
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
                                    resolvedPath,
                                    definition.Tree.TextureName),
                                definition.Tree.MinimumHeight,
                                definition.Tree.MaximumHeight,
                                definition.Tree.MinimumAspect,
                                definition.Tree.MaximumAspect),
                        definition.Paths
                            .Select(
                                static path =>
                                    new WorldSceneryPath(
                                        path.X,
                                        path.Y,
                                        path.Z,
                                        path.HeadingDegrees,
                                        path.RadiusMeters,
                                        path.LengthMeters,
                                        path.GradientStart,
                                        path.GradientEnd,
                                        path.Type,
                                        path.WidthMeters,
                                        path.Direction,
                                        path.ExtraValues,
                                        path.TrafficLightIndex))
                            .ToArray(),
                        definition.TrafficLightCycleSeconds,
                        (definition.TrafficLights ??
                         Array.Empty<OmsiSceneryTrafficLightProgram>())
                            .Select(
                                static trafficLight =>
                                    new WorldTrafficLightProgram(
                                        trafficLight.Name,
                                        trafficLight.Phases
                                            .Select(
                                                static phase =>
                                                    new WorldTrafficLightPhase(
                                                        phase.Phase,
                                                        phase.DurationSeconds))
                                            .ToArray(),
                                        trafficLight.ApproachDistanceMeters))
                            .ToArray(),
                        (definition.TrafficLightJumps ??
                         Array.Empty<OmsiSceneryTrafficLightJump>())
                            .Select(
                                static jump =>
                                    new WorldTrafficLightJump(
                                        jump.CheckTrafficLightIndex,
                                        jump.TriggerTimeSeconds,
                                        jump.JumpIfNoApproach,
                                        jump.TargetTimeSeconds))
                            .ToArray(),
                        (definition.TrafficLightStops ??
                         Array.Empty<OmsiSceneryTrafficLightStop>())
                            .Select(
                                static stop =>
                                    new WorldTrafficLightStop(
                                        stop.CheckTrafficLightIndex,
                                        stop.TriggerTimeSeconds,
                                        stop.StopIfNoApproach))
                            .ToArray(),
                        definition.ScriptManifest,
                        definition.NoCollision,
                        definition.Fixed,
                        definition.Surface,
                        definition.CollisionMeshSource,
                        definition.BoundingBox,
                        collisionGeometry?.Bounds,
                        collisionGeometry?.Geometry);

                result[
                    declaredPath] =
                    asset;

                cacheMisses++;

                if (TryBuildSceneryCacheStamps(
                        asset,
                        contentRoot.RootPath,
                        out var fileStamps))
                {
                    lock (SceneryCacheGate)
                    {
                        _sceneryCacheGeneration++;

                        SceneryCache[
                            resolvedPath] =
                            new CachedSceneryAsset(
                                sourceBytes,
                                sourceLastWriteUtc,
                                fileStamps,
                                _sceneryCacheGeneration,
                                asset);

                        while (SceneryCache.Count >
                               MaximumSceneryCacheEntries)
                        {
                            var oldest =
                                SceneryCache
                                    .OrderBy(
                                        static pair =>
                                            pair.Value
                                                .LastUsedGeneration)
                                    .First();

                            SceneryCache.Remove(
                                oldest.Key);
                        }
                    }
                }
                else
                {
                    cacheSkipped++;
                }
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
                        resolvedPath,
                        false,
                        false,
                        false,
                        null,
                        Array.Empty<WorldSceneryMeshAsset>(),
                        null,
                        Array.Empty<WorldSceneryPath>());

                cacheMisses++;
            }
        }

        Console.WriteLine(
            $"[world-cache] scenery hits={cacheHits}; misses={cacheMisses}; skipped={cacheSkipped}; retained={GetSceneryCacheCount()}");

        return result;
    }

    private static bool IsSceneryCacheEntryValid(
        CachedSceneryAsset cached,
        long sourceBytes,
        DateTime sourceLastWriteUtc) =>
        cached.SourceBytes ==
            sourceBytes &&
        cached.SourceLastWriteUtc ==
            sourceLastWriteUtc &&
        cached.FileStamps.All(
            static stamp =>
                FileStampMatches(
                    stamp.Path,
                    stamp.Bytes,
                    stamp.LastWriteUtc));

    private static bool TryBuildSceneryCacheStamps(
        WorldSceneryAsset asset,
        string contentRoot,
        out CachedFileStamp[] fileStamps)
    {
        fileStamps =
            [];

        if (string.IsNullOrWhiteSpace(
                asset.ResolvedPath) ||
            asset.Meshes.Any(
                static mesh =>
                    string.IsNullOrWhiteSpace(
                        mesh.ResolvedPath)) ||
            asset.Meshes
                .SelectMany(
                    static mesh =>
                        mesh.Materials)
                .Any(
                    static material =>
                        (!string.IsNullOrWhiteSpace(
                             material.TextureName) &&
                         string.IsNullOrWhiteSpace(
                             material.TexturePath)) ||
                        (material.RequiresExternalTransMap &&
                         string.IsNullOrWhiteSpace(
                             material.TransMapTexturePath))) ||
            (asset.Tree is
                 { TextureName.Length: > 0 } tree &&
             string.IsNullOrWhiteSpace(
                 tree.TexturePath)))
        {
            return false;
        }

        var paths =
            asset.Meshes
                .Select(
                    static mesh =>
                        mesh.ResolvedPath!)
                .ToList();

        if (!string.IsNullOrWhiteSpace(
                asset.CollisionMeshSource))
        {
            var collisionPath =
                ResolveMeshPath(
                    contentRoot,
                    asset.ResolvedPath,
                    asset.CollisionMeshSource);

            if (string.IsNullOrWhiteSpace(
                    collisionPath))
            {
                return false;
            }

            paths.Add(
                collisionPath);
        }

        fileStamps =
            paths
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    static filePath =>
                    {
                        var (bytes, lastWriteUtc) =
                            GetFileStamp(
                                filePath);

                        return new CachedFileStamp(
                            filePath,
                            bytes,
                            lastWriteUtc);
                    })
                .ToArray();

        return fileStamps.All(
            static stamp =>
                stamp.Bytes >=
                    0 &&
                File.Exists(
                    stamp.Path));
    }

    private static int GetSceneryCacheCount()
    {
        lock (SceneryCacheGate)
        {
            return SceneryCache.Count;
        }
    }

    private sealed record ResolvedSceneryCollisionGeometry(
        WorldSceneryCollisionGeometry Geometry,
        WorldSceneryCollisionBounds? Bounds);

    private static ResolvedSceneryCollisionGeometry?
        ResolveCollisionGeometry(
            string contentRoot,
            string sceneryObjectPath,
            string? collisionMeshSource)
    {
        if (string.IsNullOrWhiteSpace(
                collisionMeshSource))
        {
            return null;
        }

        var meshPath =
            ResolveMeshPath(
                contentRoot,
                sceneryObjectPath,
                collisionMeshSource);

        if (meshPath is null ||
            !File.Exists(
                meshPath))
        {
            return null;
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

        if (!string.IsNullOrWhiteSpace(
                geometry.ErrorCode) ||
            geometry.Positions.Length <
                3 ||
            geometry.Indices.Length <
                3)
        {
            return null;
        }

        var transformedPositions =
            new float[
                geometry.Positions.Length];

        var minimumX =
            double.PositiveInfinity;
        var maximumX =
            double.NegativeInfinity;
        var minimumY =
            double.PositiveInfinity;
        var maximumY =
            double.NegativeInfinity;
        var minimumZ =
            double.PositiveInfinity;
        var maximumZ =
            double.NegativeInfinity;

        for (var index = 0;
             index + 2 <
                 geometry.Positions.Length;
             index +=
                 3)
        {
            var raw =
                new Vector3(
                    geometry.Positions[
                        index],
                    geometry.Positions[
                        index +
                        1],
                    geometry.Positions[
                        index +
                        2]);

            if (!float.IsFinite(
                    raw.X) ||
                !float.IsFinite(
                    raw.Y) ||
                !float.IsFinite(
                    raw.Z))
            {
                return null;
            }

            var transformed =
                Vector3.Transform(
                    raw,
                    geometry.SourceTransform);

            if (!float.IsFinite(
                    transformed.X) ||
                !float.IsFinite(
                    transformed.Y) ||
                !float.IsFinite(
                    transformed.Z))
            {
                return null;
            }

            transformedPositions[
                index] =
                transformed.X;
            transformedPositions[
                index +
                1] =
                transformed.Y;
            transformedPositions[
                index +
                2] =
                transformed.Z;

            minimumX =
                Math.Min(
                    minimumX,
                    transformed.X);
            maximumX =
                Math.Max(
                    maximumX,
                    transformed.X);
            minimumY =
                Math.Min(
                    minimumY,
                    transformed.Y);
            maximumY =
                Math.Max(
                    maximumY,
                    transformed.Y);
            minimumZ =
                Math.Min(
                    minimumZ,
                    transformed.Z);
            maximumZ =
                Math.Max(
                    maximumZ,
                    transformed.Z);
        }

        if (!double.IsFinite(
                minimumX) ||
            !double.IsFinite(
                maximumX) ||
            !double.IsFinite(
                minimumY) ||
            !double.IsFinite(
                maximumY) ||
            !double.IsFinite(
                minimumZ) ||
            !double.IsFinite(
                maximumZ))
        {
            return null;
        }

        WorldSceneryCollisionBounds?
            bounds =
                null;

        if (maximumX -
                minimumX >
                0.0001 &&
            maximumY -
                minimumY >
                0.0001 &&
            maximumZ -
                minimumZ >
                0.0001)
        {
            bounds =
                new WorldSceneryCollisionBounds(
                    minimumX,
                    maximumX,
                    minimumY,
                    maximumY,
                    minimumZ,
                    maximumZ);
        }

        return new ResolvedSceneryCollisionGeometry(
            new WorldSceneryCollisionGeometry(
                transformedPositions,
                geometry.Indices.ToArray()),
            bounds);
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
        string errorCode,
        OmsiVehicleMeshReference? dynamicMesh = null) =>
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
            Array.Empty<WorldO3dMaterial>(),
            dynamicMesh?.VisibilityConditions,
            dynamicMesh?.Animations,
            dynamicMesh?.LightEffects,
            dynamicMesh?.MeshIdentifier,
            dynamicMesh?.AnimationParent,
            dynamicMesh?.Ordinal ??
                -1);

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

    private static IReadOnlyList<WorldTrafficRule>
        ConvertTrafficRules(
            IReadOnlyList<OmsiTrafficRule>? rules) =>
        rules is null ||
        rules.Count ==
            0
            ? Array.Empty<
                WorldTrafficRule>()
            : rules
                .Select(
                    static rule =>
                        new WorldTrafficRule(
                            rule.PathIndex,
                            rule.Name,
                            rule.Value,
                            rule.GroupIndex,
                            rule.ExtraValues,
                            rule.SourceLineNumber))
                .ToArray();

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
