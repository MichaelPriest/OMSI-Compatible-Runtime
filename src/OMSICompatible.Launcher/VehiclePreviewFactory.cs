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

        var vehicle =
            new RuntimeVehicleInfo(
                asset.Bus.DisplayName,
                asset.Bus.RelativePath,
                asset.Meshes
                    .Select(
                        static mesh =>
                            new RuntimeObjectMeshInfo(
                                mesh.DeclaredPath,
                                mesh.ResolvedPath,
                                mesh.ErrorCode,
                                new RuntimeObjectMeshTransformInfo(
                                    mesh.Transform.PositionX,
                                    mesh.Transform.PositionY,
                                    mesh.Transform.PositionZ,
                                    mesh.Transform.RotationX,
                                    mesh.Transform.RotationY,
                                    mesh.Transform.RotationZ,
                                    mesh.Transform.ScaleX,
                                    mesh.Transform.ScaleY,
                                    mesh.Transform.ScaleZ),
                                mesh.Positions,
                                mesh.Normals,
                                mesh.Uvs,
                                mesh.Indices,
                                mesh.TriangleMaterialIndices,
                                mesh.Materials
                                    .Select(
                                        static material =>
                                            new RuntimeO3dMaterialInfo(
                                                material.DiffuseR,
                                                material.DiffuseG,
                                                material.DiffuseB,
                                                material.DiffuseA,
                                                material.TexturePath,
                                                material.AlphaMode,
                                                material.TransMapTexturePath,
                                                material.NoZWrite,
                                                material.NoZCheck,
                                                material.AlphaScaleVariable,
                                                material.LightMapTexturePath,
                                                material.LightMapVariable,
                                                material.MaterialChangeTexturePath,
                                                material.MaterialChangeVariable,
                                                material.BaseAllColor is null
                                                    ? null
                                                    : ToColor(
                                                        material.BaseAllColor),
                                                material.MaterialChangeAllColor is null
                                                    ? null
                                                    : ToColor(
                                                        material.MaterialChangeAllColor),
                                                material.EnvMapTexturePath,
                                                material.EnvMapStrength,
                                                material.EnvMapMaskTexturePath,
                                                material.BumpMapTexturePath,
                                                material.BumpMapStrength,
                                                material.FreeTextures
                                                    .Select(
                                                        static texture =>
                                                            new RuntimeVehicleFreeTextureInfo(
                                                                texture.SourceTextureName,
                                                                texture.VariableName))
                                                    .ToArray(),
                                                material.TextTextureIndex,
                                                MaterialChangeSets:
                                                    null,
                                                material.HasTransMapDirective))
                                    .ToArray(),
                                mesh.ViewpointFlag,
                                mesh.LodThreshold,
                                VisibilityConditions:
                                    Array.Empty<
                                        RuntimeVehicleVisibilityConditionInfo>(),
                                Animations:
                                    Array.Empty<
                                        RuntimeVehicleAnimationInfo>(),
                                mesh.SourceTransform,
                                LightEffects:
                                    Array.Empty<
                                        RuntimeVehicleLightEffectInfo>()))
                    .ToArray(),
                DriverCameras:
                    Array.Empty<
                        RuntimeDriverCameraInfo>(),
                PassengerCameras:
                    Array.Empty<
                        RuntimePassengerCameraInfo>(),
                StandardDriverCameraIndex:
                    0,
                ScheduleDriverCameraIndex:
                    null,
                TicketSellingDriverCameraIndex:
                    null,
                OutsideCameraCenter:
                    null,
                ReflectionCameras:
                    Array.Empty<
                        RuntimeReflectionCameraInfo>(),
                new RuntimeVehiclePhysicsInfo(
                    asset.Bus.Physics.WheelBaseMeters,
                    asset.Bus.Physics.MaximumSteeringAngleDegrees),
                DriverPosition:
                    null,
                asset.ProtectedMeshCount,
                TextTextures:
                    Array.Empty<
                        RuntimeVehicleTextTextureInfo>());

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
            Vehicle:
                vehicle,
            Spawn:
                null);
    }

    private static RuntimeVehicleMaterialColorInfo ToColor(
        OmsiVehicleMaterialColor color) =>
        new(
            color.DiffuseR,
            color.DiffuseG,
            color.DiffuseB,
            color.DiffuseA,
            color.AmbientR,
            color.AmbientG,
            color.AmbientB,
            color.SpecularR,
            color.SpecularG,
            color.SpecularB,
            color.EmissiveR,
            color.EmissiveG,
            color.EmissiveB,
            color.Power);
}
