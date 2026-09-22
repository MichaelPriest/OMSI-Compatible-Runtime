using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeSplineGeometry(
    RuntimeObjectVertex[] Vertices,
    IReadOnlyList<RuntimeObjectBatch> Batches,
    int RenderedSplineCount,
    int TexturedBatchCount)
{
    public static RuntimeSplineGeometry Empty { get; } =
        new(
            [],
            Array.Empty<RuntimeObjectBatch>(),
            0,
            0);
}

internal static class RuntimeSplineGeometryBuilder
{
    private readonly record struct BatchKey(
        string? TexturePath,
        bool AlphaCutout,
        bool AlphaBlend = false);

    public static RuntimeSplineGeometry Build(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        if (splines.Count == 0)
        {
            return RuntimeSplineGeometry.Empty;
        }

        var batches =
            new Dictionary<
                BatchKey,
                List<RuntimeObjectVertex>>();

        var batchOrder =
            new List<BatchKey>();

        var rendered = 0;

        foreach (var spline in splines)
        {
            if (spline.LengthMeters < 0.01 ||
                spline.Surfaces.Count == 0)
            {
                continue;
            }

            var contributed = false;

            var segmentCount = Math.Clamp(
                (int)Math.Ceiling(
                    spline.LengthMeters / 4.0),
                1,
                256);

            foreach (var surface in spline.Surfaces)
            {
                var hasTexture =
                    !string.IsNullOrWhiteSpace(
                        surface.TexturePath);

                var key =
                    new BatchKey(
                        surface.TexturePath,
                        hasTexture &&
                        surface.AlphaMode == 1,
                        hasTexture &&
                        surface.AlphaMode == 2);

                var output =
                    GetBatch(
                        key,
                        batches,
                        batchOrder);

                var color =
                    hasTexture
                        ? new Color4(
                            1,
                            1,
                            1,
                            1)
                        : new Color4(
                            0.24f,
                            0.25f,
                            0.27f,
                            1.0f);

                var before =
                    output.Count;

                for (var segment = 0;
                     segment < segmentCount;
                     segment++)
                {
                    var distance0 =
                        spline.LengthMeters *
                        segment /
                        segmentCount;

                    var distance1 =
                        spline.LengthMeters *
                        (segment + 1) /
                        segmentCount;

                    var frame0 =
                        GetFrame(
                            spline,
                            distance0);

                    var frame1 =
                        GetFrame(
                            spline,
                            distance1);

                    var left0 =
                        Transform(
                            frame0,
                            surface.From);

                    var left1 =
                        Transform(
                            frame1,
                            surface.From);

                    var right1 =
                        Transform(
                            frame1,
                            surface.To);

                    var right0 =
                        Transform(
                            frame0,
                            surface.To);

                    var leftUv0 =
                        new Vector2(
                            (float)surface.From.TextureX,
                            (float)(
                                distance0 *
                                surface.From.TextureScale));

                    var leftUv1 =
                        new Vector2(
                            (float)surface.From.TextureX,
                            (float)(
                                distance1 *
                                surface.From.TextureScale));

                    var rightUv1 =
                        new Vector2(
                            (float)surface.To.TextureX,
                            (float)(
                                distance1 *
                                surface.To.TextureScale));

                    var rightUv0 =
                        new Vector2(
                            (float)surface.To.TextureX,
                            (float)(
                                distance0 *
                                surface.To.TextureScale));

                    AppendQuad(
                        left0,
                        left1,
                        right1,
                        right0,
                        leftUv0,
                        leftUv1,
                        rightUv1,
                        rightUv0,
                        color,
                        output);
                }

                if (output.Count > before)
                {
                    contributed = true;
                }
            }

            if (contributed)
            {
                rendered++;
            }
        }

        var vertices =
            new List<RuntimeObjectVertex>();

        var runtimeBatches =
            new List<RuntimeObjectBatch>(
                batchOrder.Count);

        foreach (var key in batchOrder)
        {
            var batchVertices =
                batches[key];

            if (batchVertices.Count == 0)
            {
                continue;
            }

            var start =
                (uint)vertices.Count;

            vertices.AddRange(
                batchVertices);

            runtimeBatches.Add(
                new RuntimeObjectBatch(
                    start,
                    (uint)batchVertices.Count,
                    key.TexturePath,
                    key.AlphaCutout,
                    key.AlphaBlend));
        }

        return new RuntimeSplineGeometry(
            vertices.ToArray(),
            runtimeBatches.ToArray(),
            rendered,
            runtimeBatches.Count(
                static batch =>
                    !string.IsNullOrWhiteSpace(
                        batch.TexturePath)));
    }

