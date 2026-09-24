namespace OmsiCompat.Map;

public sealed record OmsiMapTileSummary(
    OmsiMapTileInfo Tile,
    int SectionCount,
    IReadOnlyDictionary<string, int> SectionCounts);

public static class MapTileProbe
{
    public static OmsiMapTileSummary ReadSummary(OmsiMapTileInfo tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var document = OmsiSectionDocument.ParseFile(tile.FilePath);
        var counts = document.Sections
            .GroupBy(static section => section.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        return new OmsiMapTileSummary(tile, document.Sections.Count, counts);
    }
}
