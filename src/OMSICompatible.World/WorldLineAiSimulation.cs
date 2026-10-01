using OmsiCompat.Map;

namespace OMSICompatible.World;

public sealed record WorldLineAiAgentState(
    int AgentIndex,
    string LineName,
    string TourNumber,
    string TripName,
    string Destination,
    int SegmentIndex,
    double DistanceMeters,
    double SpeedMetersPerSecond,
    string VehiclePath,
    WorldVector3 Position,
    double HeadingRadians,
    double TraveledDistanceMeters,
    int RouteSegmentIndex,
    bool AiBrakeLight = false);

public sealed class WorldLineAiSimulation
{
    private readonly record struct TrafficLead(
        double DistanceMeters,
        double SpeedMetersPerSecond);

    private sealed class ServiceAgent(
        int agentIndex,
        WorldLineAiScheduledTrip service,
        OmsiAiVehicleDefinition vehicle,
        IReadOnlyList<int> routeSegments)
    {
        public int AgentIndex { get; } = agentIndex;
        public WorldLineAiScheduledTrip Service { get; } = service;
        public OmsiAiVehicleDefinition Vehicle { get; } = vehicle;
        public IReadOnlyList<int> RouteSegments { get; } = routeSegments;
        public int RouteSegmentIndex { get; set; }
        public double DistanceMeters { get; set; }
        public double SpeedMetersPerSecond { get; set; }
        public double TraveledDistanceMeters { get; set; }
        public bool BrakeLight { get; set; }
        public bool Active { get; set; }
        public bool Completed { get; set; }
    }

    private const double BusAccelerationMetersPerSecondSquared = 1.1;
    private const double BusBrakingMetersPerSecondSquared = 3.5;
    private const double BusFollowingTimeHeadwaySeconds = 1.8;
    private const double MinimumFollowingGapMeters = 4.5;
    private const double BusHalfLengthMeters = 6.0;
    private const double TrafficLookAheadMeters = 100.0;
    private const double DefaultBusCruiseMetersPerSecond = 11.1111111111;
    private const double DepartureGraceMinutes = 10.0;

    private readonly IReadOnlyDictionary<int, WorldTrafficPathSegment>
        _segmentsByIndex;
    private readonly List<ServiceAgent> _services = [];
    private readonly int _maximumActiveAgents;
    private WorldTrafficObstacleState? _externalObstacle;
    private double _serviceMinutes;

    public WorldLineAiSimulation(
        WorldLineAiSchedule schedule,
        WorldTrafficPathNetwork network,
        int maximumActiveAgents,
        double serviceStartMinutes,
        int serviceDaySeed = 0,
        int maximumLinePriority = int.MaxValue,
        int requiredDayBit = 0,
        int requiredSchoolBit = 0)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(network);

        _segmentsByIndex =
            network.Segments.ToDictionary(
                static segment => segment.Index);
        _maximumActiveAgents =
            Math.Max(maximumActiveAgents, 0);
        _serviceMinutes =
            NormalizeServiceMinutes(serviceStartMinutes);

        var nextAgentIndex = 1_000_000;

