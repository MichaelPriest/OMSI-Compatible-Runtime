using System.Numerics;

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
    IReadOnlyList<int> ReverseConnections,
    long? SceneryObjectId = null,
    double? SpeedLimitKilometersPerHour = null,
    double? TrafficDensityWeight = null)
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
    private const double NearBestConnectionSlackMeters = 0.75;

    public static WorldTrafficPathNetwork Build(
        IReadOnlyList<WorldSplinePlacement> splines,
        IReadOnlyDictionary<string, WorldSplineAsset> splineAssets) =>
        Build(
            splines,
            splineAssets,
            Array.Empty<WorldObjectPlacement>(),
            new Dictionary<string, WorldSceneryAsset>(
                StringComparer.OrdinalIgnoreCase),
            Array.Empty<WorldTile>());

    public static WorldTrafficPathNetwork Build(
        IReadOnlyList<WorldSplinePlacement> splines,
        IReadOnlyDictionary<string, WorldSplineAsset> splineAssets,
        IReadOnlyList<WorldObjectPlacement> objects,
        IReadOnlyDictionary<string, WorldSceneryAsset> sceneryAssets,
        IReadOnlyList<WorldTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(
            splines);
        ArgumentNullException.ThrowIfNull(
            splineAssets);
        ArgumentNullException.ThrowIfNull(
            objects);
        ArgumentNullException.ThrowIfNull(
            sceneryAssets);
        ArgumentNullException.ThrowIfNull(
            tiles);

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
                        null,
                        pathIndex,
                        path.Type,
                        path.Direction,
                        path.Width,
                        points,
                        ResolveSpeedLimit(
                            spline.TrafficRules,
                            pathIndex),
                        ResolveTrafficDensity(
                            spline.TrafficRules,
                            pathIndex)));
            }
        }

        var terrainSampler =
            new WorldTerrainSampler(
                tiles);

        foreach (var instance in objects)
        {
            if (!sceneryAssets.TryGetValue(
                    instance.AssetPath,
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

                if (path.LengthMeters <=
                    0.001)
                {
                    continue;
                }

                var points =
                    BuildPoints(
                        instance,
                        asset,
                        path,
                        terrainSampler);

                if (points.Length < 2)
                {
                    continue;
                }

                builders.Add(
                    new SegmentBuilder(
                        builders.Count,
                        null,
                        instance,
                        pathIndex,
                        path.Type,
                        path.Direction,
                        path.WidthMeters,
                        points,
                        ResolveSpeedLimit(
                            instance.TrafficRules,
                            pathIndex),
                        ResolveTrafficDensity(
                            instance.TrafficRules,
                            pathIndex)));
            }
        }

        if (builders.Count == 0)
        {
            return WorldTrafficPathNetwork.Empty;
        }

        var bySplineId =
            builders
                .Where(
                    static item =>
                        item.Spline is not null)
                .GroupBy(
                    static item =>
                        item.Spline!.Id)
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

        foreach (var builder in builders)
        {
            if (builder.Spline is null)
            {
                continue;
            }

            if (builder.AllowsForward)
            {
                TryConnectLinkedSpline(
                    builder,
                    forward:
                        true,
                    builder.Spline.NextId,
                    bySplineId);
            }

            if (builder.AllowsReverse)
            {
                TryConnectLinkedSpline(
                    builder,
                    forward:
                        false,
                    builder.Spline.PreviousId,
                    bySplineId);
            }
        }

        foreach (var builder in builders)
        {
            if (builder.AllowsForward &&
                builder.ForwardConnections.Count ==
                    0)
            {
                ConnectByGeometry(
                    builder,
                    forward:
                        true,
                    builders);
            }

            if (builder.AllowsReverse &&
                builder.ReverseConnections.Count ==
                    0)
            {
                ConnectByGeometry(
                    builder,
                    forward:
                        false,
                    builders);
            }
        }

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
            if (builder.AllowsForward)
            {
                ClassifyEndpoint(
                    builder,
                    forward:
                        true,
                    activeSplineIds,
                    ref connectedEndpoints,
                    ref boundaryEndpoints,
                    ref terminalEndpoints,
                    ref unmatchedEndpoints);
            }

            if (builder.AllowsReverse)
            {
                ClassifyEndpoint(
                    builder,
                    forward:
                        false,
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
                            builder.Spline?.Id ??
                                -1,
                            builder.LocalPathIndex,
                            builder.Type,
                            builder.Direction,
                            builder.WidthMeters,
                            builder.Points,
                            builder.ForwardConnections
                                .Distinct()
                                .Order()
                                .ToArray(),
                            builder.ReverseConnections
                                .Distinct()
                                .Order()
                                .ToArray(),
                            builder.SceneryObject?.Id,
                            builder.SpeedLimitKilometersPerHour,
                            builder.TrafficDensityWeight))
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

    private static WorldVector3[] BuildPoints(
        WorldObjectPlacement instance,
        WorldSceneryAsset asset,
        WorldSceneryPath path,
        WorldTerrainSampler terrainSampler)
    {
        var segmentCount =
            Math.Clamp(
                (int)Math.Ceiling(
                    path.LengthMeters /
                    SampleLengthMeters),
                1,
                256);

        var worldX =
            instance.Tile.X *
                TileSizeMeters +
            instance.Position.X;

        var worldZ =
            instance.Tile.Y *
                TileSizeMeters +
            instance.Position.Z;

        var terrainOffset =
            0.0;

        if (!asset.UsesAbsoluteHeight &&
            terrainSampler.TrySample(
                worldX,
                worldZ,
                out var groundHeight))
        {
            terrainOffset =
                groundHeight;
        }

        var rotation =
            Matrix4x4.CreateFromYawPitchRoll(
                DegreesToRadians(
                    instance.HeadingDegrees),
                DegreesToRadians(
                    instance.PitchDegrees),
                DegreesToRadians(
                    instance.BankDegrees));

        var points =
            new WorldVector3[
                segmentCount + 1];

        for (var sample = 0;
             sample <= segmentCount;
             sample++)
        {
            var distance =
                path.LengthMeters *
                sample /
                segmentCount;

            var local =
                TransformSceneryPathPoint(
                    path,
                    distance);

            var rotated =
                Vector3.Transform(
                    new Vector3(
                        (float)local.X,
                        (float)local.Y,
                        (float)local.Z),
                    rotation);

            points[sample] =
                new WorldVector3(
                    worldX +
                        rotated.X,
                    instance.Position.Y +
                        terrainOffset +
                        rotated.Y,
                    worldZ +
                        rotated.Z);
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
            DegreesToRadians(
                spline.HeadingDegrees);

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

    private static WorldVector3 TransformSceneryPathPoint(
        WorldSceneryPath path,
        double distance)
    {
        var clamped =
            Math.Clamp(
                distance,
                0.0,
                path.LengthMeters);

        var yaw =
            DegreesToRadians(
                path.HeadingDegrees);

        var hasCurve =
            Math.Abs(
                path.RadiusMeters) >
            0.001;

        var curveAngle =
            hasCurve
                ? clamped /
                  path.RadiusMeters
                : 0.0;

        var curveX =
            hasCurve
                ? path.RadiusMeters *
                  (1.0 -
                   Math.Cos(
                       curveAngle))
                : 0.0;

        var curveY =
            hasCurve
                ? path.RadiusMeters *
                  Math.Sin(
                      curveAngle)
                : clamped;

        var cosYaw =
            Math.Cos(
                yaw);

        var sinYaw =
            Math.Sin(
                yaw);

        // Crossing-editor path coordinates use OMSI's X/Y ground plane
        // with Z as height. Normalize them to the runtime's X/Z ground
        // plane and Y-up convention before applying the object placement.
        return new WorldVector3(
            path.X +
                curveX *
                    cosYaw +
                curveY *
                    sinYaw,
            path.Z +
                GradientRise(
                    path.GradientStart,
                    path.GradientEnd,
                    path.LengthMeters,
                    clamped),
            path.Y -
                curveX *
                    sinYaw +
                curveY *
                    cosYaw);
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

    private static void TryConnectLinkedSpline(
        SegmentBuilder source,
        bool forward,
        long neighborSplineId,
        IReadOnlyDictionary<long, SegmentBuilder[]> bySplineId)
    {
        if (neighborSplineId <
                0 ||
            !bySplineId.TryGetValue(
                neighborSplineId,
                out var candidates))
        {
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
            if (candidate.Type !=
                    source.Type ||
                (forward &&
                 !candidate.AllowsForward) ||
                (!forward &&
                 !candidate.AllowsReverse))
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
                ConnectionTolerance(
                    source,
                    candidate);

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
            return;
        }

        AddConnection(
            source,
            forward,
            best.Index);
    }

    private static void ConnectByGeometry(
        SegmentBuilder source,
        bool forward,
        IReadOnlyList<SegmentBuilder> builders)
    {
        var sourcePoint =
            forward
                ? source.Points[^1]
                : source.Points[0];

        var candidates =
            new List<(
                SegmentBuilder Segment,
                double Distance)>();

        foreach (var candidate in builders)
        {
            if (candidate.Index ==
                    source.Index ||
                candidate.Type !=
                    source.Type ||
                (forward &&
                 !candidate.AllowsForward) ||
                (!forward &&
                 !candidate.AllowsReverse))
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

            if (distance >
                ConnectionTolerance(
                    source,
                    candidate))
            {
                continue;
            }

            candidates.Add(
                (candidate, distance));
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var bestDistance =
            candidates.Min(
                static item =>
                    item.Distance);

        var threshold =
            Math.Min(
                MaximumConnectionToleranceMeters,
                bestDistance +
                    NearBestConnectionSlackMeters);

        foreach (var candidate in
                 candidates
                     .Where(
                         item =>
                             item.Distance <=
                             threshold)
                     .OrderBy(
                         static item =>
                             item.Distance)
                     .ThenBy(
                         static item =>
                             item.Segment.Index))
        {
            AddConnection(
                source,
                forward,
                candidate.Segment.Index);
        }
    }

    private static void AddConnection(
        SegmentBuilder source,
        bool forward,
        int targetIndex)
    {
        var connections =
            forward
                ? source.ForwardConnections
                : source.ReverseConnections;

        if (!connections.Contains(
                targetIndex))
        {
            connections.Add(
                targetIndex);
        }
    }

    private static void ClassifyEndpoint(
        SegmentBuilder source,
        bool forward,
        IReadOnlySet<long> activeSplineIds,
        ref int connectedEndpoints,
        ref int boundaryEndpoints,
        ref int terminalEndpoints,
        ref int unmatchedEndpoints)
    {
        var connections =
            forward
                ? source.ForwardConnections
                : source.ReverseConnections;

        if (connections.Count > 0)
        {
            connectedEndpoints++;
            return;
        }

        if (source.Spline is null)
        {
            terminalEndpoints++;
            return;
        }

        var neighborSplineId =
            forward
                ? source.Spline.NextId
                : source.Spline.PreviousId;

        if (neighborSplineId < 0)
        {
            terminalEndpoints++;
        }
        else if (!activeSplineIds.Contains(
                     neighborSplineId))
        {
            boundaryEndpoints++;
        }
        else
        {
            unmatchedEndpoints++;
        }
    }

    private static double? ResolveSpeedLimit(
        IReadOnlyList<WorldTrafficRule>? rules,
        int pathIndex)
    {
        if (rules is null ||
            rules.Count ==
                0)
        {
            return null;
        }

        return rules
            .Where(
                rule =>
                    rule.PathIndex ==
                        pathIndex &&
                    rule.Name.Equals(
                        "speedlimit",
                        StringComparison.OrdinalIgnoreCase) &&
                    (rule.GroupIndex is
                         null or 0) &&
                    double.IsFinite(
                        rule.Value) &&
                    rule.Value >
                        0.0)
            .Select(
                static rule =>
                    (double?)rule.Value)
            .LastOrDefault();
    }

    private static double? ResolveTrafficDensity(
        IReadOnlyList<WorldTrafficRule>? rules,
        int pathIndex)
    {
        if (rules is null ||
            rules.Count ==
                0)
        {
            return null;
        }

        return rules
            .Where(
                rule =>
                    rule.PathIndex ==
                        pathIndex &&
                    rule.Name.Equals(
                        "trafficdensity",
                        StringComparison.OrdinalIgnoreCase) &&
                    (rule.GroupIndex is
                         null or 0) &&
                    double.IsFinite(
                        rule.Value) &&
                    rule.Value >=
                        0.0)
            .Select(
                static rule =>
                    (double?)rule.Value)
            .LastOrDefault();
    }

    private static double ConnectionTolerance(
        SegmentBuilder source,
        SegmentBuilder candidate) =>
        Math.Clamp(
            (source.WidthMeters +
             candidate.WidthMeters) *
                0.5 +
            1.0,
            MinimumConnectionToleranceMeters,
            MaximumConnectionToleranceMeters);

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

    private static float DegreesToRadians(
        double degrees) =>
        (float)(
            degrees *
            Math.PI /
            180.0);

    private sealed class WorldTerrainSampler
    {
        private readonly Dictionary<
            (int X, int Y),
            WorldTerrainData>
            _terrainByTile;

        public WorldTerrainSampler(
            IReadOnlyList<WorldTile> tiles)
        {
            _terrainByTile =
                tiles
                    .Where(
                        static tile =>
                            tile.Terrain is not null)
                    .ToDictionary(
                        static tile =>
                            (
                                tile.Coordinate.X,
                                tile.Coordinate.Y),
                        static tile =>
                            tile.Terrain!);
        }

        public bool TrySample(
            double worldX,
            double worldZ,
            out double height)
        {
            var tileX =
                (int)Math.Floor(
                    worldX /
                    TileSizeMeters);

            var tileY =
                (int)Math.Floor(
                    worldZ /
                    TileSizeMeters);

            if (!_terrainByTile.TryGetValue(
                    (tileX, tileY),
                    out var terrain) ||
                terrain.CellCount <=
                    0)
            {
                height =
                    0.0;
                return false;
            }

            var localX =
                worldX -
                tileX *
                    TileSizeMeters;

            var localZ =
                worldZ -
                tileY *
                    TileSizeMeters;

            var spacing =
                TileSizeMeters /
                terrain.CellCount;

            var gridX =
                Math.Clamp(
                    localX /
                        spacing,
                    0.0,
                    terrain.CellCount);

            var gridZ =
                Math.Clamp(
                    localZ /
                        spacing,
                    0.0,
                    terrain.CellCount);

            var x0 =
                Math.Clamp(
                    (int)Math.Floor(
                        gridX),
                    0,
                    terrain.CellCount);

            var z0 =
                Math.Clamp(
                    (int)Math.Floor(
                        gridZ),
                    0,
                    terrain.CellCount);

            var x1 =
                Math.Min(
                    x0 +
                        1,
                    terrain.CellCount);

            var z1 =
                Math.Min(
                    z0 +
                        1,
                    terrain.CellCount);

            var tx =
                gridX -
                x0;

            var tz =
                gridZ -
                z0;

            var sampleCount =
                terrain.CellCount +
                1;

            var h00 =
                Read(
                    terrain,
                    sampleCount,
                    z0,
                    x0);

            var h10 =
                Read(
                    terrain,
                    sampleCount,
                    z0,
                    x1);

            var h01 =
                Read(
                    terrain,
                    sampleCount,
                    z1,
                    x0);

            var h11 =
                Read(
                    terrain,
                    sampleCount,
                    z1,
                    x1);

            if (!double.IsFinite(
                    h00) ||
                !double.IsFinite(
                    h10) ||
                !double.IsFinite(
                    h01) ||
                !double.IsFinite(
                    h11))
            {
                height =
                    0.0;
                return false;
            }

            var top =
                h00 +
                (h10 -
                 h00) *
                    tx;

            var bottom =
                h01 +
                (h11 -
                 h01) *
                    tx;

            height =
                top +
                (bottom -
                 top) *
                    tz;

            return true;
        }

        private static double Read(
            WorldTerrainData terrain,
            int sampleCount,
            int row,
            int column)
        {
            var index =
                row *
                    sampleCount +
                column;

            return index >=
                       0 &&
                   index <
                       terrain.Heights.Count
                ? terrain.Heights[
                    index]
                : 0.0;
        }
    }

    private sealed class SegmentBuilder(
        int index,
        WorldSplinePlacement? spline,
        WorldObjectPlacement? sceneryObject,
        int localPathIndex,
        int type,
        int direction,
        double widthMeters,
        WorldVector3[] points,
        double? speedLimitKilometersPerHour,
        double? trafficDensityWeight)
    {
        public int Index { get; } =
            index;

        public WorldSplinePlacement? Spline { get; } =
            spline;

        public WorldObjectPlacement? SceneryObject { get; } =
            sceneryObject;

        public int LocalPathIndex { get; } =
            localPathIndex;

        public int Type { get; } =
            type;

        public int Direction { get; } =
            direction;

        public double WidthMeters { get; } =
            widthMeters;

        public double? SpeedLimitKilometersPerHour { get; } =
            speedLimitKilometersPerHour;

        public double? TrafficDensityWeight { get; } =
            trafficDensityWeight;

        public WorldVector3[] Points { get; } =
            points;

        public bool AllowsForward =>
            Direction is
                0 or 2;

        public bool AllowsReverse =>
            Direction is
                1 or 2;

        public List<int> ForwardConnections { get; } =
            [];

        public List<int> ReverseConnections { get; } =
            [];
    }
}
