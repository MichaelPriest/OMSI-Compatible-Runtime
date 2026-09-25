namespace OMSICompatible.World;

public sealed record WorldTrafficPathSegment(
    int Index,
    long SplineId,
    int LocalPathIndex,
    int Type,
    int Direction,
    double WidthMeters,
    IReadOnlyList<WorldVector3> Points,
    IReadOnlyList<int> ForwardConnections,
    IReadOnlyList<int> ReverseConnections)
{
    public bool AllowsForward =>
        Direction is 0 or 2;

    public bool AllowsReverse =>
        Direction is 1 or 2;

    public WorldVector3 Start =>
        Points.Count == 0
            ? default
            : Points[0];

    public WorldVector3 End =>
        Points.Count == 0
            ? default
            : Points[^1];
}

public sealed record WorldTrafficPathNetwork(
    IReadOnlyList<WorldTrafficPathSegment> Segments,
    int RoadVehicleSegmentCount,
    int PedestrianSegmentCount,
    int RailSegmentCount,
    int AircraftSegmentCount,
    int ConnectedEndpointCount,
    int BoundaryEndpointCount,
    int TerminalEndpointCount,
    int UnmatchedEndpointCount)
{
    public static WorldTrafficPathNetwork Empty { get; } =
        new(
            Array.Empty<WorldTrafficPathSegment>(),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
}

public static class WorldTrafficPathNetworkBuilder
{
    private const double TileSizeMeters = 300.0;
    private const double SampleLengthMeters = 4.0;
    private const double MinimumConnectionToleranceMeters = 3.0;
    private const double MaximumConnectionToleranceMeters = 8.0;

    public static WorldTrafficPathNetwork Build(
        IReadOnlyList<WorldSplinePlacement> splines,
        IReadOnlyDictionary<string, WorldSplineAsset> splineAssets)
    {
        ArgumentNullException.ThrowIfNull(
            splines);
        ArgumentNullException.ThrowIfNull(
            splineAssets);

        if (splines.Count == 0)
        {
            return WorldTrafficPathNetwork.Empty;
        }

        var builders =
            new List<SegmentBuilder>();

        foreach (var spline in splines)
        {
            if (spline.LengthMeters <=
                    0.001 ||
                !splineAssets.TryGetValue(
                    spline.AssetPath,
                    out var asset) ||
                !asset.Exists ||
                asset.Paths.Count == 0)
            {
                continue;
            }

            for (var pathIndex = 0;
                 pathIndex < asset.Paths.Count;
                 pathIndex++)
            {
                var path =
                    asset.Paths[pathIndex];

                var points =
                    BuildPoints(
                        spline,
                        path);

                if (points.Length < 2)
                {
                    continue;
                }

                builders.Add(
                    new SegmentBuilder(
                        builders.Count,
                        spline,
                        pathIndex,
                        path,
                        points));
            }
        }

        if (builders.Count == 0)
        {
            return WorldTrafficPathNetwork.Empty;
        }

        var bySplineId =
            builders
                .GroupBy(
                    static item =>
                        item.Spline.Id)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.ToArray());

        var activeSplineIds =
            splines
                .Select(
                    static spline =>
                        spline.Id)
                .ToHashSet();

        var connectedEndpoints =
            0;

        var boundaryEndpoints =
            0;

        var terminalEndpoints =
            0;

        var unmatchedEndpoints =
            0;

        foreach (var builder in builders)
        {
            if (builder.Path.Direction is
                0 or 2)
            {
                Connect(
                    builder,
                    forward:
                        true,
                    builder.Spline.NextId,
                    bySplineId,
                    activeSplineIds,
                    ref connectedEndpoints,
                    ref boundaryEndpoints,
                    ref terminalEndpoints,
                    ref unmatchedEndpoints);
            }

            if (builder.Path.Direction is
                1 or 2)
            {
                Connect(
                    builder,
                    forward:
                        false,
                    builder.Spline.PreviousId,
                    bySplineId,
                    activeSplineIds,
                    ref connectedEndpoints,
                    ref boundaryEndpoints,
                    ref terminalEndpoints,
                    ref unmatchedEndpoints);
            }
        }

        var segments =
            builders
                .Select(
                    static builder =>
                        new WorldTrafficPathSegment(
                            builder.Index,
                            builder.Spline.Id,
                            builder.LocalPathIndex,
                            builder.Path.Type,
                            builder.Path.Direction,
                            builder.Path.Width,
                            builder.Points,
                            builder.ForwardConnections
                                .Distinct()
                                .Order()
                                .ToArray(),
                            builder.ReverseConnections
                                .Distinct()
                                .Order()
                                .ToArray()))
                .ToArray();

        return new WorldTrafficPathNetwork(
            segments,
            segments.Count(
                static segment =>
                    segment.Type == 0),
            segments.Count(
                static segment =>
                    segment.Type == 1),
            segments.Count(
                static segment =>
                    segment.Type == 2),
            segments.Count(
                static segment =>
                    segment.Type == 3),
            connectedEndpoints,
            boundaryEndpoints,
            terminalEndpoints,
            unmatchedEndpoints);
    }

    private static WorldVector3[] BuildPoints(
        WorldSplinePlacement spline,
        WorldSplinePath path)
    {
        var segmentCount =
            Math.Clamp(
                (int)Math.Ceiling(
                    spline.LengthMeters /
                    SampleLengthMeters),
                1,
                256);

        var points =
            new WorldVector3[
                segmentCount + 1];

        for (var sample = 0;
             sample <= segmentCount;
             sample++)
        {
            var distance =
                spline.LengthMeters *
                sample /
                segmentCount;

            points[sample] =
                TransformPathPoint(
                    spline,
                    path,
                    distance);
        }

        return points;
    }

    private static WorldVector3 TransformPathPoint(
        WorldSplinePlacement spline,
        WorldSplinePath path,
        double distance)
    {
        var clamped =
            Math.Clamp(
                distance,
                0.0,
                spline.LengthMeters);

        var yaw =
            spline.HeadingDegrees *
            Math.PI /
            180.0;

        var hasCurve =
            Math.Abs(
                spline.RadiusMeters) >
            0.001;

        var curveAngle =
            hasCurve
                ? clamped /
                  spline.RadiusMeters
                : 0.0;

        var localX =
            hasCurve
                ? spline.RadiusMeters *
                  (1.0 -
                   Math.Cos(
                       curveAngle))
                : 0.0;

        var localZ =
            hasCurve
                ? spline.RadiusMeters *
                  Math.Sin(
                      curveAngle)
                : clamped;

        var cosYaw =
            Math.Cos(
                yaw);

        var sinYaw =
            Math.Sin(
                yaw);

        var startX =
            spline.Tile.X *
                TileSizeMeters +
            spline.Position.X;

        var startZ =
            spline.Tile.Y *
                TileSizeMeters +
            spline.Position.Z;

        var centerX =
            startX +
            localX *
                cosYaw +
            localZ *
                sinYaw;

        var centerZ =
            startZ -
            localX *
                sinYaw +
            localZ *
                cosYaw;

        var heading =
            yaw +
            curveAngle;

        var lateralX =
            Math.Cos(
                heading);

        var lateralZ =
            -Math.Sin(
                heading);

        return new WorldVector3(
            centerX +
                lateralX *
                path.X,
            spline.Position.Y +
                GradientRise(
                    spline.GradientStartPercent,
                    spline.GradientEndPercent,
                    spline.LengthMeters,
                    clamped) +
                path.Z,
            centerZ +
                lateralZ *
                path.X);
    }

    private static double GradientRise(
        double startPercent,
        double endPercent,
        double lengthMeters,
        double distanceMeters)
    {
        if (lengthMeters <=
            0.0)
        {
            return 0.0;
        }

        var clamped =
            Math.Clamp(
                distanceMeters,
                0.0,
                lengthMeters);

        var startSlope =
            startPercent /
            100.0;

        var slopeDelta =
            (endPercent -
             startPercent) /
            100.0;

        return startSlope *
                   clamped +
               0.5 *
                   slopeDelta *
                   clamped *
                   clamped /
                   lengthMeters;
    }

    private static void Connect(
        SegmentBuilder source,
        bool forward,
        long neighborSplineId,
        IReadOnlyDictionary<long, SegmentBuilder[]> bySplineId,
        IReadOnlySet<long> activeSplineIds,
        ref int connectedEndpoints,
        ref int boundaryEndpoints,
        ref int terminalEndpoints,
        ref int unmatchedEndpoints)
    {
        if (neighborSplineId <
            0)
        {
            terminalEndpoints++;
            return;
        }

        if (!activeSplineIds.Contains(
                neighborSplineId))
        {
            boundaryEndpoints++;
            return;
        }

        if (!bySplineId.TryGetValue(
                neighborSplineId,
                out var candidates))
        {
            unmatchedEndpoints++;
            return;
        }

        var sourcePoint =
            forward
                ? source.Points[^1]
                : source.Points[0];

        SegmentBuilder? best =
            null;

        var bestDistance =
            double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (candidate.Path.Type !=
                    source.Path.Type ||
                (forward &&
                 candidate.Path.Direction is
                     not (0 or 2)) ||
                (!forward &&
                 candidate.Path.Direction is
                     not (1 or 2)))
            {
                continue;
            }

            var targetPoint =
                forward
                    ? candidate.Points[0]
                    : candidate.Points[^1];

            var distance =
                Distance(
                    sourcePoint,
                    targetPoint);

            var tolerance =
                Math.Clamp(
                    (source.Path.Width +
                     candidate.Path.Width) *
                        0.5 +
                    1.0,
                    MinimumConnectionToleranceMeters,
                    MaximumConnectionToleranceMeters);

            if (distance >
                    tolerance ||
                distance >=
                    bestDistance)
            {
                continue;
            }

            best =
                candidate;

            bestDistance =
                distance;
        }

        if (best is null)
        {
            unmatchedEndpoints++;
            return;
        }

        if (forward)
        {
            source.ForwardConnections.Add(
                best.Index);
        }
        else
        {
            source.ReverseConnections.Add(
                best.Index);
        }

        connectedEndpoints++;
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

    private sealed class SegmentBuilder(
        int index,
        WorldSplinePlacement spline,
        int localPathIndex,
        WorldSplinePath path,
        WorldVector3[] points)
    {
        public int Index { get; } =
            index;

        public WorldSplinePlacement Spline { get; } =
            spline;

        public int LocalPathIndex { get; } =
            localPathIndex;

        public WorldSplinePath Path { get; } =
            path;

        public WorldVector3[] Points { get; } =
            points;

        public List<int> ForwardConnections { get; } =
            [];

        public List<int> ReverseConnections { get; } =
            [];
    }
}
