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
        var version = ReadVersion(document);

        var objects = new List<OmsiObjectPlacement>();
        var splines = new List<OmsiSplinePlacement>();
        var issues = new List<OmsiPlacementParseIssue>();

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals(
                    "object",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseObject(
                        section,
                        out var placement,
                        out var error) &&
                    placement is not null)
                {
                    objects.Add(placement);
                }
                else if (error is not null)
                {
                    issues.Add(error);
                }

                continue;
            }

            if (!IsSplineSection(section.Name))
            {
                continue;
            }

            if (TryParseSpline(
                    section,
                    version,
                    out var spline,
                    out var splineError) &&
                spline is not null)
            {
                splines.Add(spline);
            }
            else if (splineError is not null)
            {
                issues.Add(splineError);
            }
        }

        return new OmsiTilePlacements(
            objects.ToArray(),
            splines.ToArray(),
            issues.ToArray());
    }

    private static int ReadVersion(OmsiSectionDocument document)
    {
        var section = document.Sections.FirstOrDefault(
            static section => section.Name.Equals(
                "version",
                StringComparison.OrdinalIgnoreCase));

        var value = section is null
            ? null
            : GetValues(section)
                .FirstOrDefault()
                ?.Value;

        return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var version) &&
            version > 0
                ? version
                : 14;
    }

    private static bool IsSplineSection(string name)
    {
        return name.Equals(
                   "spline",
                   StringComparison.OrdinalIgnoreCase) ||
               name.Equals(
                   "spline_h",
                   StringComparison.OrdinalIgnoreCase) ||
               name.Equals(
                   "splineAbschnitt",
                   StringComparison.OrdinalIgnoreCase);
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
            issue = Issue(
                section,
                $"Expected at least 9 object values but found {values.Count}.");
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
            issue = Issue(
                section,
                "Object placement contains an invalid numeric value.");
            return false;
        }

        var assetPath = NormalizeAssetPath(values[1].Value);
        if (assetPath.Length == 0)
        {
            issue = Issue(
                section,
                "Object placement has an empty scenery-object path.");
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
        int version,
        out OmsiSplinePlacement? placement,
        out OmsiPlacementParseIssue? issue)
    {
        placement = null;
        issue = null;

        var values = GetValues(section);
        var layout = SplineLayout.Create(version);

        if (values.Count < layout.MinimumValueCount)
        {
            issue = Issue(
                section,
                $"Spline v{version} expected at least {layout.MinimumValueCount} values but found {values.Count}.");
            return false;
        }

        if (!TryLong(values[layout.IdIndex].Value, out var id) ||
            !TryLong(
                values[layout.PreviousIndex].Value,
                out var previousId))
        {
            issue = Issue(
                section,
                "Spline placement contains an invalid ID.");
            return false;
        }

        var nextId = -1L;
        if (layout.NextIndex is int nextIndex &&
            !TryLong(values[nextIndex].Value, out nextId))
        {
            issue = Issue(
                section,
                "Spline placement contains an invalid next-spline ID.");
            return false;
        }

        if (!TryDouble(values[layout.XIndex].Value, out var x) ||
            !TryDouble(values[layout.ZIndex].Value, out var z) ||
            !TryDouble(values[layout.YIndex].Value, out var y) ||
            !TryDouble(
                values[layout.RotationIndex].Value,
                out var heading) ||
            !TryDouble(
                values[layout.LengthIndex].Value,
                out var length) ||
            !TryDouble(
                values[layout.RadiusIndex].Value,
                out var radius) ||
            !TryDouble(
                values[layout.GradientStartIndex].Value,
                out var gradientStart) ||
            !TryDouble(
                values[layout.GradientEndIndex].Value,
                out var gradientEnd))
        {
            issue = Issue(
                section,
                "Spline placement contains an invalid numeric value.");
            return false;
        }

        var assetPath = NormalizeAssetPath(
            values[layout.PathIndex].Value);

        if (assetPath.Length == 0)
        {
            issue = Issue(
                section,
                "Spline placement has an empty spline path.");
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
            section.Name.Equals(
                "spline_h",
                StringComparison.OrdinalIgnoreCase),
            section.HeaderLineNumber);

        return true;
    }

    private static IReadOnlyList<OmsiSectionLine> GetValues(
        OmsiSection section)
    {
        return section.Lines
            .Where(static line =>
            {
                var value = line.Value.Trim();
                return value.Length > 0 &&
                       !value.StartsWith(
                           '#');
            })
            .ToArray();
    }

    private static OmsiPlacementParseIssue Issue(
        OmsiSection section,
        string message)
    {
        return new OmsiPlacementParseIssue(
            section.Name,
            section.HeaderLineNumber,
            message);
    }

    private static bool TryDouble(
        string value,
        out double result)
    {
        return double.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
            double.IsFinite(result);
    }

    private static bool TryLong(
        string value,
        out long result)
    {
        return long.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static string NormalizeAssetPath(string value)
    {
        return value
            .Trim()
            .Trim('"')
            .Replace('/', '\\\\');
    }

    private sealed record SplineLayout(
        int PathIndex,
        int IdIndex,
        int PreviousIndex,
        int? NextIndex,
        int XIndex,
        int ZIndex,
        int YIndex,
        int RotationIndex,
        int LengthIndex,
        int RadiusIndex,
        int GradientStartIndex,
        int GradientEndIndex)
    {
        public int MinimumValueCount =>
            GradientEndIndex + 1;

        public static SplineLayout Create(int version)
        {
            var hasComplexity = version >= 9;
            var pathIndex = hasComplexity ? 1 : 0;
            var idIndex = pathIndex + 1;
            var previousIndex = idIndex + 1;

            int? nextIndex =
                version >= 11
                    ? previousIndex + 1
                    : null;

            var xIndex =
                (nextIndex ?? previousIndex) + 1;

            return new SplineLayout(
                PathIndex: pathIndex,
                IdIndex: idIndex,
                PreviousIndex: previousIndex,
                NextIndex: nextIndex,
                XIndex: xIndex,
                ZIndex: xIndex + 1,
                YIndex: xIndex + 2,
                RotationIndex: xIndex + 3,
                LengthIndex: xIndex + 4,
                RadiusIndex: xIndex + 5,
                GradientStartIndex: xIndex + 6,
                GradientEndIndex: xIndex + 7);
        }
    }
}
