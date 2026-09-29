using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldTrafficAgentState(
    int AgentIndex,
    int SegmentIndex,
    double DistanceMeters,
    double SpeedMetersPerSecond,
    string VehiclePath,
    WorldVector3 Position,
    double HeadingRadians,
    int? GroupIndex = null,
    string? GroupName = null,
    bool AiBrakeLight = false,
    bool AiBlinkerLeft = false,
    bool AiBlinkerRight = false,
    double TraveledDistanceMeters = 0.0,
    double PathCurvaturePerMeter = 0.0);

public sealed record WorldTrafficObstacleState(
    WorldVector3 Position,
    double HeadingRadians,
    double SpeedMetersPerSecond,
    double HalfLengthMeters = 6.0,
    double HalfWidthMeters = 1.35);

public sealed record WorldTrafficSignalState(
    int SegmentIndex,
    int? Phase,
    double PositionSeconds);

public sealed class WorldTrafficSimulation
{
    private readonly record struct TrafficLead(
        double DistanceMeters,
        double SpeedMetersPerSecond);

    private const double MinimumTrafficSeparationMeters =
        2.0;
    private const double FollowingTimeHeadwaySeconds =
        1.25;
    private const double TrafficAccelerationMetersPerSecondSquared =
        1.5;
    private const double TrafficBrakingMetersPerSecondSquared =
        4.0;
    private const double TrafficMaximumLateralAccelerationMetersPerSecondSquared =
        2.0;
    private const double TrafficLookAheadMeters =
        120.0;
    private const double TrafficStopLineBufferMeters =
        0.75;
    private const int MaximumTrafficLookAheadSegments =
        32;

    private readonly WorldTrafficPathNetwork _network;
    private readonly Dictionary<int, WorldTrafficPathSegment> _segmentsByIndex;
    private readonly Dictionary<int, double> _segmentLengthsByIndex;
    private readonly HashSet<long> _crossingSceneryObjectIds;
    private readonly Dictionary<long, int[]>
        _crossingCandidateSegmentsBySceneryObjectId;
    private readonly Dictionary<long, TrafficSignalGroupState> _trafficSignalGroups;
    private readonly List<Agent> _agents;
    private readonly WorldTrafficPathSegment[] _roadSegments = [];
    private readonly bool _runtimeRecyclingEnabled;
    private readonly double _spawnIntervalSeconds = 2.0;
    private readonly double _spawnExclusionRadiusMeters = 40.0;
    private WorldTrafficObstacleState? _externalObstacle;
    private double _simulationElapsedSeconds;

    public WorldTrafficSimulation(
        WorldTrafficPathNetwork network,
        OmsiMapAiCatalog aiCatalog,
        int maximumAgents = 12,
        WorldVector3? spawnExclusionCenter = null,
        double spawnExclusionRadiusMeters = 40.0,
        double spawnIntervalSeconds = 2.0)
    {
        _network =
            network ??
            throw new ArgumentNullException(
                nameof(network));

        ArgumentNullException.ThrowIfNull(
            aiCatalog);

        _segmentsByIndex =
            network.Segments.ToDictionary(
                static segment =>
                    segment.Index);

        _segmentLengthsByIndex =
            network.Segments.ToDictionary(
                static segment =>
                    segment.Index,
                CalculateSegmentLength);

        _crossingSceneryObjectIds =
            network.Segments
                .Where(
                    static segment =>
                        segment.Type ==
                            0 &&
                        segment.SceneryObjectId.HasValue)
                .GroupBy(
                    static segment =>
                        segment.SceneryObjectId!.Value)
                .Where(
                    static group =>
                        group.Count() >
                            1)
                .Select(
                    static group =>
                        group.Key)
                .ToHashSet();

        _crossingCandidateSegmentsBySceneryObjectId =
            _crossingSceneryObjectIds
                .ToDictionary(
                    static crossingId =>
                        crossingId,
                    crossingId =>
                    {
                        var crossingSegments =
                            network.Segments
                                .Where(
                                    segment =>
                                        segment.SceneryObjectId ==
                                        crossingId)
                                .Select(
                                    static segment =>
                                        segment.Index)
                                .ToHashSet();

                        return network.Segments
                            .Where(
                                segment =>
                                    crossingSegments.Contains(
                                        segment.Index) ||
                                    segment.ForwardConnections.Any(
                                        crossingSegments.Contains) ||
                                    segment.ReverseConnections.Any(
                                        crossingSegments.Contains))
                            .Select(
                                static segment =>
                                    segment.Index)
                            .Distinct()
                            .ToArray();
                    });

        _trafficSignalGroups =
            network.Segments
                .Where(
                    static segment =>
                        segment.SceneryObjectId.HasValue &&
                        segment.TrafficSignal is
                            { } signal &&
                        ((signal.Jumps?.Count ??
                          0) >
                             0 ||
                         (signal.Stops?.Count ??
                          0) >
                             0))
                .GroupBy(
                    static segment =>
                        segment.SceneryObjectId!.Value)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                    {
                        var segments =
                            group.ToArray();

                        var signal =
                            segments
                                .Select(
                                    static segment =>
                                        segment.TrafficSignal)
                                .First(
                                    static candidate =>
                                        candidate is not null)!;

                        return new TrafficSignalGroupState(
                            group.Key,
                            signal.CycleSeconds,
                            segments,
                            signal.Jumps ??
                                Array.Empty<WorldTrafficLightJump>(),
                            signal.Stops ??
                                Array.Empty<WorldTrafficLightStop>());
                    });

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

        var vehicles =
            aiCatalog.MovingVehicles
                .Where(
                    static vehicle =>
                        vehicle.Exists &&
                        vehicle.Weight >
                            0.0 &&
                        IsRoadVehicle(
                            vehicle))
                .OrderBy(
                    static vehicle =>
                        vehicle.GroupName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static vehicle =>
                        vehicle.DeclaredPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var roadSegments =
            network.Segments
                .Where(
                    segment =>
                        segment.Type ==
                            0 &&
                        segment.Points.Count >=
                            2 &&
                        (!segment.SceneryObjectId.HasValue ||
                         !_crossingSceneryObjectIds.Contains(
                             segment.SceneryObjectId.Value)))
                .OrderBy(
                    static segment =>
                        segment.Index)
                .ToArray();

        _roadSegments =
            roadSegments;

        if (vehicles.Length == 0 ||
            maximumAgents <= 0 ||
            roadSegments.Length == 0)
        {
            _agents =
                [];
            return;
        }

        var count =
            spawnExclusionCenter.HasValue
                ? maximumAgents
                : Math.Min(
                    maximumAgents,
                    roadSegments.Length);

        if (spawnExclusionCenter.HasValue)
        {
            // Runtime traffic scales with the amount of drivable road
            // currently streamed. Do not cap by path count here: a long
            // avenue may be represented by only one or two OMSI path
            // segments but can legitimately carry many AI vehicles.
            // Preserve the historical deterministic constructor path used by
            // synthetic tests when no player spawn context is given.
            var activeRoadLengthMeters =
                roadSegments.Sum(
                    SegmentLength);

            var networkCapacity =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        activeRoadLengthMeters /
                        140.0));

