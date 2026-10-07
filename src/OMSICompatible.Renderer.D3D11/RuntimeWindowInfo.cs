using System.Numerics;
using OmsiCompat.Scripting;

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

public sealed record RuntimeSplinePathInfo(
    int Type,
    double X,
    double Z,
    double Width,
    int Direction);

public sealed record RuntimeSplineInfo(
    long Id,
    long PreviousId,
    long NextId,
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
    bool UsesHeightProfile,
    double DeltaHeightMeters,
    double CantStartPercent,
    double CantEndPercent,
    double SkewStart,
    double SkewEnd,
    bool Mirror,
    IReadOnlyList<RuntimeSplineSurfaceInfo> Surfaces,
    IReadOnlyList<RuntimeSplinePathInfo> Paths,
    int? TerrainAlignMode = null);

public sealed record RuntimeTrafficPathPointInfo(
    double X,
    double Y,
    double Z);

public sealed record RuntimeTrafficSignalPhaseInfo(
    int Phase,
    double DurationSeconds);

public sealed record RuntimeTrafficSignalStateInfo(
    int SegmentIndex,
    int? Phase,
    double PositionSeconds);

public sealed record RuntimeTrafficSignalProgramInfo(
    double CycleSeconds,
    double ApproachDistanceMeters,
    IReadOnlyList<RuntimeTrafficSignalPhaseInfo> Phases);

public sealed record RuntimeTrafficPathSegmentInfo(
    int Index,
    long SplineId,
    int LocalPathIndex,
    int Type,
    int Direction,
    double WidthMeters,
    IReadOnlyList<RuntimeTrafficPathPointInfo> Points,
    IReadOnlyList<int> ForwardConnections,
    IReadOnlyList<int> ReverseConnections,
    double? SpeedLimitKilometersPerHour = null,
    int TrafficPriority = 128,
    RuntimeTrafficSignalProgramInfo? TrafficSignal = null,
    long? SceneryObjectId = null);

public readonly record struct RuntimeLocalWalkerState(
    bool Active,
    Vector3 Position,
    float HeadingDegrees,
    float SpeedMetersPerSecond,
    float CourseDegrees,
    bool Seated,
    bool Kneeling);

public sealed record RuntimeRemoteWalkerInfo(
    uint PlayerId,
    string Name,
    string FigurePath,
    double X,
    double Y,
    double Z,
    float HeadingDegrees,
    float SpeedMetersPerSecond,
    float CourseDegrees,
    bool Seated);

public sealed record RuntimeNavigationGuidancePointInfo(
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double DistanceAheadMeters,
    string Kind);

public sealed record RuntimeTrafficPathNetworkInfo(
    IReadOnlyList<RuntimeTrafficPathSegmentInfo> Segments,
    int RoadVehicleSegmentCount,
    int PedestrianSegmentCount,
    int RailSegmentCount,
    int AircraftSegmentCount,
    int ConnectedEndpointCount,
    int BoundaryEndpointCount,
    int TerminalEndpointCount,
    int UnmatchedEndpointCount)
{
    public static RuntimeTrafficPathNetworkInfo Empty { get; } =
        new(
            Array.Empty<RuntimeTrafficPathSegmentInfo>(),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
}

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
    IReadOnlyList<string> ExtraValues,
    long ObjectId = -1);

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

