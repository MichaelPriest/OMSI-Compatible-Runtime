using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

public sealed record RuntimeTerrainInfo(
    int CellCount,
    IReadOnlyList<float> Heights,
    float MinimumHeight,
    float MaximumHeight);

public sealed record RuntimeTerrainMaskInfo(
    int LayerIndex,
    string Path);

public sealed record RuntimeTileInfo(
    int X,
    int Y,
    int ObjectCount,
    int SplineCount,
    RuntimeTerrainInfo? Terrain,
    string? LightmapPath,
    IReadOnlyList<RuntimeTerrainMaskInfo> TerrainMasks);

public sealed record RuntimeGroundTextureInfo(
    int LayerIndex,
    string? MainTexturePath,
    string? DetailTexturePath,
    double MainTextureRepeating,
    double DetailTextureRepeating);

public sealed record RuntimeSplineProfilePointInfo(
    double X,
    double Z,
    double TextureX,
    double TextureScale);

public sealed record RuntimeSplineSurfaceInfo(
    RuntimeSplineProfilePointInfo From,
    RuntimeSplineProfilePointInfo To,
    string? TexturePath,
    int AlphaMode);

public sealed record RuntimeSplineInfo(
    int TileX,
    int TileY,
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double LengthMeters,
    double RadiusMeters,
    double GradientStartPercent,
    double GradientEndPercent,
    IReadOnlyList<RuntimeSplineSurfaceInfo> Surfaces);

public sealed record RuntimeObjectInfo(
    int TileX,
    int TileY,
    string AssetPath,
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double PitchDegrees,
    double BankDegrees,
    IReadOnlyList<string> ExtraValues);

public sealed record RuntimeObjectMeshTransformInfo(
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ);

public sealed record RuntimeO3dMaterialInfo(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA,
    string? TexturePath,
    int AlphaMode,
    string? TransMapTexturePath,
    bool NoZWrite,
    bool NoZCheck,
    string? AlphaScaleVariable = null,
    string? LightMapTexturePath = null,
    string? LightMapVariable = null,
    string? MaterialChangeTexturePath = null,
    string? MaterialChangeVariable = null);

public sealed record RuntimeVehicleVisibilityConditionInfo(
    string VariableName,
    double Value);

public enum RuntimeVehicleAnimationKind
{
    Translation,
    Rotation
}

public sealed record RuntimeVehicleAnimationInfo(
    RuntimeVehicleAnimationKind Kind,
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

public sealed record RuntimeObjectMeshInfo(
    string DeclaredPath,
    string? ResolvedPath,
    string? ErrorCode,
    RuntimeObjectMeshTransformInfo Transform,
    float[] Positions,
    float[] Normals,
    float[] Uvs,
    uint[] Indices,
    ushort[] TriangleMaterialIndices,
    IReadOnlyList<RuntimeO3dMaterialInfo> Materials,
    int ViewpointFlag = 0,
    double? LodThreshold = null,
    IReadOnlyList<RuntimeVehicleVisibilityConditionInfo>? VisibilityConditions = null,
    IReadOnlyList<RuntimeVehicleAnimationInfo>? Animations = null,
    Matrix4x4? SourceTransform = null);

public sealed record RuntimeTreeInfo(
    string TextureName,
    string? TexturePath,
    double MinimumHeight,
    double MaximumHeight,
    double MinimumAspect,
    double MaximumAspect);

public sealed record RuntimeSceneryAssetInfo(
    bool UsesAbsoluteHeight,
    bool OnlyEditor,
    string? RenderType,
    IReadOnlyList<RuntimeObjectMeshInfo> Meshes,
    RuntimeTreeInfo? Tree);

public sealed record RuntimeSpawnInfo(
    string Name,
    double X,
    double Y,
    double Z,
    double HeadingDegrees);

public sealed record RuntimeDriverCameraInfo(
    double X,
    double Y,
    double Z,
    double EyeDistance,
    double FieldOfViewDegrees,
    double HeadingDegrees,
    double PitchDegrees);

public sealed record RuntimePassengerCameraInfo(
    double X,
    double Y,
    double Z,
    double EyeDistance,
    double FieldOfViewDegrees,
    double HeadingDegrees,
    double PitchDegrees);

public sealed record RuntimeOutsideCameraCenterInfo(
    double X,
    double Y,
    double Z);

public sealed record RuntimeReflectionCameraInfo(
    int Index,
    double X,
    double Y,
    double Z,
    double EyeDistance,
    double FieldOfViewDegrees,
    double HeadingDegrees,
    double PitchDegrees,
    double? MaximumRenderDistanceMeters,
    string RuntimeTextureName,
    string RuntimeTextureKey);

public sealed record RuntimeVehiclePhysicsInfo(
    double? WheelBaseMeters,
    double? MaximumSteeringAngleDegrees);

public sealed record RuntimeDriverPositionInfo(
    double X,
    double Y,
    double Z,
    double SeatHeight,
    double RotationDegrees);

public sealed record RuntimeVehicleInfo(
    string DisplayName,
    string RelativePath,
    IReadOnlyList<RuntimeObjectMeshInfo> Meshes,
    IReadOnlyList<RuntimeDriverCameraInfo> DriverCameras,
    IReadOnlyList<RuntimePassengerCameraInfo> PassengerCameras,
    int StandardDriverCameraIndex,
    int? ScheduleDriverCameraIndex,
    int? TicketSellingDriverCameraIndex,
    RuntimeOutsideCameraCenterInfo? OutsideCameraCenter,
    IReadOnlyList<RuntimeReflectionCameraInfo> ReflectionCameras,
    RuntimeVehiclePhysicsInfo Physics,
    RuntimeDriverPositionInfo? DriverPosition,
    int ProtectedMeshCount);

public sealed record RuntimeWindowInfo(
    string WorldName,
    int TileCount,
    int TotalTileCount,
    int? ActiveTileRadius,
    int ObjectCount,
    int SplineCount,
    string ContentRoot,
    IReadOnlyList<RuntimeTileInfo> Tiles,
    IReadOnlyList<RuntimeSplineInfo> Splines,
    IReadOnlyList<RuntimeObjectInfo> Objects,
    IReadOnlyDictionary<string, RuntimeSceneryAssetInfo> SceneryAssets,
    IReadOnlyList<RuntimeGroundTextureInfo> GroundTextures,
    RuntimeVehicleInfo? Vehicle,
    RuntimeSpawnInfo? Spawn);
