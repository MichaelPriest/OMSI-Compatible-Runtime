using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal static class RuntimeVehicleGeometry
{
    private const string VehicleAssetKey =
        "__runtime_player_vehicle__";

    public static RuntimeObjectGeometry Build(
        RuntimeVehicleInfo? vehicle,
        int viewpointBit)
    {
        if (vehicle is null ||
            vehicle.Meshes.Count == 0)
        {
            return viewpointBit == 1
                ? BuildBusProxy()
                : RuntimeObjectGeometry.Empty;
        }

        var selectedMeshes =
            vehicle.Meshes
                .Where(
                    mesh =>
                        IsVisibleFromViewpoint(
                            mesh.ViewpointFlag,
                            viewpointBit))
                .ToArray();

        if (selectedMeshes.Length == 0)
        {
            return viewpointBit == 1
                ? BuildBusProxy()
                : RuntimeObjectGeometry.Empty;
        }

        var asset =
            new RuntimeSceneryAssetInfo(
                UsesAbsoluteHeight: true,
                OnlyEditor: false,
                RenderType: null,
                Meshes: selectedMeshes,
                Tree: null);

        var instance =
            new RuntimeObjectInfo(
                TileX: 0,
                TileY: 0,
                AssetPath: VehicleAssetKey,
                X: 0,
                Y: 0,
                Z: 0,
                HeadingDegrees: 0,
                PitchDegrees: 0,
                BankDegrees: 0,
                ExtraValues:
                    Array.Empty<string>());

        var geometry =
            RuntimeObjectGeometryBuilder.Build(
                Array.Empty<RuntimeTileInfo>(),
                [instance],
                new Dictionary<
                    string,
                    RuntimeSceneryAssetInfo>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [VehicleAssetKey] = asset
                });

        if (geometry.Vertices.Length > 0)
        {
            return geometry;
        }

        return viewpointBit == 1
            ? BuildBusProxy()
            : RuntimeObjectGeometry.Empty;
    }

    private static bool IsVisibleFromViewpoint(
        int viewpointFlag,
        int requestedBit)
    {
        // OMSI [viewpoint]:
        // 0 = always, 1 = exterior, 2 = interior, 4 = AI.
        // Flags can be summed (3, 5, 6, 7).
        return viewpointFlag is 0 or 7 ||
               (viewpointFlag & requestedBit) != 0;
    }

    private static RuntimeObjectGeometry
        BuildBusProxy()
    {
        const float halfWidth = 1.25f;
        const float halfLength = 5.25f;
        const float bottom = 0.0f;
        const float top = 3.15f;

        var p000 =
            new Vector3(
                -halfWidth,
                bottom,
                -halfLength);

        var p100 =
            new Vector3(
                halfWidth,
                bottom,
                -halfLength);

        var p010 =
            new Vector3(
                -halfWidth,
                top,
                -halfLength);

        var p110 =
            new Vector3(
                halfWidth,
                top,
                -halfLength);

        var p001 =
            new Vector3(
                -halfWidth,
                bottom,
                halfLength);

        var p101 =
            new Vector3(
                halfWidth,
                bottom,
                halfLength);

        var p011 =
            new Vector3(
                -halfWidth,
                top,
                halfLength);

        var p111 =
            new Vector3(
                halfWidth,
                top,
                halfLength);

        var body =
            new Color4(
                0.78f,
                0.22f,
                0.08f,
                1.0f);

        var roof =
            new Color4(
                0.82f,
                0.82f,
                0.84f,
                1.0f);

        var front =
            new Color4(
                0.93f,
                0.46f,
                0.10f,
                1.0f);

        var vertices =
            new List<RuntimeObjectVertex>(36);

        Quad(
            p000,
            p100,
            p110,
            p010,
            body,
            vertices);

        Quad(
            p101,
            p001,
            p011,
            p111,
            front,
            vertices);

        Quad(
            p001,
            p000,
            p010,
            p011,
            body,
            vertices);

        Quad(
            p100,
            p101,
            p111,
            p110,
            body,
            vertices);

        Quad(
            p010,
            p110,
            p111,
            p011,
            roof,
            vertices);

        Quad(
            p001,
            p101,
            p100,
            p000,
            body,
            vertices);

        return new RuntimeObjectGeometry(
            vertices.ToArray(),
            [
                new RuntimeObjectBatch(
                    0,
                    (uint)vertices.Count,
                    null,
                    false)
            ],
            1,
            1,
            0,
            0,
            0,
            0,
            false);
    }

    private static void Quad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        Add(
            a,
            color,
            new Vector2(0, 1),
            output);

        Add(
            b,
            color,
            new Vector2(1, 1),
            output);

        Add(
            c,
            color,
            new Vector2(1, 0),
            output);

        Add(
            a,
            color,
            new Vector2(0, 1),
            output);

        Add(
            c,
            color,
            new Vector2(1, 0),
            output);

        Add(
            d,
            color,
            new Vector2(0, 0),
            output);
    }

    private static void Add(
        Vector3 position,
        Color4 color,
        Vector2 uv,
        ICollection<RuntimeObjectVertex> output) =>
        output.Add(
            new RuntimeObjectVertex(
                position,
                color,
                uv));
}
