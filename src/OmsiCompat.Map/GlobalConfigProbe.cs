namespace OmsiCompat.Map;

public sealed record GlobalConfigSummary(
    string Path,
    int LineCount,
    int SectionMarkerCount,
    long Bytes);

public static class GlobalConfigProbe
{
    public static GlobalConfigSummary ReadSummary(OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var lineCount = 0;
        var sectionMarkers = 0;

        foreach (var rawLine in File.ReadLines(map.GlobalConfigPath))
        {
            lineCount++;
            var line = rawLine.Trim();

            if (line.Length >= 3 && line[0] == '[' && line[^1] == ']')
            {
                sectionMarkers++;
            }
        }

        return new GlobalConfigSummary(
            map.GlobalConfigPath,
            lineCount,
            sectionMarkers,
            map.GlobalConfigBytes);
    }
}
