using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal static class RuntimeVehicleGeometry
{
    private const string VehicleAssetKey =
        "__runtime_player_vehicle__";

    public static RuntimeObjectGeometry Build(
        RuntimeVehicleInfo? vehicle,
        int viewpointBit,
        bool forceMaterialAlphaOpaque = false)
    {
        if (vehicle is null)
        {
            return RuntimeObjectGeometry.Empty;
        }

        var renderableMeshes =
            vehicle.Meshes
                .Where(
                    static mesh =>
                        string.IsNullOrWhiteSpace(
                            mesh.ErrorCode) &&
                        mesh.Positions.Length >= 3 &&
                        mesh.Indices.Length >= 3)
                .ToArray();

        if (renderableMeshes.Length == 0)
        {
            return RuntimeObjectGeometry.Empty;
        }

        var viewpointMeshes =
            renderableMeshes
                .Where(
                    mesh =>
                        IsVisibleFromViewpoint(
                            mesh.ViewpointFlag,
                            viewpointBit))
                .ToArray();

        var selectionSource =
            viewpointMeshes.Length > 0
                ? viewpointMeshes
                : renderableMeshes;

        var detailedLod =
            selectionSource
                .Where(
                    static mesh =>
                        mesh.LodThreshold.HasValue)
                .Select(
                    static mesh =>
                        mesh.LodThreshold!.Value)
                .DefaultIfEmpty(
                    double.NaN)
                .Max();

        var selectedMeshes =
            selectionSource
                .Where(
                    mesh =>
                        IsSelectedPlayerLod(
                            mesh.LodThreshold,
                            detailedLod))
                .ToArray();

        if (selectedMeshes.Length == 0)
        {
            selectedMeshes =
                selectionSource;
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
                },
                forceMaterialAlphaOpaque:
                    forceMaterialAlphaOpaque);

        if (geometry.Vertices.Length > 0)
        {
            return geometry;
        }

        return RuntimeObjectGeometry.Empty;
    }

    private static bool IsSelectedPlayerLod(
        double? meshLod,
        double detailedLod)
    {
        if (!meshLod.HasValue ||
            double.IsNaN(
                detailedLod))
        {
            return true;
        }

        return Math.Abs(
                   meshLod.Value -
                   detailedLod) <
               0.000001;
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

}
