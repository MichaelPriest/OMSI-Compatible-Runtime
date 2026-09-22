using OmsiCompat.Map;

namespace OMSICompatible.World;

public static class WorldLoader
{
    public static WorldDefinition Load(OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var sourceTiles = MapTileDiscovery.Discover(map);
        var tiles = new List<WorldTile>(sourceTiles.Count);

        foreach (var sourceTile in sourceTiles)
        {
            var summary = MapTileProbe.ReadSummary(sourceTile);
            var assetReferences = AssetReferenceScanner.Scan(sourceTile)
                .Select(static reference => new WorldAssetReference(
                    Classify(reference.Extension),
                    NormalizePath(reference.RawPath),
                    reference.SectionName,
                    reference.LineNumber))
                .ToArray();

            tiles.Add(new WorldTile(
                new WorldTileCoordinate(sourceTile.Coordinate.X, sourceTile.Coordinate.Y),
                sourceTile.FilePath,
                sourceTile.Bytes,
                summary.SectionCount,
                summary.SectionCounts,
                assetReferences));
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

        return new WorldDefinition(
            map.FolderName,
            map.DirectoryPath,
            tiles.ToArray(),
            assets,
            bounds);
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

    private sealed class AssetKeyComparer : IEqualityComparer<(WorldAssetKind Kind, string SourcePath)>
    {
        public static AssetKeyComparer Instance { get; } = new();

        public bool Equals(
            (WorldAssetKind Kind, string SourcePath) x,
            (WorldAssetKind Kind, string SourcePath) y)
        {
            return x.Kind == y.Kind &&
                   string.Equals(x.SourcePath, y.SourcePath, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((WorldAssetKind Kind, string SourcePath) obj)
        {
            return HashCode.Combine(
                obj.Kind,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourcePath));
        }
    }
}
