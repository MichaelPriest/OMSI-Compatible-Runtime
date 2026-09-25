using System.Globalization;
using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldRailSignalObjectReference(
    long ObjectId,
    int ElementIndex,
    int SourceLineNumber);

public sealed record WorldRailSignalRoute(
    int RouteIndex,
    IReadOnlyList<int> SegmentIndices,
    int ParsedEntryCount,
    int UnresolvedEntryCount,
    WorldRailSignalObjectReference? Signal = null);

public static class WorldRailSignalRouteResolver
{
    public static IReadOnlyList<WorldRailSignalRoute> Resolve(
        WorldTrafficPathNetwork network,
        OmsiSignalRoutesFile? signalRoutes)
    {
        ArgumentNullException.ThrowIfNull(
            network);

        if (signalRoutes is null ||
            signalRoutes.Sections.Count ==
                0)
        {
            return Array.Empty<
                WorldRailSignalRoute>();
        }

        var result =
            new List<WorldRailSignalRoute>();

        foreach (var routeGroup in
                 signalRoutes
                     .Sections
                     .Where(
                         static section =>
                             section.RouteIndex.HasValue)
                     .GroupBy(
                         static section =>
                             section.RouteIndex!.Value)
                     .OrderBy(
                         static group =>
                             group.Key))
        {
            var segmentIndices =
                new List<int>();

            var parsedEntries =
                0;
            var unresolvedEntries =
                0;

            foreach (var section in
                     routeGroup.Where(
                         static section =>
                             section.Name.Equals(
                                 "entry",
                                 StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryReadEntryReference(
                        section,
                        out var sourceId,
                        out var localPathIndex))
                {
                    continue;
                }

                parsedEntries++;

                var matches =
                    network
                        .Segments
                        .Where(
                            segment =>
                                segment.Type ==
                                    2 &&
                                segment.LocalPathIndex ==
                                    localPathIndex &&
                                (segment.SplineId ==
                                     sourceId ||
                                 segment.SceneryObjectId ==
                                     sourceId))
                        .Select(
                            static segment =>
                                segment.Index)
                        .Distinct()
                        .ToArray();

                if (matches.Length !=
                    1)
                {
                    unresolvedEntries++;
                    continue;
                }

                if (!segmentIndices.Contains(
                        matches[0]))
                {
                    segmentIndices.Add(
                        matches[0]);
                }
            }

            var signal =
                routeGroup
                    .Where(
                        static section =>
                            section.Name.Equals(
                                "signal",
                                StringComparison.OrdinalIgnoreCase))
                    .Select(
                        section =>
                            TryReadSignalReference(
                                section,
                                out var signalReference)
                                ? signalReference
                                : null)
                    .FirstOrDefault(
                        static reference =>
                            reference is not null);

            result.Add(
                new WorldRailSignalRoute(
                    routeGroup.Key,
                    segmentIndices,
                    parsedEntries,
                    unresolvedEntries,
                    signal));
        }

        return result;
    }

    private static bool TryReadSignalReference(
        OmsiSignalRouteSection section,
        out WorldRailSignalObjectReference? signal)
    {
        signal =
            null;

        var values =
            section
                .Lines
                .Select(
                    static line =>
                        line.Trim())
                .Where(
                    static line =>
                        line.Length >
                        0)
                .ToArray();

        if (values.Length <
                2 ||
            !long.TryParse(
                values[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var objectId) ||
            !int.TryParse(
                values[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var elementIndex) ||
            objectId <
                0 ||
            elementIndex <
                0)
        {
            return false;
        }

        signal =
            new WorldRailSignalObjectReference(
                objectId,
                elementIndex,
                section.HeaderLineNumber);

        return true;
    }

    private static bool TryReadEntryReference(
        OmsiSignalRouteSection section,
        out long sourceId,
        out int localPathIndex)
    {
        sourceId =
            default;
        localPathIndex =
            default;

        var values =
            section
                .Lines
                .Select(
                    static line =>
                        line.Trim())
                .Where(
                    static line =>
                        line.Length >
                        0)
                .ToArray();

        // Real OMSI signalroutes.cfg files emit four values for [entry].
        // The leading pair matches the persistent spline/scenery source ID
        // and its local path index used by the map traffic-path records.
        // The trailing pair remains preserved by OmsiSignalRoutesReader and
        // is deliberately not interpreted here.
        return values.Length >=
                   4 &&
               long.TryParse(
                   values[0],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out sourceId) &&
               int.TryParse(
                   values[1],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out localPathIndex) &&
               sourceId >=
                   0 &&
               localPathIndex >=
                   0;
    }
}
