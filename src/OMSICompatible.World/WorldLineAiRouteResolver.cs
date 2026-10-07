using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldLineAiRouteEntry(
    int TimetableEntryIndex,
    OmsiTimetableTrackEntry TimetableEntry,
    int? SegmentIndex,
    bool Resolved,
    bool Ambiguous);

public sealed record WorldLineAiRoute(
    string TrackName,
    IReadOnlyList<int> SegmentIndices,
    IReadOnlyList<WorldLineAiRouteEntry> Entries,
    int UnresolvedEntryCount,
    int AmbiguousEntryCount)
{
    public bool FullyResolved =>
        Entries.Count > 0 &&
        UnresolvedEntryCount == 0 &&
        AmbiguousEntryCount == 0;
}

/// <summary>
/// Resolves ordered OMSI .ttr track entries against the exact road-path
/// segments created from the map. Resolution fails closed: when an entry
/// cannot be identified unambiguously from object/spline id, local path id
/// and route connectivity, no segment is guessed.
/// </summary>
public static class WorldLineAiRouteResolver
{
    public static WorldLineAiRoute Resolve(
        OmsiTimetableTrack track,
        WorldTrafficPathNetwork network)
    {
        ArgumentNullException.ThrowIfNull(
            track);

        return ResolveEntries(
            track.Name,
            track.Entries,
            network);
    }

    public static bool HasCompleteStationLinkChain(
        OmsiTimetableTrip trip,
        IReadOnlyList<OmsiTimetableStationLink> stationLinks)
    {
        ArgumentNullException.ThrowIfNull(
            trip);
        ArgumentNullException.ThrowIfNull(
            stationLinks);

        var stopIds =
            trip.Stops
                .Select(
                    static stop =>
                        stop.StopId)
                .ToArray();

        if (stopIds.Length <
                2 ||
            stopIds.Any(
                static stopId =>
                    !stopId.HasValue))
        {
            return false;
        }

        for (var index = 0;
             index <
                 stopIds.Length -
                 1;
             index++)
        {
            var from =
                stopIds[index]!.Value;
            var to =
                stopIds[index + 1]!.Value;

            if (!stationLinks.Any(
                    link =>
                        link.FromStopId ==
                            from &&
                        link.ToStopId ==
                            to &&
                        link.Entries.Count >
                            0))
            {
                return false;
            }
        }

        return true;
    }

    public static WorldLineAiRoute ResolveStationLinks(
        OmsiTimetableTrip trip,
        IReadOnlyList<OmsiTimetableStationLink> stationLinks,
        WorldTrafficPathNetwork network)
    {
        ArgumentNullException.ThrowIfNull(
            trip);
        ArgumentNullException.ThrowIfNull(
            stationLinks);
        ArgumentNullException.ThrowIfNull(
            network);

        if (!HasCompleteStationLinkChain(
                trip,
                stationLinks))
        {
            return new WorldLineAiRoute(
                $"StnLinks:{trip.Name}",
                Array.Empty<int>(),
                Array.Empty<WorldLineAiRouteEntry>(),
                1,
                0);
        }

        var flattened =
            new List<OmsiTimetableTrackEntry>();

        for (var stopIndex = 0;
             stopIndex <
                 trip.Stops.Count -
                 1;
             stopIndex++)
        {
            var from =
                trip.Stops[
                    stopIndex]
                    .StopId!.Value;
            var to =
                trip.Stops[
                    stopIndex +
                    1]
                    .StopId!.Value;

            var link =
                stationLinks.First(
                    candidate =>
                        candidate.FromStopId ==
                            from &&
                        candidate.ToStopId ==
                            to);

            foreach (var entry in
                     link.Entries)
            {
                if (flattened.Count >
                        0 &&
                    SamePathKey(
                        flattened[^1],
                        entry))
                {
                    // OMSI station links repeat the shared lane between
                    // consecutive stop-to-stop legs. Keep it only once.
                    continue;
                }

                flattened.Add(
                    entry);
            }
        }

        return ResolveEntries(
            $"StnLinks:{trip.Name}",
            flattened,
            network);
    }

