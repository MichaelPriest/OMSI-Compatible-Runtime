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
        IProgress<WorldLoadProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(map);

        progress?.Report(
            new WorldLoadProgress(
                8,
                "Lendo mapa",
                "Descobrindo tiles e estrutura do mundo..."));

        var sourceTiles = MapTileDiscovery.Discover(map);
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
                companions.TerrainTexturePaths);

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
                90,
                "Montando mundo",
                $"Cenário: {sceneryAssets.Values.Count(static asset => asset.IsRenderable):N0} assets renderizáveis · finalizando modelo x64..."));

        return new WorldDefinition(
            map.FolderName,
            map.DirectoryPath,
            tiles.ToArray(),
            assets,
            allObjects,
            allSplines,
            splineAssets,
            sceneryAssets,
            dependencies,
            tiles.Sum(static tile => tile.PlacementParseIssueCount),
            tiles.Count(static tile => tile.TerrainErrorCode is not null),
            bounds);
    }

    private static IReadOnlyDictionary<string, WorldSplineAsset>
        LoadSplineAssets(
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
                    Array.Empty<WorldSplineSurface>());
                continue;
            }

            try
            {
                var definition = OmsiSplineDefinitionReader.ReadFile(
                    dependency.ResolvedPath);

                var surfaces = definition.Surfaces
                    .Select(static surface => new WorldSplineSurface(
                        surface.TextureIndex,
                        surface.TextureName,
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
                            surface.To.TextureScale)))
                    .ToArray();

                result[declaredPath] = new WorldSplineAsset(
                    declaredPath,
                    dependency.ResolvedPath,
                    definition.Exists,
                    surfaces);
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
                    Array.Empty<WorldSplineSurface>());
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

                foreach (var mesh in definition.Meshes)
                {
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
                            geometry.Indices,
                            geometry.TriangleMaterialIndices,
                            geometry.Materials
                                .Select(
                                    static material =>
                                        new WorldO3dMaterial(
                                            material.DiffuseR,
                                            material.DiffuseG,
                                            material.DiffuseB,
                                            material.DiffuseA,
                                            material.TextureName))
                                .ToArray()));
                }

                result[declaredPath] =
                    new WorldSceneryAsset(
                        declaredPath,
                        dependency.ResolvedPath,
                        definition.Exists,
                        definition.UsesAbsoluteHeight,
                        definition.RenderType,
                        meshes.ToArray(),
                        definition.Tree is null
                            ? null
                            : new WorldSceneryTreeDefinition(
                                definition.Tree.TextureName,
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
                        null,
                        Array.Empty<WorldSceneryMeshAsset>(),
                        null);
            }
        }

        return result;
    }

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
            Array.Empty<uint>(),
            Array.Empty<ushort>(),
            Array.Empty<WorldO3dMaterial>());

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
