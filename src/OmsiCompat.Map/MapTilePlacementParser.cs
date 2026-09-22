using System.Globalization;

namespace OmsiCompat.Map;

public readonly record struct OmsiSourceVector3(double X, double Y, double Z);

public sealed record OmsiObjectPlacement(
    long Id,
    string AssetPath,
    OmsiSourceVector3 Position,
    double HeadingDegrees,
    double PitchDegrees,
    double BankDegrees,
    int SourceLineNumber);

public sealed record OmsiSplinePlacement(
    long Id,
    long PreviousId,
    long NextId,
    string AssetPath,
    OmsiSourceVector3 Position,
    double HeadingDegrees,
    double LengthMeters,
    double RadiusMeters,
    double GradientStartPercent,
    double GradientEndPercent,
    bool UsesHeightProfile,
    int SourceLineNumber);

public sealed record OmsiPlacementParseIssue(
    string SectionName,
    int SourceLineNumber,
    string Message);

public sealed record OmsiTilePlacements(
    IReadOnlyList<OmsiObjectPlacement> Objects,
    IReadOnlyList<OmsiSplinePlacement> Splines,
    IReadOnlyList<OmsiPlacementParseIssue> Issues);

public static class MapTilePlacementParser
{
    public static OmsiTilePlacements Parse(OmsiMapTileInfo tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var document = OmsiSectionDocument.ParseFile(tile.FilePath);
        var objects = new List<OmsiObjectPlacement>();
        var splines = new List<OmsiSplinePlacement>();
        var issues = new List<OmsiPlacementParseIssue>();

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseObject(section, out var placement, out var error) && placement is not null)
                {
                    objects.Add(placement);
                }
                else if (error is not null)
                {
                    issues.Add(error);
                }

                continue;
            }

            if (section.Name.Equals("spline", StringComparison.OrdinalIgnoreCase) ||
                section.Name.Equals("spline_h", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseSpline(section, out var placement, out var error) && placement is not null)
                {
                    splines.Add(placement);
                }
                else if (error is not null)
                {
                    issues.Add(error);
                }
            }
        }

        return new OmsiTilePlacements(
            objects.ToArray(),
            splines.ToArray(),
            issues.ToArray());
    }

    private static bool TryParseObject(
        OmsiSection section,
        out OmsiObjectPlacement? placement,
        out OmsiPlacementParseIssue? issue)
    {
        placement = null;
        issue = null;

        var values = GetValues(section);
        if (values.Count < 9)
        {
            issue = Issue(section, $"Expected at least 9 object values but found {values.Count}.");
            return false;
        }

        if (!TryLong(values[2].Value, out var id) ||
            !TryDouble(values[3].Value, out var x) ||
            !TryDouble(values[4].Value, out var y) ||
            !TryDouble(values[5].Value, out var z) ||
            !TryDouble(values[6].Value, out var heading) ||
            !TryDouble(values[7].Value, out var pitch) ||
            !TryDouble(values[8].Value, out var bank))
        {
            issue = Issue(section, "Object placement contains an invalid numeric value.");
            return false;
        }

        var assetPath = NormalizeAssetPath(values[1].Value);
        if (assetPath.Length == 0)
        {
            issue = Issue(section, "Object placement has an empty scenery-object path.");
            return false;
        }

        placement = new OmsiObjectPlacement(
            id,
            assetPath,
            new OmsiSourceVector3(x, y, z),
            heading,
            pitch,
            bank,
            section.HeaderLineNumber);

        return true;
    }

    private static bool TryParseSpline(
        OmsiSection section,
        out OmsiSplinePlacement? placement,
        out OmsiPlacementParseIssue? issue)
    {
        placement = null;
        issue = null;

        var values = GetValues(section);
        if (values.Count < 13)
        {
            issue = Issue(section, $"Expected at least 13 spline values but found {values.Count}.");
            return false;
        }

        if (!TryLong(values[2].Value, out var id) ||
            !TryLong(values[3].Value, out var previousId) ||
            !TryLong(values[4].Value, out var nextId) ||
            !TryDouble(values[5].Value, out var x) ||
            !TryDouble(values[6].Value, out var z) ||
            !TryDouble(values[7].Value, out var y) ||
            !TryDouble(values[8].Value, out var heading) ||
            !TryDouble(values[9].Value, out var length) ||
            !TryDouble(values[10].Value, out var radius) ||
            !TryDouble(values[11].Value, out var gradientStart) ||
            !TryDouble(values[12].Value, out var gradientEnd))
        {
            issue = Issue(section, "Spline placement contains an invalid numeric value.");
            return false;
        }

        var assetPath = NormalizeAssetPath(values[1].Value);
        if (assetPath.Length == 0)
        {
            issue = Issue(section, "Spline placement has an empty spline path.");
            return false;
        }

        placement = new OmsiSplinePlacement(
            id,
            previousId,
            nextId,
            assetPath,
            new OmsiSourceVector3(x, y, z),
            heading,
            length,
            radius,
            gradientStart,
            gradientEnd,
            section.Name.Equals("spline_h", StringComparison.OrdinalIgnoreCase),
            section.HeaderLineNumber);

        return true;
    }

    private static IReadOnlyList<OmsiSectionLine> GetValues(OmsiSection section)
    {
        return section.Lines
            .Where(static line => !string.IsNullOrWhiteSpace(line.Value))
            .ToArray();
    }

    private static OmsiPlacementParseIssue Issue(OmsiSection section, string message)
    {
        return new OmsiPlacementParseIssue(
            section.Name,
            section.HeaderLineNumber,
            message);
    }

    private static bool TryDouble(string value, out double result)
    {
        return double.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryLong(string value, out long result)
    {
        return long.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static string NormalizeAssetPath(string value)
    {
        return value.Trim().Trim('"').Replace('/', '\\');
    }
}