    private static WorldLineAiRoute ResolveEntries(
        string routeName,
        IReadOnlyList<OmsiTimetableTrackEntry> timetableEntries,
        WorldTrafficPathNetwork network)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            routeName);
        ArgumentNullException.ThrowIfNull(
            timetableEntries);
        ArgumentNullException.ThrowIfNull(
            network);

        var segmentsByIndex =
            network.Segments.ToDictionary(
                static segment =>
                    segment.Index);

        var resolvedSegments =
            new List<int>(
                timetableEntries.Count);

        var entries =
            new List<WorldLineAiRouteEntry>(
                timetableEntries.Count);

        WorldTrafficPathSegment? previous =
            null;

        for (var entryIndex = 0;
             entryIndex <
                 timetableEntries.Count;
             entryIndex++)
        {
            var entry =
                timetableEntries[
                    entryIndex];

            var candidates =
                network.Segments
                    .Where(
                        segment =>
                            IsRoadVehiclePath(
                                segment) &&
                            segment.LocalPathIndex ==
                                entry.PathId &&
                            MatchesTile(
                                segment,
                                entry) &&
                            (
                                segment.SplineId ==
                                    entry.ObjectId ||
                                segment.SceneryObjectId ==
                                    entry.ObjectId
                            ))
                    .OrderBy(
                        static segment =>
                            segment.Index)
                    .ToArray();

            var selected =
                SelectCandidate(
                    previous,
                    candidates,
                    segmentsByIndex,
                    out var ambiguous);

            if (selected is null)
            {
                entries.Add(
                    new WorldLineAiRouteEntry(
                        entryIndex,
                        entry,
                        null,
                        Resolved:
                            false,
                        Ambiguous:
                            ambiguous));

                previous =
                    null;
                continue;
            }

            entries.Add(
                new WorldLineAiRouteEntry(
                    entryIndex,
                    entry,
                    selected.Index,
                    Resolved:
                        true,
                    Ambiguous:
                        false));

            if (resolvedSegments.Count ==
                    0 ||
                resolvedSegments[^1] !=
                    selected.Index)
            {
                resolvedSegments.Add(
                    selected.Index);
            }

            previous =
                selected;
        }

        return new WorldLineAiRoute(
            routeName,
            resolvedSegments.ToArray(),
            entries.ToArray(),
            entries.Count(
                static entry =>
                    !entry.Resolved),
            entries.Count(
                static entry =>
                    entry.Ambiguous));
    }

    private static bool SamePathKey(
        OmsiTimetableTrackEntry first,
        OmsiTimetableTrackEntry second) =>
        first.ObjectId ==
            second.ObjectId &&
        first.PathId ==
            second.PathId &&
        first.TileIndex ==
            second.TileIndex;

    private static bool MatchesTile(
        WorldTrafficPathSegment segment,
        OmsiTimetableTrackEntry entry)
    {
        if (!entry.TileCoordinate.HasValue)
        {
            return true;
        }

        if (!segment.TileCoordinate.HasValue)
        {
            return false;
        }

        var expected =
            entry.TileCoordinate.Value;
        var actual =
            segment.TileCoordinate.Value;

        return actual.X ==
                   expected.X &&
               actual.Y ==
                   expected.Y;
    }

    private static WorldTrafficPathSegment? SelectCandidate(
        WorldTrafficPathSegment? previous,
        IReadOnlyList<WorldTrafficPathSegment> candidates,
        IReadOnlyDictionary<int, WorldTrafficPathSegment> segmentsByIndex,
        out bool ambiguous)
    {
        ambiguous =
            false;

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (previous is null)
        {
            ambiguous =
                true;
            return null;
        }

        var connected =
            candidates
                .Where(
                    candidate =>
                        AreRouteAdjacent(
                            previous,
                            candidate,
                            segmentsByIndex))
                .ToArray();

        if (connected.Length == 1)
        {
            return connected[0];
        }

        ambiguous =
            connected.Length >
                1 ||
            candidates.Count >
                1;

        return null;
    }

    private static bool AreRouteAdjacent(
        WorldTrafficPathSegment previous,
        WorldTrafficPathSegment candidate,
        IReadOnlyDictionary<int, WorldTrafficPathSegment> segmentsByIndex)
    {
        if (previous.Index ==
            candidate.Index)
        {
            return true;
        }

        if (previous.ForwardConnections.Contains(
                candidate.Index) ||
            previous.ReverseConnections.Contains(
                candidate.Index) ||
            candidate.ForwardConnections.Contains(
                previous.Index) ||
            candidate.ReverseConnections.Contains(
                previous.Index))
        {
            return true;
        }

        // Some crossings split a single timetable entry across an internal
        // scenery path. Accept a one-hop connector only when that connector
        // itself is uniquely linked to both ordered entries.
        foreach (var connectorIndex in
                 previous.ForwardConnections.Concat(
                     previous.ReverseConnections))
        {
            if (!segmentsByIndex.TryGetValue(
                    connectorIndex,
                    out var connector) ||
                connector.Index ==
                    candidate.Index)
            {
                continue;
            }

            if (connector.ForwardConnections.Contains(
                    candidate.Index) ||
                connector.ReverseConnections.Contains(
                    candidate.Index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRoadVehiclePath(
        WorldTrafficPathSegment segment) =>
        segment.Type == 0;
}
