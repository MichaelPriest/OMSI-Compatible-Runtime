namespace OMSICompatible.World;

public sealed record WorldNavigationGuidanceWaypoint(
    WorldVector3 Position,
    double DistanceAheadMeters,
    double HeadingDegrees,
    double PitchDegrees);

public sealed record WorldNavigationGroundArrow(
    WorldVector3 Position,
    double HeadingDegrees,
    double PitchDegrees,
    double DistanceAheadMeters,
    string Kind);

public sealed record WorldNavigationAssistState(
    bool RouteLoaded,
    double SuggestedMapRadiusMeters,
    string CongestionLevel,
    double? AverageNearbyTrafficSpeedKph,
    int NearbyMovingAiCount,
    int NearbySlowAiCount,
    int NearbyStoppedAiCount,
    double RouteLengthMeters,
    double ProgressMeters,
    double RemainingMeters,
    double ProgressPercent,
    double? DistanceFromRouteMeters,
    bool OffRoute,
    int? NearestRoutePointIndex,
    int? RejoinRoutePointIndex,
    WorldVector3? RejoinTarget,
    IReadOnlyList<WorldNavigationGuidanceWaypoint> GuidanceWaypoints,
    IReadOnlyList<WorldNavigationGroundArrow> GroundArrows);

/// <summary>
/// Navigation core adapted from the NavBR/openOMSI plugin behavior: adaptive
/// minimap zoom, route projection with progress continuity, off-route rejoin,
/// near-route waypoints, Forza-style ground guidance anchors and congestion.
/// It is renderer-agnostic so the HUD, full map and multiplayer can share it.
/// </summary>
public sealed class WorldNavigationAssist
{
    private readonly record struct Projection(
        int SegmentIndex,
        double T,
        double X,
        double Z,
        double Distance,
        int NearestPointIndex,
        double DistanceAlongRoute);

    private const double OffRouteThresholdMeters = 45.0;
    private const double RejoinLookAheadMeters = 60.0;
    private const double WaypointSpacingMeters = 18.0;
    private const double WaypointLookAheadMeters = 260.0;
    // Forza-inspired: repeated low chevrons, not large isolated markers.
    private const double GroundArrowSpacingMeters = 5.5;
    private const double GroundArrowStartMeters = 8.0;
    private const double GroundArrowLookAheadMeters = 175.0;
    private const int MaximumGroundArrows = 32;
    private const double CongestionSampleRadiusMeters = 350.0;
    private const int MaximumWaypoints = 24;
    private const int FreeProgressWindowSegments = 24;
    private const double ProgressPenaltyPerSegment = 0.75;
    private const double MaximumProgressPenaltyMeters = 70.0;

    private WorldVector3[] _route = [];
    private double[] _cumulative = [];
    private int? _lastProjectionSegment;

    public void SetRoute(
        IReadOnlyList<WorldVector3>? route)
    {
        _route =
            route?
                .Where(
                    static point =>
                        double.IsFinite(point.X) &&
                        double.IsFinite(point.Z))
                .ToArray() ??
            [];

        _cumulative =
            BuildCumulative(
                _route);

        _lastProjectionSegment =
            null;
    }

    public void ClearRoute()
    {
        _route =
            [];

        _cumulative =
            [];

        _lastProjectionSegment =
            null;
    }

