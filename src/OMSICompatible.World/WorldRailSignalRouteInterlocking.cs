namespace OMSICompatible.World;

public sealed class WorldRailSignalRouteInterlocking
{
    private const double ConflictToleranceMeters =
        0.35;

    private readonly IReadOnlyDictionary<int, WorldRailSignalRoute>
        _routes;
    private readonly IReadOnlyDictionary<int, IReadOnlySet<int>>
        _conflicts;
    private readonly Dictionary<int, int>
        _reservations =
            [];

    public WorldRailSignalRouteInterlocking(
        WorldTrafficPathNetwork network,
        IReadOnlyList<WorldRailSignalRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(
            network);
        ArgumentNullException.ThrowIfNull(
            routes);

        _routes =
            routes
                .GroupBy(
                    static route =>
                        route.RouteIndex)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.First());

        var segmentsByIndex =
            network
                .Segments
                .ToDictionary(
                    static segment =>
                        segment.Index);

        var conflicts =
            _routes.Keys.ToDictionary(
                static routeIndex =>
                    routeIndex,
                static _ =>
                    new HashSet<int>());

        var orderedRoutes =
            _routes
                .Values
                .OrderBy(
                    static route =>
                        route.RouteIndex)
                .ToArray();

        for (var leftIndex = 0;
             leftIndex <
                 orderedRoutes.Length;
             leftIndex++)
        {
            for (var rightIndex =
                     leftIndex +
                     1;
                 rightIndex <
                     orderedRoutes.Length;
                 rightIndex++)
            {
                var left =
                    orderedRoutes[
                        leftIndex];

                var right =
                    orderedRoutes[
                        rightIndex];

                if (!RoutesConflict(
                        left,
                        right,
                        segmentsByIndex))
                {
                    continue;
                }

                conflicts[
                    left.RouteIndex]
                    .Add(
                        right.RouteIndex);

                conflicts[
                    right.RouteIndex]
                    .Add(
                        left.RouteIndex);
            }
        }