    private static List<RuntimeObjectVertex> GetBatch(
        BatchKey key,
        IDictionary<BatchKey, List<RuntimeObjectVertex>> batches,
        ICollection<BatchKey> batchOrder)
    {
        if (batches.TryGetValue(
                key,
                out var existing))
        {
            return existing;
        }

        var created =
            new List<RuntimeObjectVertex>();

        batches[key] =
            created;

        batchOrder.Add(
            key);

        return created;
    }

    private static (
        Vector3 Center,
        Vector3 Lateral,
        Vector3 Forward)
        GetFrame(
            RuntimeSplineInfo spline,
            double distance)
    {
        var clamped = Math.Clamp(
            distance,
            0.0,
            spline.LengthMeters);

        // OMSI map coordinates use X/Y on the ground plane
        // with Z up. The renderer maps them to X/Z with Y up.
        // This Y<->Z swap reverses handedness, so OMSI rot/heading
        // around source Z becomes renderer yaw around Y with
        // the opposite sign.
        var yaw =
            -spline.HeadingDegrees *
            Math.PI /
            180.0;

        var hasCurve =
            Math.Abs(
                spline.RadiusMeters) >
            0.001;

        var curveAngle = hasCurve
            ? clamped /
              spline.RadiusMeters
            : 0.0;

        var localX = hasCurve
            ? spline.RadiusMeters *
              (1.0 -
               Math.Cos(
                   curveAngle))
            : 0.0;

        var localZ = hasCurve
            ? spline.RadiusMeters *
              Math.Sin(
                  curveAngle)
            : clamped;

        var cosYaw =
            Math.Cos(yaw);

        var sinYaw =
            Math.Sin(yaw);

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

        var heading =
            yaw +
            curveAngle;

        var forward =
            Vector3.Normalize(
                new Vector3(
                    (float)Math.Sin(
                        heading),
                    0.0f,
                    (float)Math.Cos(
                        heading)));

        var lateral =
            new Vector3(
                forward.Z,
                0.0f,
                -forward.X);

        var worldY =
            spline.Y +
            GradientRise(
                spline.GradientStartPercent,
                spline.GradientEndPercent,
                spline.LengthMeters,
                clamped) +
            0.025;

        return (
            new Vector3(
                (float)worldX,
                (float)worldY,
                (float)worldZ),
            lateral,
            forward);
    }

    private static Vector3 Transform(
        (
            Vector3 Center,
            Vector3 Lateral,
            Vector3 Forward
        ) frame,
        RuntimeSplineProfilePointInfo point)
    {
        return frame.Center +
               frame.Lateral *
               (float)point.X +
               Vector3.UnitY *
               (float)point.Z;
    }

    private static double GradientRise(
        double start,
        double end,
        double length,
        double distance)
    {
        if (length <= 0.0)
        {
            return 0.0;
        }

        var clamped = Math.Clamp(
            distance,
            0.0,
            length);

        var startSlope =
            start /
            100.0;

        var slopeDelta =
            (end - start) /
            100.0;

        return startSlope *
               clamped +
               0.5 *
               slopeDelta *
               clamped *
               clamped /
               length;
    }

    private static void AppendQuad(
        Vector3 left0,
        Vector3 left1,
        Vector3 right1,
        Vector3 right0,
        Vector2 leftUv0,
        Vector2 leftUv1,
        Vector2 rightUv1,
        Vector2 rightUv0,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        AppendTriangle(
            left0,
            left1,
            right1,
            leftUv0,
            leftUv1,
            rightUv1,
            color,
            output);

        AppendTriangle(
            left0,
            right1,
            right0,
            leftUv0,
            rightUv1,
            rightUv0,
            color,
            output);
    }

    private static void AppendTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        output.Add(
            new RuntimeObjectVertex(
                a,
                color,
                uvA));

        output.Add(
            new RuntimeObjectVertex(
                b,
                color,
                uvB));

        output.Add(
            new RuntimeObjectVertex(
                c,
                color,
                uvC));
    }
}
