using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldTrafficAgentState(
    int AgentIndex,
    int SegmentIndex,
    double DistanceMeters,
    double SpeedMetersPerSecond,
    string VehiclePath,
    WorldVector3 Position,
    double HeadingRadians);

public sealed class WorldTrafficSimulation
{
    private readonly WorldTrafficPathNetwork _network;
    private readonly Dictionary<int, WorldTrafficPathSegment> _segmentsByIndex;
    private readonly List<Agent> _agents;

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

        var vehicles =
            aiCatalog.MovingVehicles
                .Where(
                    static vehicle =>
                        vehicle.Exists)
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
                    static segment =>
                        segment.Type ==
                        0 &&
                        segment.Points.Count >=
                        2)
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
            var segment =
                roadSegments[
                    index %
                    roadSegments.Length];

            var vehicle =
                SelectVehicle(
                    vehicles,
                    index);

            var length =
                SegmentLength(
                    segment);

            var distance =
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

            _agents.Add(
                new Agent(
                    index,
                    segment.Index,
                    distance,
                    ResolveCruiseSpeed(
                        index),
                    vehicle.ResolvedPath!));
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
                var remaining =
                    agent.SpeedMetersPerSecond *
                    step;

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
                        Math.Max(
                            length -
                                agent.DistanceMeters,
                            0.0);

                    if (remaining <=
                        available)
                    {
                        agent.DistanceMeters +=
                            remaining;

                        remaining =
                            0.0;

                        break;
                    }

                    remaining -=
                        available;

                    agent.DistanceMeters =
                        length;

                    if (!TryAdvanceSegment(
                            agent,
                            segment))
                    {
                        remaining =
                            0.0;

                        break;
                    }
                }
            }

            simulationSeconds -=
                step;
        }
    }

    private bool TryAdvanceSegment(
        Agent agent,
        WorldTrafficPathSegment segment)
    {
        var candidates =
            segment.ForwardConnections
                .Where(
                    _segmentsByIndex.ContainsKey)
                .Order()
                .ToArray();

        if (candidates.Length ==
            0)
        {
            agent.SpeedMetersPerSecond =
                0.0;

            return false;
        }

        var next =
            candidates[
                agent.AgentIndex %
                candidates.Length];

        agent.SegmentIndex =
            next;

        agent.DistanceMeters =
            0.0;

        return true;
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
                0.0);
        }

        SampleSegment(
            segment,
            agent.DistanceMeters,
            out var position,
            out var heading);

        return new WorldTrafficAgentState(
            agent.AgentIndex,
            agent.SegmentIndex,
            agent.DistanceMeters,
            agent.SpeedMetersPerSecond,
            agent.VehiclePath,
            position,
            heading);
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
        double speedMetersPerSecond,
        string vehiclePath)
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

        public double SpeedMetersPerSecond
        {
            get;
            set;
        } =
            speedMetersPerSecond;

        public string VehiclePath { get; } =
            vehiclePath;
    }
}
