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
    bool AiBlinkerRight = false);

public sealed class WorldTrafficSimulation
{
    private const double MinimumTrafficSeparationMeters =
        6.0;
    private const double FollowingTimeHeadwaySeconds =
        1.25;
    private const double TrafficAccelerationMetersPerSecondSquared =
        1.5;
    private const double TrafficBrakingMetersPerSecondSquared =
        4.0;
    private const double TrafficLookAheadMeters =
        120.0;
    private const double TrafficStopLineBufferMeters =
        0.75;
    private const int MaximumTrafficLookAheadSegments =
        32;

    private readonly WorldTrafficPathNetwork _network;
    private readonly Dictionary<int, WorldTrafficPathSegment> _segmentsByIndex;
    private readonly HashSet<long> _crossingSceneryObjectIds;
    private readonly List<Agent> _agents;
    private double _simulationElapsedSeconds;

    public WorldTrafficSimulation(
        WorldTrafficPathNetwork network,
        OmsiMapAiCatalog aiCatalog,
        int maximumAgents = 12)
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

        if (vehicles.Length == 0 ||
            maximumAgents <= 0)
        {
            _agents =
                [];
            return;
        }

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

        if (roadSegments.Length == 0)
        {
            _agents =
                [];
            return;
        }

        var count =
            Math.Min(
                maximumAgents,
                roadSegments.Length);

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

            var length =
                SegmentLength(
                    segment);

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

            var distance =
                travelForward
                    ? offset
                    : Math.Max(
                        length -
                            offset,
                        0.0);

            var cruiseSpeed =
                ResolveCruiseSpeed(
                    index);

            var initialSpeed =
                ResolveSegmentMaximumSpeed(
                    segment,
                    cruiseSpeed);

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
                    vehicle.GroupName));
        }
    }

    public IReadOnlyList<WorldTrafficAgentState> Snapshot() =>
        _agents
            .Select(
                CreateState)
            .ToArray();

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

        var simulationSeconds =
            deltaSeconds;

        while (simulationSeconds >
               0.000001)
        {
            var step =
                Math.Min(
                    simulationSeconds,
                    0.25);

            foreach (var agent in
                     _agents)
            {
                var leadingDistance =
                    FindLeadingDistance(
                        agent);

                double? blockedEntryDistance =
                    null;

                if (_segmentsByIndex.TryGetValue(
                        agent.SegmentIndex,
                        out var currentSegment))
                {
                    blockedEntryDistance =
                        ResolveBlockedEntryDistance(
                            agent,
                            currentSegment);
                }

                var targetSpeed =
                    ResolveTargetSpeed(
                        agent,
                        leadingDistance,
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

                if (leadingDistance.HasValue)
                {
                    remaining =
                        Math.Min(
                            remaining,
                            Math.Max(
                                leadingDistance.Value -
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

                    if (remaining <=
                        available)
                    {
                        agent.DistanceMeters +=
                            agent.TravelForward
                                ? remaining
                                : -remaining;

                        remaining =
                            0.0;

                        break;
                    }

                    remaining -=
                        available;

                    agent.DistanceMeters =
                        agent.TravelForward
                            ? length
                            : 0.0;

                    if (!TryAdvanceSegment(
                            agent,
                            segment))
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
        WorldTrafficPathSegment segment)
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
            agent.SpeedMetersPerSecond =
                0.0;
            agent.BrakeLight =
                true;

            return false;
        }

        if (!CanEnterSegment(
                agent,
                segment,
                nextSegment))
        {
            agent.SpeedMetersPerSecond =
                0.0;
            agent.BrakeLight =
                true;

            return false;
        }

        agent.SegmentIndex =
            next.Value;

        agent.DistanceMeters =
            agent.TravelForward
                ? 0.0
                : SegmentLength(
                    nextSegment);

        return true;
    }

    private bool CanEnterSegment(
        Agent agent,
        WorldTrafficPathSegment currentSegment,
        WorldTrafficPathSegment nextSegment)
    {
        if (!IsTrafficSignalGreen(
                nextSegment.TrafficSignal,
                _simulationElapsedSeconds))
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

        foreach (var other in
                 _agents)
        {
            if (ReferenceEquals(
                    other,
                    agent) ||
                !_segmentsByIndex.TryGetValue(
                    other.SegmentIndex,
                    out var otherSegment))
            {
                continue;
            }

            if (otherSegment.SceneryObjectId ==
                crossingId)
            {
                return false;
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
                    crossingId)
            {
                continue;
            }

            if (otherSegment.TrafficPriority >
                currentSegment.TrafficPriority)
            {
                return false;
            }

            if (otherSegment.TrafficPriority ==
                    currentSegment.TrafficPriority &&
                other.AgentIndex <
                    agent.AgentIndex)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTrafficSignalGreen(
        WorldTrafficSignalProgram? signal,
        double elapsedSeconds)
    {
        if (signal is null ||
            signal.Phases.Count ==
                0 ||
            !double.IsFinite(
                elapsedSeconds))
        {
            return true;
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
            return true;
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
                return phase.Phase is
                    >= 6 and <= 8;
            }

            position -=
                phase.DurationSeconds;
        }

        return lastPhase is not null &&
               lastPhase.Phase is
                   >= 6 and <= 8;
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
                return candidate
                    .Segment
                    .Index;
            }
        }

        return candidates[^1]
            .Segment
            .Index;
    }

    private double? FindLeadingDistance(
        Agent agent)
    {
        var nearest =
            double.PositiveInfinity;

        foreach (var candidate in
                 _agents)
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

            if (distance.HasValue &&
                distance.Value >
                    0.0001 &&
                distance.Value <
                    nearest)
            {
                nearest =
                    distance.Value;
            }
        }

        return double.IsPositiveInfinity(
                   nearest)
            ? null
            : nearest;
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
        double? leadingDistance,
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

        if (leadingDistance.HasValue)
        {
            var usableDistance =
                Math.Max(
                    leadingDistance.Value -
                        MinimumTrafficSeparationMeters,
                    0.0);

            var followingSpeed =
                usableDistance /
                FollowingTimeHeadwaySeconds;

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
        WorldTrafficPathSegment segment)
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
                nextSegment))
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
                false);
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
            blinkerRight);
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

        var nextIndex =
            ResolveNextSegmentIndex(
                agent,
                segment);

        if (!nextIndex.HasValue ||
            !_segmentsByIndex.TryGetValue(
                nextIndex.Value,
                out var nextSegment))
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

        var nextLength =
            SegmentLength(
                nextSegment);

        var nextSampleDistance =
            agent.TravelForward
                ? 0.0
                : nextLength;

        SampleSegment(
            nextSegment,
            nextSampleDistance,
            out _,
            out var nextHeading);

        if (!agent.TravelForward)
        {
            nextHeading =
                ReverseHeading(
                    nextHeading);
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
        double cruiseSpeedMetersPerSecond,
        double initialSpeedMetersPerSecond,
        string vehiclePath,
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

        public bool BrakeLight
        {
            get;
            set;
        }
    }
}