    public WorldNavigationAssistState Build(
        WorldVector3 position,
        double headingDegrees,
        double speedKph,
        bool onFoot,
        IReadOnlyList<WorldTrafficAgentState>? traffic = null,
        double? maneuverDistanceMeters = null,
        string? maneuverKind = null)
    {
        var radius =
            SuggestedRadius(
                speedKph,
                onFoot);

        var congestion =
            ResolveCongestion(
                position,
                traffic);

        if (_route.Length <
                2 ||
            _cumulative.Length !=
                _route.Length)
        {
            return new WorldNavigationAssistState(
                false,
                radius,
                congestion.Level,
                congestion.AverageSpeedKph,
                congestion.Moving,
                congestion.Slow,
                congestion.Stopped,
                0.0,
                0.0,
                0.0,
                0.0,
                null,
                false,
                null,
                null,
                null,
                Array.Empty<WorldNavigationGuidanceWaypoint>(),
                Array.Empty<WorldNavigationGroundArrow>());
        }

        var projection =
            ProjectToRoute(
                position.X,
                position.Z,
                headingDegrees);

        if (!projection.HasValue)
        {
            return new WorldNavigationAssistState(
                true,
                radius,
                congestion.Level,
                congestion.AverageSpeedKph,
                congestion.Moving,
                congestion.Slow,
                congestion.Stopped,
                _cumulative[^1],
                0.0,
                _cumulative[^1],
                0.0,
                null,
                false,
                null,
                null,
                null,
                Array.Empty<WorldNavigationGuidanceWaypoint>(),
                Array.Empty<WorldNavigationGroundArrow>());
        }

        _lastProjectionSegment =
            projection.Value.SegmentIndex;

        var total =
            _cumulative[^1];

        var progress =
            Math.Clamp(
                projection.Value.DistanceAlongRoute,
                0.0,
                total);

        var remaining =
            Math.Max(
                total -
                    progress,
                0.0);

        var offRoute =
            projection.Value.Distance >
            OffRouteThresholdMeters;

        var rejoin =
            offRoute
                ? PointAtDistance(
                    Math.Min(
                        progress +
                            RejoinLookAheadMeters,
                        total))
                : new RouteSample(
                    projection.Value.NearestPointIndex,
                    projection.Value.X,
                    InterpolateHeight(
                        projection.Value.SegmentIndex,
                        projection.Value.T),
                    projection.Value.Z,
                    SegmentHeadingDegrees(
                        projection.Value.SegmentIndex),
                    SegmentPitchDegrees(
                        projection.Value.SegmentIndex));

        var waypoints =
            BuildWaypoints(
                progress,
                total);

        var arrows =
            BuildGroundArrows(
                progress,
                total,
                offRoute,
                maneuverDistanceMeters,
                maneuverKind);

        return new WorldNavigationAssistState(
            true,
            radius,
            congestion.Level,
            congestion.AverageSpeedKph,
            congestion.Moving,
            congestion.Slow,
            congestion.Stopped,
            Math.Round(total, 1),
            Math.Round(progress, 1),
            Math.Round(remaining, 1),
            total <=
                    0.000001
                ? 100.0
                : Math.Round(
                    Math.Clamp(
                        progress /
                            total *
                            100.0,
                        0.0,
                        100.0),
                    1),
            Math.Round(
                projection.Value.Distance,
                1),
            offRoute,
            projection.Value.NearestPointIndex,
            rejoin.Index,
            new WorldVector3(
                rejoin.X,
                rejoin.Y,
                rejoin.Z),
            waypoints,
            arrows);
    }

    public static double SuggestedRadius(
        double speedKph,
        bool onFoot)
    {
        if (onFoot)
        {
            return 250.0;
        }

        var speed =
            Math.Abs(
                double.IsFinite(
                    speedKph)
                    ? speedKph
                    : 0.0);

        return speed switch
        {
            < 10.0 => 450.0,
            < 25.0 => 650.0,
            < 45.0 => 900.0,
            < 65.0 => 1250.0,
            < 90.0 => 1750.0,
            _ => 2400.0
        };
    }

    private Projection? ProjectToRoute(
        double x,
        double z,
        double headingDegrees)
    {
        Projection? best =
            null;

        var bestScore =
            double.PositiveInfinity;

        for (var index = 0;
             index <
                 _route.Length -
                     1;
             index++)
        {
            var a =
                _route[index];
            var b =
                _route[index + 1];
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

            var t =
                lengthSquared <=
                        0.000000001
                    ? 0.0
                    : Math.Clamp(
                        (
                            (x -
                             a.X) *
                                dx +
                            (z -
                             a.Z) *
                                dz
                        ) /
                        lengthSquared,
                        0.0,
                        1.0);

            var px =
                a.X +
                dx *
                t;
            var pz =
                a.Z +
                dz *
                t;
            var distance =
                Distance2D(
                    x,
                    z,
                    px,
                    pz);
            var nearestPoint =
                t <
                        0.5
                    ? index
                    : index +
                      1;

            var along =
                _cumulative[index] +
                Math.Sqrt(
                    lengthSquared) *
                t;

            var score =
                distance;

            if (double.IsFinite(
                    headingDegrees) &&
                lengthSquared >
                    0.000000001)
            {
                var mismatch =
                    Math.Abs(
                        WrapDegrees(
                            Math.Atan2(
                                dx,
                                dz) *
                            180.0 /
                            Math.PI -
                            headingDegrees));

                score +=
                    mismatch switch
                    {
                        > 120.0 => 35.0,
                        > 80.0 => 16.0,
                        > 50.0 => 6.0,
                        _ => 0.0
                    };
            }

            if (_lastProjectionSegment is
                int previous)
            {
                var jump =
                    Math.Abs(
                        index -
                        previous);

                if (jump >
                    FreeProgressWindowSegments)
                {
                    score +=
                        Math.Min(
                            MaximumProgressPenaltyMeters,
                            (
                                jump -
                                FreeProgressWindowSegments
                            ) *
                            ProgressPenaltyPerSegment);
                }

                if (index <
                    previous -
                        3)
                {
                    score +=
                        8.0;
                }
            }

            if (score <
                bestScore)
            {
                bestScore =
                    score;

                best =
                    new Projection(
                        index,
                        t,
                        px,
                        pz,
                        distance,
                        nearestPoint,
                        along);
            }
        }

        return best;
    }