        foreach (var trip in
                 schedule.Trips.Where(
                     trip =>
                         trip.Ready &&
                         trip.Route is { FullyResolved: true } &&
                         trip.LinePriority <=
                             maximumLinePriority &&
                         (requiredDayBit ==
                              0 ||
                          (trip.DayMask &
                           requiredDayBit) !=
                              0) &&
                         (requiredSchoolBit ==
                              0 ||
                          (trip.DayMask &
                           requiredSchoolBit) !=
                              0)))
        {
            var vehicle =
                WorldLineAiScheduleResolver.SelectVehicle(
                    trip,
                    serviceDaySeed);

            if (vehicle?.ResolvedPath is null ||
                trip.Route is null ||
                trip.Route.SegmentIndices.Count == 0)
            {
                continue;
            }

            var agent =
                new ServiceAgent(
                    nextAgentIndex++,
                    trip,
                    vehicle,
                    trip.Route.SegmentIndices);

            if (trip.DepartureMinutes <
                _serviceMinutes - DepartureGraceMinutes)
            {
                agent.Completed = true;
            }

            _services.Add(agent);
        }
    }

    public double ServiceMinutes => _serviceMinutes;

    public int ActiveCount =>
        _services.Count(
            static service =>
                service.Active &&
                !service.Completed);

    public IReadOnlyList<string> RequiredVehiclePaths =>
        _services
            .Where(static service => !service.Completed)
            .Select(static service => service.Vehicle.ResolvedPath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void SetExternalObstacle(
        WorldTrafficObstacleState? obstacle)
    {
        _externalObstacle =
            obstacle;
    }

    public void Step(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) ||
            deltaSeconds <= 0.0)
        {
            return;
        }

        var remainingStep =
            Math.Clamp(deltaSeconds, 0.0, 0.5);

        while (remainingStep > 0.000001)
        {
            var step =
                Math.Min(remainingStep, 0.25);

            _serviceMinutes =
                NormalizeServiceMinutes(
                    _serviceMinutes +
                    step / 60.0);

            ActivateDueServices();

            foreach (var service in _services)
            {
                if (service.Active &&
                    !service.Completed)
                {
                    StepService(
                        service,
                        step);
                }
            }

            remainingStep -= step;
        }
    }

    public IReadOnlyList<WorldLineAiAgentState> Snapshot()
    {
        var result = new List<WorldLineAiAgentState>();
        AppendSnapshotTo(result);
        return result.ToArray();
    }

    public void AppendSnapshotTo(
        List<WorldLineAiAgentState> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var service in _services)
        {
            if (!service.Active ||
                service.Completed ||
                service.RouteSegmentIndex < 0 ||
                service.RouteSegmentIndex >=
                    service.RouteSegments.Count ||
                !_segmentsByIndex.TryGetValue(
                    service.RouteSegments[
                        service.RouteSegmentIndex],
                    out var segment))
            {
                continue;
            }

            var travelForward =
                ResolveTravelForward(
                    service,
                    service.RouteSegmentIndex,
                    segment);

            SampleSegment(
                segment,
                service.DistanceMeters,
                out var position,
                out var heading);

            if (!travelForward)
            {
                heading =
                    ReverseHeading(heading);
            }

            destination.Add(
                new WorldLineAiAgentState(
                    service.AgentIndex,
                    service.Service.LineName,
                    service.Service.TourNumber,
                    service.Service.TripName,
                    service.Service.Trip?.Destination ??
                        string.Empty,
                    segment.Index,
                    service.DistanceMeters,
                    service.SpeedMetersPerSecond,
                    service.Vehicle.ResolvedPath!,
                    position,
                    heading,
                    service.TraveledDistanceMeters,
                    service.RouteSegmentIndex,
                    service.BrakeLight));
        }
    }

    private void ActivateDueServices()
    {
        if (_maximumActiveAgents <= 0)
        {
            return;
        }

        var active = ActiveCount;

        foreach (var service in
                 _services
                     .Where(
                         static service =>
                             !service.Active &&
                             !service.Completed)
                     .OrderBy(
                         static service =>
                             service.Service.DepartureMinutes)
                     .ThenBy(
                         static service =>
                             service.AgentIndex))
        {
            if (active >= _maximumActiveAgents)
            {
                break;
            }

            var departure =
                service.Service.DepartureMinutes;

            if (departure > _serviceMinutes)
            {
                break;
            }

            if (_serviceMinutes - departure >
                DepartureGraceMinutes)
            {
                service.Completed = true;
                continue;
            }

            if (!CanOccupyRouteStart(
                    service))
            {
                continue;
            }

            if (!TryInitializeService(service))
            {
                service.Completed = true;
                continue;
            }

            service.Active = true;
            active++;
        }
    }

    private bool TryInitializeService(
        ServiceAgent service)
    {
        if (service.RouteSegments.Count == 0 ||
            !_segmentsByIndex.TryGetValue(
                service.RouteSegments[0],
                out var first))
        {
            return false;
        }

        service.RouteSegmentIndex = 0;

        var forward =
            ResolveTravelForward(
                service,
                0,
                first);

        service.DistanceMeters =
            forward
                ? 0.0
                : SegmentLength(first);
        service.SpeedMetersPerSecond = 0.0;
        service.TraveledDistanceMeters = 0.0;

        return true;
    }

    private void StepService(
        ServiceAgent service,
        double deltaSeconds)
    {
        if (!_segmentsByIndex.TryGetValue(
                service.RouteSegments[
                    service.RouteSegmentIndex],
                out var segment))
        {
            service.Completed = true;
            service.Active = false;
            return;
        }

        var travelForward =
            ResolveTravelForward(
                service,
                service.RouteSegmentIndex,
                segment);

        var lineLead =
            FindLineLead(
                service,
                segment,
                travelForward);
        var obstacleLead =
            FindExternalObstacleLead(
                service,
                segment,
                travelForward);

        var leading =
            SelectNearestLead(
                lineLead,
                obstacleLead);

        var targetSpeed =
            ResolveTargetSpeed(
                segment,
                leading);

        var previousSpeed =
            service.SpeedMetersPerSecond;

        service.BrakeLight =
            targetSpeed <
                previousSpeed -
                    0.01 ||
            (targetSpeed <=
                 0.05 &&
             previousSpeed <=
                 0.5);

        UpdateSpeed(
            service,
            targetSpeed,
            deltaSeconds);

        var remaining =
            service.SpeedMetersPerSecond *
            deltaSeconds;

        if (leading.HasValue)
        {
            remaining =
                Math.Min(
                    remaining,
                    Math.Max(
                        leading.Value.DistanceMeters -
                            MinimumFollowingGapMeters,
                        0.0));
        }

        while (remaining > 0.000001)
        {
            var length =
                SegmentLength(segment);

            var available =
                travelForward
                    ? Math.Max(
                        length -
                        service.DistanceMeters,
                        0.0)
                    : Math.Max(
                        service.DistanceMeters,
                        0.0);

            if (remaining < available)
            {
                service.DistanceMeters +=
                    travelForward
                        ? remaining
                        : -remaining;
                service.TraveledDistanceMeters +=
                    remaining;
                return;
            }

            service.TraveledDistanceMeters +=
                available;
            remaining -= available;
            service.RouteSegmentIndex++;

            if (service.RouteSegmentIndex >=
                service.RouteSegments.Count)
            {
                service.Completed = true;
                service.Active = false;
                service.SpeedMetersPerSecond = 0.0;
                service.BrakeLight = true;
                return;
            }

            if (!_segmentsByIndex.TryGetValue(
                    service.RouteSegments[
                        service.RouteSegmentIndex],
                    out segment))
            {
                service.Completed = true;
                service.Active = false;
                return;
            }

            travelForward =
                ResolveTravelForward(
                    service,
                    service.RouteSegmentIndex,
                    segment);
            service.DistanceMeters =
                travelForward
                    ? 0.0
                    : SegmentLength(segment);

            targetSpeed =
                ResolveTargetSpeed(
                    segment,
                    null);
            service.SpeedMetersPerSecond =
                Math.Min(
                    service.SpeedMetersPerSecond,
                    targetSpeed);
        }
    }

    private bool CanOccupyRouteStart(
        ServiceAgent service)
    {
        if (service.RouteSegments.Count ==
                0 ||
            !_segmentsByIndex.TryGetValue(
                service.RouteSegments[0],
                out var first))
        {
            return false;
        }

        var forward =
            ResolveTravelForward(
                service,
                0,
                first);
        var startDistance =
            forward
                ? 0.0
                : SegmentLength(
                    first);

        foreach (var other in
                 _services)
        {
            if (!other.Active ||
                other.Completed ||
                other.RouteSegmentIndex !=
                    0 ||
                other.RouteSegments.Count ==
                    0 ||
                other.RouteSegments[0] !=
                    first.Index)
            {
                continue;
            }

            var otherForward =
                ResolveTravelForward(
                    other,
                    0,
                    first);

            if (otherForward !=
                forward)
            {
                continue;
            }

            if (Math.Abs(
                    other.DistanceMeters -
                    startDistance) <
                BusHalfLengthMeters *
                    2.0 +
                MinimumFollowingGapMeters)
            {
                return false;
            }
        }

        return true;
    }

    private TrafficLead? FindLineLead(
        ServiceAgent source,
        WorldTrafficPathSegment segment,
        bool sourceForward)
    {
        TrafficLead? nearest =
            null;

        foreach (var target in
                 _services)
        {
            if (ReferenceEquals(
                    source,
                    target) ||
                !target.Active ||
                target.Completed ||
                target.RouteSegmentIndex <
                    0 ||
                target.RouteSegmentIndex >=
                    target.RouteSegments.Count)
            {
                continue;
            }

            double centerDistance;

            if (target.RouteSegments[
                    target.RouteSegmentIndex] ==
                segment.Index)
            {
                var targetForward =
                    ResolveTravelForward(
                        target,
                        target.RouteSegmentIndex,
                        segment);

                if (targetForward !=
                    sourceForward)
                {
                    continue;
                }

                centerDistance =
                    sourceForward
                        ? target.DistanceMeters -
                          source.DistanceMeters
                        : source.DistanceMeters -
                          target.DistanceMeters;
            }
            else if (source.RouteSegmentIndex + 1 <
                         source.RouteSegments.Count &&
                     source.RouteSegments[
                         source.RouteSegmentIndex + 1] ==
                         target.RouteSegments[
                             target.RouteSegmentIndex] &&
                     _segmentsByIndex.TryGetValue(
                         target.RouteSegments[
                             target.RouteSegmentIndex],
                         out var targetSegment))
            {
                var targetForward =
                    ResolveTravelForward(
                        target,
                        target.RouteSegmentIndex,
                        targetSegment);

                var sourceToExit =
                    sourceForward
                        ? Math.Max(
                            SegmentLength(segment) -
                            source.DistanceMeters,
                            0.0)
                        : Math.Max(
                            source.DistanceMeters,
                            0.0);

                var targetFromEntry =
                    targetForward
                        ? Math.Max(
                            target.DistanceMeters,
                            0.0)
                        : Math.Max(
                            SegmentLength(targetSegment) -
                            target.DistanceMeters,
                            0.0);

                centerDistance =
                    sourceToExit +
                    targetFromEntry;
            }
            else
            {
                continue;
            }

            if (centerDistance <=
                    0.0 ||
                centerDistance >
                    TrafficLookAheadMeters)
            {
                continue;
            }

            var lead =
                new TrafficLead(
                    Math.Max(
                        centerDistance -
                        BusHalfLengthMeters *
                            2.0,
                        0.0),
                    Math.Max(
                        target.SpeedMetersPerSecond,
                        0.0));

            if (!nearest.HasValue ||
                lead.DistanceMeters <
                    nearest.Value.DistanceMeters)
            {
                nearest =
                    lead;
            }
        }

        return nearest;
    }

    private TrafficLead? FindExternalObstacleLead(
        ServiceAgent service,
        WorldTrafficPathSegment segment,
        bool travelForward)
    {
        if (_externalObstacle is not
            { } obstacle)
        {
            return null;
        }

        SampleSegment(
            segment,
            service.DistanceMeters,
            out var position,
            out var heading);

        if (!travelForward)
        {
            heading =
                ReverseHeading(
                    heading);
        }

        if (Math.Abs(
                obstacle.Position.Y -
                position.Y) >
            3.5)
        {
            return null;
        }

        var deltaX =
            obstacle.Position.X -
            position.X;
        var deltaZ =
            obstacle.Position.Z -
            position.Z;

        var forwardX =
            Math.Sin(
                heading);
        var forwardZ =
            Math.Cos(
                heading);
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

        var maximumLateral =
            Math.Max(
                Math.Abs(
                    segment.WidthMeters) *
                    0.5,
                1.1) +
            Math.Max(
                obstacleLateralExtent,
                0.45);

        if (lateral >
            maximumLateral)
        {
            return null;
        }

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

        var headingDelta =
            NormalizeHeadingDelta(
                obstacle.HeadingRadians -
                heading);

        var projectedSpeed =
            Math.Max(
                obstacle.SpeedMetersPerSecond *
                Math.Cos(
                    headingDelta),
                0.0);

        return new TrafficLead(
            Math.Max(
                longitudinal -
                obstacleLongitudinalExtent -
                BusHalfLengthMeters,
                0.0),
            projectedSpeed);
    }

    private static TrafficLead? SelectNearestLead(
        TrafficLead? first,
        TrafficLead? second)
    {
        if (!first.HasValue)
        {
            return second;
        }

        if (!second.HasValue)
        {
            return first;
        }

        return first.Value.DistanceMeters <=
               second.Value.DistanceMeters
            ? first
            : second;
    }

    private static void UpdateSpeed(
        ServiceAgent service,
        double targetSpeed,
        double deltaSeconds)
    {
        if (targetSpeed >=
            service.SpeedMetersPerSecond)
        {
            service.SpeedMetersPerSecond =
                Math.Min(
                    targetSpeed,
                    service.SpeedMetersPerSecond +
                    BusAccelerationMetersPerSecondSquared *
                    deltaSeconds);
            return;
        }

        service.SpeedMetersPerSecond =
            Math.Max(
                targetSpeed,
                service.SpeedMetersPerSecond -
                BusBrakingMetersPerSecondSquared *
                deltaSeconds);
    }

    private static double NormalizeHeadingDelta(
        double radians)
    {
        while (radians >
               Math.PI)
        {
            radians -=
                Math.PI *
                2.0;
        }

        while (radians <
               -Math.PI)
        {
            radians +=
                Math.PI *
                2.0;
        }

        return radians;
    }

    private bool ResolveTravelForward(
        ServiceAgent service,
        int routeIndex,
        WorldTrafficPathSegment segment)
    {
        if (segment.Direction == 0)
        {
            return true;
        }

        if (segment.Direction == 1)
        {
            return false;
        }

        if (routeIndex + 1 <
            service.RouteSegments.Count)
        {
            var next =
                service.RouteSegments[
                    routeIndex + 1];

            if (segment.ForwardConnections.Contains(next))
            {
                return true;
            }

            if (segment.ReverseConnections.Contains(next))
            {
                return false;
            }
        }

        if (routeIndex > 0)
        {
            var previous =
                service.RouteSegments[
                    routeIndex - 1];

            if (segment.ReverseConnections.Contains(previous))
            {
                return true;
            }

            if (segment.ForwardConnections.Contains(previous))
            {
                return false;
            }
        }

        return true;
    }

    private static double ResolveTargetSpeed(
        WorldTrafficPathSegment segment,
        TrafficLead? leading)
    {
        var limit =
            segment.SpeedLimitKilometersPerHour
                is { } speedKph &&
            double.IsFinite(speedKph) &&
            speedKph > 0.0
                ? speedKph / 3.6
                : DefaultBusCruiseMetersPerSecond;

        var targetSpeed =
            Math.Clamp(
                limit,
                2.0,
                DefaultBusCruiseMetersPerSecond);

        if (!leading.HasValue)
        {
            return targetSpeed;
        }

        var usableDistance =
            Math.Max(
                leading.Value.DistanceMeters -
                MinimumFollowingGapMeters,
                0.0);

        var leaderSpeed =
            Math.Max(
                leading.Value.SpeedMetersPerSecond,
                0.0);

        var leaderStoppingDistance =
            leaderSpeed *
            leaderSpeed /
            (2.0 *
             BusBrakingMetersPerSecondSquared);

        var headwayBrakingTerm =
            BusBrakingMetersPerSecondSquared *
            BusFollowingTimeHeadwaySeconds;

        var followingSpeed =
            Math.Max(
                -headwayBrakingTerm +
                Math.Sqrt(
                    headwayBrakingTerm *
                        headwayBrakingTerm +
                    2.0 *
                        BusBrakingMetersPerSecondSquared *
                        (usableDistance +
                         leaderStoppingDistance)),
                0.0);

        return Math.Min(
            targetSpeed,
            followingSpeed);
    }

    private static double SegmentLength(
        WorldTrafficPathSegment segment)
    {
        var length = 0.0;

        for (var index = 1;
             index < segment.Points.Count;
             index++)
        {
            length +=
                Distance(
                    segment.Points[index - 1],
                    segment.Points[index]);
        }

        return length;
    }

    private static void SampleSegment(
        WorldTrafficPathSegment segment,
        double distanceMeters,
        out WorldVector3 position,
        out double headingRadians)
    {
        if (segment.Points.Count == 0)
        {
            position = default;
            headingRadians = 0.0;
            return;
        }

        if (segment.Points.Count == 1)
        {
            position = segment.Points[0];
            headingRadians = 0.0;
            return;
        }

        var remaining =
            Math.Max(distanceMeters, 0.0);

        for (var index = 1;
             index < segment.Points.Count;
             index++)
        {
            var from =
                segment.Points[index - 1];
            var to =
                segment.Points[index];
            var length =
                Distance(from, to);

            if (length <= 0.000001)
            {
                continue;
            }

            if (remaining <= length)
            {
                var t =
                    Math.Clamp(
                        remaining / length,
                        0.0,
                        1.0);

                position =
                    new WorldVector3(
                        from.X +
                        (to.X - from.X) * t,
                        from.Y +
                        (to.Y - from.Y) * t,
                        from.Z +
                        (to.Z - from.Z) * t);

                headingRadians =
                    Math.Atan2(
                        to.X - from.X,
                        to.Z - from.Z);
                return;
            }

            remaining -= length;
        }

        var last =
            segment.Points[^1];
        var previous =
            segment.Points[^2];

        position = last;
        headingRadians =
            Math.Atan2(
                last.X - previous.X,
                last.Z - previous.Z);
    }

    private static double Distance(
        WorldVector3 first,
        WorldVector3 second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        var z = second.Z - first.Z;

        return Math.Sqrt(
            x * x +
            y * y +
            z * z);
    }

    private static double ReverseHeading(
        double headingRadians) =>
        Math.Atan2(
            -Math.Sin(headingRadians),
            -Math.Cos(headingRadians));

    private static double NormalizeServiceMinutes(
        double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.0;
        }

        var result =
            value % 1440.0;

        return result < 0.0
            ? result + 1440.0
            : result;
    }
}
