using OmsiCompat.Vehicles;

namespace OMSICompatible.Renderer.D3D11;

public static class RuntimeVehicleInfoFactory
{
    public static RuntimeVehicleInfo FromAsset(
        OmsiVehicleAsset vehicle)
    {
        ArgumentNullException.ThrowIfNull(
            vehicle);

        return new RuntimeVehicleInfo(
            vehicle.Bus.DisplayName,
            vehicle.Bus.RelativePath,
            vehicle.Bus.SoundConfigPath,
            vehicle.Meshes
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
                                            ConvertColor(
                                                material.BaseAllColor),
                                            ConvertColor(
                                                material.MaterialChangeAllColor),
                                            material.EnvMapTexturePath,
                                            material.EnvMapStrength,
                                            material.EnvMapMaskTexturePath,
                                            material.BumpMapTexturePath,
                                            material.BumpMapStrength,
                                            material.FreeTextures
                                                .Select(
                                                    static freeTexture =>
                                                        new RuntimeVehicleFreeTextureInfo(
                                                            freeTexture.SourceTextureName,
                                                            freeTexture.VariableName))
                                                .ToArray(),
                                            material.TextTextureIndex,
                                            material.MaterialChangeSets?
                                                .Select(
                                                    static changeSet =>
                                                        new RuntimeVehicleMaterialChangeSetInfo(
                                                            changeSet.VariableName,
                                                            changeSet.GroupIndex,
                                                            changeSet.Items
                                                                .Select(
                                                                    static item =>
                                                                        new RuntimeVehicleMaterialChangeItemInfo(
                                                                            item.ItemIndex,
                                                                            item.AlphaMode,
                                                                            item.TransMapTexturePath,
                                                                            item.HasTransMapDirective,
                                                                            item.NoZWrite,
                                                                            item.NoZCheck,
                                                                            item.AlphaScaleVariable,
                                                                            item.LightMapTexturePath,
                                                                            item.LightMapVariable,
                                                                            item.MaterialChangeTexturePath,
                                                                            ConvertColor(
                                                                                item.AllColor),
                                                                            item.EnvMapTexturePath,
                                                                            item.EnvMapStrength,
                                                                            item.EnvMapMaskTexturePath,
                                                                            item.BumpMapTexturePath,
                                                                            item.BumpMapStrength,
                                                                            item.FreeTextures
                                                                                .Select(
                                                                                    static freeTexture =>
                                                                                        new RuntimeVehicleFreeTextureInfo(
                                                                                            freeTexture.SourceTextureName,
                                                                                            freeTexture.VariableName))
                                                                                .ToArray(),
                                                                            item.TextTextureIndex))
                                                                .ToArray()))
                                                .ToArray(),
                                            material.HasTransMapDirective))
                                .ToArray(),
                            mesh.ViewpointFlag,
                            mesh.LodThreshold,
                            mesh.VisibilityConditions
                                .Select(
                                    static condition =>
                                        new RuntimeVehicleVisibilityConditionInfo(
                                            condition.VariableName,
                                            condition.Value))
                                .ToArray(),
                            mesh.Animations
                                .Select(
                                    static animation =>
                                        new RuntimeVehicleAnimationInfo(
                                            animation.Kind ==
                                                OmsiVehicleAnimationKind.Translation
                                                ? RuntimeVehicleAnimationKind.Translation
                                                : RuntimeVehicleAnimationKind.Rotation,
                                            animation.VariableName,
                                            animation.Delta,
                                            animation.OriginFromMesh,
                                            animation.OriginX,
                                            animation.OriginY,
                                            animation.OriginZ,
                                            animation.OriginRotationX,
                                            animation.OriginRotationY,
                                            animation.OriginRotationZ,
                                            animation.Offset,
                                            animation.MaxSpeed,
                                            animation.Delay))
                                .ToArray(),
                            mesh.SourceTransform,
                            mesh.LightEffects?
                                .Select(
                                    static light =>
                                        new RuntimeVehicleLightEffectInfo(
                                            light.PositionX,
                                            light.PositionY,
                                            light.PositionZ,
                                            light.DirectionX,
                                            light.DirectionY,
                                            light.DirectionZ,
                                            light.UpX,
                                            light.UpY,
                                            light.UpZ,
                                            light.Omni,
                                            light.Rotating,
                                            light.Red,
                                            light.Green,
                                            light.Blue,
                                            light.SizeMeters,
                                            light.InnerConeAngleDegrees,
                                            light.OuterConeAngleDegrees,
                                            light.BrightnessVariable,
                                            light.BrightnessFactor,
                                            light.CameraOffsetMeters,
                                            light.Parameters,
                                            light.ConeEffect,
                                            light.TimeConstantSeconds,
                                            light.BitmapSource,
                                            light.Enhanced))
                                .ToArray(),
                            mesh.MeshIdentifier,
                            mesh.AnimationParent,
                            mesh.SectionIndex,
                            mesh.ModelOrdinal,
                            mesh.SkinWeights,
                            mesh.SkinBoneMeshOrdinals,
                            mesh.MouseEventTrigger))
                .ToArray(),
            vehicle.Bus.DriverCameras
                .Select(
                    static camera =>
                        new RuntimeDriverCameraInfo(
                            -camera.X,
                            camera.Z,
                            camera.Y,
                            camera.EyeDistance,
                            camera.FieldOfViewDegrees,
                            camera.HeadingDegrees,
                            camera.PitchDegrees))
                .ToArray(),
            vehicle.Bus.PassengerCameras
                .Select(
                    static camera =>
                        new RuntimePassengerCameraInfo(
                            -camera.X,
                            camera.Z,
                            camera.Y,
                            camera.EyeDistance,
                            camera.FieldOfViewDegrees,
                            camera.HeadingDegrees,
                            camera.PitchDegrees))
                .ToArray(),
            vehicle.Bus.StandardDriverCameraIndex,
            vehicle.Bus.ScheduleDriverCameraIndex,
            vehicle.Bus.TicketSellingDriverCameraIndex,
            vehicle.Bus.OutsideCameraCenter is null
                ? null
                : new RuntimeOutsideCameraCenterInfo(
                    -vehicle.Bus.OutsideCameraCenter.X,
                    vehicle.Bus.OutsideCameraCenter.Z,
                    vehicle.Bus.OutsideCameraCenter.Y),
            vehicle.Bus.ReflectionCameras
                .Select(
                    static camera =>
                        new RuntimeReflectionCameraInfo(
                            camera.Index,
                            -camera.X,
                            camera.Z,
                            camera.Y,
                            camera.EyeDistance,
                            camera.FieldOfViewDegrees,
                            camera.HeadingDegrees,
                            camera.PitchDegrees,
                            camera.MaximumRenderDistanceMeters,
                            camera.RuntimeTextureName,
                            camera.RuntimeTextureKey))
                .ToArray(),
            new RuntimeVehiclePhysicsInfo(
                vehicle.Bus.Physics.WheelBaseMeters,
                vehicle.Bus.Physics.MaximumSteeringAngleDegrees,
                vehicle.Bus.Physics.MassTonnes,
                vehicle.Bus.Physics.CenterOfGravityHeightMeters,
                vehicle.Bus.Physics.RollingResistanceNewtons,
                vehicle.Bus.Physics.TrackWidthMeters,
                vehicle.Bus.Physics.AverageWheelDiameterMeters,
                AverageAxleValue(
                    vehicle.Bus.Physics.Axles,
                    static axle =>
                        axle.SpringRateKilonewtonsPerMeter),
                AverageAxleValue(
                    vehicle.Bus.Physics.Axles,
                    static axle =>
                        axle.DamperRateKilonewtonSecondsPerMeter),
                vehicle.Bus.Physics.MomentOfInertiaZ,
                vehicle.Bus.Physics.RotationPointLongitudinalMeters,
                vehicle.Bus.Physics.InverseMinimumTurnRadius,
                vehicle.Bus.Physics.Axles.Count > 0
                    ? vehicle.Bus.Physics.Axles.Max(
                        static axle =>
                            axle.LongitudinalPositionMeters)
                    : null,
                vehicle.Bus.Physics.Axles.Count > 0
                    ? vehicle.Bus.Physics.Axles.Min(
                        static axle =>
                            axle.LongitudinalPositionMeters)
                    : null),
            vehicle.DriverPosition is null
                ? null
                : new RuntimeDriverPositionInfo(
                    -vehicle.DriverPosition.X,
                    vehicle.DriverPosition.Z,
                    vehicle.DriverPosition.Y,
                    vehicle.DriverPosition.SeatHeight,
                    vehicle.DriverPosition.RotationDegrees),
            vehicle.ProtectedMeshCount,
            vehicle.TextTextures
                .Select(
                    static texture =>
                        new RuntimeVehicleTextTextureInfo(
                            texture.Index,
                            texture.StringVariable,
                            texture.FontName,
                            texture.Width,
                            texture.Height,
                            texture.FullColor,
                            texture.Red,
                            texture.Green,
                            texture.Blue,
                            texture.Alignment,
                            texture.GridAligned))
                .ToArray(),
            vehicle.Sections?
                .Select(
                    static section =>
                        new RuntimeVehicleSectionInfo(
                            section.Index,
                            section.ParentIndex,
                            -section.JointX,
                            section.JointZ,
                            section.JointY,
                            -section.OriginX,
                            section.OriginZ,
                            section.OriginY,
                            section.FollowerLengthMeters,
                            section.MaximumYawDegrees,
                            section.Reverse,
                            section.SoundConfigPath,
                            section.OpenForSound,
                            section.MassTonnes,
                            section.YawInertiaTonneSquareMeters,
                            section.RotationPointLongitudinalMeters,
                            section.WheelBaseMeters,
                            section.RollingResistanceNewtons,
                            section.AverageWheelDiameterMeters))
                .ToArray());
    }

    private static double? AverageAxleValue(
        IReadOnlyList<OmsiVehicleAxle> axles,
        Func<OmsiVehicleAxle, double?> selector)
    {
        var values =
            axles
                .Select(
                    selector)
                .Where(
                    static value =>
                        value.HasValue &&
                        double.IsFinite(
                            value.Value))
                .Select(
                    static value =>
                        value!.Value)
                .ToArray();

        return values.Length == 0
            ? null
            : values.Average();
    }

    private static RuntimeVehicleMaterialColorInfo?
        ConvertColor(
            OmsiVehicleMaterialColor? color) =>
        color is null
            ? null
            : new RuntimeVehicleMaterialColorInfo(
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
