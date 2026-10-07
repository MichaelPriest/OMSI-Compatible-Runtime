using System.Numerics;
using OMSICompatible.Renderer.Common;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal static class RuntimeNavigationGuidanceGeometry
{
    public static RuntimeObjectVertex[] Build(
        IReadOnlyList<RuntimeNavigationGuidancePointInfo> points)
    {
        if (points.Count == 0)
        {
            return [];
        }

        var vertices =
            new List<RuntimeObjectVertex>(
                points.Count *
                24);

        foreach (var point in points)
        {
            AppendArrow(
                point,
                vertices);
        }

        return vertices.ToArray();
    }

    private static void AppendArrow(
        RuntimeNavigationGuidancePointInfo point,
        ICollection<RuntimeObjectVertex> output)
    {
        var radians =
            point.HeadingDegrees *
            Math.PI /
            180.0;

        var forward =
            Vector3.Normalize(
                new Vector3(
                    (float)Math.Sin(
                        radians),
                    0.0f,
                    (float)Math.Cos(
                        radians)));

        var right =
            new Vector3(
                forward.Z,
                0.0f,
                -forward.X);

        var center =
            new Vector3(
                (float)point.X,
                (float)point.Y +
                    0.07f,
                (float)point.Z);

        var color =
            point.Kind.Equals(
                "rejoin",
                StringComparison.OrdinalIgnoreCase)
                ? new Color4(
                    1.0f,
                    0.52f,
                    0.08f,
                    0.82f)
                : point.Kind.Equals(
                    "route",
                    StringComparison.OrdinalIgnoreCase)
                    ? new Color4(
                        0.08f,
                        0.72f,
                        1.0f,
                        0.78f)
                    : new Color4(
                        0.22f,
                        1.0f,
                        0.48f,
                        0.84f);

        const float stemLength =
            2.2f;
        const float headLength =
            2.6f;
        const float stemHalfWidth =
            0.55f;
        const float headHalfWidth =
            1.35f;

        var rear =
            center -
            forward *
            stemLength;
        var neck =
            center;
        var tip =
            center +
            forward *
            headLength;

        var rearLeft =
            rear -
            right *
            stemHalfWidth;
        var rearRight =
            rear +
            right *
            stemHalfWidth;
        var neckLeft =
            neck -
            right *
            stemHalfWidth;
        var neckRight =
            neck +
            right *
            stemHalfWidth;
        var headLeft =
            neck -
            right *
            headHalfWidth;
        var headRight =
            neck +
            right *
            headHalfWidth;

        AppendQuad(
            rearLeft,
            neckLeft,
            neckRight,
            rearRight,
            color,
            output);

        AppendTriangleDoubleSided(
            tip,
            headLeft,
            headRight,
            color,
            output);
    }

    private static void AppendQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        AppendTriangleDoubleSided(
            a,
            b,
            c,
            color,
            output);

        AppendTriangleDoubleSided(
            a,
            c,
            d,
            color,
            output);
    }

    private static void AppendTriangleDoubleSided(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var uv =
            Vector2.Zero;

        output.Add(
            new RuntimeObjectVertex(
                a,
                color,
                uv));
        output.Add(
            new RuntimeObjectVertex(
                b,
                color,
                uv));
        output.Add(
            new RuntimeObjectVertex(
                c,
                color,
                uv));

        output.Add(
            new RuntimeObjectVertex(
                c,
                color,
                uv));
        output.Add(
            new RuntimeObjectVertex(
                b,
                color,
                uv));
        output.Add(
            new RuntimeObjectVertex(
                a,
                color,
                uv));
    }
}
