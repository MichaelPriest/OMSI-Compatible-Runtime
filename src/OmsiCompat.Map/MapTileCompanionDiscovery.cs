namespace OmsiCompat.Map;

public sealed record OmsiMapTileCompanions(
    string? TerrainPath,
    string? LightmapPath,
    string? WaterPath,
    IReadOnlyList<string> ReadyMeshPaths,
    IReadOnlyList<string> TerrainTexturePaths);

public static class MapTileCompanionDiscovery
{
    public static OmsiMapTileCompanions Discover(OmsiMapTileInfo tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var mapDirectory = Path.GetDirectoryName(tile.FilePath)
            ?? throw new InvalidOperationException("Tile file has no directory.");

        var tileFileName = Path.GetFileName(tile.FilePath);

        var terrain = Existing(tile.FilePath + ".terrain");
        var lightmap = Existing(tile.FilePath + ".LM.bmp");
        var water = Existing(tile.FilePath + ".water");

        var readyPrefix = tileFileName + ".terrain_";
        var readyMeshes = Directory
            .EnumerateFiles(mapDirectory)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.StartsWith(readyPrefix, StringComparison.OrdinalIgnoreCase) &&
                       name.EndsWith(".rdy", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var terrainTextures = Array.Empty<string>();
        var textureMapDirectory = Path.Combine(mapDirectory, "texture", "map");

        if (Directory.Exists(textureMapDirectory))
        {
            var texturePrefix = tileFileName + ".";

            terrainTextures = Directory
                .EnumerateFiles(textureMapDirectory)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.StartsWith(texturePrefix, StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return new OmsiMapTileCompanions(
            terrain,
            lightmap,
            water,
            readyMeshes,
            terrainTextures);
    }

    private static string? Existing(string path)
    {
        return File.Exists(path) ? path : null;
    }
}
