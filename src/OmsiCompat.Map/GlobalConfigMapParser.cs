using System.Globalization;

namespace OmsiCompat.Map;

public sealed record OmsiGlobalTileDeclaration(
    OmsiTileCoordinate Coordinate,
    string FileName,
    int SourceLineNumber);

public static class GlobalConfigMapParser
{
    public static IReadOnlyList<OmsiGlobalTileDeclaration> ReadTileDeclarations(OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var document = OmsiSectionDocument.ParseFile(map.GlobalConfigPath);
        var declarations = new List<OmsiGlobalTileDeclaration>();

        foreach (var section in document.Sections)
        {
            if (!section.Name.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = section.Lines
                .Where(static line => !string.IsNullOrWhiteSpace(line.Value))
                .ToArray();

            if (values.Length < 3 ||
                !int.TryParse(
                    values[0].Value.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var x) ||
                !int.TryParse(
                    values[1].Value.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var y))
            {
                continue;
            }

            var fileName = values[2].Value.Trim().Trim('"');
            if (fileName.Length == 0)
            {
                continue;
            }

            declarations.Add(new OmsiGlobalTileDeclaration(
                new OmsiTileCoordinate(x, y),
                fileName,
                section.HeaderLineNumber));
        }

        return declarations;
    }
}
