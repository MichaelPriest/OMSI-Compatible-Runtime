using System.Numerics;
using OMSICompatible.Renderer.Common;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal static class RuntimeWalkerGeometry
{
    public static RuntimeObjectVertex[] Build(
        IReadOnlyList<RuntimeRemoteWalkerInfo> walkers)
    {
        if (walkers.Count == 0)
        {
            return [];
        }

        var vertices =
            new List<RuntimeObjectVertex>(
                walkers.Count *
                108);

        foreach (var walker in walkers)
        {
            AppendWalker(
                walker,
                vertices);
        }

        return vertices.ToArray();
    }

    private static void AppendWalker(
        RuntimeRemoteWalkerInfo walker,
        ICollection<RuntimeObjectVertex> output)
    {
        var yaw =
            -walker.HeadingDegrees *
            MathF.PI /
            180.0f;

        var rotation =
            Matrix4x4.CreateRotationY(
                yaw);

        var feet =
            new Vector3(
                (float)walker.X,
                (float)walker.Y,
                (float)walker.Z);

        var bodyColor =
            new Color4(
                0.16f,
                0.52f,
                0.92f,
                0.92f);

        var skinColor =
            new Color4(
                0.82f,
                0.66f,
                0.52f,
                0.95f);

        if (walker.Seated)
        {
            AppendBox(
                feet +
                new Vector3(
                    0.0f,
                    0.72f,
                    0.0f),
                new Vector3(
                    0.48f,
                    0.72f,
                    0.34f),
                rotation,
                bodyColor,
                output);

            AppendBox(
                feet +
                new Vector3(
                    0.0f,
                    1.32f,
                    0.02f),
                new Vector3(
                    0.30f,
                    0.30f,
                    0.30f),
                rotation,
                skinColor,
                output);

            return;
        }

        AppendBox(
            feet +
            new Vector3(
                0.0f,
                0.88f,
                0.0f),
            new Vector3(
                0.46f,
                1.10f,
                0.32f),
            rotation,
            bodyColor,
            output);

        AppendBox(
            feet +
            new Vector3(
                0.0f,
                1.62f,
                0.0f),
            new Vector3(
                0.31f,
                0.31f,
                0.31f),
            rotation,
            skinColor,
            output);

        AppendBox(
            feet +
            TransformOffset(
                new Vector3(
                    -0.13f,
                    0.30f,
                    0.0f),
                rotation),
            new Vector3(
                0.16f,
                0.58f,
                0.20f),
            rotation,
            bodyColor,
            output);

        AppendBox(
            feet +
            TransformOffset(
                new Vector3(
                    0.13f,
                    0.30f,
                    0.0f),
                rotation),
            new Vector3(
                0.16f,
                0.58f,
                0.20f),
            rotation,
            bodyColor,
            output);
    }

    private static Vector3 TransformOffset(
        Vector3 value,
        Matrix4x4 rotation) =>
        Vector3.TransformNormal(
            value,
            rotation);

    private static void AppendBox(
        Vector3 center,
        Vector3 size,
        Matrix4x4 rotation,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var half =
            size *
            0.5f;

        Span<Vector3> p =
            stackalloc Vector3[8]
            {
                new(-half.X, -half.Y, -half.Z),
                new( half.X, -half.Y, -half.Z),
                new( half.X,  half.Y, -half.Z),
                new(-half.X,  half.Y, -half.Z),
                new(-half.X, -half.Y,  half.Z),
                new( half.X, -half.Y,  half.Z),
                new( half.X,  half.Y,  half.Z),
                new(-half.X,  half.Y,  half.Z)
            };

        for (var index = 0;
             index <
                 p.Length;
             index++)
        {
            p[index] =
                Vector3.TransformNormal(
                    p[index],
                    rotation) +
                center;
        }

        AppendQuad(p[0], p[1], p[2], p[3], color, output);
        AppendQuad(p[5], p[4], p[7], p[6], color, output);
        AppendQuad(p[4], p[0], p[3], p[7], color, output);
        AppendQuad(p[1], p[5], p[6], p[2], color, output);
        AppendQuad(p[3], p[2], p[6], p[7], color, output);
        AppendQuad(p[4], p[5], p[1], p[0], color, output);
    }

    private static void AppendQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        AppendTriangle(a, b, c, color, output);
        AppendTriangle(a, c, d, color, output);
    }

    private static void AppendTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var uv =
            Vector2.Zero;

        output.Add(new RuntimeObjectVertex(a, color, uv));
        output.Add(new RuntimeObjectVertex(b, color, uv));
        output.Add(new RuntimeObjectVertex(c, color, uv));
    }
}
