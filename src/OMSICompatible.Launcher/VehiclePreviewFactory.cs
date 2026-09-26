using OMSICompatible.Renderer.D3D11;
using OmsiCompat.Vehicles;

namespace OMSICompatible.Launcher;

internal static class VehiclePreviewFactory
{
    public static RuntimeWindowInfo Create(
        string contentRoot,
        OmsiVehicleAsset asset)
    {
        ArgumentNullException.ThrowIfNull(
            asset);

        return new RuntimeWindowInfo(
            WorldName:
                "Vehicle Preview",
            TileCount:
                0,
            TotalTileCount:
                0,
            ActiveTileRadius:
                null,
            ObjectCount:
                0,
            SplineCount:
                0,
            ContentRoot:
                contentRoot,
            Tiles:
                Array.Empty<
                    RuntimeTileInfo>(),
            Splines:
                Array.Empty<
                    RuntimeSplineInfo>(),
            Objects:
                Array.Empty<
                    RuntimeObjectInfo>(),
            SceneryAssets:
                new Dictionary<
                    string,
                    RuntimeSceneryAssetInfo>(
                    StringComparer.OrdinalIgnoreCase),
            GroundTextures:
                Array.Empty<
                    RuntimeGroundTextureInfo>(),
            TrafficPaths:
                RuntimeTrafficPathNetworkInfo.Empty,
            AiCatalog:
                RuntimeAiCatalogInfo.Empty,
            Vehicle:
                RuntimeVehicleInfoFactory
                    .FromAsset(
                        asset),
            Spawn:
                null);
    }
}