            count =
                Math.Min(
                    count,
                    networkCapacity);
        }

        var activationIntervalSeconds =
            double.IsFinite(
                spawnIntervalSeconds)
                ? Math.Clamp(
                    spawnIntervalSeconds,
                    0.25,
                    60.0)
                : 2.0;

        _runtimeRecyclingEnabled =
            spawnExclusionCenter.HasValue;
        _spawnIntervalSeconds =
            activationIntervalSeconds;
        _spawnExclusionRadiusMeters =
            Math.Max(
                spawnExclusionRadiusMeters,
                0.0);
        _agents =
            new List<Agent>(
                count);

        for (var index = 0;
             index < count;
             index++)
        {
            var vehicle =
                SelectVehicle(
                    vehicles,
                    index);

            var groupDefinition =
                groupDefinitions.TryGetValue(
                    vehicle.GroupName,
                    out var resolvedGroupDefinition)
                    ? resolvedGroupDefinition
                    : null;

            var groupIndex =
                groupDefinition?.Index;

            var defaultDensityClassIndex =
                groupDefinition?.DefaultDensityClassIndex;

            var allowedSegments =
                roadSegments
                    .Where(
                        segment =>
                            IsTrafficGroupAllowed(
                                segment,
                                groupIndex,
                                defaultDensityClassIndex) &&
                            (!spawnExclusionCenter.HasValue ||
                             HasUsableTrafficExit(
                                 segment)))
                    .ToArray();

            if (allowedSegments.Length ==
                0)
            {
                continue;
            }

            var weightedSegments =
                allowedSegments
                    .Select(
                        segment =>
                            (
                                Segment: segment,
                                Weight:
                                    ResolveTrafficDensityWeight(
                                        segment,
                                        groupIndex,
                                        defaultDensityClassIndex)
                            ))
                    .Where(
                        static candidate =>
                            candidate.Weight >
                                0.0)
                    .ToArray();

            if (weightedSegments.Length ==
                0)
            {
                continue;
            }

            WorldTrafficPathSegment? segment =
                null;

            if (!spawnExclusionCenter.HasValue)
            {
                // Preserve the public simulation's deterministic historical
                // placement when it is used without a runtime/player spawn
                // context. RuntimeApplicationContext supplies a spawn center
                // and therefore uses the distributed branch below.
                segment =
                    allowedSegments[
                        index %
                        allowedSegments.Length];
            }
            else
            {
                // Spread initial AI across the active network, but honor
                // the OMSI traffic-density weights for the vehicle group.
                // Low-density paths remain possible without being sampled as
                // frequently as high-density paths.
                var attemptedSegments =
                    new HashSet<int>();

                for (var attempt = 0;
                     attempt <
                         weightedSegments.Length *
                         3;
                     attempt++)
                {
                    var selector =
                        ((index +
                          1) *
                         0.6180339887498949 +
                         attempt *
                         0.3819660112501051) %
                        1.0;

                    var candidate =
                        SelectWeightedSpawnSegment(
                            weightedSegments,
                            selector);

                    if (candidate is null ||
                        !attemptedSegments.Add(
                            candidate.Index))
                    {
                        continue;
                    }

                    var candidateLength =
                        SegmentLength(
                            candidate);

                    if (candidateLength <=
                        0.0001)
                    {
                        continue;
                    }

                    segment =
                        candidate;
                    break;
                }
            }

            if (segment is null)
            {
                continue;
            }

            var length =
                SegmentLength(
                    segment);

            var travelForward =
                segment.Direction switch
                {
                    1 =>
                        false,
                    2 =>
                        index %
                            2 ==
                        0,
                    _ =>
                        true
                };

            double distance;

            if (!spawnExclusionCenter.HasValue)
            {
                var offset =
                    length >
                            1.0
                        ? Math.Min(
                            length *
                                0.15 *
                                (index %
                                     5),
                            Math.Max(
                                length -
                                    0.1,
                                0.0))
                        : 0.0;

                distance =
                    travelForward
                        ? offset
                        : Math.Max(
                            length -
                                offset,
                            0.0);
            }
            else
            {
                var placed =
                    false;

                distance =
                    0.0;

                var orderedPlacementSegments =
                    weightedSegments
                        .Select(
                            candidate =>
                            {
                                var unitSelector =
                                    ((index +
                                      1) *
                                     0.6180339887498949 +
                                     (candidate.Segment.Index +
                                      1) *
                                     0.4142135623730950) %
                                    1.0;

                                unitSelector =
                                    Math.Clamp(
                                        unitSelector,
                                        0.000001,
                                        0.999999);

                                return
                                    (
                                        candidate.Segment,
                                        Rank:
                                            -Math.Log(
                                                unitSelector) /
                                            Math.Max(
                                                candidate.Weight,
                                                0.000001)
                                    );
                            })
                        .OrderBy(
                            static candidate =>
                                candidate.Rank)
                        .ThenBy(
                            static candidate =>
                                candidate.Segment.Index)
                        .ToArray();

                for (var segmentAttempt = 0;
                     segmentAttempt <
                         orderedPlacementSegments.Length &&
                     !placed;
                     segmentAttempt++)
                {
                    var placementSegment =
                        orderedPlacementSegments[
                            segmentAttempt]
                            .Segment;

                    var placementLength =
                        SegmentLength(
                            placementSegment);

                    if (placementLength <=
                        0.0001)
                    {
                        continue;
                    }

                    var placementTravelForward =
                        placementSegment.Direction switch
                        {
                            1 =>
                                false,
                            2 =>
                                ((index +
                                  segmentAttempt) &
                                 1) ==
                                0,
                            _ =>
                                true
                        };

                    for (var placementAttempt = 0;
                         placementAttempt <
                             12;
                         placementAttempt++)
                    {
                        var selector =
                            (((index +
                               1) *
                              0.4142135623730950 +
                              (segmentAttempt +
                               1) *
                              0.1732050807568877 +
                              placementAttempt *
                              0.2360679774997897) %
                             1.0);

                        var offset =
                            placementLength >
                                    1.0
                                ? Math.Clamp(
                                    placementLength *
                                        (0.10 +
                                         0.80 *
                                         selector),
                                    0.1,
                                    Math.Max(
                                        placementLength -
                                            0.1,
                                        0.1))
                                : 0.0;

                        var candidateDistance =
                            placementTravelForward
                                ? offset
                                : Math.Max(
                                    placementLength -
                                        offset,
                                    0.0);

                        SampleSegment(
                            placementSegment,
                            candidateDistance,
                            out var spawnPosition,
                            out _);

                        if (_spawnExclusionRadiusMeters >
                                0.0 &&
                            HorizontalDistance(
                                spawnPosition,
                                spawnExclusionCenter.Value) <
                            _spawnExclusionRadiusMeters)
                        {
                            continue;
                        }

                        var tooCloseToExistingSpawn =
                            _agents.Any(
                                existing =>
                                {
                                    if (!_segmentsByIndex.TryGetValue(
                                            existing.SegmentIndex,
                                            out var existingSegment))
                                    {
                                        return false;
                                    }

                                    SampleSegment(
                                        existingSegment,
                                        existing.DistanceMeters,
                                        out var existingPosition,
                                        out _);

                                    return HorizontalDistance(
                                               spawnPosition,
                                               existingPosition) <
                                           22.0;
                                });

                        if (tooCloseToExistingSpawn)
                        {
                            continue;
                        }

                        segment =
                            placementSegment;

                        length =
                            placementLength;

                        travelForward =
                            placementTravelForward;

                        distance =
                            candidateDistance;

                        placed =
                            true;

                        break;
                    }
                }

                if (!placed)
                {
                    continue;
                }
            }

            var cruiseSpeed =
                ResolveCruiseSpeed(
                    index);

            var segmentSpeed =
                ResolveSegmentMaximumSpeed(
                    segment,
                    cruiseSpeed);

            // Match OMSI-style traffic startup: the AI appears stationary,
            // then begins accelerating on the first active simulation step.
            // Keep the shorter stagger so vehicles become visible promptly
            // without popping the whole fleet at once.
            var initialSpeed =
                spawnExclusionCenter.HasValue
                    ? 0.0
                    : segmentSpeed;

            var activationTimeSeconds =
                spawnExclusionCenter.HasValue
                    ? index *
                      Math.Min(
                          activationIntervalSeconds,
                          0.75)
                    : 0.0;

            _agents.Add(
                new Agent(
                    index,
                    segment.Index,
                    distance,
                    travelForward,
                    cruiseSpeed,
                    initialSpeed,
                    vehicle.ResolvedPath!,
                    groupIndex,
                    defaultDensityClassIndex,
                    vehicle.GroupName,
                    activationTimeSeconds));
        }
    }

    public IReadOnlyList<WorldTrafficAgentState> Snapshot() =>
        _agents
            .Where(
                agent =>
                    agent.ActivationTimeSeconds <=
                    _simulationElapsedSeconds)
            .Select(
                CreateState)
            .ToArray();

    public IReadOnlyList<WorldTrafficSignalState> SnapshotTrafficSignals() =>
        _segmentsByIndex.Values
            .Where(
                static segment =>
                    segment.TrafficSignal is not null)
            .OrderBy(
                static segment =>
                    segment.Index)
            .Select(
                segment =>
                {
                    var positionSeconds =
                        segment.SceneryObjectId.HasValue &&
                        _trafficSignalGroups.TryGetValue(
                            segment.SceneryObjectId.Value,
                            out var group)
                            ? group.PositionSeconds
                            : _simulationElapsedSeconds;

                    return new WorldTrafficSignalState(
                        segment.Index,
                        ResolveTrafficSignalPhase(
                            segment.TrafficSignal,
                            positionSeconds),
                        positionSeconds);
                })
            .ToArray();

    public void SetExternalObstacle(
        WorldTrafficObstacleState? obstacle)
    {
        _externalObstacle =
            obstacle;
    }

    public void ApplyCollisionResponse(
        int agentIndex,
        double relativeImpactSpeedKph)
    {
        var agent =
            _agents.FirstOrDefault(
                candidate =>
                    candidate.AgentIndex ==
                    agentIndex);

        if (agent is null ||
            agent.PendingRespawn ||
            agent.ActivationTimeSeconds >
                _simulationElapsedSeconds)
        {
            return;
        }

        var impactSeverity =
            Math.Clamp(
                relativeImpactSpeedKph /
                    40.0,
                0.0,
                1.0);

        var speedRetention =
            Math.Clamp(
                1.0 -
                    impactSeverity *
                    0.85,
                0.10,
                1.0);

        agent.SpeedMetersPerSecond *=
            speedRetention;

        agent.BrakeLight =
            true;

        agent.CollisionHoldUntilSeconds =
            Math.Max(
                agent.CollisionHoldUntilSeconds,
                _simulationElapsedSeconds +
                    0.35 +
                    impactSeverity *
                        0.65);
    }

    public void Step(
        double deltaSeconds)
    {
        if (!double.IsFinite(
                deltaSeconds) ||
            deltaSeconds <=
                0.0)
        {
            return;
        }

        var simulationSeconds =
            deltaSeconds;

        while (simulationSeconds >
               0.000001)
        {
            var step =
                Math.Min(
                    simulationSeconds,
                    0.25);

            UpdateTrafficSignalGroups(
                step);

            var activeAgentsBySegment =
                BuildActiveAgentBuckets();

            foreach (var agent in
                     _agents)
            {
                if (agent.ActivationTimeSeconds >
                    _simulationElapsedSeconds)
                {
                    continue;
                }

                if (_runtimeRecyclingEnabled &&
                    !agent.ActivationPlacementValidated)
                {
                    if (NeedsActivationRelocation(
                            agent))
                    {
                        agent.PendingRespawn =
                            true;
                        RemoveActiveAgentBucket(
                            activeAgentsBySegment,
                            agent.SegmentIndex,
                            agent);

                        if (!TryPlaceRecycledAgent(
                                agent))
                        {
                            agent.ActivationTimeSeconds =
                                _simulationElapsedSeconds +
                                _spawnIntervalSeconds;
                            continue;
                        }

                        agent.PendingRespawn =
                            false;
                        AddActiveAgentBucket(
                            activeAgentsBySegment,
                            agent);
                    }

                    agent.ActivationPlacementValidated =
                        true;
                }

                if (agent.PendingRespawn)
                {
                    if (!TryPlaceRecycledAgent(
                            agent))
                    {
                        agent.ActivationTimeSeconds =
                            _simulationElapsedSeconds +
                            _spawnIntervalSeconds;
                        continue;
                    }

                    agent.PendingRespawn =
                        false;
                    AddActiveAgentBucket(
                        activeAgentsBySegment,
                        agent);
                }

                var collisionHoldActive =
                    agent.CollisionHoldUntilSeconds >
                    _simulationElapsedSeconds;

                var leading =
                    FindLeadingObservation(
                        agent,
                        activeAgentsBySegment);

                double? blockedEntryDistance =
                    null;

                if (_segmentsByIndex.TryGetValue(
                        agent.SegmentIndex,
                        out var currentSegment))
                {
                    blockedEntryDistance =
                        ResolveBlockedEntryDistance(
                            agent,
                            currentSegment,
                            activeAgentsBySegment);
                }

                var targetSpeed =
                    collisionHoldActive
                        ? 0.0
                        : ResolveTargetSpeed(
                            agent,
                            leading,
                            blockedEntryDistance);

                var previousSpeed =
                    agent.SpeedMetersPerSecond;

                agent.BrakeLight =
                    targetSpeed <
                        previousSpeed -
                            0.01 ||
                    (targetSpeed <=
                         0.05 &&
                     previousSpeed <=
                         0.5);

                UpdateAgentSpeed(
                    agent,
                    targetSpeed,
                    step);

                var remaining =
                    agent.SpeedMetersPerSecond *
                    step;

                if (leading.HasValue)
                {
                    remaining =
                        Math.Min(
                            remaining,
                            Math.Max(
                                leading.Value.DistanceMeters -
                                    MinimumTrafficSeparationMeters,
                                0.0));
                }

                if (blockedEntryDistance.HasValue)
                {
                    remaining =
                        Math.Min(
                            remaining,
                            Math.Max(
                                blockedEntryDistance.Value -
                                    TrafficStopLineBufferMeters,
                                0.0));
                }

                var guard =
                    0;

                while (remaining >
                           0.0001 &&
                       guard++ <
                           32)
                {
                    if (!_segmentsByIndex.TryGetValue(
                            agent.SegmentIndex,
                            out var segment))
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
                                segment,
                                activeAgentsBySegment))
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

                    if (remaining <=
                        available)
                    {
                        agent.DistanceMeters +=
                            agent.TravelForward
                                ? remaining
                                : -remaining;

                        agent.TraveledDistanceMeters +=
                            remaining;

                        remaining =
                            0.0;

                        break;
                    }

                    remaining -=
                        available;

                    agent.TraveledDistanceMeters +=
                        available;

                    agent.DistanceMeters =
                        agent.TravelForward
                            ? length
                            : 0.0;

                    if (!TryAdvanceSegment(
                            agent,
                            segment,
                            activeAgentsBySegment))
                    {
                        remaining =
                            0.0;

                        break;
                    }
                }

                if (blockedEntryDistance.HasValue &&
                    _segmentsByIndex.TryGetValue(
                        agent.SegmentIndex,
                        out var stoppedSegment))
                {
                    var stoppedLength =
                        SegmentLength(
                            stoppedSegment);

                    var distanceToEntry =
                        agent.TravelForward
                            ? Math.Max(
                                stoppedLength -
                                    agent.DistanceMeters,
                                0.0)
                            : Math.Max(
                                agent.DistanceMeters,
                                0.0);

                    if (distanceToEntry <=
                        TrafficStopLineBufferMeters +
                            0.0001)
                    {
                        agent.SpeedMetersPerSecond =
                            0.0;
                        agent.BrakeLight =
                            true;
                    }
                }
            }

            _simulationElapsedSeconds +=
                step;

            simulationSeconds -=
                step;
        }
    }

    private bool TryAdvanceSegment(
        Agent agent,
        WorldTrafficPathSegment segment,
        Dictionary<int, List<Agent>> activeAgentsBySegment)
    {
        var next =
            ResolveNextSegmentIndex(
                agent,
                segment);

        if (!next.HasValue ||
            !_segmentsByIndex.TryGetValue(
                next.Value,
                out var nextSegment))
        {
            if (TryRecycleTerminalAgent(
                    agent))
            {
                RemoveActiveAgentBucket(
                    activeAgentsBySegment,
                    agent.SegmentIndex,
                    agent);
                return false;
            }

            agent.SpeedMetersPerSecond =
                0.0;
            agent.BrakeLight =
                true;

            return false;
        }

        if (!CanEnterSegment(
                agent,
                segment,
                nextSegment,
                activeAgentsBySegment))
        {
            agent.SpeedMetersPerSecond =
                0.0;
            agent.BrakeLight =
                true;

            return false;
        }

        var previousSegmentIndex =
            agent.SegmentIndex;

        agent.SegmentIndex =
            next.Value;

        MoveActiveAgentBucket(
            activeAgentsBySegment,
            previousSegmentIndex,
            agent);

        agent.DistanceMeters =
            agent.TravelForward
                ? 0.0
                : SegmentLength(
                    nextSegment);

        return true;
    }

    private bool NeedsActivationRelocation(
        Agent agent)
    {
        if (!_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            return true;
        }

        SampleSegment(
            segment,
            agent.DistanceMeters,
            out var position,
            out _);

        if (_externalObstacle is
                { } obstacle)
        {
            if (_spawnExclusionRadiusMeters >
                    0.0 &&
                HorizontalDistance(
                    position,
                    obstacle.Position) <
                _spawnExclusionRadiusMeters)
            {
                return true;
            }

            if (IsVisibleRespawnPop(
                    position,
                    obstacle))
            {
                return true;
            }
        }

        foreach (var other in
                 _agents)
        {
            if (ReferenceEquals(
                    other,
                    agent) ||
                other.PendingRespawn ||
                other.ActivationTimeSeconds >
                    _simulationElapsedSeconds ||
                !_segmentsByIndex.TryGetValue(
                    other.SegmentIndex,
                    out var otherSegment))
            {
                continue;
            }

            SampleSegment(
                otherSegment,
                other.DistanceMeters,
                out var otherPosition,
                out _);

            if (HorizontalDistance(
                    position,
                    otherPosition) <
                22.0)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryRecycleTerminalAgent(
        Agent agent)
    {
        if (!_runtimeRecyclingEnabled ||
            _roadSegments.Length ==
                0)
        {
            return false;
        }

        agent.SpeedMetersPerSecond =
            0.0;
        agent.BrakeLight =
            true;
        agent.PendingRespawn =
            true;
        agent.ActivationTimeSeconds =
            _simulationElapsedSeconds +
            _spawnIntervalSeconds *
            (1.0 +
             (agent.AgentIndex %
              3) *
             0.35);

        return true;
    }

    private bool TryPlaceRecycledAgent(
        Agent agent)
    {
        if (!_runtimeRecyclingEnabled ||
            _roadSegments.Length ==
                0)
        {
            return false;
        }

        var candidates =
            _roadSegments
                .Where(
                    segment =>
                        IsTrafficGroupAllowed(
                            segment,
                            agent.GroupIndex,
                            agent.DefaultDensityClassIndex) &&
                        HasUsableTrafficExit(
                            segment) &&
                        !IsCriticalRespawnApproach(
                            agent,
                            segment))
                .Select(
                    segment =>
                        (
                            Segment: segment,
                            Weight:
                                ResolveTrafficDensityWeight(
                                    segment,
                                    agent.GroupIndex,
                                    agent.DefaultDensityClassIndex)
                        ))
                .Where(
                    static candidate =>
                        candidate.Weight >
                            0.0)
                .ToArray();

        if (candidates.Length ==
            0)
        {
            return false;
        }

        var recycleEpoch =
            Math.Max(
                0,
                (int)Math.Floor(
                    _simulationElapsedSeconds /
                    Math.Max(
                        _spawnIntervalSeconds,
                        0.25)));

        var orderedCandidates =
            candidates
                .Select(
                    candidate =>
                    {
                        var unitSelector =
                            ((agent.AgentIndex +
                              1) *
                             0.6180339887498949 +
                             (recycleEpoch +
                              1) *
                             0.3819660112501051 +
                             (candidate.Segment.Index +
                              1) *
                             0.4142135623730950) %
                            1.0;

                        unitSelector =
                            Math.Clamp(
                                unitSelector,
                                0.000001,
                                0.999999);

                        return
                            (
                                candidate.Segment,
                                Rank:
                                    -Math.Log(
                                        unitSelector) /
                                    Math.Max(
                                        candidate.Weight,
                                        0.000001)
                            );
                    })
                .OrderBy(
                    static candidate =>
                        candidate.Rank)
                .ThenBy(
                    static candidate =>
                        candidate.Segment.Index)
                .ToArray();

        for (var segmentAttempt = 0;
             segmentAttempt <
                 orderedCandidates.Length;
             segmentAttempt++)
        {
            var segment =
                orderedCandidates[
                    segmentAttempt]
                    .Segment;

            var length =
                SegmentLength(
                    segment);

            if (length <=
                1.0)
            {
                continue;
            }

            var travelForward =
                segment.Direction switch
                {
                    1 =>
                        false,
                    2 =>
                        ((agent.AgentIndex +
                          recycleEpoch +
                          segmentAttempt) &
                         1) ==
                        0,
                    _ =>
                        true
                };

            for (var placementAttempt = 0;
                 placementAttempt <
                     3;
                 placementAttempt++)
            {
                var offset =
                    Math.Clamp(
                        length *
                            (0.15 +
                             0.70 *
                             (((agent.AgentIndex +
                                recycleEpoch +
                                segmentAttempt +
                                placementAttempt +
                                1) *
                               0.4142135623730950) %
                              1.0)),
                        0.1,
                        Math.Max(
                            length -
                                0.1,
                            0.1));

                var distance =
                    travelForward
                        ? offset
                        : Math.Max(
                            length -
                                offset,
                            0.0);

                SampleSegment(
                    segment,
                    distance,
                    out var spawnPosition,
                    out _);

                var exclusionCenter =
                    _externalObstacle?.Position;

                if (exclusionCenter.HasValue &&
                    _spawnExclusionRadiusMeters >
                        0.0 &&
                    HorizontalDistance(
                        spawnPosition,
                        exclusionCenter.Value) <
                        _spawnExclusionRadiusMeters)
                {
                    continue;
                }

                if (_externalObstacle is
                        { } playerObstacle &&
                    IsVisibleRespawnPop(
                        spawnPosition,
                        playerObstacle))
                {
                    continue;
                }

                var tooCloseToTraffic =
                    _agents.Any(
                        other =>
                        {
                            if (ReferenceEquals(
                                    other,
                                    agent) ||
                                other.PendingRespawn ||
                                other.ActivationTimeSeconds >
                                    _simulationElapsedSeconds ||
                                !_segmentsByIndex.TryGetValue(
                                    other.SegmentIndex,
                                    out var otherSegment))
                            {
                                return false;
                            }

                            SampleSegment(
                                otherSegment,
                                other.DistanceMeters,
                                out var otherPosition,
                                out _);

                            var horizontalDistance =
                                HorizontalDistance(
                                    spawnPosition,
                                    otherPosition);

                            if (horizontalDistance <
                                22.0)
                            {
                                return true;
                            }

                            return other.SegmentIndex ==
                                       segment.Index &&
                                   other.SpeedMetersPerSecond <=
                                       2.5 &&
                                   Math.Abs(
                                       other.DistanceMeters -
                                       distance) <
                                       35.0;
                        });

                if (tooCloseToTraffic)
                {
                    continue;
                }

                agent.SegmentIndex =
                    segment.Index;
                agent.DistanceMeters =
                    distance;
                agent.TravelForward =
                    travelForward;
                agent.SpeedMetersPerSecond =
                    0.0;
                agent.BrakeLight =
                    false;
                agent.CollisionHoldUntilSeconds =
                    0.0;
                agent.ActivationTimeSeconds =
                    _simulationElapsedSeconds;

                return true;
            }
        }

        return false;
    }

    private static bool IsVisibleRespawnPop(
        WorldVector3 spawnPosition,
        WorldTrafficObstacleState player)
    {
        var dx =
            spawnPosition.X -
            player.Position.X;
        var dz =
            spawnPosition.Z -
            player.Position.Z;

        var distanceSquared =
            dx *
                dx +
            dz *
                dz;

        const double visibleRespawnDistanceMeters =
            120.0;

        if (distanceSquared <=
                0.0001 ||
            distanceSquared >
                visibleRespawnDistanceMeters *
                visibleRespawnDistanceMeters)
        {
            return false;
        }

        var distance =
            Math.Sqrt(
                distanceSquared);

        var forwardX =
            Math.Sin(
                player.HeadingRadians);
        var forwardZ =
            Math.Cos(
                player.HeadingRadians);

        var forwardProjection =
            (dx *
                 forwardX +
             dz *
                 forwardZ) /
            distance;

        // Keep the forward 120-degree field clear from visible pop-in.
        return forwardProjection >=
            0.5;
    }

    private bool IsCriticalRespawnApproach(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var nextIndex =
            ResolveNextSegmentIndex(
                agent,
                segment);

        if (!nextIndex.HasValue ||
            !_segmentsByIndex.TryGetValue(
                nextIndex.Value,
                out var nextSegment))
        {
            return false;
        }

        return nextSegment.TrafficSignal is
                   not null ||
               (nextSegment.SceneryObjectId.HasValue &&
                _crossingSceneryObjectIds.Contains(
                    nextSegment.SceneryObjectId.Value));
    }

    private bool CanEnterSegment(
        Agent agent,
        WorldTrafficPathSegment currentSegment,
        WorldTrafficPathSegment nextSegment,
        IReadOnlyDictionary<int, List<Agent>> activeAgentsBySegment)
    {
        if (!IsTrafficSignalGreen(
                nextSegment))
        {
            return false;
        }

        if (!nextSegment.SceneryObjectId.HasValue ||
            !_crossingSceneryObjectIds.Contains(
                nextSegment.SceneryObjectId.Value))
        {
            return true;
        }

        var crossingId =
            nextSegment.SceneryObjectId.Value;

        if (IsCrossingExitBlocked(
                agent,
                nextSegment,
                activeAgentsBySegment))
        {
            return false;
        }

        if (IsExternalObstacleBlockingCrossing(
                agent,
                nextSegment))
        {
            return false;
        }

        foreach (var other in
                 EnumerateCrossingCandidateAgents(
                     crossingId,
                     activeAgentsBySegment))
        {
            if (ReferenceEquals(
                    other,
                    agent) ||
                other.PendingRespawn ||
                other.ActivationTimeSeconds >
                    _simulationElapsedSeconds ||
                !_segmentsByIndex.TryGetValue(
                    other.SegmentIndex,
                    out var otherSegment))
            {
                continue;
            }

            if (otherSegment.SceneryObjectId ==
                crossingId)
            {
                if (TrafficPathsConflict(
                        nextSegment,
                        otherSegment))
                {
                    return false;
                }

                continue;
            }

            if (otherSegment.SceneryObjectId.HasValue &&
                _crossingSceneryObjectIds.Contains(
                    otherSegment.SceneryObjectId.Value))
            {
                continue;
            }

            var otherNextIndex =
                ResolveNextSegmentIndex(
                    other,
                    otherSegment);

            if (!otherNextIndex.HasValue ||
                !_segmentsByIndex.TryGetValue(
                    otherNextIndex.Value,
                    out var otherNextSegment) ||
                otherNextSegment.SceneryObjectId !=
                    crossingId ||
                !TrafficPathsConflict(
                    nextSegment,
                    otherNextSegment))
            {
                continue;
            }

            // A red approach cannot reserve a conflict against
            // another approach that is already allowed to enter.
            if (!IsTrafficSignalGreen(
                    otherNextSegment))
            {
                continue;
            }

            // Do not let an approach reserve the crossing when its own exit
            // is blocked by a stopped queue. Otherwise a high-priority lane
            // that cannot clear the box can deadlock the transverse traffic
            // even though it has no safe path through the junction.
            if (IsCrossingExitBlocked(
                    other,
                    otherNextSegment,
                    activeAgentsBySegment))
            {
                continue;
            }

            if (otherSegment.TrafficPriority >
                currentSegment.TrafficPriority)
            {
                var currentArrival =
                    EstimateApproachArrivalSeconds(
                        agent,
                        currentSegment);

                var otherArrival =
                    EstimateApproachArrivalSeconds(
                        other,
                        otherSegment);

                var otherApproachIsActive =
                    other.SpeedMetersPerSecond >
                        0.75;

                // A stopped or collision-held vehicle on a priority road
                // must not reserve the conflict. As soon as it accelerates
                // again, normal priority and arrival-time rules immediately
                // restore its reservation.
                if (otherApproachIsActive &&
                    (otherArrival <=
                         4.0 ||
                     otherArrival <=
                         currentArrival +
                         2.0))
                {
                    return false;
                }

                continue;
            }

            if (otherSegment.TrafficPriority ==
                currentSegment.TrafficPriority)
            {
                var otherRemainingDistance =
                    ResolveApproachRemainingDistance(
                        other,
                        otherSegment);

                if (other.SpeedMetersPerSecond <=
                        0.75 &&
                    otherRemainingDistance >
                        2.0)
                {
                    continue;
                }

                var currentArrival =
                    EstimateApproachArrivalSeconds(
                        agent,
                        currentSegment);

                var otherArrival =
                    EstimateApproachArrivalSeconds(
                        other,
                        otherSegment);

                // Vehicles that are materially closer to the conflict reserve
                // it first. For near-simultaneous arrivals, preserve OMSI-like
                // right-hand priority and finally use a stable agent-id tie
                // breaker so two approaches cannot both decide to enter.
                if (otherArrival +
                        0.75 <
                    currentArrival)
                {
                    return false;
                }

                if (Math.Abs(
                        otherArrival -
                        currentArrival) <=
                        0.75 &&
                    ApproachesFromRight(
                        currentSegment,
                        agent.TravelForward,
                        otherSegment,
                        other.TravelForward))
                {
                    return false;
                }

                if (Math.Abs(
                        otherArrival -
                        currentArrival) <=
                        0.10 &&
                    other.AgentIndex <
                        agent.AgentIndex)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsExternalObstacleBlockingCrossing(
        Agent agent,
        WorldTrafficPathSegment crossingSegment)
    {
        if (_externalObstacle is not
                { } obstacle ||
            crossingSegment.Points.Count <
                2)
        {
            return false;
        }

        var forwardX =
            Math.Sin(
                obstacle.HeadingRadians);

        var forwardZ =
            Math.Cos(
                obstacle.HeadingRadians);

        var obstacleStart =
            new WorldVector3(
                obstacle.Position.X -
                    forwardX *
                    obstacle.HalfLengthMeters,
                obstacle.Position.Y,
                obstacle.Position.Z -
                    forwardZ *
                    obstacle.HalfLengthMeters);

        var obstacleEnd =
            new WorldVector3(
                obstacle.Position.X +
                    forwardX *
                    obstacle.HalfLengthMeters,
                obstacle.Position.Y,
                obstacle.Position.Z +
                    forwardZ *
                    obstacle.HalfLengthMeters);

        var clearance =
            obstacle.HalfWidthMeters +
            EstimateTrafficVehicleHalfWidth(
                agent.VehiclePath) +
            0.15;

        var clearanceSquared =
            clearance *
            clearance;

        for (var index = 1;
             index <
                 crossingSegment.Points.Count;
             index++)
        {
            var pathStart =
                crossingSegment.Points[
                    index -
                        1];
            var pathEnd =
                crossingSegment.Points[
                    index];

            // SegmentDistanceSquared is fully 3D, so grade-separated
            // crossings are rejected by their true spatial separation while
            // sloped ramps remain valid when they actually intersect the
            // player's footprint.
            if (SegmentDistanceSquared(
                    pathStart,
                    pathEnd,
                    obstacleStart,
                    obstacleEnd) <=
                clearanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsCrossingExitBlocked(
        Agent agent,
        WorldTrafficPathSegment crossingSegment,
        IReadOnlyDictionary<int, List<Agent>> activeAgentsBySegment)
    {
        var exitIndex =
            ResolveNextSegmentIndex(
                agent,
                crossingSegment);

        if (!exitIndex.HasValue ||
            !_segmentsByIndex.TryGetValue(
                exitIndex.Value,
                out var exitSegment))
        {
            return false;
        }

        var exitLength =
            SegmentLength(
                exitSegment);

        foreach (var other in
                 GetActiveSegmentAgents(
                     exitSegment.Index,
                     activeAgentsBySegment))
        {
            if (ReferenceEquals(
                    other,
                    agent) ||
                other.PendingRespawn ||
                other.ActivationTimeSeconds >
                    _simulationElapsedSeconds ||
                other.SegmentIndex !=
                    exitSegment.Index)
            {
                continue;
            }

            var distanceFromEntry =
                agent.TravelForward
                    ? Math.Max(
                        other.DistanceMeters,
                        0.0)
                    : Math.Max(
                        exitLength -
                            other.DistanceMeters,
                        0.0);

            var requiredExitClearance =
                4.0 +
                EstimateTrafficVehicleHalfLength(
                    agent.VehiclePath) +
                EstimateTrafficVehicleHalfLength(
                    other.VehiclePath);

            if (distanceFromEntry <=
                    requiredExitClearance &&
                other.SpeedMetersPerSecond <=
                    2.5)
            {
                return true;
            }
        }

        if (_externalObstacle is
                { } obstacle &&
            obstacle.SpeedMetersPerSecond <=
                2.5)
        {
            var requiredPlayerClearance =
                Math.Min(
                    exitLength,
                    4.0 +
                    EstimateTrafficVehicleHalfLength(
                        agent.VehiclePath) +
                    obstacle.HalfLengthMeters);

            var entryDistance =
                agent.TravelForward
                    ? 0.0
                    : exitLength;

            var clearanceDistance =
                agent.TravelForward
                    ? requiredPlayerClearance
                    : Math.Max(
                        exitLength -
                            requiredPlayerClearance,
                        0.0);

            SampleSegment(
                exitSegment,
                entryDistance,
                out var exitEntry,
                out _);

            SampleSegment(
                exitSegment,
                clearanceDistance,
                out var exitClearanceEnd,
                out _);

            var obstacleForwardX =
                Math.Sin(
                    obstacle.HeadingRadians);
            var obstacleForwardZ =
                Math.Cos(
                    obstacle.HeadingRadians);

            var obstacleStart =
                new WorldVector3(
                    obstacle.Position.X -
                        obstacleForwardX *
                        obstacle.HalfLengthMeters,
                    obstacle.Position.Y,
                    obstacle.Position.Z -
                        obstacleForwardZ *
                        obstacle.HalfLengthMeters);

            var obstacleEnd =
                new WorldVector3(
                    obstacle.Position.X +
                        obstacleForwardX *
                        obstacle.HalfLengthMeters,
                    obstacle.Position.Y,
                    obstacle.Position.Z +
                        obstacleForwardZ *
                        obstacle.HalfLengthMeters);

            var clearanceWidth =
                obstacle.HalfWidthMeters +
                EstimateTrafficVehicleHalfWidth(
                    agent.VehiclePath) +
                0.15;

            if (SegmentDistanceSquared(
                    exitEntry,
                    exitClearanceEnd,
                    obstacleStart,
                    obstacleEnd) <=
                clearanceWidth *
                    clearanceWidth)
            {
                return true;
            }
        }

        return false;
    }

    private static double ResolveApproachRemainingDistance(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var segmentLength =
            CalculateSegmentLength(
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

    private static double EstimateApproachArrivalSeconds(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var segmentLength =
            CalculateSegmentLength(
                segment);

        var remainingDistance =
            agent.TravelForward
                ? Math.Max(
                    segmentLength -
                        agent.DistanceMeters,
                    0.0)
                : Math.Max(
                    agent.DistanceMeters,
                    0.0);

        var approachSpeed =
            Math.Max(
                agent.SpeedMetersPerSecond,
                1.0);

        return remainingDistance /
               approachSpeed;
    }

    private static bool ApproachesFromRight(
        WorldTrafficPathSegment currentSegment,
        bool currentTravelForward,
        WorldTrafficPathSegment otherSegment,
        bool otherTravelForward)
    {
        var currentHeading =
            HeadingAtExit(
                currentSegment,
                currentTravelForward);

        var otherHeading =
            HeadingAtExit(
                otherSegment,
                otherTravelForward);

        var delta =
            NormalizeHeadingDelta(
                otherHeading -
                currentHeading);

        // In OMSI's renderer frame, a vehicle approaching from
        // the right has a travel heading roughly 90 degrees
        // clockwise from the current approach.
        return delta <
                   -Math.PI /
                   4.0 &&
               delta >
                   -3.0 *
                   Math.PI /
                   4.0;
    }

    private static double HeadingAtExit(
        WorldTrafficPathSegment segment,
        bool travelForward)
    {
        var length =
            CalculateSegmentLength(
                segment);

        SampleSegment(
            segment,
            travelForward
                ? length
                : 0.0,
            out _,
            out var heading);

        return travelForward
            ? heading
            : ReverseHeading(
                heading);
    }

    private static bool TrafficPathsConflict(
        WorldTrafficPathSegment first,
        WorldTrafficPathSegment second)
    {
        if (first.Index ==
            second.Index)
        {
            return true;
        }

        if (!first.SceneryObjectId.HasValue ||
            first.SceneryObjectId !=
                second.SceneryObjectId ||
            first.Points.Count <
                2 ||
            second.Points.Count <
                2)
        {
            return false;
        }

        const double conflictToleranceMeters =
            0.5;

        var maximumDistanceSquared =
            conflictToleranceMeters *
            conflictToleranceMeters;

        for (var firstIndex = 1;
             firstIndex <
                 first.Points.Count;
             firstIndex++)
        {
            for (var secondIndex = 1;
                 secondIndex <
                     second.Points.Count;
                 secondIndex++)
            {
                if (SegmentDistanceSquared(
                        first.Points[
                            firstIndex -
                            1],
                        first.Points[
                            firstIndex],
                        second.Points[
                            secondIndex -
                            1],
                        second.Points[
                            secondIndex]) <=
                    maximumDistanceSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double SegmentDistanceSquared(
        WorldVector3 firstStart,
        WorldVector3 firstEnd,
        WorldVector3 secondStart,
        WorldVector3 secondEnd)
    {
        var d1X =
            firstEnd.X -
            firstStart.X;
        var d1Y =
            firstEnd.Y -
            firstStart.Y;
        var d1Z =
            firstEnd.Z -
            firstStart.Z;

        var d2X =
            secondEnd.X -
            secondStart.X;
        var d2Y =
            secondEnd.Y -
            secondStart.Y;
        var d2Z =
            secondEnd.Z -
            secondStart.Z;

        var rX =
            firstStart.X -
            secondStart.X;
        var rY =
            firstStart.Y -
            secondStart.Y;
        var rZ =
            firstStart.Z -
            secondStart.Z;

        var a =
            d1X * d1X +
            d1Y * d1Y +
            d1Z * d1Z;

        var e =
            d2X * d2X +
            d2Y * d2Y +
            d2Z * d2Z;

        var f =
            d2X * rX +
            d2Y * rY +
            d2Z * rZ;

        const double epsilon =
            0.000000001;

        double s;
        double t;

        if (a <=
                epsilon &&
            e <=
                epsilon)
        {
            return DistanceSquared(
                firstStart,
                secondStart);
        }

        if (a <=
            epsilon)
        {
            s =
                0.0;
            t =
                Math.Clamp(
                    f /
                    e,
                    0.0,
                    1.0);
        }
        else
        {
            var c =
                d1X * rX +
                d1Y * rY +
                d1Z * rZ;

            if (e <=
                epsilon)
            {
                t =
                    0.0;
                s =
                    Math.Clamp(
                        -c /
                        a,
                        0.0,
                        1.0);
            }
            else
            {
                var b =
                    d1X * d2X +
                    d1Y * d2Y +
                    d1Z * d2Z;

                var denominator =
                    a *
                    e -
                    b *
                    b;

                s =
                    Math.Abs(
                        denominator) >
                    epsilon
                        ? Math.Clamp(
                            (b *
                                 f -
                             c *
                                 e) /
                            denominator,
                            0.0,
                            1.0)
                        : 0.0;

                t =
                    (b *
                         s +
                     f) /
                    e;

                if (t <
                    0.0)
                {
                    t =
                        0.0;
                    s =
                        Math.Clamp(
                            -c /
                            a,
                            0.0,
                            1.0);
                }
                else if (t >
                         1.0)
                {
                    t =
                        1.0;
                    s =
                        Math.Clamp(
                            (b -
                             c) /
                            a,
                            0.0,
                            1.0);
                }
            }
        }

        var firstClosest =
            new WorldVector3(
                firstStart.X +
                d1X *
                s,
                firstStart.Y +
                d1Y *
                s,
                firstStart.Z +
                d1Z *
                s);

        var secondClosest =
            new WorldVector3(
                secondStart.X +
                d2X *
                t,
                secondStart.Y +
                d2Y *
                t,
                secondStart.Z +
                d2Z *
                t);

        return DistanceSquared(
            firstClosest,
            secondClosest);
    }

    private void UpdateTrafficSignalGroups(
        double deltaSeconds)
    {
        foreach (var group in
                 _trafficSignalGroups.Values)
        {
            AdvanceTrafficSignalGroup(
                group,
                deltaSeconds);
        }
    }

    private void AdvanceTrafficSignalGroup(
        TrafficSignalGroupState group,
        double deltaSeconds)
    {
        if (!double.IsFinite(
                deltaSeconds) ||
            deltaSeconds <=
                0.0 ||
            !double.IsFinite(
                group.CycleSeconds) ||
            group.CycleSeconds <=
                0.0)
        {
            return;
        }

        var remaining =
            deltaSeconds;

        var guard =
            0;

        while (remaining >
                   0.000001 &&
               guard++ <
                   32)
        {
            SignalControlEvent? nextEvent =
                null;

            foreach (var jump in
                     group.Jumps)
            {
                var distance =
                    ForwardCycleDistance(
                        group.PositionSeconds,
                        jump.TriggerTimeSeconds,
                        group.CycleSeconds);

                if (distance <=
                        remaining +
                            0.000001 &&
                    (!nextEvent.HasValue ||
                     distance <
                         nextEvent.Value.DistanceSeconds))
                {
                    nextEvent =
                        new SignalControlEvent(
                            distance,
                            jump.CheckTrafficLightIndex,
                            jump.JumpIfNoApproach,
                            jump.TargetTimeSeconds,
                            IsStop:
                                false);
                }
            }

            foreach (var stop in
                     group.Stops)
            {
                var distance =
                    ForwardCycleDistance(
                        group.PositionSeconds,
                        stop.TriggerTimeSeconds,
                        group.CycleSeconds);

                if (distance <=
                        remaining +
                            0.000001 &&
                    (!nextEvent.HasValue ||
                     distance <
                         nextEvent.Value.DistanceSeconds))
                {
                    nextEvent =
                        new SignalControlEvent(
                            distance,
                            stop.CheckTrafficLightIndex,
                            stop.StopIfNoApproach,
                            TargetTimeSeconds:
                                stop.TriggerTimeSeconds,
                            IsStop:
                                true);
                }
            }

            if (!nextEvent.HasValue)
            {
                group.PositionSeconds =
                    WrapCyclePosition(
                        group.PositionSeconds +
                            remaining,
                        group.CycleSeconds);
                break;
            }

            var control =
                nextEvent.Value;

            group.PositionSeconds =
                WrapCyclePosition(
                    group.PositionSeconds +
                        control.DistanceSeconds,
                    group.CycleSeconds);

            remaining -=
                control.DistanceSeconds;

            var hasApproach =
                HasTrafficSignalApproach(
                    group,
                    control.CheckTrafficLightIndex);

            var shouldApply =
                control.IfNoApproach
                    ? !hasApproach
                    : hasApproach;

            if (!shouldApply)
            {
                var pass =
                    Math.Min(
                        remaining,
                        0.000001);

                if (pass <=
                    0.0)
                {
                    break;
                }

                group.PositionSeconds =
                    WrapCyclePosition(
                        group.PositionSeconds +
                            pass,
                        group.CycleSeconds);

                remaining -=
                    pass;
                continue;
            }

            if (control.IsStop)
            {
                break;
            }

            var target =
                WrapCyclePosition(
                    control.TargetTimeSeconds,
                    group.CycleSeconds);

            if (Math.Abs(
                    target -
                    group.PositionSeconds) <
                0.000001)
            {
                var pass =
                    Math.Min(
                        remaining,
                        0.000001);

                if (pass <=
                    0.0)
                {
                    break;
                }

                group.PositionSeconds =
                    WrapCyclePosition(
                        group.PositionSeconds +
                            pass,
                        group.CycleSeconds);

                remaining -=
                    pass;
                continue;
            }

            group.PositionSeconds =
                target;
        }
    }

    private bool HasTrafficSignalApproach(
        TrafficSignalGroupState group,
        int signalIndex)
    {
        var targetSegments =
            group.Segments
                .Where(
                    segment =>
                        segment.TrafficSignal?.SignalIndex ==
                            signalIndex)
                .ToArray();

        if (targetSegments.Length ==
            0)
        {
            return false;
        }

        foreach (var target in
                 targetSegments)
        {
            var approachDistance =
                Math.Max(
                    target.TrafficSignal?.ApproachDistanceMeters ??
                        0.0,
                    0.0);

            foreach (var agent in
                     _agents)
            {
                if (agent.PendingRespawn ||
                    agent.ActivationTimeSeconds >
                        _simulationElapsedSeconds)
                {
                    continue;
                }

                if (DistanceAheadToSegment(
                        agent,
                        target,
                        approachDistance) <=
                    approachDistance +
                        0.0001)
                {
                    return true;
                }
            }

            if (ExternalObstacleApproachesSegment(
                    target,
                    approachDistance))
            {
                return true;
            }
        }

        return false;
    }

    private bool ExternalObstacleApproachesSegment(
        WorldTrafficPathSegment target,
        double maximumDistance)
    {
        if (_externalObstacle is not
                { } obstacle ||
            maximumDistance <
                0.0 ||
            !TryResolveExternalObstaclePath(
                obstacle,
                out var current,
                out var travelForward,
                out var distanceAlongCurrent))
        {
            return false;
        }

        if (current.Index ==
            target.Index)
        {
            return true;
        }

        var currentLength =
            SegmentLength(
                current);

        var distance =
            travelForward
                ? Math.Max(
                    currentLength -
                        distanceAlongCurrent,
                    0.0)
                : Math.Max(
                    distanceAlongCurrent,
                    0.0);

        var guard =
            0;

        while (guard++ <
               MaximumTrafficLookAheadSegments)
        {
            if (distance >
                maximumDistance +
                    0.0001)
            {
                return false;
            }

            var connections =
                travelForward
                    ? current.ForwardConnections
                    : current.ReverseConnections;

            if (connections.Count !=
                1)
            {
                // The player path is not known beyond an ambiguous branch.
                // Do not guess which signal request should be triggered.
                return false;
            }

            if (!_segmentsByIndex.TryGetValue(
                    connections[0],
                    out var next))
            {
                return false;
            }

            if (next.Index ==
                target.Index)
            {
                return true;
            }

            distance +=
                SegmentLength(
                    next);

            current =
                next;
        }

        return false;
    }

    private bool TryResolveExternalObstaclePath(
        WorldTrafficObstacleState obstacle,
        out WorldTrafficPathSegment segment,
        out bool travelForward,
        out double distanceAlongSegment)
    {
        segment =
            default!;
        travelForward =
            true;
        distanceAlongSegment =
            0.0;

        var bestDistanceSquared =
            double.PositiveInfinity;

        var obstacleForwardX =
            Math.Sin(
                obstacle.HeadingRadians);

        var obstacleForwardZ =
            Math.Cos(
                obstacle.HeadingRadians);

        foreach (var candidate in
                 _roadSegments)
        {
            if (candidate.Points.Count <
                2)
            {
                continue;
            }

            var accumulated =
                0.0;

            for (var index = 1;
                 index <
                     candidate.Points.Count;
                 index++)
            {
                var a =
                    candidate.Points[
                        index - 1];

                var b =
                    candidate.Points[
                        index];

                if (Math.Abs(
                        obstacle.Position.Y -
                        (a.Y +
                         b.Y) *
                            0.5) >
                    5.0)
                {
                    accumulated +=
                        Distance(
                            a,
                            b);
                    continue;
                }

                var dx =
                    b.X -
                    a.X;

                var dz =
                    b.Z -
                    a.Z;

                var lengthSquared =
                    dx *
                        dx +
                    dz *
                        dz;

                if (lengthSquared <=
                    0.000001)
                {
                    continue;
                }

                var length =
                    Math.Sqrt(
                        lengthSquared);

                var normalizedX =
                    dx /
                    length;

                var normalizedZ =
                    dz /
                    length;

                var alignment =
                    obstacleForwardX *
                        normalizedX +
                    obstacleForwardZ *
                        normalizedZ;

                var candidateTravelForward =
                    alignment >=
                    0.0;

                var directionAllowed =
                    candidate.Direction switch
                    {
                        0 =>
                            candidateTravelForward,
                        1 =>
                            !candidateTravelForward,
                        2 =>
                            true,
                        _ =>
                            true
                    };

                if (!directionAllowed ||
                    Math.Abs(
                        alignment) <
                    0.25)
                {
                    accumulated +=
                        length;
                    continue;
                }

                var t =
                    Math.Clamp(
                        ((obstacle.Position.X -
                          a.X) *
                             dx +
                         (obstacle.Position.Z -
                          a.Z) *
                             dz) /
                        lengthSquared,
                        0.0,
                        1.0);

                var closestX =
                    a.X +
                    dx *
                        t;

                var closestZ =
                    a.Z +
                    dz *
                        t;

                var offsetX =
                    obstacle.Position.X -
                    closestX;

                var offsetZ =
                    obstacle.Position.Z -
                    closestZ;

                var distanceSquared =
                    offsetX *
                        offsetX +
                    offsetZ *
                        offsetZ;

                if (distanceSquared >=
                    bestDistanceSquared)
                {
                    accumulated +=
                        length;
                    continue;
                }

                bestDistanceSquared =
                    distanceSquared;

                segment =
                    candidate;

                travelForward =
                    candidateTravelForward;

                distanceAlongSegment =
                    accumulated +
                    length *
                        t;

                accumulated +=
                    length;
            }
        }

        // Match the renderer's existing 8 m path-proximity ceiling.
        return !double.IsPositiveInfinity(
                   bestDistanceSquared) &&
               bestDistanceSquared <=
                   64.0;
    }

    private double DistanceAheadToSegment(
        Agent agent,
        WorldTrafficPathSegment target,
        double maximumDistance)
    {
        if (!_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var current))
        {
            return double.PositiveInfinity;
        }

        if (current.Index ==
            target.Index)
        {
            return 0.0;
        }

        var currentLength =
            SegmentLength(
                current);

        var distance =
            agent.TravelForward
                ? Math.Max(
                    currentLength -
                        agent.DistanceMeters,
                    0.0)
                : Math.Max(
                    agent.DistanceMeters,
                    0.0);

        var guard =
            0;

        while (guard++ <
               MaximumTrafficLookAheadSegments)
        {
            if (distance >
                maximumDistance +
                    0.0001)
            {
                return double.PositiveInfinity;
            }

            var nextIndex =
                ResolveNextSegmentIndex(
                    agent,
                    current);

            if (!nextIndex.HasValue ||
                !_segmentsByIndex.TryGetValue(
                    nextIndex.Value,
                    out var next))
            {
                return double.PositiveInfinity;
            }

            if (next.Index ==
                target.Index)
            {
                return distance;
            }

            distance +=
                SegmentLength(
                    next);

            current =
                next;
        }

        return double.PositiveInfinity;
    }

    private static double ForwardCycleDistance(
        double position,
        double trigger,
        double cycleSeconds)
    {
        var normalizedPosition =
            WrapCyclePosition(
                position,
                cycleSeconds);

        var normalizedTrigger =
            WrapCyclePosition(
                trigger,
                cycleSeconds);

        var distance =
            normalizedTrigger -
            normalizedPosition;

        if (distance <
            -0.000001)
        {
            distance +=
                cycleSeconds;
        }

        return Math.Abs(
                   distance) <=
               0.000001
            ? 0.0
            : distance;
    }

    private static double WrapCyclePosition(
        double position,
        double cycleSeconds)
    {
        if (!double.IsFinite(
                position) ||
            !double.IsFinite(
                cycleSeconds) ||
            cycleSeconds <=
                0.0)
        {
            return 0.0;
        }

        var wrapped =
            position %
            cycleSeconds;

        return wrapped <
               0.0
            ? wrapped +
                cycleSeconds
            : wrapped;
    }

    private static double DistanceSquared(
        WorldVector3 first,
        WorldVector3 second)
    {
        var x =
            first.X -
            second.X;
        var y =
            first.Y -
            second.Y;
        var z =
            first.Z -
            second.Z;

        return x *
                   x +
               y *
                   y +
               z *
                   z;
    }

    private bool IsTrafficSignalGreen(
        WorldTrafficPathSegment segment)
    {
        var signal =
            segment.TrafficSignal;

        if (signal is null)
        {
            return true;
        }

        var elapsedSeconds =
            segment.SceneryObjectId.HasValue &&
            _trafficSignalGroups.TryGetValue(
                segment.SceneryObjectId.Value,
                out var group)
                ? group.PositionSeconds
                : _simulationElapsedSeconds;

        return IsTrafficSignalGreen(
            signal,
            elapsedSeconds);
    }

    private static bool IsTrafficSignalGreen(
        WorldTrafficSignalProgram? signal,
        double elapsedSeconds) =>
        ResolveTrafficSignalPhase(
            signal,
            elapsedSeconds) is
            >= 6 and <= 8;

    private static int? ResolveTrafficSignalPhase(
        WorldTrafficSignalProgram? signal,
        double elapsedSeconds)
    {
        if (signal is null ||
            signal.Phases.Count ==
                0 ||
            !double.IsFinite(
                elapsedSeconds))
        {
            return null;
        }

        var phaseDuration =
            signal.Phases
                .Where(
                    static phase =>
                        phase.DurationSeconds >
                            0.0 &&
                        double.IsFinite(
                            phase.DurationSeconds))
                .Sum(
                    static phase =>
                        phase.DurationSeconds);

        var cycleSeconds =
            signal.CycleSeconds;

        if (!double.IsFinite(
                cycleSeconds) ||
            cycleSeconds <=
                0.0)
        {
            cycleSeconds =
                phaseDuration;
        }

        if (cycleSeconds <=
                0.0 ||
            phaseDuration <=
                0.0)
        {
            return null;
        }

        var position =
            elapsedSeconds %
            cycleSeconds;

        if (position <
            0.0)
        {
            position +=
                cycleSeconds;
        }

        WorldTrafficSignalPhase? lastPhase =
            null;

        foreach (var phase in
                 signal.Phases)
        {
            if (phase.DurationSeconds <=
                    0.0 ||
                !double.IsFinite(
                    phase.DurationSeconds))
            {
                continue;
            }

            lastPhase =
                phase;

            if (position <
                phase.DurationSeconds)
            {
                return phase.Phase;
            }

            position -=
                phase.DurationSeconds;
        }

        return lastPhase?.Phase;
    }

    private int? ResolveNextSegmentIndex(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var connections =
            agent.TravelForward
                ? segment.ForwardConnections
                : segment.ReverseConnections;

        var candidates =
            connections
                .Select(
                    candidateIndex =>
                        _segmentsByIndex.TryGetValue(
                            candidateIndex,
                            out var candidate)
                            ? candidate
                            : null)
                .Where(
                    candidate =>
                        candidate is not null &&
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
            (((agent.AgentIndex +
               1) *
              0.6180339887498949 +
              (segment.Index +
               1) *
              0.4142135623730950) %
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
                return candidate
                    .Segment
                    .Index;
            }
        }

        return candidates[^1]
            .Segment
            .Index;
    }

    private Dictionary<int, List<Agent>> BuildActiveAgentBuckets()
    {
        var buckets =
            new Dictionary<int, List<Agent>>();

        foreach (var agent in
                 _agents)
        {
            if (agent.PendingRespawn ||
                agent.ActivationTimeSeconds >
                    _simulationElapsedSeconds)
            {
                continue;
            }

            AddActiveAgentBucket(
                buckets,
                agent);
        }

        return buckets;
    }

    private IEnumerable<Agent> EnumerateCrossingCandidateAgents(
        long crossingId,
        IReadOnlyDictionary<int, List<Agent>> activeAgentsBySegment)
    {
        if (!_crossingCandidateSegmentsBySceneryObjectId.TryGetValue(
                crossingId,
                out var candidateSegmentIndices))
        {
            yield break;
        }

        foreach (var segmentIndex in
                 candidateSegmentIndices)
        {
            if (!activeAgentsBySegment.TryGetValue(
                    segmentIndex,
                    out var candidates))
            {
                continue;
            }

            foreach (var candidate in
                     candidates)
            {
                yield return candidate;
            }
        }
    }

    private static IReadOnlyList<Agent> GetActiveSegmentAgents(
        int segmentIndex,
        IReadOnlyDictionary<int, List<Agent>> activeAgentsBySegment) =>
        activeAgentsBySegment.TryGetValue(
            segmentIndex,
            out var agents)
            ? agents
            : Array.Empty<Agent>();

    private static void AddActiveAgentBucket(
        Dictionary<int, List<Agent>> buckets,
        Agent agent)
    {
        if (!buckets.TryGetValue(
                agent.SegmentIndex,
                out var bucket))
        {
            bucket =
                [];
            buckets[
                agent.SegmentIndex] =
                bucket;
        }

        if (!bucket.Contains(
                agent))
        {
            bucket.Add(
                agent);
        }
    }

    private static void RemoveActiveAgentBucket(
        Dictionary<int, List<Agent>> buckets,
        int segmentIndex,
        Agent agent)
    {
        if (!buckets.TryGetValue(
                segmentIndex,
                out var bucket))
        {
            return;
        }

        bucket.Remove(
            agent);

        if (bucket.Count ==
            0)
        {
            buckets.Remove(
                segmentIndex);
        }
    }

    private static void MoveActiveAgentBucket(
        Dictionary<int, List<Agent>> buckets,
        int previousSegmentIndex,
        Agent agent)
    {
        if (previousSegmentIndex ==
            agent.SegmentIndex)
        {
            return;
        }

        RemoveActiveAgentBucket(
            buckets,
            previousSegmentIndex,
            agent);
        AddActiveAgentBucket(
            buckets,
            agent);
    }

    private TrafficLead? FindLeadingObservation(
        Agent agent,
        IReadOnlyDictionary<int, List<Agent>> activeAgentsBySegment)
    {
        TrafficLead? nearest =
            null;

        foreach (var segmentIndex in
                 EnumerateTrafficLookAheadSegments(
                     agent))
        {
            if (!activeAgentsBySegment.TryGetValue(
                    segmentIndex,
                    out var candidates))
            {
                continue;
            }

            foreach (var candidate in
                     candidates)
            {
                if (ReferenceEquals(
                        candidate,
                        agent) ||
                    candidate.TravelForward !=
                        agent.TravelForward)
                {
                    continue;
                }

                var distance =
                    DistanceAlongRoute(
                        agent,
                        candidate);

                if (!distance.HasValue)
                {
                    continue;
                }

                var bumperClearance =
                    distance.Value -
                    EstimateTrafficVehicleHalfLength(
                        agent.VehiclePath) -
                    EstimateTrafficVehicleHalfLength(
                        candidate.VehiclePath);

                if (bumperClearance >
                        0.0001 &&
                    (!nearest.HasValue ||
                     bumperClearance <
                        nearest.Value.DistanceMeters))
                {
                    nearest =
                        new TrafficLead(
                            bumperClearance,
                            Math.Max(
                                candidate.SpeedMetersPerSecond,
                                0.0));
                }
            }
        }

        var externalLead =
            FindExternalObstacleLead(
                agent);

        if (externalLead.HasValue &&
            (!nearest.HasValue ||
             externalLead.Value.DistanceMeters <
                nearest.Value.DistanceMeters))
        {
            nearest =
                externalLead;
        }

        return nearest;
    }

    private IEnumerable<int> EnumerateTrafficLookAheadSegments(
        Agent agent)
    {
        if (!_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            yield break;
        }

        yield return segment.Index;

        var remainingLookAhead =
            TrafficLookAheadMeters;

        var segmentLength =
            SegmentLength(
                segment);

        remainingLookAhead -=
            agent.TravelForward
                ? Math.Max(
                    segmentLength -
                        agent.DistanceMeters,
                    0.0)
                : Math.Max(
                    agent.DistanceMeters,
                    0.0);

        var current =
            segment;

        var visited =
            new HashSet<int>
            {
                segment.Index
            };

        for (var hop = 0;
             hop <
                 MaximumTrafficLookAheadSegments &&
             remainingLookAhead >
                 0.0;
             hop++)
        {
            var nextIndex =
                ResolveNextSegmentIndex(
                    agent,
                    current);

            if (!nextIndex.HasValue ||
                !visited.Add(
                    nextIndex.Value) ||
                !_segmentsByIndex.TryGetValue(
                    nextIndex.Value,
                    out var next))
            {
                yield break;
            }

            yield return next.Index;

            remainingLookAhead -=
                SegmentLength(
                    next);

            current =
                next;
        }
    }

    private TrafficLead? FindExternalObstacleLead(
        Agent agent)
    {
        if (_externalObstacle is not
                { } obstacle ||
            !_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            return null;
        }

        SampleSegment(
            segment,
            agent.DistanceMeters,
            out var agentPosition,
            out var agentHeading);

        if (!agent.TravelForward)
        {
            agentHeading =
                ReverseHeading(
                    agentHeading);
        }

        if (Math.Abs(
                obstacle.Position.Y -
                agentPosition.Y) >
            3.5)
        {
            return null;
        }

        var deltaX =
            obstacle.Position.X -
            agentPosition.X;
        var deltaZ =
            obstacle.Position.Z -
            agentPosition.Z;

        var forwardX =
            Math.Sin(
                agentHeading);
        var forwardZ =
            Math.Cos(
                agentHeading);

        var rightX =
            forwardZ;
        var rightZ =
            -forwardX;

        var longitudinal =
            deltaX *
                forwardX +
            deltaZ *
                forwardZ;

        if (longitudinal <=
                0.0 ||
            longitudinal >
                TrafficLookAheadMeters)
        {
            return null;
        }

        var lateral =
            Math.Abs(
                deltaX *
                    rightX +
                deltaZ *
                    rightZ);

        var obstacleForwardX =
            Math.Sin(
                obstacle.HeadingRadians);
        var obstacleForwardZ =
            Math.Cos(
                obstacle.HeadingRadians);

        var obstacleRightX =
            obstacleForwardZ;
        var obstacleRightZ =
            -obstacleForwardX;

        var obstacleLateralExtent =
            Math.Abs(
                obstacleForwardX *
                    rightX +
                obstacleForwardZ *
                    rightZ) *
                obstacle.HalfLengthMeters +
            Math.Abs(
                obstacleRightX *
                    rightX +
                obstacleRightZ *
                    rightZ) *
                obstacle.HalfWidthMeters;

        var obstacleLongitudinalExtent =
            Math.Abs(
                obstacleForwardX *
                    forwardX +
                obstacleForwardZ *
                    forwardZ) *
                obstacle.HalfLengthMeters +
            Math.Abs(
                obstacleRightX *
                    forwardX +
                obstacleRightZ *
                    forwardZ) *
                obstacle.HalfWidthMeters;

        var pathHalfWidth =
            Math.Max(
                Math.Abs(
                    segment.WidthMeters) *
                    0.5,
                1.1);

        var maximumLateral =
            pathHalfWidth +
            Math.Max(
                obstacleLateralExtent,
                0.45);

        if (lateral >
            maximumLateral)
        {
            return null;
        }

        var headingDelta =
            NormalizeHeadingDelta(
                obstacle.HeadingRadians -
                agentHeading);

        var obstacleHeadingDelta =
            Math.Abs(
                headingDelta);

        var obstacleCrossesLane =
            obstacleHeadingDelta >=
                Math.PI /
                    4.0 &&
            obstacleHeadingDelta <=
                3.0 *
                    Math.PI /
                    4.0 &&
            lateral <=
                maximumLateral;

        if (obstacleCrossesLane)
        {
            // A long vehicle already crossing the AI corridor is not a
            // conventional lead vehicle. Hold before its oriented footprint
            // instead of allowing a following-distance approximation to
            // enter the occupied lane.
            return new TrafficLead(
                0.0,
                0.0);
        }

        var projectedObstacleSpeed =
            Math.Max(
                obstacle.SpeedMetersPerSecond *
                    Math.Cos(
                        headingDelta),
                0.0);

        return new TrafficLead(
            Math.Max(
                longitudinal -
                    Math.Max(
                        obstacleLongitudinalExtent,
                        2.0) -
                    EstimateTrafficVehicleHalfLength(
                        agent.VehiclePath),
                0.0),
            projectedObstacleSpeed);
    }

    private double? DistanceAlongRoute(
        Agent source,
        Agent target)
    {
        if (!_segmentsByIndex.TryGetValue(
                source.SegmentIndex,
                out var sourceSegment) ||
            !_segmentsByIndex.TryGetValue(
                target.SegmentIndex,
                out _))
        {
            return null;
        }

        if (source.SegmentIndex ==
            target.SegmentIndex)
        {
            var sameSegmentDistance =
                source.TravelForward
                    ? target.DistanceMeters -
                      source.DistanceMeters
                    : source.DistanceMeters -
                      target.DistanceMeters;

            return sameSegmentDistance >
                   0.0001
                ? sameSegmentDistance
                : null;
        }

        var sourceLength =
            SegmentLength(
                sourceSegment);

        var accumulated =
            source.TravelForward
                ? Math.Max(
                    sourceLength -
                        source.DistanceMeters,
                    0.0)
                : Math.Max(
                    source.DistanceMeters,
                    0.0);

        var current =
            sourceSegment;

        var visited =
            new HashSet<int>
            {
                sourceSegment.Index
            };

        for (var hop = 0;
             hop <
                 MaximumTrafficLookAheadSegments &&
             accumulated <=
                 TrafficLookAheadMeters;
             hop++)
        {
            var nextIndex =
                ResolveNextSegmentIndex(
                    source,
                    current);

            if (!nextIndex.HasValue ||
                !visited.Add(
                    nextIndex.Value) ||
                !_segmentsByIndex.TryGetValue(
                    nextIndex.Value,
                    out var nextSegment))
            {
                return null;
            }

            var nextLength =
                SegmentLength(
                    nextSegment);

            if (nextSegment.Index ==
                target.SegmentIndex)
            {
                var targetDistanceFromEntry =
                    source.TravelForward
                        ? Math.Clamp(
                            target.DistanceMeters,
                            0.0,
                            nextLength)
                        : Math.Clamp(
                            nextLength -
                                target.DistanceMeters,
                            0.0,
                            nextLength);

                return accumulated +
                       targetDistanceFromEntry;
            }

            accumulated +=
                nextLength;

            current =
                nextSegment;
        }

        return null;
    }

    private double ResolveTargetSpeed(
        Agent agent,
        TrafficLead? leading,
        double? blockedEntryDistance)
    {
        var segmentMaximum =
            _segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment)
                ? ResolveSegmentMaximumSpeed(
                    segment,
                    agent.CruiseSpeedMetersPerSecond)
                : agent.CruiseSpeedMetersPerSecond;

        var targetSpeed =
            segmentMaximum;

        if (segment is not null)
        {
            var curvature =
                Math.Abs(
                    ResolvePathCurvaturePerMeter(
                        agent,
                        segment));

            if (double.IsFinite(
                    curvature) &&
                curvature >
                    0.001)
            {
                var curveLimitedSpeed =
                    Math.Sqrt(
                        TrafficMaximumLateralAccelerationMetersPerSecondSquared /
                        curvature);

                targetSpeed =
                    Math.Min(
                        targetSpeed,
                        curveLimitedSpeed);
            }
        }

        if (leading.HasValue)
        {
            var usableDistance =
                Math.Max(
                    leading.Value.DistanceMeters -
                        MinimumTrafficSeparationMeters,
                    0.0);

            var leaderSpeed =
                Math.Max(
                    leading.Value.SpeedMetersPerSecond,
                    0.0);

            // Keep a time headway while also accounting for the distance
            // the vehicle ahead can cover before stopping. This avoids the
            // old distance-only controller's stop/go behavior in queues and
            // gives progressive braking when the lead vehicle slows.
            var leaderStoppingDistance =
                leaderSpeed *
                leaderSpeed /
                (2.0 *
                 TrafficBrakingMetersPerSecondSquared);

            var headwayBrakingTerm =
                TrafficBrakingMetersPerSecondSquared *
                ResolveFollowingTimeHeadwaySeconds(
                    agent);

            var followingSpeed =
                Math.Max(
                    -headwayBrakingTerm +
                    Math.Sqrt(
                        headwayBrakingTerm *
                            headwayBrakingTerm +
                        2.0 *
                            TrafficBrakingMetersPerSecondSquared *
                            (usableDistance +
                             leaderStoppingDistance)),
                    0.0);

            targetSpeed =
                Math.Min(
                    targetSpeed,
                    followingSpeed);
        }

        if (blockedEntryDistance.HasValue)
        {
            var usableStopDistance =
                Math.Max(
                    blockedEntryDistance.Value -
                        TrafficStopLineBufferMeters,
                    0.0);

            var brakingLimitedSpeed =
                Math.Sqrt(
                    2.0 *
                    TrafficBrakingMetersPerSecondSquared *
                    usableStopDistance);

            targetSpeed =
                Math.Min(
                    targetSpeed,
                    brakingLimitedSpeed);
        }

        return targetSpeed;
    }

    private double? ResolveBlockedEntryDistance(
        Agent agent,
        WorldTrafficPathSegment segment,
        IReadOnlyDictionary<int, List<Agent>> activeAgentsBySegment)
    {
        var nextIndex =
            ResolveNextSegmentIndex(
                agent,
                segment);

        if (!nextIndex.HasValue ||
            !_segmentsByIndex.TryGetValue(
                nextIndex.Value,
                out var nextSegment) ||
            CanEnterSegment(
                agent,
                segment,
                nextSegment,
                activeAgentsBySegment))
        {
            return null;
        }

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

    private static bool HasUsableTrafficExit(
        WorldTrafficPathSegment segment) =>
        segment.Direction switch
        {
            1 =>
                segment.ReverseConnections.Count >
                    0,
            2 =>
                segment.ForwardConnections.Count >
                    0 ||
                segment.ReverseConnections.Count >
                    0,
            _ =>
                segment.ForwardConnections.Count >
                    0
        };

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
        WorldTrafficPathSegment segment,
        double cruiseSpeedMetersPerSecond)
    {
        if (!segment.SpeedLimitKilometersPerHour.HasValue ||
            !double.IsFinite(
                segment.SpeedLimitKilometersPerHour.Value) ||
            segment.SpeedLimitKilometersPerHour.Value <=
                0.0)
        {
            return cruiseSpeedMetersPerSecond;
        }

        return Math.Min(
            cruiseSpeedMetersPerSecond,
            segment.SpeedLimitKilometersPerHour.Value /
                3.6);
    }

    private static void UpdateAgentSpeed(
        Agent agent,
        double targetSpeed,
        double stepSeconds)
    {
        var clampedTarget =
            Math.Clamp(
                targetSpeed,
                0.0,
                agent.CruiseSpeedMetersPerSecond);

        var rate =
            clampedTarget <
                agent.SpeedMetersPerSecond
                ? TrafficBrakingMetersPerSecondSquared
                : TrafficAccelerationMetersPerSecondSquared;

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

    private WorldTrafficAgentState CreateState(
        Agent agent)
    {
        if (!_segmentsByIndex.TryGetValue(
                agent.SegmentIndex,
                out var segment))
        {
            return new WorldTrafficAgentState(
                agent.AgentIndex,
                agent.SegmentIndex,
                agent.DistanceMeters,
                agent.SpeedMetersPerSecond,
                agent.VehiclePath,
                default,
                0.0,
                agent.GroupIndex,
                agent.GroupName,
                agent.BrakeLight,
                false,
                false,
                agent.TraveledDistanceMeters,
                0.0);
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

        ResolveTurnIndicators(
            agent,
            segment,
            out var blinkerLeft,
            out var blinkerRight);

        var pathCurvaturePerMeter =
            ResolvePathCurvaturePerMeter(
                agent,
                segment);

        return new WorldTrafficAgentState(
            agent.AgentIndex,
            agent.SegmentIndex,
            agent.DistanceMeters,
            agent.SpeedMetersPerSecond,
            agent.VehiclePath,
            position,
            heading,
            agent.GroupIndex,
            agent.GroupName,
            agent.BrakeLight,
            blinkerLeft,
            blinkerRight,
            agent.TraveledDistanceMeters,
            pathCurvaturePerMeter);
    }

    private static bool IsRoadVehicle(
        OmsiAiVehicleDefinition vehicle)
    {
        var extension =
            Path.GetExtension(
                vehicle.DeclaredPath);

        return extension.Equals(
                   ".bus",
                   StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(
                   ".ovh",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static WorldTrafficPathSegment?
        SelectWeightedSpawnSegment(
            IReadOnlyList<(WorldTrafficPathSegment Segment, double Weight)> candidates,
            double selectorUnit)
    {
        if (candidates.Count ==
            0)
        {
            return null;
        }

        var totalWeight =
            candidates.Sum(
                static candidate =>
                    Math.Max(
                        candidate.Weight,
                        0.0));

        if (!double.IsFinite(
                totalWeight) ||
            totalWeight <=
                0.0)
        {
            return null;
        }

        var selector =
            Math.Clamp(
                selectorUnit,
                0.0,
                0.999999999) *
            totalWeight;

        foreach (var candidate in
                 candidates)
        {
            selector -=
                Math.Max(
                    candidate.Weight,
                    0.0);

            if (selector <=
                0.0)
            {
                return candidate.Segment;
            }
        }

        return candidates[^1].Segment;
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
                        0.0));

        var selector =
            (((index +
               1) *
              0.6180339887498949) %
             1.0) *
            totalWeight;

        foreach (var vehicle in
                 vehicles)
        {
            selector -=
                Math.Max(
                    vehicle.Weight,
                    0.0);

            if (selector <=
                0.0)
            {
                return vehicle;
            }
        }

        return vehicles[^1];
    }

    private static double ResolveFollowingTimeHeadwaySeconds(
        Agent agent)
    {
        var extension =
            Path.GetExtension(
                agent.VehiclePath);

        if (extension.Equals(
                ".bus",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1.60;
        }

        return FollowingTimeHeadwaySeconds;
    }

    private static double EstimateTrafficVehicleHalfWidth(
        string? vehiclePath)
    {
        if (string.IsNullOrWhiteSpace(
                vehiclePath))
        {
            return 1.15;
        }

        var extension =
            Path.GetExtension(
                vehiclePath);

        if (extension.Equals(
                ".bus",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1.30;
        }

        if (extension.Equals(
                ".ovh",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1.10;
        }

        return 1.15;
    }

    private static double EstimateTrafficVehicleHalfLength(
        string? vehiclePath)
    {
        if (string.IsNullOrWhiteSpace(
                vehiclePath))
        {
            return 2.6;
        }

        var extension =
            Path.GetExtension(
                vehiclePath);

        if (extension.Equals(
                ".bus",
                StringComparison.OrdinalIgnoreCase))
        {
            return 6.0;
        }

        if (extension.Equals(
                ".ovh",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2.4;
        }

        return 2.6;
    }

    private static double ResolveCruiseSpeed(
        int index)
    {
        var kilometersPerHour =
            25.0 +
            (index %
             5) *
            5.0;

        return kilometersPerHour /
               3.6;
    }

    private double SegmentLength(
        WorldTrafficPathSegment segment)
    {
        if (_segmentLengthsByIndex.TryGetValue(
                segment.Index,
                out var length))
        {
            return length;
        }

        return CalculateSegmentLength(
            segment);
    }

    private static double CalculateSegmentLength(
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
                    Lerp(
                        a,
                        b,
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

    private double ResolvePathCurvaturePerMeter(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        const double lookAheadMeters =
            6.0;

        SampleSegment(
            segment,
            agent.DistanceMeters,
            out _,
            out var currentHeading);

        if (!agent.TravelForward)
        {
            currentHeading =
                ReverseHeading(
                    currentHeading);
        }

        if (!TrySampleRouteHeadingAhead(
                agent,
                segment,
                lookAheadMeters,
                out var futureHeading,
                out var traveledMeters) ||
            traveledMeters <=
                0.001)
        {
            return 0.0;
        }

        var delta =
            NormalizeHeadingDelta(
                futureHeading -
                currentHeading);

        var curvature =
            delta /
            traveledMeters;

        return double.IsFinite(
                   curvature)
            ? curvature
            : 0.0;
    }

    private bool TrySampleRouteHeadingAhead(
        Agent agent,
        WorldTrafficPathSegment startSegment,
        double lookAheadMeters,
        out double headingRadians,
        out double traveledMeters)
    {
        headingRadians =
            0.0;
        traveledMeters =
            0.0;

        if (!double.IsFinite(
                lookAheadMeters) ||
            lookAheadMeters <=
                0.0)
        {
            return false;
        }

        var current =
            startSegment;

        var currentDistance =
            Math.Clamp(
                agent.DistanceMeters,
                0.0,
                SegmentLength(
                    current));

        var remaining =
            lookAheadMeters;

        var visited =
            new HashSet<int>
            {
                current.Index
            };

        for (var hop = 0;
             hop <
                 MaximumTrafficLookAheadSegments;
             hop++)
        {
            var length =
                SegmentLength(
                    current);

            var available =
                agent.TravelForward
                    ? Math.Max(
                        length -
                            currentDistance,
                        0.0)
                    : Math.Max(
                        currentDistance,
                        0.0);

            if (remaining <=
                    available &&
                remaining >
                    0.000001)
            {
                var sampleDistance =
                    agent.TravelForward
                        ? currentDistance +
                          remaining
                        : currentDistance -
                          remaining;

                SampleSegment(
                    current,
                    sampleDistance,
                    out _,
                    out headingRadians);

                if (!agent.TravelForward)
                {
                    headingRadians =
                        ReverseHeading(
                            headingRadians);
                }

                traveledMeters +=
                    remaining;

                return true;
            }

            if (available >
                0.000001)
            {
                traveledMeters +=
                    available;

                remaining -=
                    available;
            }

            var nextIndex =
                ResolveNextSegmentIndex(
                    agent,
                    current);

            if (!nextIndex.HasValue ||
                !visited.Add(
                    nextIndex.Value) ||
                !_segmentsByIndex.TryGetValue(
                    nextIndex.Value,
                    out var nextSegment))
            {
                if (traveledMeters <=
                    0.000001)
                {
                    return false;
                }

                var sampleDistance =
                    agent.TravelForward
                        ? length
                        : 0.0;

                SampleSegment(
                    current,
                    sampleDistance,
                    out _,
                    out headingRadians);

                if (!agent.TravelForward)
                {
                    headingRadians =
                        ReverseHeading(
                            headingRadians);
                }

                return true;
            }

            current =
                nextSegment;

            currentDistance =
                agent.TravelForward
                    ? 0.0
                    : SegmentLength(
                        current);
        }

        return false;
    }

    private void ResolveTurnIndicators(
        Agent agent,
        WorldTrafficPathSegment segment,
        out bool left,
        out bool right)
    {
        left =
            false;
        right =
            false;

        var currentLength =
            SegmentLength(
                segment);

        var distanceToExit =
            agent.TravelForward
                ? Math.Max(
                    currentLength -
                        agent.DistanceMeters,
                    0.0)
                : Math.Max(
                    agent.DistanceMeters,
                    0.0);

        if (distanceToExit >
            30.0)
        {
            return;
        }

        var currentSampleDistance =
            agent.TravelForward
                ? currentLength
                : 0.0;

        SampleSegment(
            segment,
            currentSampleDistance,
            out _,
            out var currentHeading);

        if (!agent.TravelForward)
        {
            currentHeading =
                ReverseHeading(
                    currentHeading);
        }

        var indicatorLookAheadMeters =
            distanceToExit +
            12.0;

        if (!TrySampleRouteHeadingAhead(
                agent,
                segment,
                indicatorLookAheadMeters,
                out var nextHeading,
                out _) )
        {
            return;
        }

        var delta =
            NormalizeHeadingDelta(
                nextHeading -
                currentHeading);

        const double minimumTurnRadians =
            Math.PI /
            12.0;

        right =
            delta >
            minimumTurnRadians;

        left =
            delta <
            -minimumTurnRadians;
    }

    private static double NormalizeHeadingDelta(
        double radians) =>
        Math.Atan2(
            Math.Sin(
                radians),
            Math.Cos(
                radians));

    private static double ReverseHeading(
        double headingRadians) =>
        Math.Atan2(
            -Math.Sin(
                headingRadians),
            -Math.Cos(
                headingRadians));

    private static WorldVector3 Lerp(
        WorldVector3 a,
        WorldVector3 b,
        double t) =>
        new(
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

    private static double HorizontalDistance(
        WorldVector3 a,
        WorldVector3 b)
    {
        var x =
            a.X -
            b.X;

        var z =
            a.Z -
            b.Z;

        return Math.Sqrt(
            x *
                x +
            z *
                z);
    }

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

    private readonly record struct SignalControlEvent(
        double DistanceSeconds,
        int CheckTrafficLightIndex,
        bool IfNoApproach,
        double TargetTimeSeconds,
        bool IsStop);

    private sealed class TrafficSignalGroupState(
        long sceneryObjectId,
        double cycleSeconds,
        IReadOnlyList<WorldTrafficPathSegment> segments,
        IReadOnlyList<WorldTrafficLightJump> jumps,
        IReadOnlyList<WorldTrafficLightStop> stops)
    {
        public long SceneryObjectId { get; } =
            sceneryObjectId;

        public double CycleSeconds { get; } =
            cycleSeconds;

        public IReadOnlyList<WorldTrafficPathSegment> Segments { get; } =
            segments;

        public IReadOnlyList<WorldTrafficLightJump> Jumps { get; } =
            jumps;

        public IReadOnlyList<WorldTrafficLightStop> Stops { get; } =
            stops;

        public double PositionSeconds
        {
            get;
            set;
        }
    }

    private sealed class Agent(
        int agentIndex,
        int segmentIndex,
        double distanceMeters,
        bool travelForward,
        double cruiseSpeedMetersPerSecond,
        double initialSpeedMetersPerSecond,
        string vehiclePath,
        int? groupIndex,
        int? defaultDensityClassIndex,
        string groupName,
        double activationTimeSeconds)
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

        public bool TravelForward
        {
            get;
            set;
        } =
            travelForward;

        public double CruiseSpeedMetersPerSecond { get; } =
            cruiseSpeedMetersPerSecond;

        public double SpeedMetersPerSecond
        {
            get;
            set;
        } =
            initialSpeedMetersPerSecond;

        public string VehiclePath { get; } =
            vehiclePath;

        public int? GroupIndex { get; } =
            groupIndex;

        public int? DefaultDensityClassIndex { get; } =
            defaultDensityClassIndex;

        public string GroupName { get; } =
            groupName;

        public double ActivationTimeSeconds
        {
            get;
            set;
        } =
            Math.Max(
                activationTimeSeconds,
                0.0);

        public bool BrakeLight
        {
            get;
            set;
        }

        public bool PendingRespawn
        {
            get;
            set;
        }

        public bool ActivationPlacementValidated
        {
            get;
            set;
        } =
            activationTimeSeconds <=
                0.75;

        public double CollisionHoldUntilSeconds
        {
            get;
            set;
        }

        public double TraveledDistanceMeters
        {
            get;
            set;
        }
    }
}
