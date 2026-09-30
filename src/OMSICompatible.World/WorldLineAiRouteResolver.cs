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
        ArgumentNullException.ThrowIfNull(
            network);

        var segmentsByIndex =
            network.Segments.ToDictionary(
                static segment =>
                    segment.Index);

        var resolvedSegments =
            new List<int>(
                track.Entries.Count);

        var entries =
            new List<WorldLineAiRouteEntry>(
                track.Entries.Count);

        WorldTrafficPathSegment? previous =
            null;

        for (var entryIndex = 0;
             entryIndex <
                 track.Entries.Count;
             entryIndex++)
        {
            var entry =
                track.Entries[
                    entryIndex];

            var candidates =
                network.Segments
                    .Where(
                        segment =>
                            IsRoadVehiclePath(
                                segment) &&
                            segment.LocalPathIndex ==
                                entry.PathId &&
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

                // Do not carry connectivity across an unresolved timetable
                // entry. Reusing an older segment would allow a later
                // candidate to appear unique even though the route has a gap.
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
            track.Name,
            resolvedSegments.ToArray(),
            entries.ToArray(),
            entries.Count(
                static entry =>
                    !entry.Resolved),
            entries.Count(
                static entry =>
                    entry.Ambiguous));
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
