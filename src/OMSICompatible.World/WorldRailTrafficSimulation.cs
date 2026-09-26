using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldRailTrafficAgentState(
    int AgentIndex,
    int SegmentIndex,
    double DistanceMeters,
    double SpeedMetersPerSecond,
    string TrainConsistPath,
    WorldVector3 Position,
    double HeadingRadians,
    int? GroupIndex = null,
    string? GroupName = null,
    double TraveledDistanceMeters = 0.0);

public sealed record WorldRailSignalRouteState(
    int RouteIndex,
    IReadOnlyList<int> SegmentIndices,
    WorldRailSignalObjectReference? Signal,
    bool Reserved,
    int? ReservedAgentIndex);

public sealed class WorldRailTrafficSimulation
{
    private const double AccelerationMetersPerSecondSquared =
        0.75;
    private const double BrakingMetersPerSecondSquared =
        1.25;
    private const double DefaultCruiseSpeedMetersPerSecond =
        40.0 /
        3.6;

    private readonly Dictionary<int, WorldTrafficPathSegment>
        _segmentsByIndex;
    private readonly IReadOnlyDictionary<int, WorldRailSignalRoute>
        _signalRoutesByIndex;
    private readonly IReadOnlyDictionary<int, WorldRailSignalRoute[]>
        _signalRoutesByFirstSegment;
    private readonly WorldRailSignalRouteInterlocking?
        _interlocking;
    private readonly Dictionary<string, double>
        _consistTrailingDistances =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly List<Agent> _agents;

    public WorldRailTrafficSimulation(
        WorldTrafficPathNetwork network,
        OmsiMapAiCatalog aiCatalog,
        int maximumAgents = 4,
        OmsiSignalRoutesFile? signalRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(
            network);
        ArgumentNullException.ThrowIfNull(
            aiCatalog);

        _segmentsByIndex =
            network.Segments
                .Where(
                    static segment =>
                        segment.Type ==
                        2)
                .ToDictionary(
                    static segment =>
                        segment.Index);

        var resolvedSignalRoutes =
            WorldRailSignalRouteResolver.Resolve(
                    network,
                    signalRoutes)
                .Where(
                    static route =>
                        route.SegmentIndices.Count >
                            0 &&
                        route.UnresolvedEntryCount ==
                            0)
                .OrderBy(
                    static route =>
                        route.RouteIndex)
                .ToArray();

        _signalRoutesByIndex =
            resolvedSignalRoutes
                .ToDictionary(
                    static route =>
                        route.RouteIndex);

        _signalRoutesByFirstSegment =
            resolvedSignalRoutes
                .GroupBy(
                    static route =>
                        route.SegmentIndices[0])
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.ToArray());

        _interlocking =
            resolvedSignalRoutes.Length >
                    0
                ? new WorldRailSignalRouteInterlocking(
                    network,
                    resolvedSignalRoutes)
                : null;

        if (_segmentsByIndex.Count ==
                0 ||
            maximumAgents <=
                0)
        {
            _agents =
                [];
            return;
        }

        var groupDefinitions =
            (aiCatalog.UnscheduledVehicleGroups ??
             Array.Empty<OmsiUnscheduledVehicleGroup>())
                .GroupBy(
                    static group =>
                        group.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.First(),
                    StringComparer.OrdinalIgnoreCase);

