using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeSplineGeometry(
    RuntimeTerrainVertex[] Vertices,
    int RenderedSplineCount)
{
    public static RuntimeSplineGeometry Empty { get; } =
        new([], 0);
}

internal static class RuntimeSplineGeometryBuilder
{
    public static RuntimeSplineGeometry Build(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        if (splines.Count == 0)
        {
            return RuntimeSplineGeometry.Empty;
        }

        var vertices = new List<RuntimeTerrainVertex>(64_000);
        var rendered = 0;

        foreach (var spline in splines)
        {
            if (spline.LengthMeters < 0.01 ||
                spline.Surfaces.Count == 0)
            {
                continue;
            }

            var before = vertices.Count;
            var segmentCount = Math.Clamp(
                (int)Math.Ceiling(spline.LengthMeters / 4.0),
                1,
                256);

            foreach (var surface in spline.Surfaces)
            {
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

                    var frame0 = GetFrame(spline, distance0);
                    var frame1 = GetFrame(spline, distance1);

                    var a = Transform(frame0, surface.From);
                    var b = Transform(frame1, surface.From);
                    var c = Transform(frame1, surface.To);
                    var d = Transform(frame0, surface.To);

                    var vertical =
                        Math.Abs(
                            surface.From.Z -
                            surface.To.Z) > 0.10;

                    var color = vertical
                        ? new Color4(0.32f, 0.32f, 0.32f, 1.0f)
                        : new Color4(0.24f, 0.25f, 0.27f, 1.0f);

                    AddTriangle(a, b, c, color, vertices);
                    AddTriangle(a, c, d, color, vertices);
                }
            }

            if (vertices.Count > before)
            {
                rendered++;
            }
        }

        return new RuntimeSplineGeometry(
            vertices.ToArray(),
            rendered);
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

        var yaw =
            spline.HeadingDegrees *
            Math.PI /
            180.0;

        var hasCurve =
            Math.Abs(spline.RadiusMeters) >
            0.001;

        var curveAngle = hasCurve
            ? clamped / spline.RadiusMeters
            : 0.0;

        var localX = hasCurve
            ? spline.RadiusMeters *
              (1.0 - Math.Cos(curveAngle))
            : 0.0;

        var localZ = hasCurve
            ? spline.RadiusMeters *
              Math.Sin(curveAngle)
            : clamped;

        var cosYaw = Math.Cos(yaw);
        var sinYaw = Math.Sin(yaw);

        var startX =
            spline.TileX * 300.0 +
            spline.X;

        var startZ =
            spline.TileY * 300.0 +
            spline.Z;

        var worldX =
            startX +
            localX * cosYaw +
            localZ * sinYaw;

        var worldZ =
            startZ -
            localX * sinYaw +
            localZ * cosYaw;

        var heading =
            yaw +
            curveAngle;

        var forward = Vector3.Normalize(
            new Vector3(
                (float)Math.Sin(heading),
                0.0f,
                (float)Math.Cos(heading)));

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
               frame.Lateral * (float)point.X +
               Vector3.UnitY * (float)point.Z;
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

        var startSlope = start / 100.0;
        var slopeDelta = (end - start) / 100.0;

        return startSlope * clamped +
               0.5 *
               slopeDelta *
               clamped *
               clamped /
               length;
    }

    private static void AddTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color4 color,
        ICollection<RuntimeTerrainVertex> output)
    {
        output.Add(new RuntimeTerrainVertex(a, color));
        output.Add(new RuntimeTerrainVertex(b, color));
        output.Add(new RuntimeTerrainVertex(c, color));
    }
}
