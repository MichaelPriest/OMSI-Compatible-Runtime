using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeSplineTerrainAlignmentSampler
{
    private const float CellSizeMeters = 24.0f;
    private const float TerrainClearanceMeters = 0.01f;
    private const float AlignmentFeatherMeters = 2.0f;
    private readonly Dictionary<(int X, int Z), AlignmentSegment[]> _segmentsByCell;

    private RuntimeSplineTerrainAlignmentSampler(
        Dictionary<(int X, int Z), AlignmentSegment[]> segmentsByCell,
        int segmentCount)
    {
        _segmentsByCell = segmentsByCell;
        SegmentCount = segmentCount;
    }

    public static RuntimeSplineTerrainAlignmentSampler Empty { get; } =
        new(
            new Dictionary<(int X, int Z), AlignmentSegment[]>(),
            0);

    public int SegmentCount { get; }

    public static RuntimeSplineTerrainAlignmentSampler Create(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        if (splines.Count == 0)
        {
            return Empty;
        }

        var cells =
            new Dictionary<
                (int X, int Z),
                List<AlignmentSegment>>();

        var segmentCount =
            0;

        foreach (var spline in splines)
        {
            if (spline.TerrainAlignMode is not
                    > 0 ||
                spline.LengthMeters <=
                    0.01 ||
                spline.Surfaces.Count ==
                    0)
            {
                continue;
            }

            var profileHalfWidth =
                ResolveProfileHalfWidth(
                    spline);

            if (profileHalfWidth <=
                0.05f)
            {
                continue;
            }

            var sampleCount =
                Math.Clamp(
                    (int)Math.Ceiling(
                        spline.LengthMeters /
                        4.0),
                    1,
                    256);

            var previous =
                SampleCenter(
                    spline,
                    0.0);

            for (var index = 1;
                 index <=
                     sampleCount;
                 index++)
            {
                var distance =
                    spline.LengthMeters *
                    index /
                    sampleCount;

                var current =
                    SampleCenter(
                        spline,
                        distance);

                var segment =
                    new AlignmentSegment(
                        previous,
                        current,
                        profileHalfWidth,
                        profileHalfWidth +
                            AlignmentFeatherMeters);

                Register(
                    segment,
                    cells);

                segmentCount++;
                previous =
                    current;
            }
        }

        return segmentCount ==
               0
            ? Empty
            : new RuntimeSplineTerrainAlignmentSampler(
                cells.ToDictionary(
                    static pair =>
                        pair.Key,
                    static pair =>
                        pair.Value.ToArray()),
                segmentCount);
    }

    public float AlignHeight(
        float x,
        float z,
        float originalHeight)
    {
        if (SegmentCount == 0)
        {
            return originalHeight;
        }

        var cell =
            CellOf(
                x,
                z);

        if (!_segmentsByCell.TryGetValue(
                cell,
                out var segments))
        {
            return originalHeight;
        }

        var bestDistance =
            float.PositiveInfinity;

        var bestHeight =
            originalHeight;

        var bestCoreWidth =
            0.0f;

        var bestOuterWidth =
            0.0f;

        foreach (var segment in segments)
        {
            if (!TrySample(
                    segment,
                    x,
                    z,
                    out var distance,
                    out var height))
            {
                continue;
            }

            var candidateHeight =
                height -
                TerrainClearanceMeters;

            var closerInPlan =
                distance <
                bestDistance -
                    0.05f;

            var samePlanDistance =
                Math.Abs(
                    distance -
                    bestDistance) <=
                0.05f;

            var closerToOriginalTerrain =
                Math.Abs(
                    candidateHeight -
                    originalHeight) <
                Math.Abs(
                    bestHeight -
                    originalHeight);

            if (!closerInPlan &&
                !(samePlanDistance &&
                  closerToOriginalTerrain))
            {
                continue;
            }

            bestDistance =
                distance;

            bestHeight =
                candidateHeight;

            bestCoreWidth =
                segment.CoreHalfWidth;

            bestOuterWidth =
                segment.OuterHalfWidth;
        }

        if (!float.IsFinite(
                bestDistance) ||
            bestDistance >
                bestOuterWidth)
        {
            return originalHeight;
        }

        if (bestDistance <=
            bestCoreWidth)
        {
            return bestHeight;
        }

        var feather =
            Math.Max(
                bestOuterWidth -
                    bestCoreWidth,
                0.001f);

        var weight =
            1.0f -
            Math.Clamp(
                (bestDistance -
                 bestCoreWidth) /
                feather,
                0.0f,
                1.0f);

        return originalHeight +
               (bestHeight -
                originalHeight) *
               weight;
    }

    private static float ResolveProfileHalfWidth(
        RuntimeSplineInfo spline)
    {
        var maximum =
            0.0f;

        foreach (var surface in
                 spline.Surfaces)
        {
            maximum =
                Math.Max(
                    maximum,
                    Math.Abs(
                        (float)surface.From.X));

            maximum =
                Math.Max(
                    maximum,
                    Math.Abs(
                        (float)surface.To.X));
        }

        return Math.Clamp(
            maximum,
            0.0f,
            40.0f);
    }

    private static Vector3 SampleCenter(
        RuntimeSplineInfo spline,
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
            spline.TileX *
                300.0 +
            spline.X;

        var startZ =
            spline.TileY *
                300.0 +
            spline.Z;

        var worldX =
            startX +
            localX *
                cosYaw +
            localZ *
                sinYaw;

        var worldZ =
            startZ -
            localX *
                sinYaw +
            localZ *
                cosYaw;

        var worldY =
            spline.Y +
            GradientRise(
                spline,
                clamped);

        return new Vector3(
            (float)worldX,
            (float)worldY,
            (float)worldZ);
    }

    private static double GradientRise(
        RuntimeSplineInfo spline,
        double distance)
    {
        var length =
            spline.LengthMeters;

        if (length <=
            0.0)
        {
            return 0.0;
        }

        var clamped =
            Math.Clamp(
                distance,
                0.0,
                length);

        var startSlope =
            spline.GradientStartPercent /
            100.0;

        if (spline.UsesHeightProfile)
        {
            var endSlope =
                spline.GradientEndPercent /
                100.0;

            var heightResidual =
                spline.DeltaHeightMeters -
                startSlope *
                    length;

            var c =
                ((endSlope -
                  startSlope) *
                     length -
                 2.0 *
                     heightResidual) /
                (length *
                 length *
                 length);

            var a =
                (-(endSlope -
                   startSlope) *
                     length +
                 3.0 *
                     heightResidual) /
                (length *
                 length);

            return c *
                       clamped *
                       clamped *
                       clamped +
                   a *
                       clamped *
                       clamped +
                   startSlope *
                       clamped;
        }

        var slopeDelta =
            (spline.GradientEndPercent -
             spline.GradientStartPercent) /
            100.0;

        return startSlope *
                   clamped +
               0.5 *
                   slopeDelta *
                   clamped *
                   clamped /
                   length;
    }

    private static void Register(
        AlignmentSegment segment,
        Dictionary<
            (int X, int Z),
            List<AlignmentSegment>> cells)
    {
        var minimumX =
            Math.Min(
                segment.Start.X,
                segment.End.X) -
            segment.OuterHalfWidth;

        var maximumX =
            Math.Max(
                segment.Start.X,
                segment.End.X) +
            segment.OuterHalfWidth;

        var minimumZ =
            Math.Min(
                segment.Start.Z,
                segment.End.Z) -
            segment.OuterHalfWidth;

        var maximumZ =
            Math.Max(
                segment.Start.Z,
                segment.End.Z) +
            segment.OuterHalfWidth;

        var minimumCell =
            CellOf(
                minimumX,
                minimumZ);

        var maximumCell =
            CellOf(
                maximumX,
                maximumZ);

        for (var z = minimumCell.Z;
             z <=
                 maximumCell.Z;
             z++)
        {
            for (var x = minimumCell.X;
                 x <=
                     maximumCell.X;
                 x++)
            {
                var key =
                    (x, z);

                if (!cells.TryGetValue(
                        key,
                        out var list))
                {
                    list =
                        [];

                    cells[key] =
                        list;
                }

                list.Add(
                    segment);
            }
        }
    }

    private static bool TrySample(
        AlignmentSegment segment,
        float x,
        float z,
        out float distance,
        out float height)
    {
        var dx =
            segment.End.X -
            segment.Start.X;

        var dz =
            segment.End.Z -
            segment.Start.Z;

        var lengthSquared =
            dx *
                dx +
            dz *
                dz;

        if (lengthSquared <=
            0.000001f)
        {
            distance =
                float.PositiveInfinity;

            height =
                0.0f;

            return false;
        }

        var t =
            Math.Clamp(
                ((x -
                  segment.Start.X) *
                     dx +
                 (z -
                  segment.Start.Z) *
                     dz) /
                lengthSquared,
                0.0f,
                1.0f);

        var closestX =
            segment.Start.X +
            dx *
                t;

        var closestZ =
            segment.Start.Z +
            dz *
                t;

        var offsetX =
            x -
            closestX;

        var offsetZ =
            z -
            closestZ;

        distance =
            MathF.Sqrt(
                offsetX *
                    offsetX +
                offsetZ *
                    offsetZ);

        if (distance >
            segment.OuterHalfWidth)
        {
            height =
                0.0f;

            return false;
        }

        height =
            segment.Start.Y +
            (segment.End.Y -
             segment.Start.Y) *
                t;

        return true;
    }

    private static (int X, int Z) CellOf(
        float x,
        float z) =>
        (
            (int)MathF.Floor(
                x /
                CellSizeMeters),
            (int)MathF.Floor(
                z /
                CellSizeMeters)
        );

    private readonly record struct AlignmentSegment(
        Vector3 Start,
        Vector3 End,
        float CoreHalfWidth,
        float OuterHalfWidth);
}