        var consists =
            aiCatalog.MovingVehicles
                .Where(
                    static vehicle =>
                        vehicle.Exists &&
                        Path.GetExtension(
                                vehicle.DeclaredPath)
                            .Equals(
                                ".zug",
                                StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    static vehicle =>
                        vehicle.GroupName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static vehicle =>
                        vehicle.DeclaredPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (consists.Length ==
            0)
        {
            _agents =
                [];
            return;
        }

        var railSegments =
            _segmentsByIndex
                .Values
                .Where(
                    static segment =>
                        segment.Points.Count >=
                            2 &&
                        (segment.AllowsForward ||
                         segment.AllowsReverse))
                .OrderBy(
                    static segment =>
                        segment.Index)
                .ToArray();

        if (railSegments.Length ==
            0)
        {
            _agents =
                [];
            return;
        }

        var count =
            Math.Min(
                maximumAgents,
                Math.Min(
                    consists.Length,
                    railSegments.Length));

        _agents =
            new List<Agent>(
                count);

        for (var index = 0;
             index <
                 count;
             index++)
        {
            var consist =
                SelectVehicle(
                    consists,
                    index);

            var groupDefinition =
                groupDefinitions.TryGetValue(
                    consist.GroupName,
                    out var resolvedGroupDefinition)
                    ? resolvedGroupDefinition
                    : null;

            var groupIndex =
                groupDefinition?.Index;

            var defaultDensityClassIndex =
                groupDefinition?.DefaultDensityClassIndex;

            var allowedSegments =
                railSegments
                    .Where(
                        segment =>
                            IsTrafficGroupAllowed(
                                segment,
                                groupIndex,
                                defaultDensityClassIndex))
                    .ToArray();

            if (allowedSegments.Length ==
                0)
            {
                continue;
            }

            var segment =
                allowedSegments[
                    index %
                    allowedSegments.Length];

            var travelForward =
                segment.Direction switch
                {
                    1 => false,
                    2 => index % 2 == 0,
                    _ => true
                };

            var length =
                SegmentLength(
                    segment);

            var distance =
                travelForward
                    ? 0.0
                    : length;

            _agents.Add(
                new Agent(
                    index,
                    segment.Index,
                    distance,
                    travelForward,
                    ResolveSegmentMaximumSpeed(
                        segment),
                    consist.ResolvedPath!,
                    groupIndex,
                    defaultDensityClassIndex,
                    consist.GroupName));
        }
    }

    public IReadOnlyList<WorldRailTrafficAgentState>
        Snapshot() =>
        _agents
            .Select(
                CreateState)
            .ToArray();

    public IReadOnlyList<WorldRailSignalRouteState>
        SignalRouteSnapshot() =>
        _signalRoutesByIndex
            .Values
            .OrderBy(
                static route =>
                    route.RouteIndex)
            .Select(
                route =>
                {
                    var owner =
                        default(int);

                    var reserved =
                        _interlocking?.Reservations.TryGetValue(
                            route.RouteIndex,
                            out owner) ==
                        true;

                    return new WorldRailSignalRouteState(
                        route.RouteIndex,
                        route.SegmentIndices,
                        route.Signal,
                        reserved,
                        reserved
                            ? owner
                            : null);
                })
            .ToArray();

    public void SetConsistTrailingDistance(
        string trainConsistPath,
        double trailingDistanceMeters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            trainConsistPath);

        if (!double.IsFinite(
                trailingDistanceMeters) ||
            trailingDistanceMeters <
                0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trailingDistanceMeters));
        }

        _consistTrailingDistances[
            trainConsistPath] =
            trailingDistanceMeters;
    }

    public bool IsSignalRouteReservedBy(
        int routeIndex,
        int agentIndex) =>
        _interlocking?.IsReservedBy(
            routeIndex,
            agentIndex) ??
        false;

    public bool TrySampleBehind(
        int agentIndex,
        double trailingDistanceMeters,
        out int segmentIndex,
        out double segmentDistanceMeters,
        out WorldVector3 position,
        out double headingRadians)
    {
        segmentIndex =
            -1;
        segmentDistanceMeters =
            0.0;
        position =
            default;
        headingRadians =
            0.0;

        if (!double.IsFinite(
                trailingDistanceMeters) ||
            trailingDistanceMeters <
                0.0)
        {
            return false;
        }

        var agent =
            _agents.FirstOrDefault(
                candidate =>
                    candidate.AgentIndex ==
                    agentIndex);

        if (agent is null ||
            !_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            return false;
        }

        var remaining =
            trailingDistanceMeters;
        var distance =
            agent.DistanceMeters;

        var historyIndex =
            agent.SegmentHistory.Count -
            1;

        if (historyIndex <
                0 ||
            agent.SegmentHistory[
                historyIndex] !=
            agent.SegmentIndex)
        {
            return false;
        }

        var guard =
            0;

        while (guard++ <
               128)
        {
            var length =
                SegmentLength(
                    segment);

            var progressedFromEntry =
                agent.TravelForward
                    ? Math.Clamp(
                        distance,
                        0.0,
                        length)
                    : Math.Clamp(
                        length -
                            distance,
                        0.0,
                        length);

            if (remaining <=
                progressedFromEntry +
                    0.000001)
            {
                segmentDistanceMeters =
                    agent.TravelForward
                        ? Math.Max(
                            distance -
                                remaining,
                            0.0)
                        : Math.Min(
                            distance +
                                remaining,
                            length);

                segmentIndex =
                    segment.Index;

                SampleSegment(
                    segment,
                    segmentDistanceMeters,
                    out position,
                    out headingRadians);

                if (!agent.TravelForward)
                {
                    headingRadians =
                        ReverseHeading(
                            headingRadians);
                }

                return true;
            }

            remaining -=
                progressedFromEntry;

            historyIndex--;

            if (historyIndex <
                    0 ||
                !_segmentsByIndex.TryGetValue(
                    agent.SegmentHistory[
                        historyIndex],
                    out var predecessor))
            {
                return false;
            }

            segment =
                predecessor;

            var predecessorLength =
                SegmentLength(
                    segment);

            distance =
                agent.TravelForward
                    ? predecessorLength
                    : 0.0;
        }

        return false;
    }

    public void Step(
        double deltaSeconds)
    {
        if (!double.IsFinite(
                deltaSeconds) ||
            deltaSeconds <=
                0.0 ||
            _agents.Count ==
                0)
        {
            return;
        }

        var remainingSeconds =
            deltaSeconds;

        while (remainingSeconds >
               0.000001)
        {
            var step =
                Math.Min(
                    remainingSeconds,
                    0.25);

            foreach (var agent in
                     _agents)
            {
                if (!_segmentsByIndex.TryGetValue(
                        agent.SegmentIndex,
                        out var segment))
                {
                    continue;
                }

                var blockedEntryDistance =
                    ResolveBlockedEntryDistance(
                        agent,
                        segment);

                var targetSpeed =
                    ResolveSegmentMaximumSpeed(
                        segment);

                if (blockedEntryDistance.HasValue)
                {
                    var brakingLimitedSpeed =
                        Math.Sqrt(
                            2.0 *
                            BrakingMetersPerSecondSquared *
                            Math.Max(
                                blockedEntryDistance.Value,
                                0.0));

                    targetSpeed =
                        Math.Min(
                            targetSpeed,
                            brakingLimitedSpeed);
                }

                UpdateAgentSpeed(
                    agent,
                    targetSpeed,
                    step);

                var remainingDistance =
                    agent.SpeedMetersPerSecond *
                    step;

                if (blockedEntryDistance.HasValue)
                {
                    remainingDistance =
                        Math.Min(
                            remainingDistance,
                            Math.Max(
                                blockedEntryDistance.Value,
                                0.0));
                }

                var guard =
                    0;

                while (remainingDistance >
                           0.0001 &&
                       guard++ <
                           32)
                {
                    if (!_segmentsByIndex.TryGetValue(
                            agent.SegmentIndex,
                            out segment))
                    {
                        break;
                    }

                    var length =
                        SegmentLength(
                            segment);

                    if (length <=
                        0.0001)
                    {
                        if (!TryAdvanceSegment(
                                agent,
                                segment))
                        {
                            break;
                        }

                        continue;
                    }

                    var available =
                        agent.TravelForward
                            ? Math.Max(
                                length -
                                agent.DistanceMeters,
                                0.0)
                            : Math.Max(
                                agent.DistanceMeters,
                                0.0);

                    if (remainingDistance <=
                        available)
                    {
                        agent.DistanceMeters +=
                            agent.TravelForward
                                ? remainingDistance
                                : -remainingDistance;

                        agent.TraveledDistanceMeters +=
                            remainingDistance;

                        remainingDistance =
                            0.0;
                        continue;
                    }

                    agent.DistanceMeters =
                        agent.TravelForward
                            ? length
                            : 0.0;

                    agent.TraveledDistanceMeters +=
                        available;

                    remainingDistance -=
                        available;

                    if (!TryAdvanceSegment(
                            agent,
                            segment))
                    {
                        break;
                    }
                }

                if (blockedEntryDistance.HasValue &&
                    _segmentsByIndex.TryGetValue(
                        agent.SegmentIndex,
                        out var stoppedSegment) &&
                    DistanceToSegmentExit(
                        agent,
                        stoppedSegment) <=
                        0.0001)
                {
                    agent.SpeedMetersPerSecond =
                        0.0;
                }

                ReleaseClearedSignalRoutes(
                    agent);
            }

            remainingSeconds -=
                step;
        }
    }

    private bool TryAdvanceSegment(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var connections =
            agent.TravelForward
                ? segment.ForwardConnections
                : segment.ReverseConnections;

        var activeSignalRoute =
            ResolveActiveSignalRoute(
                agent);

        if (activeSignalRoute is not null)
        {
            var routePosition =
                IndexOfSegment(
                    activeSignalRoute,
                    segment.Index);

            if (routePosition >=
                    0 &&
                routePosition <
                    activeSignalRoute.SegmentIndices.Count -
                    1)
            {
                var expectedSegmentIndex =
                    activeSignalRoute.SegmentIndices[
                        routePosition +
                        1];

                if (!connections.Contains(
                        expectedSegmentIndex) ||
                    !_segmentsByIndex.TryGetValue(
                        expectedSegmentIndex,
                        out var expectedSegment) ||
                    IsSegmentOccupiedByOtherAgent(
                        expectedSegmentIndex,
                        agent.AgentIndex) ||
                    !IsTrafficGroupAllowed(
                        expectedSegment,
                        agent.GroupIndex,
                        agent.DefaultDensityClassIndex) ||
                    !(agent.TravelForward
                        ? expectedSegment.AllowsForward
                        : expectedSegment.AllowsReverse))
                {
                    agent.SpeedMetersPerSecond =
                        0.0;
                    return false;
                }

                MoveAgentToSegment(
                    agent,
                    expectedSegment);

                return true;
            }

            if (routePosition <
                0)
            {
                MarkSignalRouteForTailClearance(
                    agent,
                    activeSignalRoute.RouteIndex);

                agent.ReservedSignalRouteIndex =
                    null;

                activeSignalRoute =
                    null;
            }
        }

        var next =
            SelectNextAvailableSegment(
                agent,
                connections);

        if (next is null)
        {
            agent.SpeedMetersPerSecond =
                0.0;
            return false;
        }

        WorldRailSignalRoute? nextSignalRoute =
            null;

        if (_signalRoutesByFirstSegment.TryGetValue(
                next.Index,
                out var candidateSignalRoutes))
        {
            nextSignalRoute =
                candidateSignalRoutes
                    .Where(
                        route =>
                            IsSignalRouteTraversable(
                                agent,
                                route))
                    .OrderBy(
                        static route =>
                            route.RouteIndex)
                    .FirstOrDefault();

            if (nextSignalRoute is null ||
                _interlocking is null ||
                !_interlocking.TryReserve(
                    nextSignalRoute.RouteIndex,
                    agent.AgentIndex,
                    BuildOccupiedSegments(
                        agent.AgentIndex)))
            {
                agent.SpeedMetersPerSecond =
                    0.0;
                return false;
            }
        }

        var previousSignalRouteIndex =
            agent.ReservedSignalRouteIndex;

        MoveAgentToSegment(
            agent,
            next);

        if (activeSignalRoute is not null &&
            IndexOfSegment(
                activeSignalRoute,
                next.Index) <
                0)
        {
            MarkSignalRouteForTailClearance(
                agent,
                activeSignalRoute.RouteIndex);

            if (previousSignalRouteIndex ==
                activeSignalRoute.RouteIndex)
            {
                agent.ReservedSignalRouteIndex =
                    null;
            }
        }

        if (nextSignalRoute is not null)
        {
            agent.PendingSignalRouteClearanceOrigins.Remove(
                nextSignalRoute.RouteIndex);

            agent.ReservedSignalRouteIndex =
                nextSignalRoute.RouteIndex;
        }

        return true;
    }

    private double? ResolveBlockedEntryDistance(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var connections =
            agent.TravelForward
                ? segment.ForwardConnections
                : segment.ReverseConnections;

        var activeSignalRoute =
            ResolveActiveSignalRoute(
                agent);

        if (activeSignalRoute is not null)
        {
            var routePosition =
                IndexOfSegment(
                    activeSignalRoute,
                    segment.Index);

            if (routePosition >=
                    0 &&
                routePosition <
                    activeSignalRoute.SegmentIndices.Count -
                    1)
            {
                var expectedSegmentIndex =
                    activeSignalRoute.SegmentIndices[
                        routePosition +
                        1];

                if (!connections.Contains(
                        expectedSegmentIndex) ||
                    !_segmentsByIndex.TryGetValue(
                        expectedSegmentIndex,
                        out var expectedSegment) ||
                    IsSegmentOccupiedByOtherAgent(
                        expectedSegmentIndex,
                        agent.AgentIndex) ||
                    !IsTrafficGroupAllowed(
                        expectedSegment,
                        agent.GroupIndex,
                        agent.DefaultDensityClassIndex) ||
                    !(agent.TravelForward
                        ? expectedSegment.AllowsForward
                        : expectedSegment.AllowsReverse))
                {
                    return DistanceToSegmentExit(
                        agent,
                        segment);
                }

                return null;
            }
        }

        var next =
            SelectNextAvailableSegment(
                agent,
                connections);

        if (next is null)
        {
            return DistanceToSegmentExit(
                agent,
                segment);
        }

        if (_signalRoutesByFirstSegment.TryGetValue(
                next.Index,
                out var candidateSignalRoutes))
        {
            var nextSignalRoute =
                candidateSignalRoutes
                    .Where(
                        route =>
                            IsSignalRouteTraversable(
                                agent,
                                route))
                    .OrderBy(
                        static route =>
                            route.RouteIndex)
                    .FirstOrDefault();

            if (nextSignalRoute is null ||
                _interlocking is null ||
                !_interlocking.CanReserve(
                    nextSignalRoute.RouteIndex,
                    agent.AgentIndex,
                    BuildOccupiedSegments(
                        agent.AgentIndex)))
            {
                return DistanceToSegmentExit(
                    agent,
                    segment);
            }
        }

        return null;
    }

    private WorldTrafficPathSegment? SelectNextAvailableSegment(
        Agent agent,
        IReadOnlyList<int> connections)
    {
        var candidates =
            connections
                .Select(
                    index =>
                        _segmentsByIndex.TryGetValue(
                            index,
                            out var candidate)
                            ? candidate
                            : null)
                .Where(
                    candidate =>
                        candidate is not null &&
                        !IsSegmentOccupiedByOtherAgent(
                            candidate.Index,
                            agent.AgentIndex) &&
                        IsTrafficGroupAllowed(
                            candidate,
                            agent.GroupIndex,
                            agent.DefaultDensityClassIndex) &&
                        (agent.TravelForward
                            ? candidate.AllowsForward
                            : candidate.AllowsReverse))
                .Select(
                    candidate =>
                        new
                        {
                            Segment =
                                candidate!,
                            Weight =
                                ResolveTrafficDensityWeight(
                                    candidate!,
                                    agent.GroupIndex,
                                    agent.DefaultDensityClassIndex)
                        })
                .Where(
                    static candidate =>
                        candidate.Weight >
                            0.0)
                .OrderBy(
                    static candidate =>
                        candidate.Segment.Index)
                .ToArray();

        if (candidates.Length ==
            0)
        {
            return null;
        }

        var totalWeight =
            candidates.Sum(
                static candidate =>
                    candidate.Weight);

        if (!double.IsFinite(
                totalWeight) ||
            totalWeight <=
                0.0)
        {
            return null;
        }

        var selector =
            ((agent.AgentIndex +
              1) *
             0.6180339887498949 %
             1.0) *
            totalWeight;

        foreach (var candidate in
                 candidates)
        {
            selector -=
                candidate.Weight;

            if (selector <=
                0.0)
            {
                return candidate.Segment;
            }
        }

        return candidates[^1]
            .Segment;
    }

    private static double DistanceToSegmentExit(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var segmentLength =
            SegmentLength(
                segment);

        return agent.TravelForward
            ? Math.Max(
                segmentLength -
                    agent.DistanceMeters,
                0.0)
            : Math.Max(
                agent.DistanceMeters,
                0.0);
    }

    private WorldRailSignalRoute? ResolveActiveSignalRoute(
        Agent agent)
    {
        if (!agent.ReservedSignalRouteIndex.HasValue)
        {
            return null;
        }

        return _signalRoutesByIndex.TryGetValue(
                   agent.ReservedSignalRouteIndex.Value,
                   out var route)
            ? route
            : null;
    }

    private static int IndexOfSegment(
        WorldRailSignalRoute route,
        int segmentIndex)
    {
        for (var index = 0;
             index <
                 route.SegmentIndices.Count;
             index++)
        {
            if (route.SegmentIndices[
                    index] ==
                segmentIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsSignalRouteTraversable(
        Agent agent,
        WorldRailSignalRoute route)
    {
        for (var index = 0;
             index <
                 route.SegmentIndices.Count;
             index++)
        {
            if (!_segmentsByIndex.TryGetValue(
                    route.SegmentIndices[
                        index],
                    out var segment) ||
                !IsTrafficGroupAllowed(
                    segment,
                    agent.GroupIndex,
                    agent.DefaultDensityClassIndex) ||
                !(agent.TravelForward
                    ? segment.AllowsForward
                    : segment.AllowsReverse))
            {
                return false;
            }

            if (index ==
                route.SegmentIndices.Count -
                    1)
            {
                continue;
            }

            var nextSegmentIndex =
                route.SegmentIndices[
                    index +
                    1];

            var connections =
                agent.TravelForward
                    ? segment.ForwardConnections
                    : segment.ReverseConnections;

            if (!connections.Contains(
                    nextSegmentIndex))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsSegmentOccupiedByOtherAgent(
        int segmentIndex,
        int excludedAgentIndex)
    {
        foreach (var agent in
                 _agents)
        {
            if (agent.AgentIndex ==
                excludedAgentIndex)
            {
                continue;
            }

            if (EnumerateOccupiedSegmentIndices(
                    agent)
                .Contains(
                    segmentIndex))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyDictionary<int, int>
        BuildOccupiedSegments(
            int excludedAgentIndex)
    {
        var occupied =
            new Dictionary<int, int>();

        foreach (var agent in
                 _agents
                     .Where(
                         agent =>
                             agent.AgentIndex !=
                             excludedAgentIndex)
                     .OrderBy(
                         static agent =>
                             agent.AgentIndex))
        {
            foreach (var segmentIndex in
                     EnumerateOccupiedSegmentIndices(
                         agent))
            {
                occupied.TryAdd(
                    segmentIndex,
                    agent.AgentIndex);
            }
        }

        return occupied;
    }

    private IEnumerable<int>
        EnumerateOccupiedSegmentIndices(
            Agent agent)
    {
        if (!_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            yield break;
        }

        yield return segment.Index;

        if (!_consistTrailingDistances.TryGetValue(
                agent.TrainConsistPath,
                out var trailingDistanceMeters) ||
            !double.IsFinite(
                trailingDistanceMeters) ||
            trailingDistanceMeters <=
                0.000001)
        {
            yield break;
        }

        var remaining =
            trailingDistanceMeters;

        var distance =
            agent.DistanceMeters;

        var historyIndex =
            agent.SegmentHistory.Count -
            1;

        var guard =
            0;

        while (remaining >
                   0.000001 &&
               guard++ <
                   128)
        {
            var length =
                SegmentLength(
                    segment);

            var progressedFromEntry =
                agent.TravelForward
                    ? Math.Clamp(
                        distance,
                        0.0,
                        length)
                    : Math.Clamp(
                        length -
                            distance,
                        0.0,
                        length);

            if (remaining <=
                progressedFromEntry +
                    0.000001)
            {
                yield break;
            }

            remaining -=
                progressedFromEntry;

            historyIndex--;

            if (historyIndex <
                    0 ||
                !_segmentsByIndex.TryGetValue(
                    agent.SegmentHistory[
                        historyIndex],
                    out segment))
            {
                yield break;
            }

            yield return segment.Index;

            var predecessorLength =
                SegmentLength(
                    segment);

            distance =
                agent.TravelForward
                    ? predecessorLength
                    : 0.0;
        }
    }

    private void MarkSignalRouteForTailClearance(
        Agent agent,
        int routeIndex)
    {
        if (agent.PendingSignalRouteClearanceOrigins
            .ContainsKey(
                routeIndex))
        {
            return;
        }

        agent.PendingSignalRouteClearanceOrigins[
            routeIndex] =
            agent.TraveledDistanceMeters;
    }

    private void ReleaseClearedSignalRoutes(
        Agent agent)
    {
        if (agent.PendingSignalRouteClearanceOrigins.Count ==
            0)
        {
            return;
        }

        var trailingDistanceMeters =
            _consistTrailingDistances.TryGetValue(
                agent.TrainConsistPath,
                out var configuredTrailingDistance)
                ? Math.Max(
                    configuredTrailingDistance,
                    0.0)
                : double.PositiveInfinity;

        foreach (var pair in
                 agent.PendingSignalRouteClearanceOrigins
                     .ToArray())
        {
            var traveledSinceHeadExit =
                agent.TraveledDistanceMeters -
                pair.Value;

            if (traveledSinceHeadExit +
                    0.000001 <
                trailingDistanceMeters)
            {
                continue;
            }

            _interlocking?.Release(
                pair.Key,
                agent.AgentIndex);

            agent.PendingSignalRouteClearanceOrigins.Remove(
                pair.Key);
        }
    }

    private static void MoveAgentToSegment(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        agent.SegmentHistory.Add(
            segment.Index);

        agent.SegmentIndex =
            segment.Index;

        agent.DistanceMeters =
            agent.TravelForward
                ? 0.0
                : SegmentLength(
                    segment);
    }

    private WorldRailTrafficAgentState CreateState(
        Agent agent)
    {
        if (!_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            return new WorldRailTrafficAgentState(
                agent.AgentIndex,
                agent.SegmentIndex,
                agent.DistanceMeters,
                agent.SpeedMetersPerSecond,
                agent.TrainConsistPath,
                default,
                0.0,
                agent.GroupIndex,
                agent.GroupName,
                agent.TraveledDistanceMeters);
        }

        SampleSegment(
            segment,
            agent.DistanceMeters,
            out var position,
            out var heading);

        if (!agent.TravelForward)
        {
            heading =
                ReverseHeading(
                    heading);
        }

        return new WorldRailTrafficAgentState(
            agent.AgentIndex,
            agent.SegmentIndex,
            agent.DistanceMeters,
            agent.SpeedMetersPerSecond,
            agent.TrainConsistPath,
            position,
            heading,
            agent.GroupIndex,
            agent.GroupName,
            agent.TraveledDistanceMeters);
    }

    private static void UpdateAgentSpeed(
        Agent agent,
        double targetSpeed,
        double stepSeconds)
    {
        var clampedTarget =
            Math.Max(
                targetSpeed,
                0.0);

        var rate =
            clampedTarget <
            agent.SpeedMetersPerSecond
                ? BrakingMetersPerSecondSquared
                : AccelerationMetersPerSecondSquared;

        var maximumChange =
            rate *
            stepSeconds;

        if (agent.SpeedMetersPerSecond <
            clampedTarget)
        {
            agent.SpeedMetersPerSecond =
                Math.Min(
                    clampedTarget,
                    agent.SpeedMetersPerSecond +
                        maximumChange);
        }
        else
        {
            agent.SpeedMetersPerSecond =
                Math.Max(
                    clampedTarget,
                    agent.SpeedMetersPerSecond -
                        maximumChange);
        }
    }

    private static bool IsTrafficGroupAllowed(
        WorldTrafficPathSegment segment,
        int? groupIndex,
        int? defaultDensityClassIndex)
    {
        if (!groupIndex.HasValue)
        {
            return true;
        }

        if (segment.BlockedUnscheduledGroupIndices is not null &&
            segment.BlockedUnscheduledGroupIndices.Contains(
                groupIndex.Value))
        {
            return false;
        }

        if (segment.TrafficDensityWeights is not null &&
            segment.TrafficDensityWeights.TryGetValue(
                groupIndex.Value,
                out var explicitWeight) &&
            double.IsFinite(
                explicitWeight))
        {
            return explicitWeight >
                   0.0;
        }

        return defaultDensityClassIndex !=
               0;
    }

    private static double ResolveTrafficDensityWeight(
        WorldTrafficPathSegment segment,
        int? groupIndex,
        int? defaultDensityClassIndex)
    {
        if (!groupIndex.HasValue)
        {
            return 1.0;
        }

        if (segment.TrafficDensityWeights is not null &&
            segment.TrafficDensityWeights.TryGetValue(
                groupIndex.Value,
                out var value) &&
            double.IsFinite(
                value))
        {
            return Math.Max(
                value,
                0.0);
        }

        return defaultDensityClassIndex ==
               0
            ? 0.0
            : 1.0;
    }

    private static double ResolveSegmentMaximumSpeed(
        WorldTrafficPathSegment segment)
    {
        if (!segment.SpeedLimitKilometersPerHour.HasValue ||
            !double.IsFinite(
                segment.SpeedLimitKilometersPerHour.Value) ||
            segment.SpeedLimitKilometersPerHour.Value <=
                0.0)
        {
            return DefaultCruiseSpeedMetersPerSecond;
        }

        return segment.SpeedLimitKilometersPerHour.Value /
               3.6;
    }

    private static OmsiAiVehicleDefinition SelectVehicle(
        IReadOnlyList<OmsiAiVehicleDefinition> vehicles,
        int index)
    {
        var totalWeight =
            vehicles.Sum(
                static vehicle =>
                    Math.Max(
                        vehicle.Weight,
                        0.0001));

        var selector =
            ((index *
              0.6180339887498949) %
             1.0) *
            totalWeight;

        foreach (var vehicle in
                 vehicles)
        {
            selector -=
                Math.Max(
                    vehicle.Weight,
                    0.0001);

            if (selector <=
                0.0)
            {
                return vehicle;
            }
        }

        return vehicles[^1];
    }

    private static double SegmentLength(
        WorldTrafficPathSegment segment)
    {
        var total =
            0.0;

        for (var index = 1;
             index <
                 segment.Points.Count;
             index++)
        {
            total +=
                Distance(
                    segment.Points[
                        index - 1],
                    segment.Points[
                        index]);
        }

        return total;
    }

    private static void SampleSegment(
        WorldTrafficPathSegment segment,
        double distanceMeters,
        out WorldVector3 position,
        out double headingRadians)
    {
        if (segment.Points.Count ==
            0)
        {
            position =
                default;
            headingRadians =
                0.0;
            return;
        }

        if (segment.Points.Count ==
            1)
        {
            position =
                segment.Points[0];
            headingRadians =
                0.0;
            return;
        }

        var remaining =
            Math.Max(
                distanceMeters,
                0.0);

        for (var index = 1;
             index <
                 segment.Points.Count;
             index++)
        {
            var a =
                segment.Points[
                    index - 1];
            var b =
                segment.Points[
                    index];

            var length =
                Distance(
                    a,
                    b);

            if (length <=
                0.000001)
            {
                continue;
            }

            if (remaining <=
                length)
            {
                var t =
                    Math.Clamp(
                        remaining /
                        length,
                        0.0,
                        1.0);

                position =
                    new WorldVector3(
                        a.X +
                        (b.X -
                         a.X) *
                        t,
                        a.Y +
                        (b.Y -
                         a.Y) *
                        t,
                        a.Z +
                        (b.Z -
                         a.Z) *
                        t);

                headingRadians =
                    Math.Atan2(
                        b.X -
                        a.X,
                        b.Z -
                        a.Z);
                return;
            }

            remaining -=
                length;
        }

        var previous =
            segment.Points[^2];
        var last =
            segment.Points[^1];

        position =
            last;

        headingRadians =
            Math.Atan2(
                last.X -
                previous.X,
                last.Z -
                previous.Z);
    }

    private static double ReverseHeading(
        double headingRadians) =>
        Math.Atan2(
            -Math.Sin(
                headingRadians),
            -Math.Cos(
                headingRadians));

    private static double Distance(
        WorldVector3 a,
        WorldVector3 b)
    {
        var x =
            a.X -
            b.X;
        var y =
            a.Y -
            b.Y;
        var z =
            a.Z -
            b.Z;

        return Math.Sqrt(
            x *
                x +
            y *
                y +
            z *
                z);
    }

    private sealed class Agent(
        int agentIndex,
        int segmentIndex,
        double distanceMeters,
        bool travelForward,
        double initialSpeedMetersPerSecond,
        string trainConsistPath,
        int? groupIndex,
        int? defaultDensityClassIndex,
        string groupName)
    {
        public int AgentIndex { get; } =
            agentIndex;

        public int SegmentIndex
        {
            get;
            set;
        } =
            segmentIndex;

        public double DistanceMeters
        {
            get;
            set;
        } =
            distanceMeters;

        public bool TravelForward { get; } =
            travelForward;

        public double SpeedMetersPerSecond
        {
            get;
            set;
        } =
            initialSpeedMetersPerSecond;

        public string TrainConsistPath { get; } =
            trainConsistPath;

        public int? GroupIndex { get; } =
            groupIndex;

        public int? DefaultDensityClassIndex { get; } =
            defaultDensityClassIndex;

        public string GroupName { get; } =
            groupName;

        public List<int> SegmentHistory
        {
            get;
        } =
            [segmentIndex];

        public int? ReservedSignalRouteIndex
        {
            get;
            set;
        }

        public Dictionary<int, double>
            PendingSignalRouteClearanceOrigins
        {
            get;
        } =
            [];

        public double TraveledDistanceMeters
        {
            get;
            set;
        }
    }
}
