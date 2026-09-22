using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal static class RuntimeVehicleGeometry
{
    public static RuntimeVehicleVertex[] BuildBusProxy()
    {
        const float halfWidth = 1.25f;
        const float halfLength = 5.25f;
        const float bottom = 0.0f;
        const float top = 3.15f;

        var p000 = new Vector3(-halfWidth, bottom, -halfLength);
        var p100 = new Vector3(halfWidth, bottom, -halfLength);
        var p010 = new Vector3(-halfWidth, top, -halfLength);
        var p110 = new Vector3(halfWidth, top, -halfLength);

        var p001 = new Vector3(-halfWidth, bottom, halfLength);
        var p101 = new Vector3(halfWidth, bottom, halfLength);
        var p011 = new Vector3(-halfWidth, top, halfLength);
        var p111 = new Vector3(halfWidth, top, halfLength);

        var body =
            new Color4(0.78f, 0.22f, 0.08f, 1.0f);
        var roof =
            new Color4(0.82f, 0.82f, 0.84f, 1.0f);
        var front =
            new Color4(0.93f, 0.46f, 0.10f, 1.0f);

        var vertices =
            new List<RuntimeVehicleVertex>(36);

        Quad(p000, p100, p110, p010, body, vertices);
        Quad(p101, p001, p011, p111, front, vertices);
        Quad(p001, p000, p010, p011, body, vertices);
        Quad(p100, p101, p111, p110, body, vertices);
        Quad(p010, p110, p111, p011, roof, vertices);
        Quad(p001, p101, p100, p000, body, vertices);

        return vertices.ToArray();
    }

    private static void Quad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color4 color,
        ICollection<RuntimeVehicleVertex> output)
    {
        output.Add(new RuntimeVehicleVertex(a, color));
        output.Add(new RuntimeVehicleVertex(b, color));
        output.Add(new RuntimeVehicleVertex(c, color));

        output.Add(new RuntimeVehicleVertex(a, color));
        output.Add(new RuntimeVehicleVertex(c, color));
        output.Add(new RuntimeVehicleVertex(d, color));
    }
}


internal readonly struct RuntimeVehicleVertex
{
    public const uint SizeInBytes = 28;

    public RuntimeVehicleVertex(
        Vector3 position,
        Color4 color)
    {
        Position = position;
        Color = color;
    }

    public readonly Vector3 Position;
    public readonly Color4 Color;
}
