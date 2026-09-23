namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleFreeTexture(
    string SourceTextureName,
    string VariableName);

public sealed record OmsiVehicleMaterialColor(
    double DiffuseR,
    double DiffuseG,
    double DiffuseB,
    double DiffuseA,
    double AmbientR,
    double AmbientG,
    double AmbientB,
    double SpecularR,
    double SpecularG,
    double SpecularB,
    double EmissiveR,
    double EmissiveG,
    double EmissiveB,
    double Power);

public sealed record OmsiVehicleTextTexture(
    int Index,
    string StringVariable,
    string FontName,
    int Width,
    int Height,
    bool FullColor,
    byte Red,
    byte Green,
    byte Blue,
    int? Alignment,
    bool? GridAligned);

public sealed record OmsiVehicleMaterialOverride(
    string TextureName,
    int MaterialIndex,
    int? AlphaMode,
    string? TransMapSource,
    bool HasTransMapDirective,
    bool NoZWrite,
    bool NoZCheck,
    string? AlphaScaleVariable,
    string? LightMapSource,
    string? LightMapVariable,
    string? MaterialChangeVariable,
    string? MaterialChangeMapSource,
    OmsiVehicleMaterialColor? AllColor,
    string? EnvMapSource,
    double EnvMapStrength,
    string? EnvMapMaskSource,
    string? BumpMapSource,
    double BumpMapStrength,
    IReadOnlyList<OmsiVehicleFreeTexture> FreeTextures,
    int? TextTextureIndex,
    string? TextureCoordinateXVariable,
    string? TextureCoordinateYVariable,
    int MaterialChangeGroupIndex = -1,
    int MaterialChangeItemIndex = 0);

public sealed record OmsiVehicleMeshTransform(
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ)
{
    public static OmsiVehicleMeshTransform Identity { get; } =
        new(
            0, 0, 0,
            0, 0, 0,
            1, 1, 1);
}

public sealed record OmsiVehicleLightEffect(
    double PositionX,
    double PositionY,
    double PositionZ,
    double DirectionX,
    double DirectionY,
    double DirectionZ,
    double UpX,
    double UpY,
    double UpZ,
    int Omni,
    int Rotating,
    byte Red,
    byte Green,
    byte Blue,
    double SizeMeters,
    double InnerConeAngleDegrees,
    double OuterConeAngleDegrees,
    string BrightnessVariable,
    double BrightnessFactor,
    double CameraOffsetMeters,
    int Parameters,
    bool ConeEffect,
    double TimeConstantSeconds,
    string? BitmapSource,
    bool Enhanced);

public sealed record OmsiVehicleVisibilityCondition(
    string VariableName,
    double Value);

public enum OmsiVehicleAnimationKind
{
    Translation,
    Rotation
}

public sealed record OmsiVehicleAnimation(
    OmsiVehicleAnimationKind Kind,
    string VariableName,
    double Delta,
    bool OriginFromMesh,
    double OriginX,
    double OriginY,
    double OriginZ,
    double OriginRotationX,
    double OriginRotationY,
    double OriginRotationZ,
    double Offset,
    double? MaxSpeed,
    double? Delay);

public sealed record OmsiVehicleMeshReference(
    int Ordinal,
    string DeclaredPath,
    OmsiVehicleMeshTransform Transform,
    int ViewpointFlag,
    double? LodThreshold,
    IReadOnlyList<OmsiVehicleVisibilityCondition> VisibilityConditions,
    IReadOnlyList<OmsiVehicleAnimation> Animations,
    IReadOnlyList<OmsiVehicleMaterialOverride> MaterialOverrides,
    IReadOnlyList<OmsiVehicleLightEffect>? LightEffects = null);

public sealed record OmsiVehicleModel(
    string SourcePath,
    IReadOnlyList<OmsiVehicleMeshReference> Meshes,
    IReadOnlyList<OmsiVehicleTextTexture> TextTextures);
