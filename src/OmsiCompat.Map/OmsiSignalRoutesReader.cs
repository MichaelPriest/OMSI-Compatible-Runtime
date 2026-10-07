using System.Globalization;
using OmsiCompat.Core;

namespace OmsiCompat.Map;

public sealed record OmsiSignalRouteSection(
    int? RouteIndex,
    string Name,
    int HeaderLineNumber,
    IReadOnlyList<string> Lines);

public sealed record OmsiSignalRoutesFile(
    string SourcePath,
    IReadOnlyList<OmsiSignalRouteSection> Sections)
{
    public static OmsiSignalRoutesFile Empty(
        string sourcePath) =>
        new(
            sourcePath,
            Array.Empty<OmsiSignalRouteSection>());
}

public static class OmsiSignalRoutesReader
{
    public static OmsiSignalRoutesFile ReadMap(
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(
            map);

        return ReadFile(
            Path.Combine(
                map.DirectoryPath,
                "signalroutes.cfg"));
    }

    public static OmsiSignalRoutesFile ReadFile(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        var fullPath =
            Path.GetFullPath(
                path);

        if (!File.Exists(
                fullPath))
        {
            return OmsiSignalRoutesFile.Empty(
                fullPath);
        }

        var input =
            OmsiText.ReadAllLines(
                fullPath);

        var sections =
            new List<OmsiSignalRouteSection>();

        int? currentRouteIndex =
            null;
        string? currentName =
            null;
        var currentHeaderLine =
            0;
        var currentLines =
            new List<string>();

        void Commit()
        {
            if (currentName is null)
            {
                return;
            }

            sections.Add(
                new OmsiSignalRouteSection(
                    currentRouteIndex,
                    currentName,
                    currentHeaderLine,
                    currentLines.ToArray()));

            currentName =
                null;
            currentHeaderLine =
                0;
            currentLines =
                [];
        }

        for (var index = 0;
             index <
                 input.Count;
             index++)
        {
            var raw =
                input[index];

            var trimmed =
                raw.Trim();

            if (TryParseRouteIndex(
                    trimmed,
                    out var routeIndex))
            {
                Commit();

                currentRouteIndex =
                    routeIndex;
                continue;
            }

            if (trimmed.Length >=
                    3 &&
                trimmed[0] ==
                    '[' &&
                trimmed[^1] ==
                    ']')
            {
                Commit();

                currentName =
                    trimmed[
                        1..
                        ^1]
                        .Trim();

                currentHeaderLine =
                    index +
                    1;

                continue;
            }

            if (currentName is not null)
            {
                currentLines.Add(
                    raw);
            }
        }

        Commit();

        return new OmsiSignalRoutesFile(
            fullPath,
            sections);
    }

    private static bool TryParseRouteIndex(
        string value,
        out int routeIndex)
    {
        routeIndex =
            default;

        if (!value.EndsWith(
                ':') ||
            value.Length <
                2)
        {
            return false;
        }

        return int.TryParse(
            value[
                ..
                ^1]
                .Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out routeIndex) &&
            routeIndex >=
                0;
    }
}
