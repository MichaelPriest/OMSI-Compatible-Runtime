using OmsiCompat.Core;
using OmsiCompat.Map;

namespace OMSICompatible.World;

public static class WorldLoader
{
    public static WorldDefinition Load(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(map);

        var sourceTiles = MapTileDiscovery.Discover(map);
        var tiles = new List<WorldTile>(sourceTiles.Count);

        foreach (var sourceTile in sourceTiles)
        {
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

        var dependencies = WorldAssetResolver.ResolvePrimaryDependencies(
            contentRoot,
            allObjects,
            allSplines);

        return new WorldDefinition(
            map.FolderName,
            map.DirectoryPath,
            tiles.ToArray(),
            assets,
            allObjects,
            allSplines,
            dependencies,
            tiles.Sum(static tile => tile.PlacementParseIssueCount),
            tiles.Count(static tile => tile.TerrainErrorCode is not null),
            bounds);
    }

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
        return new WorldVector3(source.X, source.Y, source.Z);
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
        return path.Trim().Replace('/', '\\');
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