    // Ground decals must follow the same path projection as the route but
    // use their own close spacing. The old 18-metre waypoint spacing is
    // retained for map/UI waypoints (these are separate display concepts).
    private WorldNavigationGroundArrow[] BuildGroundArrows(
        double progress,
        double total,
        bool offRoute,
        double? maneuverDistanceMeters,
        string? maneuverKind)
    {
        var results =
            new List<WorldNavigationGroundArrow>(MaximumGroundArrows);
        var maximum =
            Math.Min(total, progress + GroundArrowLookAheadMeters);

        for (var distance = progress + GroundArrowStartMeters;
             distance <= maximum &&
             results.Count < MaximumGroundArrows;
             distance += GroundArrowSpacingMeters)
        {
            var sample = PointAtDistance(distance);
            if (!double.IsFinite(sample.X) ||
                !double.IsFinite(sample.Y) ||
                !double.IsFinite(sample.Z) ||
                !double.IsFinite(sample.HeadingDegrees) ||
                !double.IsFinite(sample.PitchDegrees))
            {
                continue;
            }

            var ahead = distance - progress;
            var kind =
                offRoute
                    ? "rejoin"
                    : maneuverDistanceMeters.HasValue &&
                      Math.Abs(ahead - maneuverDistanceMeters.Value) <= 22.0 &&
                      !string.IsNullOrWhiteSpace(maneuverKind)
                        ? maneuverKind!
                        : "route";

            results.Add(new WorldNavigationGroundArrow(
                new WorldVector3(sample.X, sample.Y, sample.Z),
                sample.HeadingDegrees,
                sample.PitchDegrees,
                Math.Round(ahead, 1),
                kind));
        }

        return results.ToArray();
    }

    private WorldNavigationGuidanceWaypoint[]
        BuildWaypoints(
            double progress,
            double total)
    {
        var result =
            new List<WorldNavigationGuidanceWaypoint>();

        var target =
            progress +
            WaypointSpacingMeters;

        var maximumTarget =
            Math.Min(
                total,
                progress +
                    WaypointLookAheadMeters);

        while (target <=
                   maximumTarget +
                       0.000001 &&
               result.Count <
                   MaximumWaypoints)
        {
            var sample =
                PointAtDistance(
                    target);

            result.Add(
                new WorldNavigationGuidanceWaypoint(
                    new WorldVector3(
                        sample.X,
                        sample.Y,
                        sample.Z),
                    Math.Round(
                        target -
                            progress,
                        1),
                    Math.Round(
                        sample.HeadingDegrees,
                        1),
                    Math.Round(
                        sample.PitchDegrees,
                        2)));

            target +=
                WaypointSpacingMeters;
        }

        return result.ToArray();
    }

    private readonly record struct RouteSample(
        int Index,
        double X,
        double Y,
        double Z,
        double HeadingDegrees,
        double PitchDegrees);

    private RouteSample PointAtDistance(
        double distance)
    {
        if (_route.Length ==
            1)
        {
            return new RouteSample(
                0,
                _route[0].X,
                _route[0].Y,
                _route[0].Z,
                0.0,
                0.0);
        }

        var clamped =
            Math.Clamp(
                distance,
                0.0,
                _cumulative[^1]);

        for (var index = 0;
             index <
                 _route.Length -
                     1;
             index++)
        {
            var start =
                _cumulative[index];
            var end =
                _cumulative[index + 1];

            if (clamped >
                    end &&
                index <
                    _route.Length -
                        2)
            {
                continue;
            }

            var length =
                Math.Max(
                    end -
                        start,
                    0.000000001);

            var t =
                Math.Clamp(
                    (
                        clamped -
                        start
                    ) /
                    length,
                    0.0,
                    1.0);

            var a =
                _route[index];
            var b =
                _route[index + 1];

            return new RouteSample(
                index +
                    1,
                a.X +
                    (
                        b.X -
                        a.X
                    ) *
                    t,
                a.Y +
                    (
                        b.Y -
                        a.Y
                    ) *
                    t,
                a.Z +
                    (
                        b.Z -
                        a.Z
                    ) *
                    t,
                SegmentHeadingDegrees(
                    index),
                SegmentPitchDegrees(
                    index));
        }

        var last =
            _route[^1];

        return new RouteSample(
            _route.Length -
                1,
            last.X,
            last.Y,
            last.Z,
            SegmentHeadingDegrees(
                _route.Length -
                    2),
            SegmentPitchDegrees(
                _route.Length -
                    2));
    }