public sealed record RuntimeVehicleMaterialColorInfo(
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

public sealed record RuntimeVehicleFreeTextureInfo(
    string SourceTextureName,
    string VariableName);

public sealed record RuntimeVehicleTextTextureInfo(
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

public sealed record RuntimeVehicleMaterialChangeItemInfo(
    int ItemIndex,
    int? AlphaMode,
    string? TransMapTexturePath,
    bool HasTransMapDirective,
    bool NoZWrite,
    bool NoZCheck,
    string? AlphaScaleVariable,
    string? LightMapTexturePath,
    string? LightMapVariable,
    string? MaterialChangeTexturePath,
    RuntimeVehicleMaterialColorInfo? AllColor,
    string? EnvMapTexturePath,
    double EnvMapStrength,
    string? EnvMapMaskTexturePath,
    string? BumpMapTexturePath,
    double BumpMapStrength,
    IReadOnlyList<RuntimeVehicleFreeTextureInfo>? FreeTextures,
    int? TextTextureIndex,
    bool MaterialChangeIsNightMap = false);

public sealed record RuntimeVehicleMaterialChangeSetInfo(
    string VariableName,
    int GroupIndex,
    IReadOnlyList<RuntimeVehicleMaterialChangeItemInfo> Items);

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
    string? MaterialChangeVariable = null,
    RuntimeVehicleMaterialColorInfo? BaseAllColor = null,
    RuntimeVehicleMaterialColorInfo? MaterialChangeAllColor = null,
    string? EnvMapTexturePath = null,
    double EnvMapStrength = 0.0,
    string? EnvMapMaskTexturePath = null,
    string? BumpMapTexturePath = null,
    double BumpMapStrength = 0.0,
    IReadOnlyList<RuntimeVehicleFreeTextureInfo>? FreeTextures = null,
    int? TextTextureIndex = null,
    IReadOnlyList<RuntimeVehicleMaterialChangeSetInfo>? MaterialChangeSets = null,
    bool HasTransMapDirective = false,
    bool MaterialChangeIsNightMap = false,
    bool RequiresExternalTransMap = false);

public sealed record RuntimeVehicleLightEffectInfo(
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
    Matrix4x4? SourceTransform = null,
    IReadOnlyList<RuntimeVehicleLightEffectInfo>? LightEffects = null,
    string? MeshIdentifier = null,
    string? AnimationParent = null,
    int SectionIndex = 0,
    int ModelOrdinal = -1,
    float[]? SkinWeights = null,
    IReadOnlyList<int>? SkinBoneMeshOrdinals = null,
    string? MouseEventTrigger = null);

public sealed record RuntimeTreeInfo(
    string TextureName,
    string? TexturePath,
    double MinimumHeight,
    double MaximumHeight,
    double MinimumAspect,
    double MaximumAspect);

public sealed record RuntimeSceneryBoundingBoxInfo(
    double LengthX,
    double WidthY,
    double HeightZ,
    double CenterX,
    double CenterY,
    double CenterZ);

public sealed record RuntimeSceneryCollisionBoundsInfo(
    double MinimumX,
    double MaximumX,
    double MinimumY,
    double MaximumY,
    double MinimumZ,
    double MaximumZ);

public sealed record RuntimeSceneryCollisionGeometryInfo(
    float[] Positions,
    uint[] Indices);

public sealed record RuntimeSceneryAssetInfo(
    bool UsesAbsoluteHeight,
    bool OnlyEditor,
    string? RenderType,
    IReadOnlyList<RuntimeObjectMeshInfo> Meshes,
    RuntimeTreeInfo? Tree,
    bool NoCollision = false,
    bool Fixed = false,
    bool Surface = false,
    string? CollisionMeshSource = null,
    RuntimeSceneryBoundingBoxInfo? BoundingBox = null,
    RuntimeSceneryCollisionBoundsInfo? CollisionBounds = null,
    RuntimeSceneryCollisionGeometryInfo? CollisionGeometry = null);

public sealed record RuntimeRailSignalRouteStateInfo(
    int RouteIndex,
    long SignalObjectId,
    int SignalState,
    bool Reserved,
    int? ReservedAgentIndex,
    OmsiScriptRuntime? ScriptRuntime = null);

public sealed record RuntimeTrafficObstacleInfo(
    double X,
    double Y,
    double Z,
    double HeadingRadians,
    double SpeedMetersPerSecond,
    double HalfLengthMeters,
    double HalfWidthMeters);

public sealed record RuntimeTrafficAgentInfo(
    int AgentIndex,
    int SegmentIndex,
    double DistanceMeters,
    double SpeedMetersPerSecond,
    string VehiclePath,
    double X,
    double Y,
    double Z,
    double HeadingRadians,
    bool AiBrakeLight = false,
    bool AiBlinkerLeft = false,
    bool AiBlinkerRight = false,
    double TraveledDistanceMeters = 0.0,
    double PathCurvaturePerMeter = 0.0,
    OmsiScriptRuntime? ScriptRuntime = null);

public sealed record RuntimeAiFileReferenceInfo(
    string DeclaredPath,
    string? ResolvedPath);

public sealed record RuntimeAiVehicleDefinitionInfo(
    string GroupName,
    string DeclaredPath,
    string? ResolvedPath,
    double Weight);

public sealed record RuntimeAiCatalogInfo(
    IReadOnlyList<RuntimeAiVehicleDefinitionInfo> MovingVehicles,
    IReadOnlyList<RuntimeAiFileReferenceInfo> Humans,
    IReadOnlyList<RuntimeAiFileReferenceInfo> Drivers,
    IReadOnlyList<RuntimeAiFileReferenceInfo> ParkedVehicles)
{
    public static RuntimeAiCatalogInfo Empty { get; } =
        new(
            Array.Empty<RuntimeAiVehicleDefinitionInfo>(),
            Array.Empty<RuntimeAiFileReferenceInfo>(),
            Array.Empty<RuntimeAiFileReferenceInfo>(),
            Array.Empty<RuntimeAiFileReferenceInfo>());
}

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

public sealed record RuntimeVehicleEntryInfo(
    int Index,
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
    double? VisibilityThreshold,
    bool ContinuousRendering,
    string RuntimeTextureName,
    string RuntimeTextureKey);

public sealed record RuntimeVehicleAxleInfo(
    double LongitudinalPositionMeters,
    double? WheelDiameterMeters,
    double? DriveFactor,
    double? MaximumWidthMeters,
    double? MinimumWidthMeters,
    double? SpringRateKilonewtonsPerMeter,
    double? MaximumForceKilonewtons,
    double? DamperRateKilonewtonSecondsPerMeter);

public sealed record RuntimeVehiclePhysicsInfo(
    double? WheelBaseMeters,
    double? MaximumSteeringAngleDegrees,
    double? MassTonnes = null,
    double? CenterOfGravityHeightMeters = null,
    double? RollingResistanceNewtons = null,
    double? TrackWidthMeters = null,
    double? AverageWheelDiameterMeters = null,
    double? SuspensionSpringKilonewtonsPerMeter = null,
    double? SuspensionDamperKilonewtonSecondsPerMeter = null,
    double? MomentOfInertiaYawTonneSquareMeters = null,
    double? RotationPointLongitudinalMeters = null,
    double? InverseMinimumTurnRadius = null,
    double? FrontAxleLongitudinalMeters = null,
    double? RearAxleLongitudinalMeters = null,
    double? FrontSuspensionSpringKilonewtonsPerMeter = null,
    double? RearSuspensionSpringKilonewtonsPerMeter = null,
    double? FrontSuspensionDamperKilonewtonSecondsPerMeter = null,
    double? RearSuspensionDamperKilonewtonSecondsPerMeter = null,
    double? MomentOfInertiaX = null,
    double? MomentOfInertiaY = null,
    double? MomentOfInertiaZ = null,
    IReadOnlyList<RuntimeVehicleAxleInfo>? Axles = null,
    double? AiDeltaHeightMeters = null);

public sealed record RuntimeDriverPositionInfo(
    double X,
    double Y,
    double Z,
    double SeatHeight,
    double RotationDegrees);

public sealed record RuntimeVehicleSectionInfo(
    int Index,
    int ParentIndex,
    double JointX,
    double JointY,
    double JointZ,
    double OriginX,
    double OriginY,
    double OriginZ,
    double FollowerLengthMeters,
    double MaximumYawDegrees,
    double MinimumPitchDegrees,
    double MaximumPitchDegrees,
    int CouplingType,
    bool Reverse,
    string? SoundConfigPath = null,
    bool OpenForSound = false,
    double? MassTonnes = null,
    double? YawInertiaTonneSquareMeters = null,
    double? RotationPointLongitudinalMeters = null,
    double? WheelBaseMeters = null,
    double? RollingResistanceNewtons = null,
    double? AverageWheelDiameterMeters = null,
    RuntimeVehiclePhysicsInfo? Physics = null);

public sealed record RuntimeVehicleInfo(
    string DisplayName,
    string RelativePath,
    string? SoundConfigPath,
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
    int ProtectedMeshCount,
    IReadOnlyList<RuntimeVehicleTextTextureInfo> TextTextures,
    IReadOnlyList<RuntimeVehicleSectionInfo>? Sections = null,
    IReadOnlyList<RuntimeVehicleEntryInfo>? PassengerEntries = null);

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
    RuntimeTrafficPathNetworkInfo TrafficPaths,
    RuntimeAiCatalogInfo AiCatalog,
    RuntimeVehicleInfo? Vehicle,
    RuntimeSpawnInfo? Spawn,
    IReadOnlyDictionary<string, RuntimeVehicleInfo>? TrafficVehicleAssets = null,
    IReadOnlySet<long>? DynamicSceneryObjectIds = null);