        _conflicts =
            conflicts.ToDictionary(
                static pair =>
                    pair.Key,
                static pair =>
                    (IReadOnlySet<int>)
                    pair.Value);
    }

    public IReadOnlyDictionary<int, int>
        Reservations =>
        _reservations;

    public bool IsReservedBy(
        int routeIndex,
        int agentIndex) =>
        _reservations.TryGetValue(
            routeIndex,
            out var owner) &&
        owner ==
            agentIndex;

    public bool CanReserve(
        int routeIndex,
        int agentIndex,
        IReadOnlyDictionary<int, int>? occupiedSegments =
            null)
    {
        if (!_routes.TryGetValue(
                routeIndex,
                out var route) ||
            route.SegmentIndices.Count ==
                0 ||
            route.UnresolvedEntryCount >
                0)
        {
            return false;
        }

        if (_reservations.TryGetValue(
                routeIndex,
                out var existingOwner))
        {
            return existingOwner ==
                   agentIndex;
        }

        if (occupiedSegments is not null)
        {
            foreach (var segmentIndex in
                     route.SegmentIndices)
            {
                if (occupiedSegments.TryGetValue(
                        segmentIndex,
                        out var occupant) &&
                    occupant !=
                        agentIndex)
                {
                    return false;
                }
            }
        }

        if (_conflicts.TryGetValue(
                routeIndex,
                out var conflictingRoutes))
        {
            foreach (var conflictingRoute in
                     conflictingRoutes)
            {
                if (_reservations.TryGetValue(
                        conflictingRoute,
                        out var conflictingOwner) &&
                    conflictingOwner !=
                        agentIndex)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool TryReserve(
        int routeIndex,
        int agentIndex,
        IReadOnlyDictionary<int, int>? occupiedSegments =
            null)
    {
        if (!CanReserve(
                routeIndex,
                agentIndex,
                occupiedSegments))
        {
            return false;
        }

        _reservations[
            routeIndex] =
            agentIndex;

        return true;
    }

    public void Release(
        int routeIndex,
        int agentIndex)
    {
        if (_reservations.TryGetValue(
                routeIndex,
                out var owner) &&
            owner ==
                agentIndex)
        {
            _reservations.Remove(
                routeIndex);
        }
    }

    public void ReleaseAll(
        int agentIndex)
    {
        foreach (var routeIndex in
                 _reservations
                     .Where(
                         pair =>
                             pair.Value ==
                             agentIndex)
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _reservations.Remove(
                routeIndex);
        }
    }

    private static bool RoutesConflict(
        WorldRailSignalRoute left,
        WorldRailSignalRoute right,
        IReadOnlyDictionary<int, WorldTrafficPathSegment>
            segmentsByIndex)
    {
        if (left.SegmentIndices.Intersect(
                right.SegmentIndices)
            .Any())
        {
            return true;
        }

        foreach (var leftSegmentIndex in
                 left.SegmentIndices)
        {
            if (!segmentsByIndex.TryGetValue(
                    leftSegmentIndex,
                    out var leftSegment))
            {
                continue;
            }

            foreach (var rightSegmentIndex in
                     right.SegmentIndices)
            {
                if (!segmentsByIndex.TryGetValue(
                        rightSegmentIndex,
                        out var rightSegment))
                {
                    continue;
                }

                if (SegmentsConflict(
                        leftSegment,
                        rightSegment))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsConflict(
        WorldTrafficPathSegment left,
        WorldTrafficPathSegment right)
    {
        if (left.Points.Count <
                2 ||
            right.Points.Count <
                2)
        {
            return false;
        }

        var maximumDistanceSquared =
            ConflictToleranceMeters *
            ConflictToleranceMeters;

        for (var leftIndex = 1;
             leftIndex <
                 left.Points.Count;
             leftIndex++)
        {
            var leftA =
                left.Points[
                    leftIndex -
                    1];

            var leftB =
                left.Points[
                    leftIndex];

            for (var rightIndex = 1;
                 rightIndex <
                     right.Points.Count;
                 rightIndex++)
            {
                var rightA =
                    right.Points[
                        rightIndex -
                        1];

                var rightB =
                    right.Points[
                        rightIndex];

                if (SegmentDistanceSquared(
                        leftA,
                        leftB,
                        rightA,
                        rightB) <=
                    maximumDistanceSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double SegmentDistanceSquared(
        WorldVector3 firstA,
        WorldVector3 firstB,
        WorldVector3 secondA,
        WorldVector3 secondB)
    {
        var u =
            Subtract(
                firstB,
                firstA);

        var v =
            Subtract(
                secondB,
                secondA);

        var w =
            Subtract(
                firstA,
                secondA);

        var a =
            Dot(
                u,
                u);
        var b =
            Dot(
                u,
                v);
        var c =
            Dot(
                v,
                v);
        var d =
            Dot(
                u,
                w);
        var e =
            Dot(
                v,
                w);

        var denominator =
            a *
            c -
            b *
            b;

        double sNumerator;
        double sDenominator =
            denominator;

        double tNumerator;
        double tDenominator =
            denominator;

        const double epsilon =
            0.000000001;

        if (denominator <
            epsilon)
        {
            sNumerator =
                0.0;
            sDenominator =
                1.0;
            tNumerator =
                e;
            tDenominator =
                c;
        }
        else
        {
            sNumerator =
                b *
                e -
                c *
                d;

            tNumerator =
                a *
                e -
                b *
                d;

            if (sNumerator <
                0.0)
            {
                sNumerator =
                    0.0;
                tNumerator =
                    e;
                tDenominator =
                    c;
            }
            else if (sNumerator >
                     sDenominator)
            {
                sNumerator =
                    sDenominator;
                tNumerator =
                    e +
                    b;
                tDenominator =
                    c;
            }
        }

        if (tNumerator <
            0.0)
        {
            tNumerator =
                0.0;

            if (-d <
                0.0)
            {
                sNumerator =
                    0.0;
            }
            else if (-d >
                     a)
            {
                sNumerator =
                    sDenominator;
            }
            else
            {
                sNumerator =
                    -d;
                sDenominator =
                    a;
            }
        }
        else if (tNumerator >
                 tDenominator)
        {
            tNumerator =
                tDenominator;

            if (-d +
                    b <
                0.0)
            {
                sNumerator =
                    0.0;
            }
            else if (-d +
                         b >
                     a)
            {
                sNumerator =
                    sDenominator;
            }
            else
            {
                sNumerator =
                    -d +
                    b;
                sDenominator =
                    a;
            }
        }

        var sc =
            Math.Abs(
                sNumerator) <
            epsilon
                ? 0.0
                : sNumerator /
                  sDenominator;

        var tc =
            Math.Abs(
                tNumerator) <
            epsilon
                ? 0.0
                : tNumerator /
                  tDenominator;

        var difference =
            Add(
                w,
                Subtract(
                    Scale(
                        u,
                        sc),
                    Scale(
                        v,
                        tc)));

        return Dot(
            difference,
            difference);
    }

    private static WorldVector3 Subtract(
        WorldVector3 left,
        WorldVector3 right) =>
        new(
            left.X -
                right.X,
            left.Y -
                right.Y,
            left.Z -
                right.Z);

    private static WorldVector3 Add(
        WorldVector3 left,
        WorldVector3 right) =>
        new(
            left.X +
                right.X,
            left.Y +
                right.Y,
            left.Z +
                right.Z);

    private static WorldVector3 Scale(
        WorldVector3 value,
        double scale) =>
        new(
            value.X *
                scale,
            value.Y *
                scale,
            value.Z *
                scale);

    private static double Dot(
        WorldVector3 left,
        WorldVector3 right) =>
        left.X *
            right.X +
        left.Y *
            right.Y +
        left.Z *
            right.Z;
}