    private double InterpolateHeight(
        int segmentIndex,
        double t)
    {
        var a =
            _route[
                Math.Clamp(
                    segmentIndex,
                    0,
                    _route.Length -
                        1)];
        var b =
            _route[
                Math.Clamp(
                    segmentIndex +
                        1,
                    0,
                    _route.Length -
                        1)];

        return a.Y +
               (
                   b.Y -
                   a.Y
               ) *
               Math.Clamp(
                   t,
                   0.0,
                   1.0);
    }

    private double SegmentHeadingDegrees(
        int segmentIndex)
    {
        var index =
            Math.Clamp(
                segmentIndex,
                0,
                _route.Length -
                    2);

        var a =
            _route[index];
        var b =
            _route[index + 1];

        var heading =
            Math.Atan2(
                b.X -
                    a.X,
                b.Z -
                    a.Z) *
            180.0 /
            Math.PI;

        return heading <
                   0.0
            ? heading +
              360.0
            : heading;
    }

    private double SegmentPitchDegrees(
        int segmentIndex)
    {
        var index =
            Math.Clamp(
                segmentIndex,
                0,
                _route.Length -
                    2);

        var a =
            _route[index];
        var b =
            _route[index + 1];

        var horizontal =
            Distance2D(
                a.X,
                a.Z,
                b.X,
                b.Z);

        if (horizontal <=
            0.000001)
        {
            return 0.0;
        }

        return Math.Atan2(
                   b.Y -
                       a.Y,
                   horizontal) *
               180.0 /
               Math.PI;
    }

    private static double[] BuildCumulative(
        IReadOnlyList<WorldVector3> route)
    {
        if (route.Count ==
            0)
        {
            return [];
        }

        var result =
            new double[
                route.Count];

        for (var index = 1;
             index <
                 route.Count;
             index++)
        {
            result[index] =
                result[index - 1] +
                Distance2D(
                    route[index - 1].X,
                    route[index - 1].Z,
                    route[index].X,
                    route[index].Z);
        }

        return result;
    }

    private static (
        string Level,
        double? AverageSpeedKph,
        int Moving,
        int Slow,
        int Stopped)
        ResolveCongestion(
            WorldVector3 position,
            IReadOnlyList<WorldTrafficAgentState>? traffic)
    {
        if (traffic is null ||
            traffic.Count ==
                0)
        {
            return (
                "clear",
                null,
                0,
                0,
                0);
        }

        var speeds =
            traffic
                .Where(
                    agent =>
                        Distance2D(
                            position.X,
                            position.Z,
                            agent.Position.X,
                            agent.Position.Z) <=
                        CongestionSampleRadiusMeters)
                .Select(
                    static agent =>
                        agent.SpeedMetersPerSecond *
                        3.6)
                .Where(
                    static speed =>
                        double.IsFinite(speed) &&
                        speed >=
                            0.0 &&
                        speed <=
                            220.0)
                .ToArray();

        if (speeds.Length ==
            0)
        {
            return (
                "clear",
                null,
                0,
                0,
                0);
        }

        var stopped =
            speeds.Count(
                static speed =>
                    speed <
                    3.0);
        var slow =
            speeds.Count(
                static speed =>
                    speed >=
                        3.0 &&
                    speed <
                        15.0);
        var moving =
            speeds.Count(
                static speed =>
                    speed >=
                    15.0);
        var average =
            speeds.Average();
        var constrainedRatio =
            (
                stopped +
                slow
            ) /
            (double)speeds.Length;

        var level =
            (
                average,
                constrainedRatio,
                speeds.Length
            ) switch
            {
                (_, _, < 3) => "light",
                (< 8.0, >= 0.70, _) => "heavy",
                (< 15.0, >= 0.50, _) => "moderate",
                (< 25.0, >= 0.35, _) => "light",
                _ => "clear"
            };

        return (
            level,
            Math.Round(
                average,
                1),
            moving,
            slow,
            stopped);
    }

    private static double Distance2D(
        double x1,
        double z1,
        double x2,
        double z2)
    {
        var dx =
            x2 -
            x1;
        var dz =
            z2 -
            z1;

        return Math.Sqrt(
            dx *
                dx +
            dz *
                dz);
    }

    private static double WrapDegrees(
        double value)
    {
        var wrapped =
            (
                value +
                180.0
            ) %
            360.0;

        if (wrapped <
            0.0)
        {
            wrapped +=
                360.0;
        }

        return wrapped -
               180.0;
    }
}
