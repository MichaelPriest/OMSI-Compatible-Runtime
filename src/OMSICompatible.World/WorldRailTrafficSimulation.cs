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
    private readonly List<Agent> _agents;

    public WorldRailTrafficSimulation(
        WorldTrafficPathNetwork network,
        OmsiMapAiCatalog aiCatalog,
        int maximumAgents = 4)
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

            var segment =
                railSegments[
                    index %
                    railSegments.Length];

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

            var groupIndex =
                groupDefinitions.TryGetValue(
                    consist.GroupName,
                    out var groupDefinition)
                    ? groupDefinition.Index
                    : (int?)null;

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
                    consist.GroupName));
        }
    }

    public IReadOnlyList<WorldRailTrafficAgentState>
        Snapshot() =>
        _agents
            .Select(
                CreateState)
            .ToArray();

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

            var predecessor =
                ResolvePredecessorSegment(
                    segment.Index,
                    agent.TravelForward);

            if (predecessor is null)
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

                var targetSpeed =
                    ResolveSegmentMaximumSpeed(
                        segment);

                UpdateAgentSpeed(
                    agent,
                    targetSpeed,
                    step);

                var remainingDistance =
                    agent.SpeedMetersPerSecond *
                    step;

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
            }

            remainingSeconds -=
                step;
        }
    }

    private WorldTrafficPathSegment? ResolvePredecessorSegment(
        int segmentIndex,
        bool travelForward) =>
        _segmentsByIndex
            .Values
            .Where(
                candidate =>
                    (travelForward
                        ? candidate.ForwardConnections
                        : candidate.ReverseConnections)
                    .Contains(
                        segmentIndex) &&
                    (travelForward
                        ? candidate.AllowsForward
                        : candidate.AllowsReverse))
            .OrderBy(
                static candidate =>
                    candidate.Index)
            .FirstOrDefault();

    private bool TryAdvanceSegment(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var connections =
            agent.TravelForward
                ? segment.ForwardConnections
                : segment.ReverseConnections;

        var next =
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
                        (agent.TravelForward
                            ? candidate.AllowsForward
                            : candidate.AllowsReverse))
                .OrderBy(
                    static candidate =>
                        candidate!.Index)
                .FirstOrDefault();

        if (next is null)
        {
            agent.SpeedMetersPerSecond =
                0.0;
            return false;
        }

        agent.SegmentIndex =
            next.Index;

        agent.DistanceMeters =
            agent.TravelForward
                ? 0.0
                : SegmentLength(
                    next);

        return true;
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

        public string GroupName { get; } =
            groupName;

        public double TraveledDistanceMeters
        {
            get;
            set;
        }
    }
}
