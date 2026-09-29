using System.Diagnostics;
using OMSICompatible.Renderer.Common;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OmsiCompat.Scripting;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace OMSICompatible.Renderer.D3D11;

public sealed class D3D11RenderWindow : Form
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeSkyConstants
    {
        // x = yaw, y = pitch, z = tan(verticalFov / 2), w = aspect.
        public Vector4 ViewParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeCameraConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector3 CameraPosition;
        public float CameraPadding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimePostProcessConstants
    {
        public Vector2 TexelSize;
        public float SharpenStrength;
        public float Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeModelConstants
    {
        public Matrix4x4 World;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeTrafficInstanceData
    {
        public const uint SizeInBytes = 64;

        public Matrix4x4 World;
    }

    private struct TrafficVehicleBindingState
    {
        public bool HasMaterial;
        public RuntimeVehicleMaterialConstants Material;
        public ID3D11BlendState? BlendState;
        public ID3D11DepthStencilState? DepthState;
        public ID3D11PixelShader? PixelShader;
        public ID3D11ShaderResourceView? DiffuseView;
        public ID3D11ShaderResourceView? TransMapView;
        public ID3D11ShaderResourceView? LightMapView;
        public ID3D11ShaderResourceView? MaterialChangeView;
        public ID3D11ShaderResourceView? EnvMapView;
        public ID3D11ShaderResourceView? EnvMapMaskView;
        public ID3D11ShaderResourceView? BumpMapView;
    }

    private enum TrafficAnimationBindingKind
    {
        Unsupported,
        Steering,
        WheelRotation
    }

    private readonly record struct TrafficAnimationBinding(
        TrafficAnimationBindingKind Kind,
        int AxleIndex,
        bool IsLeft);

    private readonly record struct CompiledTrafficAnimation(
        RuntimeVehicleAnimationInfo Animation,
        TrafficAnimationBinding Binding,
        Vector3 Pivot,
        Vector3 Axis);

    private readonly record struct TrafficVehicleAnimationPhysics(
        double? WheelBaseMeters,
        double? TrackWidthMeters,
        double? MaximumSteeringRadians,
        double? AverageWheelRadiusMeters,
        double[] WheelRadiiMeters);

    private sealed record PreparedTrafficVehicleGeometry(
        string Path,
        RuntimeObjectGeometry Geometry);

    private sealed record PreparedStreamedTileWork(
        int X,
        int Y,
        RuntimeTileInfo Tile,
        RuntimeSplineInfo[] Splines,
        RuntimeObjectInfo[] Objects,
        string[] RegularTexturePaths,
        string[] MaskTexturePaths);

    private sealed record PreparedStreamedGeometry(
        RuntimeVertex[] TileVertices,
        RuntimeTerrainGeometry Terrain,
        RuntimeSplineGeometry Splines,
        RuntimeObjectGeometry Objects,
        RuntimeTerrainSampler TerrainSampler,
        RuntimeSplineSurfaceSampler SplineSurfaceSampler,
        RuntimeSplineSurfaceSampler ScenerySurfaceSampler,
        RuntimeSplineSurfaceSampler CollisionScenerySurfaceSampler,
        IReadOnlyList<RuntimeSceneryCollisionVolume> CollisionVolumes,
        string[] RegularTexturePaths,
        string[] MaskTexturePaths,
        PreparedTrafficVehicleGeometry[] TrafficVehicleGeometries,
        PreparedStreamedTileWork[] TileWork);

    private sealed class PreparedTrafficVehicleGpuResource :
        IDisposable
    {
        public PreparedTrafficVehicleGpuResource(
            string path,
            RuntimeObjectGeometry geometry,
            ID3D11Buffer buffer)
        {
            Path =
                path;
            Geometry =
                geometry;
            Buffer =
                buffer;
        }

        public string Path
        {
            get;
        }

        public RuntimeObjectGeometry Geometry
        {
            get;
        }

        public ID3D11Buffer? Buffer
        {
            get;
            set;
        }

        public void Dispose()
        {
            Buffer?.Dispose();
            Buffer =
                null;
        }
    }

    private sealed class PreparedStreamedGpuResources :
        IDisposable
    {
        public ID3D11Buffer? TileVertexBuffer
        {
            get;
            set;
        }

        public ID3D11Buffer? TerrainVertexBuffer
        {
            get;
            set;
        }

        public ID3D11Buffer? SplineVertexBuffer
        {
            get;
            set;
        }

        public ID3D11Buffer? ObjectVertexBuffer
        {
            get;
            set;
        }

        public List<PreparedTrafficVehicleGpuResource>
            TrafficVehicles
        {
            get;
        } =
            [];

        public void Dispose()
        {
            TileVertexBuffer?.Dispose();
            TileVertexBuffer =
                null;

            TerrainVertexBuffer?.Dispose();
            TerrainVertexBuffer =
                null;

            SplineVertexBuffer?.Dispose();
            SplineVertexBuffer =
                null;

            ObjectVertexBuffer?.Dispose();
            ObjectVertexBuffer =
                null;

            foreach (var resource in
                     TrafficVehicles)
            {
                resource.Dispose();
            }

            TrafficVehicles.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeVehicleSkinConstants
    {
        public Matrix4x4 Bone0;
        public Matrix4x4 Bone1;
        public Matrix4x4 Bone2;
        public Matrix4x4 Bone3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeVehicleMaterialConstants
    {
        public float AlphaScale;
        public float LightMapStrength;
        public float MaterialChangeStrength;
        public float EnvMapStrength;

        public float EnvMapMaskEnabled;
        public float BumpMapStrength;
        public float MaterialChangeTextureEnabled;
        public float MaterialChangeColorEnabled;

        public Vector4 MaterialChangeDiffuse;
        public Vector4 BaseEmissive;
        public Vector4 MaterialChangeEmissive;
    }

    private enum RuntimeVehicleViewMode
    {
        Driver,
        Passenger,
        Exterior
    }

    private enum RuntimeSceneryRenderPass
    {
        PreSurface,
        Surface,
        OnSurface,
        One,
        Two,
        Three,
        Four
    }

    private readonly record struct RuntimeSceneryCollisionTriangle(
        Vector2 A,
        Vector2 B,
        Vector2 C,
        float MinimumY,
        float MaximumY);

    private readonly record struct RuntimeSceneryCollisionVolume(
        int Key,
        long ObjectId,
        string AssetPath,
        Vector2 Center,
        Vector2 Forward,
        Vector2 Right,
        float HalfLength,
        float HalfWidth,
        float MinimumY,
        float MaximumY,
        bool Surface,
        bool UsesCollisionMesh,
        IReadOnlyList<RuntimeSceneryCollisionTriangle>? Triangles);

    private static readonly FeatureLevel[] RequestedFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    private RuntimeWindowInfo _windowInfo;
    private readonly Func<
        double,
        IReadOnlyList<RuntimeTrafficAgentInfo>>?
        _trafficStep;
    private readonly Action<int, float>?
        _trafficCollisionResponse;
    private readonly Action<
        double,
        OmsiScriptRuntime,
        Action<string>>?
        _pluginStep;
    private IReadOnlyList<RuntimeTrafficAgentInfo>
        _trafficAgents =
            Array.Empty<RuntimeTrafficAgentInfo>();
    private readonly Func<
        IReadOnlyList<RuntimeTrafficSignalStateInfo>>?
        _trafficSignalStateProvider;
    private IReadOnlyList<RuntimeTrafficSignalStateInfo>
        _trafficSignalStates =
            Array.Empty<RuntimeTrafficSignalStateInfo>();
    private readonly Dictionary<int, RuntimeTrafficSignalStateInfo>
        _trafficSignalStateBySegmentIndex =
            [];
    private readonly Dictionary<int, RuntimeTrafficPathSegmentInfo>
        _runtimeTrafficSegmentByIndex =
            [];
    private readonly Dictionary<(int First, int Second), bool>
        _runtimeTrafficPathConflictCache =
            [];
    private readonly Func<
        IReadOnlyList<RuntimeRailSignalRouteStateInfo>>?
        _railSignalStateProvider;
    private IReadOnlyList<RuntimeRailSignalRouteStateInfo>
        _railSignalRouteStates =
            Array.Empty<RuntimeRailSignalRouteStateInfo>();
    private readonly Dictionary<long, OmsiScriptRuntime>
        _railSignalRuntimeByObjectId =
            [];
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly RuntimeFreeCamera _camera = new();
    private readonly RuntimeDriveVehicle _vehicle;
    private readonly OmsiScriptRuntime? _scriptRuntime;
    private readonly IReadOnlyDictionary<int, OmsiScriptRuntime>
        _sectionScriptRuntimes;
    private readonly IReadOnlyDictionary<string, double>? _initialVehicleVariables;
    private readonly OmsiSystemMacroHandler? _previousSystemMacroHandler;
    private readonly HashSet<string> _reportedUnhandledSystemMacros =
        new(
            StringComparer.Ordinal);
    private readonly HashSet<string> _reportedMissingVehicleFonts =
        new(
            StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedMissingTransMaps =
        new(
            StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Keys> _pressedKeys = [];
    private readonly IReadOnlyList<RuntimeOmsiKeyboardBinding>
        _omsiKeyboardBindings;
    private readonly IReadOnlyDictionary<
        string,
        RuntimeOmsiHostInputAction>
        _omsiHostActionsByTrigger;
    private readonly HashSet<RuntimeOmsiKeyboardBinding>
        _activeOmsiContinuousBindings =
            [];
    private readonly HashSet<RuntimeOmsiKeyboardBinding>
        _activeOmsiPressedBindings =
            [];
    private readonly HashSet<RuntimeOmsiHostInputAction>
        _activeControllerHostActions =
            [];
    private readonly bool _gameControllerEnabled;
    private readonly bool _automaticSteeringCenter;
    private RuntimeOmsiGameControllerHost? _omsiGameController;
    private RuntimeOmsiAudioHost? _omsiAudio;
    private readonly Dictionary<int, RuntimeOmsiAudioHost>
        _articulatedOmsiAudio =
            [];
    private readonly Dictionary<int, TrafficOmsiAudioState>
        _trafficOmsiAudio =
            [];
    private readonly HashSet<int>
        _activeTrafficAudioAgentIds =
            [];
    private readonly List<int>
        _staleTrafficAudioAgentIds =
            [];

    private const float TrafficAudioActivationDistanceMeters =
        180.0f;
    private const float TrafficAudioDeactivationDistanceMeters =
        240.0f;
    private bool _controllerInputEnabled = true;
    private float _controllerClutchInput;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private readonly Dictionary<RuntimeVehicleAnimationInfo, double>
        _vehicleAnimationValues =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RuntimeVehicleLightEffectInfo, double>
        _vehicleLightValues =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RuntimeObjectBatch>
        _vehicleAnimationParentBatches =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly List<(
        RuntimeObjectBatch Batch,
        ResolvedVehicleMaterialState Material)>
        _vehicleDrawItems =
            [];
    private readonly List<RuntimeObjectMeshInfo>
        _vehicleLightMeshes =
            [];
    private readonly List<RuntimeObjectMeshInfo>
        _vehicleViewpointLightMeshes =
            [];
    private readonly Dictionary<
        RuntimeObjectBatch,
        RuntimeVehicleMaterialChangeSetInfo[]>
        _vehicleOrderedMaterialChangeSets =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        RuntimeVehicleMaterialChangeSetInfo,
        Dictionary<int, RuntimeVehicleMaterialChangeItemInfo>>
        _vehicleMaterialChangeItems =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(int SectionIndex, int ModelOrdinal), RuntimeObjectBatch>
        _vehicleMeshOrdinalBatches =
            [];
    private readonly Dictionary<int, float>
        _articulatedSectionAbsoluteHeadingRadians =
            [];
    private readonly Dictionary<int, float>
        _articulatedSectionYawRadians =
            [];
    private readonly Dictionary<int, float>
        _articulatedSectionYawRateRadiansPerSecond =
            [];
    private readonly Dictionary<int, float>
        _articulatedSectionPitchRadians =
            [];
    private readonly Dictionary<int, float>
        _articulatedSectionPitchRateRadiansPerSecond =
            [];
    private readonly Dictionary<int, Vector2>
        _articulatedSectionJointWorldPosition =
            [];

    private double _lastFrameTimeSeconds;
    private double _trafficStepAccumulatedSeconds;
    private double _trafficAudioStepAccumulatedSeconds;
    private double _lastTrafficSimulationStepSeconds;
    private double _odometerMeters;
    private double _lastVehiclePhysicsDiagnosticsSeconds =
        double.NegativeInfinity;
    private bool _graphicsPrepared;
    private bool _mouseLooking;
    private MouseButtons _freeCameraDragButton =
        MouseButtons.None;
    private bool _mouseDriveMode;
    private string? _activeVehicleMouseTrigger;
    private int _activeVehicleMouseSectionIndex;
    private float _vehicleMouseDeltaX;
    private float _vehicleMouseDeltaY;
    private readonly Dictionary<Keys, string>
        _fallbackOmsiPressTriggers =
            [];
    private bool _vehicleRemoved;
    private bool _driveMode = true;
    private RuntimeVehicleViewMode _vehicleViewMode =
        RuntimeVehicleViewMode.Driver;
    private int _driverCameraIndex;
    private int _passengerCameraIndex;
    private float _interiorCameraYawOffsetRadians;
    private float _interiorCameraPitchOffsetRadians;
    private float _interiorCameraFieldOfViewScale = 1.0f;
    private float _exteriorCameraYawOffsetRadians;
    private float _exteriorCameraPitchOffsetRadians;
    private float _exteriorCameraDistanceScale = 1.0f;
    private float _mouseDriveAccelerator;
    private float _mouseDriveBrake;
    private float _mouseDriveSteering;
    private bool _simulationPaused;
    private bool _vehiclePanelAuditWritten;
    private bool _suppressVehicleInitAudio;
    private int _statusInfoLevel = 1;
    private bool _specialViewActive;
    private bool _specialPreviousDriveMode;
    private RuntimeVehicleViewMode _specialPreviousViewMode =
        RuntimeVehicleViewMode.Driver;
    private int _specialPreviousDriverCameraIndex;
    private int _specialPreviousPassengerCameraIndex;
    private readonly RuntimeOmsiMenuBar? _omsiMenuBar;
    private readonly RuntimeBusSelectorPanel? _busSelectorPanel;
    private int _captionFrame;
    private int? _streamingTileX;
    private int? _streamingTileY;
    private int _streamedWorldPreparationGeneration;
    private long _renderFrameSequence;
    private readonly Queue<(ID3D11Buffer Buffer, long ReleaseAfterFrame)>
        _retiredStreamingVertexBuffers =
            new();
    private const int StreamingBufferRetirementFrames =
        3;
    private System.Drawing.Point _lastMousePosition;

    public event Action<int, int>?
        StreamingCenterChanged;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _deviceContext;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11Texture2D? _multisampleColorTexture;
    private ID3D11Texture2D? _postProcessTexture;
    private ID3D11ShaderResourceView? _postProcessShaderResourceView;
    private ID3D11RenderTargetView? _postProcessBackBufferView;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11Texture2D? _depthTexture;
    private ID3D11DepthStencilView? _depthStencilView;
    private ID3D11VertexShader? _postProcessVertexShader;
    private ID3D11PixelShader? _postProcessPixelShader;
    private ID3D11SamplerState? _postProcessSampler;
    private ID3D11Buffer? _postProcessConstantsBuffer;
    private readonly int _requestedMsaaSamples;
    private int _activeMsaaSamples;
    private readonly float _sharpenStrength;

    private ID3D11VertexShader? _skyVertexShader;
    private ID3D11PixelShader? _skyPixelShader;
    private ID3D11SamplerState? _skySampler;
    private ID3D11Buffer? _skyConstantsBuffer;
    private RuntimeGpuTexture? _skyTexture;

    private ID3D11Buffer? _tileVertexBuffer;
    private ID3D11VertexShader? _tileVertexShader;
    private ID3D11PixelShader? _tilePixelShader;
    private ID3D11InputLayout? _tileInputLayout;
    private uint _tileVertexCount;

    private ID3D11Buffer? _terrainVertexBuffer;
    private ID3D11Buffer? _terrainCameraBuffer;
    private ID3D11VertexShader? _terrainVertexShader;
    private ID3D11PixelShader? _terrainPixelShader;
    private ID3D11PixelShader? _terrainTexturedPixelShader;
    private ID3D11PixelShader? _terrainBaseDetailPixelShader;
    private ID3D11PixelShader? _terrainLayerPixelShader;
    private ID3D11PixelShader? _terrainLayerDetailPixelShader;
    private ID3D11PixelShader? _terrainLightmapPixelShader;
    private ID3D11InputLayout? _terrainInputLayout;
    private ID3D11SamplerState? _terrainTextureSampler;
    private ID3D11SamplerState? _terrainTextureSamplerPerformance;
    private ID3D11SamplerState? _terrainMaskSampler;
    private ID3D11BlendState? _terrainAlphaBlendState;
    private ID3D11BlendState? _terrainAdditiveBlendState;
    private ID3D11RasterizerState? _terrainRasterizerState;
    private RuntimeTerrainGeometry _terrainGeometry =
        RuntimeTerrainGeometry.Empty;
    private string? _lastTerrainAlignmentDiagnosticSignature;
    private RuntimeTerrainSampler _terrainSurfaceSampler;
    private uint _terrainVertexCount;

    private ID3D11Buffer? _splineVertexBuffer;
    private RuntimeSplineGeometry _splineGeometry =
        RuntimeSplineGeometry.Empty;
    private uint _splineVertexCount;

    private ID3D11Buffer? _objectVertexBuffer;
    private ID3D11VertexShader? _objectVertexShader;
    private ID3D11PixelShader? _objectColorPixelShader;
    private ID3D11PixelShader? _objectTexturedPixelShader;
    private ID3D11PixelShader? _objectAlphaCutoutPixelShader;
    private ID3D11PixelShader? _objectAlphaBlendPixelShader;
    private ID3D11PixelShader? _objectAlphaCutoutTransMapPixelShader;
    private ID3D11PixelShader? _objectAlphaBlendTransMapPixelShader;
    private ID3D11BlendState? _objectAlphaBlendState;
    private ID3D11DepthStencilState? _objectDepthReadState;
    private ID3D11DepthStencilState? _objectDepthDisabledState;
    private ID3D11InputLayout? _objectInputLayout;
    private ID3D11SamplerState? _objectSampler;
    private ID3D11SamplerState? _objectSamplerPerformance;
    private RuntimeGpuTextureLoader? _objectTextureLoader;
    private RuntimeOmsiTextTextureRenderer? _vehicleTextTextureRenderer;
    private readonly Dictionary<string, RuntimeGpuTexture>
        _objectTextureCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long>
        _objectTextureLastUsedGeneration =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(RuntimeGpuTexture Texture, long ReleaseAfterFrame)>
        _retiredStreamingTextures =
            new();
    private readonly HashSet<string>
        _failedObjectTexturePaths =
            new(
                StringComparer.OrdinalIgnoreCase);
    private long _streamingTextureGeneration;
    private readonly Queue<(string Path, bool AlphaMask)>
        _pendingStreamingTextureLoads =
            new();
    private readonly HashSet<string>
        _pendingStreamingTexturePaths =
            new(
                StringComparer.OrdinalIgnoreCase);
    private const int MaximumInactiveStreamingTextureCacheEntries =
        512;
    private const long DefaultMaximumInactiveStreamingTextureCacheBytes =
        768L * 1024L * 1024L;
    private const long DefaultMaximumStreamingTextureCacheBytes =
        1536L * 1024L * 1024L;
    private readonly long _maximumStreamingTextureCacheBytes;
    private readonly long _maximumInactiveStreamingTextureCacheBytes;
    private long _currentStreamingTextureCacheBytes;
    private const int MaximumStreamingTextureUploadsPerFrame =
        4;
    private const double MaximumStreamingTextureUploadBudgetMilliseconds =
        2.0;
    private int _currentStreamingTextureUploadLimit =
        MaximumStreamingTextureUploadsPerFrame;
    private double _currentStreamingTextureUploadBudgetMilliseconds =
        MaximumStreamingTextureUploadBudgetMilliseconds;
    private RuntimeObjectGeometry _objectGeometry =
        RuntimeObjectGeometry.Empty;
    private readonly Dictionary<
        RuntimeSceneryRenderPass,
        RuntimeObjectBatch[]>
        _objectBatchesByRenderPass =
            [];
    private uint _objectVertexCount;

    private ID3D11Buffer? _vehicleExteriorVertexBuffer;
    private ID3D11Buffer? _vehicleInteriorVertexBuffer;
    private ID3D11Buffer? _vehicleLightVertexBuffer;
    private ID3D11Buffer? _vehicleModelBuffer;
    private ID3D11Buffer? _vehicleMaterialBuffer;
    private ID3D11Buffer? _vehicleSkinBuffer;
    private ID3D11VertexShader? _vehicleVertexShader;
    private ID3D11VertexShader? _trafficInstancedVertexShader;
    private ID3D11InputLayout? _trafficInstancedInputLayout;
    private ID3D11Buffer? _trafficInstanceBuffer;
    private int _trafficInstanceBufferCapacity;
    private RuntimeTrafficInstanceData[] _trafficInstanceScratch =
        Array.Empty<RuntimeTrafficInstanceData>();
    private ID3D11PixelShader? _vehicleColorPixelShader;
    private ID3D11PixelShader? _vehicleLightPixelShader;
    private ID3D11PixelShader? _vehicleTexturedPixelShader;
    private ID3D11PixelShader? _vehicleAlphaCutoutPixelShader;
    private ID3D11PixelShader? _vehicleAlphaBlendPixelShader;
    private ID3D11PixelShader? _vehicleAlphaCutoutTransMapPixelShader;
    private ID3D11PixelShader? _vehicleAlphaBlendTransMapPixelShader;
    private ID3D11InputLayout? _vehicleInputLayout;
    private ID3D11SamplerState? _vehicleSampler;
    private ID3D11BlendState? _vehicleAlphaBlendState;
    private ID3D11DepthStencilState? _vehicleDepthReadState;
    private ID3D11DepthStencilState? _vehicleDepthDisabledState;
    private RuntimeObjectGeometry _vehicleExteriorGeometry =
        RuntimeObjectGeometry.Empty;
    private RuntimeObjectGeometry _vehicleInteriorGeometry =
        RuntimeObjectGeometry.Empty;
    private string[] _pinnedVehicleTexturePaths =
        Array.Empty<string>();
    private readonly Dictionary<string, RuntimeObjectGeometry>
        _trafficVehicleGeometries =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ID3D11Buffer>
        _trafficVehicleVertexBuffers =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeObjectBatch[]>
        _trafficVehicleRenderBatches =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrafficVehicleRenderBatchSummary>
        _trafficVehicleRenderBatchSummaries =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<
        RuntimeVehicleAnimationInfo,
        TrafficAnimationBinding>
        _trafficAnimationBindings =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        RuntimeObjectBatch,
        CompiledTrafficAnimation[]>
        _compiledTrafficAnimations =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        RuntimeVehicleInfo,
        TrafficVehicleAnimationPhysics>
        _trafficVehicleAnimationPhysics =
            new(
                ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RuntimeObjectMeshInfo[]>
        _trafficVehicleLightMeshes =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TrafficVehicleDrawItem>>
        _trafficVisibleDrawItemsByVehiclePath =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly List<TrafficVehicleDrawItem>
        _trafficVisibleDrawItems =
            [];

    private readonly record struct TrafficVehicleDrawItem(
        RuntimeTrafficAgentInfo Agent,
        Matrix4x4 VehicleWorld);

    private readonly record struct TrafficVehicleRenderBatchSummary(
        bool HasStaticOpaque,
        bool HasAnimatedOpaque);

    private readonly uint _reflectionTextureSize;
    private readonly string _reflectionMode;
    private long _reflectionFrameIndex;
    private readonly Dictionary<string, RuntimeReflectionTarget>
        _reflectionTargets =
            new(
                StringComparer.OrdinalIgnoreCase);
    private ID3D11Texture2D? _reflectionDepthTexture;
    private ID3D11DepthStencilView? _reflectionDepthStencilView;
    private ID3D11RenderTargetView? _activeRenderTargetView;
    private ID3D11DepthStencilView? _activeDepthStencilView;
    private Matrix4x4? _viewProjectionOverride;
    private Vector3? _cameraPositionOverride;
    private Vector4? _skyViewParametersOverride;
    private Matrix4x4? _renderSceneViewProjection;
    private Vector3? _renderSceneCameraPosition;
    private bool _reflectionRenderingEnabled;
    private bool _renderingReflectionPass;
    private readonly bool _vehiclePreviewMode;
    private float _previewYaw = 0.62f;
    private float _previewPitch = 0.16f;
    private float _previewDistance = 14.0f;
    private Vector3 _previewCenter =
        new(
            0.0f,
            1.6f,
            0.0f);
    private float _previewRadius = 5.0f;

    private FeatureLevel _featureLevel;
    private readonly bool _vsync;
    private readonly bool _showFps;
    private readonly Label? _fpsLabel;
    private long _fpsFrameCount;
    private double _fpsSampleStartSeconds;
    private double _fpsPreviousFrameSeconds;
    private double _previousRenderTickSeconds;
    private double _lastObservedFrameMilliseconds;
    private readonly double _targetFrameMilliseconds;
    private double _lastStreamingPrepareMilliseconds;
    private double _lastStreamingGpuPrepareMilliseconds;
    private double _lastStreamingSwapMilliseconds;
    private readonly Queue<double> _frameTimeSamplesMilliseconds =
        new();
    private const int MaximumFrameTimeSamples =
        240;
    private readonly float _masterVolume;
    private readonly int _maximumSoundCount;
    private readonly bool _aiVehicleSoundsEnabled;
    private readonly bool _vehicleToVehicleCollisionsEnabled;
    private readonly bool _vehicleLandscapeCollisionsEnabled;
    private readonly HashSet<int> _activeTrafficCollisionAgents = [];
    private readonly Dictionary<int, double> _lastTrafficCollisionSeconds = [];
    private readonly HashSet<int> _activeSceneryCollisionVolumes = [];
    private IReadOnlyList<RuntimeSceneryCollisionVolume> _sceneryCollisionVolumes =
        Array.Empty<RuntimeSceneryCollisionVolume>();
    private int _trafficCollisionCount;
    private int _speedViolationCount;
    private int _redLightViolationCount;
    private int _priorityViolationCount;
    private int _trafficPenaltyPoints;
    private int _trafficFineCredits;
    private int _lastRedLightSegmentIndex = -1;
    private double _lastRedLightViolationSeconds =
        double.NegativeInfinity;
    private long? _lastPriorityCrossingObjectId;
    private double _lastPriorityViolationSeconds =
        double.NegativeInfinity;
    private double _trafficRuleSampleSeconds;
    private double _speedingSeconds;
    private double? _lastSpeedLimitKilometersPerHour;
    private double _lastSpeedViolationSeconds =
        double.NegativeInfinity;
    private readonly bool _materialLightMapEnabled;
    private readonly bool _materialReflectionMapEnabled;
    private readonly bool _materialBumpMapEnabled;
    private readonly bool _materialNightMapEnabled;
    private readonly double _maximumObjectVisibilityMeters;
    private const float TrafficFrustumCullNearDistanceMeters =
        35.0f;
    private const float TrafficFrustumCullMargin =
        1.20f;
    private const float SceneryFrustumCullMargin =
        1.15f;
    private const float SceneryFrustumRadiusScale =
        2.0f;

    public D3D11RenderWindow(
        RuntimeWindowInfo windowInfo,
        OmsiScriptRuntime? scriptRuntime = null,
        int targetFps = 60,
        bool vsync = true,
        bool showFps = false,
        int msaaSamples = 0,
        double sharpenStrength = 0.0,
        bool vehiclePreviewMode = false,
        IReadOnlyDictionary<string, double>? initialVehicleVariables = null,
        string? inputLanguage = null,
        bool gameControllerEnabled = true,
        IReadOnlyDictionary<int, OmsiScriptRuntime>? sectionScriptRuntimes = null,
        int masterVolumePercent = 100,
        bool automaticSteeringCenter = false,
        int maximumSoundCount = 400,
        bool aiVehicleSoundsEnabled = true,
        bool vehicleToVehicleCollisionsEnabled = true,
        bool vehicleLandscapeCollisionsEnabled = true,
        bool materialLightMapEnabled = true,
        bool materialReflectionMapEnabled = true,
        bool materialBumpMapEnabled = true,
        bool materialNightMapEnabled = true,
        double maximumObjectVisibilityMeters = 1000.0,
        Func<
            double,
            IReadOnlyList<RuntimeTrafficAgentInfo>>?
            trafficStep = null,
        Func<
            IReadOnlyList<RuntimeTrafficSignalStateInfo>>?
            trafficSignalStateProvider = null,
        Func<
            IReadOnlyList<RuntimeRailSignalRouteStateInfo>>?
            railSignalStateProvider = null,
        Action<int, float>?
            trafficCollisionResponse = null,
        Action<
            double,
            OmsiScriptRuntime,
            Action<string>>?
            pluginStep = null,
        bool terrainCollisionsEnabled = true,
        int reflectionTextureSize = 512,
        string? reflectionMode = "economy")
    {
        _windowInfo = windowInfo;
        _trafficStep =
            trafficStep;
        _trafficCollisionResponse =
            trafficCollisionResponse;
        _pluginStep =
            pluginStep;
        _trafficAgents =
            _trafficStep?.Invoke(
                0.0) ??
            Array.Empty<RuntimeTrafficAgentInfo>();
        _trafficSignalStateProvider =
            trafficSignalStateProvider;
        _trafficSignalStates =
            _trafficSignalStateProvider?.Invoke() ??
            Array.Empty<RuntimeTrafficSignalStateInfo>();
        RebuildTrafficSignalStateLookup();
        RebuildRuntimeTrafficSegmentLookup();

        _railSignalStateProvider =
            railSignalStateProvider;
        _railSignalRouteStates =
            _railSignalStateProvider?.Invoke() ??
            Array.Empty<RuntimeRailSignalRouteStateInfo>();
        RebuildRailSignalRuntimeLookup();
        _scriptRuntime = scriptRuntime;
        _sectionScriptRuntimes =
            sectionScriptRuntimes is null
                ? new Dictionary<int, OmsiScriptRuntime>()
                : new Dictionary<int, OmsiScriptRuntime>(
                    sectionScriptRuntimes);
        _initialVehicleVariables =
            initialVehicleVariables is null
                ? null
                : new Dictionary<string, double>(
                    initialVehicleVariables,
                    StringComparer.OrdinalIgnoreCase);

        _odometerMeters =
            ResolveInitialOdometerMeters(
                _initialVehicleVariables);

        _vsync = vsync;
        _requestedMsaaSamples =
            msaaSamples >=
                    4
                ? 4
                : msaaSamples >=
                    2
                    ? 2
                    : 0;
        _sharpenStrength =
            (float)Math.Clamp(
                sharpenStrength,
                0.0,
                1.0);
        _showFps =
            showFps &&
            !vehiclePreviewMode;
        _fpsSampleStartSeconds =
            _frameClock.Elapsed.TotalSeconds;
        _fpsPreviousFrameSeconds =
            _fpsSampleStartSeconds;
        _previousRenderTickSeconds =
            _fpsSampleStartSeconds;
        _targetFrameMilliseconds =
            1000.0 /
            Math.Clamp(
                targetFps,
                10,
                240);
        _masterVolume =
            Math.Clamp(
                masterVolumePercent,
                0,
                100) /
            100.0f;
        _maximumSoundCount =
            Math.Clamp(
                maximumSoundCount,
                1,
                10_000);
        _aiVehicleSoundsEnabled =
            aiVehicleSoundsEnabled;
        _vehicleToVehicleCollisionsEnabled =
            vehicleToVehicleCollisionsEnabled;
        _vehicleLandscapeCollisionsEnabled =
            vehicleLandscapeCollisionsEnabled;
        _reflectionTextureSize =
            (uint)Math.Clamp(
                reflectionTextureSize,
                64,
                4096);
        _reflectionMode =
            string.IsNullOrWhiteSpace(
                reflectionMode)
                ? "economy"
                : reflectionMode
                    .Trim()
                    .ToLowerInvariant();
        _materialLightMapEnabled =
            materialLightMapEnabled;
        _materialReflectionMapEnabled =
            materialReflectionMapEnabled;
        _materialBumpMapEnabled =
            materialBumpMapEnabled;
        _materialNightMapEnabled =
            materialNightMapEnabled;
        _maximumObjectVisibilityMeters =
            double.IsFinite(
                    maximumObjectVisibilityMeters) &&
                maximumObjectVisibilityMeters >
                    0.0
                ? maximumObjectVisibilityMeters
                : 1000.0;

        _maximumStreamingTextureCacheBytes =
            ResolveStreamingTextureBudgetBytes();

        _maximumInactiveStreamingTextureCacheBytes =
            Math.Min(
                DefaultMaximumInactiveStreamingTextureCacheBytes,
                Math.Max(
                    256L * 1024L * 1024L,
                    _maximumStreamingTextureCacheBytes /
                        2L));

        _vehiclePreviewMode =
            vehiclePreviewMode;
        _gameControllerEnabled =
            gameControllerEnabled;
        _automaticSteeringCenter =
            automaticSteeringCenter;
        _omsiKeyboardBindings =
            _vehiclePreviewMode
                ? Array.Empty<
                    RuntimeOmsiKeyboardBinding>()
                : RuntimeOmsiKeyboardBindings.Load(
                    windowInfo.ContentRoot,
                    inputLanguage);
        _omsiHostActionsByTrigger =
            _vehiclePreviewMode
                ? new Dictionary<
                    string,
                    RuntimeOmsiHostInputAction>(
                    StringComparer.OrdinalIgnoreCase)
                : RuntimeOmsiKeyboardBindings.LoadHostActions(
                    windowInfo.ContentRoot,
                    inputLanguage);
        _previousSystemMacroHandler =
            _scriptRuntime?.SystemMacroHandler;

        if (_scriptRuntime is not null)
        {
            _scriptRuntime.SystemMacroHandler =
                HandleOmsiSystemMacro;
            _scriptRuntime.UnhandledSystemMacro +=
                OnUnhandledSystemMacro;
            _scriptRuntime.DebugMessageRequested +=
                OnScriptDebugMessage;
            _scriptRuntime.SoundTriggerRequested +=
                OnScriptSoundTriggerRequested;
            _scriptRuntime.FileSoundTriggerRequested +=
                OnScriptFileSoundTriggerRequested;
        }

        foreach (var pair in
                 _sectionScriptRuntimes)
        {
            var sectionIndex =
                pair.Key;

            var runtime =
                pair.Value;

            runtime.SystemMacroHandler =
                (name, context) =>
                    HandleOmsiSectionSystemMacro(
                        sectionIndex,
                        name,
                        context);

            runtime.UnhandledSystemMacro +=
                OnUnhandledSystemMacro;

            runtime.DebugMessageRequested +=
                OnScriptDebugMessage;

            runtime.SoundTriggerRequested +=
                trigger =>
                    TriggerSectionOmsiAudio(
                        sectionIndex,
                        trigger);

            runtime.FileSoundTriggerRequested +=
                (trigger, declaredFile) =>
                    OnSectionScriptFileSoundTriggerRequested(
                        sectionIndex,
                        trigger,
                        declaredFile);
        }

        _terrainSurfaceSampler =
            new RuntimeTerrainSampler(
                windowInfo.Tiles,
                windowInfo.Splines);

        _sceneryCollisionVolumes =
            BuildSceneryCollisionVolumes(
                windowInfo,
                _terrainSurfaceSampler);

        _vehicle = new RuntimeDriveVehicle(
            windowInfo.Tiles,
            windowInfo.Vehicle?.Physics,
            windowInfo.Vehicle?.Sections,
            terrainCollisionsEnabled,
            windowInfo.Splines);
        _driveMode =
            windowInfo.Vehicle is not null &&
            !_vehiclePreviewMode;
        _reflectionRenderingEnabled =
            !_vehiclePreviewMode &&
            windowInfo.Vehicle?.ReflectionCameras.Count is
                > 0;

        if (_reflectionRenderingEnabled &&
            windowInfo.Vehicle is
                { } reflectionVehicle)
        {
            foreach (var camera in
                     reflectionVehicle.ReflectionCameras)
            {
                Console.WriteLine(
                    $"[mirror] index={camera.Index}; texture={camera.RuntimeTextureName}; key={camera.RuntimeTextureKey}; continuous={camera.ContinuousRendering}; visibilityThreshold={(camera.VisibilityThreshold?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "<none>")}; fov={camera.FieldOfViewDegrees:0.###}; heading={camera.HeadingDegrees:0.###}; pitch={camera.PitchDegrees:0.###}; mode={_reflectionMode}; size={_reflectionTextureSize}");
            }
        }

        _driverCameraIndex =
            Math.Max(
                windowInfo.Vehicle?.StandardDriverCameraIndex ?? 0,
                0);

        Text =
            _vehiclePreviewMode
                ? $"Prévia 3D — {windowInfo.Vehicle?.DisplayName ?? "Veículo"}"
                : $"OMSI Compatible Runtime — {windowInfo.WorldName}";

        ClientSize =
            _vehiclePreviewMode
                ? new System.Drawing.Size(
                    520,
                    260)
                : new System.Drawing.Size(
                    1280,
                    720);

        MinimumSize =
            _vehiclePreviewMode
                ? System.Drawing.Size.Empty
                : new System.Drawing.Size(
                    960,
                    540);

        StartPosition =
            FormStartPosition.CenterScreen;
        KeyPreview =
            !_vehiclePreviewMode;

        KeyDown += OnRuntimeKeyDown;
        KeyUp += OnRuntimeKeyUp;
        MouseDown += OnRuntimeMouseDown;
        MouseUp += OnRuntimeMouseUp;
        MouseMove += OnRuntimeMouseMove;
        MouseWheel += OnRuntimeMouseWheel;

        if (_showFps)
        {
            _fpsLabel =
                new Label
                {
                    AutoSize = true,
                    BackColor =
                        System.Drawing.Color.Black,
                    ForeColor =
                        System.Drawing.Color.White,
                    Font =
                        new System.Drawing.Font(
                            "Segoe UI",
                            10.0f,
                            System.Drawing.FontStyle.Bold),
                    Padding =
                        new Padding(
                            7,
                            4,
                            7,
                            4),
                    Location =
                        new System.Drawing.Point(
                            12,
                            12),
                    Text =
                        "FPS --  |  --.- ms\n1% -- FPS · max --.- ms\nMSAA -- · Sharp --"
                };

            Controls.Add(
                _fpsLabel);
        }

        _omsiMenuBar =
            _vehiclePreviewMode
                ? null
                : new RuntimeOmsiMenuBar();

        if (_omsiMenuBar is not null)
        {
            _omsiMenuBar.Anchor =
                AnchorStyles.Right |
                AnchorStyles.Bottom;
            _omsiMenuBar.CommandInvoked +=
                OnOmsiMenuCommandInvoked;

            Controls.Add(
                _omsiMenuBar);

            PerformLayout();
            LayoutOmsiMenuBar();
            SyncOmsiMenuState();
        }

        _busSelectorPanel =
            _vehiclePreviewMode
                ? null
                : new RuntimeBusSelectorPanel(
                    windowInfo.ContentRoot);

        if (_busSelectorPanel is not null)
        {
            _busSelectorPanel.SelectionConfirmed +=
                OnRuntimeBusSelectionConfirmed;

            Controls.Add(
                _busSelectorPanel);

            LayoutRuntimeBusSelector();
        }

        _renderTimer = new System.Windows.Forms.Timer
        {
            Interval =
                Math.Max(
                    1,
                    (int)Math.Round(
                        1000.0 /
                        Math.Clamp(
                            targetFps,
                            10,
                            240)))
        };
        _renderTimer.Tick += RenderTimerOnTick;

        Shown += OnWindowShown;
        ClientSizeChanged += OnClientSizeChanged;
    }

    public static int WarmTextureFileCache(
        IEnumerable<string> paths) =>
        RuntimeGpuTextureLoader.WarmFileCache(
            paths);

    public static int WarmDecodedTextureCache(
        IEnumerable<string> paths) =>
        RuntimeGpuTextureLoader.WarmDecodedCache(
            paths);

    public static string GetTextureFileCacheDiagnostics() =>
        RuntimeGpuTextureLoader.GetFileCacheDiagnostics();

    public RuntimeTrafficObstacleInfo?
        PlayerTrafficObstacle
    {
        get
        {
            if (!_driveMode ||
                _vehicleRemoved ||
                _windowInfo.Vehicle is null)
            {
                return null;
            }

            var wheelBase =
                _windowInfo.Vehicle
                    .Physics
                    .WheelBaseMeters ??
                6.0;

            var trackWidth =
                _windowInfo.Vehicle
                    .Physics
                    .TrackWidthMeters ??
                2.4;

            var sectionAllowance =
                (_windowInfo.Vehicle.Sections?
                     .Count ??
                 0) >
                    0
                    ? 1.75
                    : 0.0;

            var halfLength =
                Math.Clamp(
                    wheelBase *
                        0.5 +
                    2.8 +
                    sectionAllowance,
                    4.5,
                    12.0);

            var halfWidth =
                Math.Clamp(
                    trackWidth *
                        0.5 +
                    0.25,
                    1.15,
                    1.75);

            return new RuntimeTrafficObstacleInfo(
                _vehicle.Position.X,
                _vehicle.Position.Y,
                _vehicle.Position.Z,
                _vehicle.HeadingRadians,
                _vehicle.SpeedMetersPerSecond,
                halfLength,
                halfWidth);
        }
    }

    public void ApplyStreamedWorld(
        RuntimeWindowInfo windowInfo)
    {
        _ =
            ApplyStreamedWorldAsync(
                windowInfo);
    }

    public async Task ApplyStreamedWorldAsync(
        RuntimeWindowInfo windowInfo)
    {
        ArgumentNullException.ThrowIfNull(
            windowInfo);

        if (IsDisposed)
        {
            return;
        }

        var generation =
            Interlocked.Increment(
                ref _streamedWorldPreparationGeneration);

        await PrepareAndApplyStreamedWorldAsync(
            windowInfo,
            generation);
    }

    private async Task PrepareAndApplyStreamedWorldAsync(
        RuntimeWindowInfo windowInfo,
        int generation)
    {
        PreparedStreamedGeometry prepared;
        PreparedStreamedGpuResources? preparedGpu =
            null;

        var missingTrafficVehicleAssets =
            windowInfo.TrafficVehicleAssets?
                .Where(
                    pair =>
                        !_trafficVehicleVertexBuffers.ContainsKey(
                            pair.Key))
                .ToArray()
            ?? Array.Empty<
                KeyValuePair<
                    string,
                    RuntimeVehicleInfo>>();

        var started =
            Stopwatch.GetTimestamp();

        try
        {
            var tileWorkTask =
                Task.Run(
                    () =>
                        BuildStreamedTileWork(
                            windowInfo));

            var tileVerticesTask =
                Task.Run(
                    () =>
                        BuildTileVertices(
                            windowInfo.Tiles));

            var terrainTask =
                Task.Run(
                    () =>
                        RuntimeTerrainGeometryBuilder.Build(
                            windowInfo.Tiles,
                            windowInfo.GroundTextures,
                            windowInfo.Splines));

            var splinesTask =
                Task.Run(
                    () =>
                        RuntimeSplineGeometryBuilder.Build(
                            windowInfo.Splines));

            var objectsTask =
                Task.Run(
                    () =>
                        RuntimeObjectGeometryBuilder.Build(
                            windowInfo.Tiles,
                            windowInfo.Objects,
                            windowInfo.SceneryAssets,
                            useNativeOmsiModelSpace:
                                true,
                            isolatedObjectIds:
                                windowInfo.DynamicSceneryObjectIds));

            var terrainSamplerTask =
                Task.Run(
                    () =>
                        new RuntimeTerrainSampler(
                            windowInfo.Tiles,
                            windowInfo.Splines));

            var collisionScenerySurfaceSamplerTask =
                Task.Run(
                    () =>
                        RuntimeSplineSurfaceSampler.CreateCollisionSurfaceObjects(
                            windowInfo));

            var trafficVehicleGeometriesTask =
                Task.Run(
                    () =>
                        missingTrafficVehicleAssets
                            .AsParallel()
                            .Select(
                                pair =>
                                    new PreparedTrafficVehicleGeometry(
                                        pair.Key,
                                        RuntimeVehicleGeometry.Build(
                                            pair.Value,
                                            viewpointBit:
                                                4,
                                            forceMaterialAlphaOpaque:
                                                true)))
                            .Where(
                                static item =>
                                    item.Geometry.Vertices.Length >
                                        0)
                            .ToArray());

            await Task.WhenAll(
                    tileWorkTask,
                    tileVerticesTask,
                    terrainTask,
                    splinesTask,
                    objectsTask,
                    terrainSamplerTask,
                    collisionScenerySurfaceSamplerTask,
                    trafficVehicleGeometriesTask)
                .ConfigureAwait(false);

            var terrain =
                await terrainTask.ConfigureAwait(false);

            var splines =
                await splinesTask.ConfigureAwait(false);

            var objects =
                await objectsTask.ConfigureAwait(false);

            var terrainSampler =
                await terrainSamplerTask.ConfigureAwait(false);

            var dependentSamplersTask =
                Task.Run(
                    () =>
                        (
                            Spline:
                                RuntimeSplineSurfaceSampler.Create(
                                    splines),
                            Scenery:
                                RuntimeSplineSurfaceSampler.CreateSurfaceObjects(
                                    objects),
                            CollisionVolumes:
                                BuildSceneryCollisionVolumes(
                                    windowInfo,
                                    terrainSampler),
                            TexturePaths:
                                CollectStreamingTexturePaths(
                                    terrain,
                                    splines,
                                    objects)
                        ));

            var dependents =
                await dependentSamplersTask.ConfigureAwait(false);

            prepared =
                new PreparedStreamedGeometry(
                    await tileVerticesTask.ConfigureAwait(false),
                    terrain,
                    splines,
                    objects,
                    terrainSampler,
                    dependents.Spline,
                    dependents.Scenery,
                    await collisionScenerySurfaceSamplerTask.ConfigureAwait(false),
                    dependents.CollisionVolumes,
                    dependents.TexturePaths.RegularTexturePaths,
                    dependents.TexturePaths.MaskTexturePaths,
                    await trafficVehicleGeometriesTask.ConfigureAwait(false),
                    await tileWorkTask.ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[streaming-geometry] prepare failed: {exception.Message}");
            return;
        }

        if (generation !=
                Volatile.Read(
                    ref _streamedWorldPreparationGeneration) ||
            IsDisposed)
        {
            return;
        }

        var elapsed =
            Stopwatch.GetElapsedTime(
                started);

        if (prepared.TileWork.Length >
            0)
        {
            var maximumObjects =
                prepared.TileWork.Max(
                    static tile =>
                        tile.Objects.Length);

            var maximumSplines =
                prepared.TileWork.Max(
                    static tile =>
                        tile.Splines.Length);

            var terrainTiles =
                prepared.TileWork.Count(
                    static tile =>
                        tile.Tile.Terrain is
                        {
                            CellCount: > 0,
                            Heights.Count: > 0
                        });

            Console.WriteLine(
                $"[streaming-tiles] planned={prepared.TileWork.Length:N0}; terrain={terrainTiles:N0}; maxObjects={maximumObjects:N0}; maxSplines={maximumSplines:N0}");
        }

        var gpuPrepareStarted =
            Stopwatch.GetTimestamp();

        try
        {
            preparedGpu =
                await PrepareStreamedGpuResourcesAsync(
                        prepared,
                        generation)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            preparedGpu?.Dispose();
            return;
        }
        catch (Exception exception)
        {
            preparedGpu?.Dispose();

            Console.Error.WriteLine(
                $"[streaming-geometry] GPU prepare failed: {exception.Message}");

            return;
        }

        if (generation !=
                Volatile.Read(
                    ref _streamedWorldPreparationGeneration) ||
            IsDisposed)
        {
            preparedGpu.Dispose();
            return;
        }

        var gpuPrepareElapsed =
            Stopwatch.GetElapsedTime(
                gpuPrepareStarted);

        _lastStreamingPrepareMilliseconds =
            elapsed.TotalMilliseconds;

        _lastStreamingGpuPrepareMilliseconds =
            gpuPrepareElapsed.TotalMilliseconds;

        Console.WriteLine(
            $"[streaming-geometry] prepared generation={generation}; tiles={windowInfo.Tiles.Count}; terrainVertices={prepared.Terrain.Vertices.Length:N0}; splineVertices={prepared.Splines.Vertices.Length:N0}; objectVertices={prepared.Objects.Vertices.Length:N0}; cpuMs={_lastStreamingPrepareMilliseconds:0.0}; gpuPrepareMs={_lastStreamingGpuPrepareMilliseconds:0.0}");

        if (!InvokeRequired)
        {
            ApplyPreparedStreamedWorld(
                windowInfo,
                prepared,
                preparedGpu,
                generation);

            return;
        }

        var completion =
            new TaskCompletionSource<bool>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        try
        {
            BeginInvoke(
                () =>
                {
                    try
                    {
                        ApplyPreparedStreamedWorld(
                            windowInfo,
                            prepared,
                            preparedGpu,
                            generation);

                        completion.TrySetResult(
                            true);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(
                            exception);
                    }
                });
        }
        catch (ObjectDisposedException)
        {
            preparedGpu.Dispose();
            return;
        }
        catch (InvalidOperationException)
        {
            preparedGpu.Dispose();

            // Window can close while a background geometry build is finishing.
            return;
        }

        try
        {
            await completion.Task;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ApplyPreparedStreamedWorld(
        RuntimeWindowInfo windowInfo,
        PreparedStreamedGeometry prepared,
        PreparedStreamedGpuResources preparedGpu,
        int generation)
    {
        if (IsDisposed ||
            generation !=
                Volatile.Read(
                    ref _streamedWorldPreparationGeneration))
        {
            preparedGpu.Dispose();
            return;
        }

        _windowInfo =
            windowInfo;

        RebuildRuntimeTrafficSegmentLookup();

        _terrainSurfaceSampler =
            prepared.TerrainSampler;

        _sceneryCollisionVolumes =
            prepared.CollisionVolumes;

        _activeSceneryCollisionVolumes.Clear();

        if (_device is null)
        {
            preparedGpu.Dispose();
            return;
        }

        var uploadStarted =
            Stopwatch.GetTimestamp();

        var trafficStarted =
            Stopwatch.GetTimestamp();

        ApplyPreparedTrafficVehicleResources(
            preparedGpu);

        var trafficMilliseconds =
            Stopwatch.GetElapsedTime(
                trafficStarted)
                .TotalMilliseconds;

        var geometryStarted =
            Stopwatch.GetTimestamp();

        ApplyPreparedStreamedGeometry(
            prepared,
            preparedGpu);

        var geometryMilliseconds =
            Stopwatch.GetElapsedTime(
                geometryStarted)
                .TotalMilliseconds;

        var textureCacheStarted =
            Stopwatch.GetTimestamp();

        RefreshStreamingTextureCache(
            prepared);

        var textureCacheMilliseconds =
            Stopwatch.GetElapsedTime(
                textureCacheStarted)
                .TotalMilliseconds;

        var captionStarted =
            Stopwatch.GetTimestamp();

        UpdateCaption();

        var captionMilliseconds =
            Stopwatch.GetElapsedTime(
                captionStarted)
                .TotalMilliseconds;

        var uploadElapsed =
            Stopwatch.GetElapsedTime(
                uploadStarted);

        _lastStreamingSwapMilliseconds =
            uploadElapsed.TotalMilliseconds;

        Console.WriteLine(
            $"[streaming-geometry] applied generation={generation}; swapMs={_lastStreamingSwapMilliseconds:0.0}; trafficMs={trafficMilliseconds:0.0}; geometryMs={geometryMilliseconds:0.0}; textureCacheMs={textureCacheMilliseconds:0.0}; captionMs={captionMilliseconds:0.0}");
    }

    private static PreparedStreamedTileWork[] BuildStreamedTileWork(
        RuntimeWindowInfo windowInfo)
    {
        var splinesByTile =
            windowInfo.Splines
                .GroupBy(
                    static spline =>
                        (spline.TileX, spline.TileY))
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.ToArray());

        var objectsByTile =
            windowInfo.Objects
                .GroupBy(
                    static item =>
                        (item.TileX, item.TileY))
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.ToArray());

        return windowInfo.Tiles
            .Select(
                tile =>
                {
                    splinesByTile.TryGetValue(
                        (tile.X, tile.Y),
                        out var tileSplines);

                    objectsByTile.TryGetValue(
                        (tile.X, tile.Y),
                        out var tileObjects);

                    var resolvedSplines =
                        tileSplines ??
                        Array.Empty<RuntimeSplineInfo>();

                    var resolvedObjects =
                        tileObjects ??
                        Array.Empty<RuntimeObjectInfo>();

                    var regularTexturePaths =
                        CollectTileRegularTexturePaths(
                            tile,
                            resolvedSplines,
                            resolvedObjects,
                            windowInfo);

                    var maskTexturePaths =
                        tile.TerrainMasks
                            .Select(
                                static mask =>
                                    mask.Path)
                            .Where(
                                static texturePath =>
                                    !string.IsNullOrWhiteSpace(
                                        texturePath))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                    return new PreparedStreamedTileWork(
                        tile.X,
                        tile.Y,
                        tile,
                        resolvedSplines,
                        resolvedObjects,
                        regularTexturePaths,
                        maskTexturePaths);
                })
            .ToArray();
    }

    private static string[] CollectTileRegularTexturePaths(
        RuntimeTileInfo tile,
        IReadOnlyList<RuntimeSplineInfo> splines,
        IReadOnlyList<RuntimeObjectInfo> objects,
        RuntimeWindowInfo windowInfo)
    {
        var paths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        static void Add(
            ISet<string> destination,
            string? path)
        {
            if (!string.IsNullOrWhiteSpace(
                    path))
            {
                destination.Add(
                    path);
            }
        }

        foreach (var ground in
                 windowInfo.GroundTextures)
        {
            if (ground.LayerIndex ==
                    0 ||
                tile.TerrainMasks.Any(
                    mask =>
                        mask.LayerIndex ==
                        ground.LayerIndex))
            {
                Add(
                    paths,
                    ground.MainTexturePath);

                Add(
                    paths,
                    ground.DetailTexturePath);
            }
        }

        Add(
            paths,
            tile.LightmapPath);

        foreach (var spline in
                 splines)
        {
            foreach (var surface in
                     spline.Surfaces)
            {
                Add(
                    paths,
                    surface.TexturePath);
            }
        }

        foreach (var item in
                 objects)
        {
            if (!windowInfo.SceneryAssets.TryGetValue(
                    item.AssetPath,
                    out var asset))
            {
                continue;
            }

            Add(
                paths,
                asset.Tree?.TexturePath);

            foreach (var mesh in
                     asset.Meshes)
            {
                foreach (var material in
                         mesh.Materials)
                {
                    Add(
                        paths,
                        material.TexturePath);
                    Add(
                        paths,
                        material.TransMapTexturePath);
                    Add(
                        paths,
                        material.LightMapTexturePath);
                    Add(
                        paths,
                        material.MaterialChangeTexturePath);
                    Add(
                        paths,
                        material.EnvMapTexturePath);
                    Add(
                        paths,
                        material.EnvMapMaskTexturePath);
                    Add(
                        paths,
                        material.BumpMapTexturePath);
                }
            }
        }

        return paths.ToArray();
    }

    private static (
        string[] RegularTexturePaths,
        string[] MaskTexturePaths)
        CollectStreamingTexturePaths(
            RuntimeTerrainGeometry terrain,
            RuntimeSplineGeometry splines,
            RuntimeObjectGeometry objects)
    {
        var regularTexturePaths =
            objects.Batches
                .Concat(
                    splines.Batches)
                .SelectMany(
                    static batch =>
                        new[]
                        {
                            batch.TexturePath,
                            batch.TransMapTexturePath,
                            batch.LightMapTexturePath,
                            batch.MaterialChangeTexturePath
                        })
                .Concat(
                    terrain.Batches
                        .SelectMany(
                            static batch =>
                                new[]
                                {
                                    batch.TexturePath,
                                    batch.DetailTexturePath
                                }))
                .Where(
                    static texturePath =>
                        !string.IsNullOrWhiteSpace(
                            texturePath))
                .Select(
                    static texturePath =>
                        texturePath!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var maskTexturePaths =
            terrain.Batches
                .Select(
                    static batch =>
                        batch.MaskTexturePath)
                .Where(
                    static texturePath =>
                        !string.IsNullOrWhiteSpace(
                            texturePath))
                .Select(
                    static texturePath =>
                        texturePath!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return (
            regularTexturePaths,
            maskTexturePaths);
    }

    private async Task<PreparedStreamedGpuResources> PrepareStreamedGpuResourcesAsync(
        PreparedStreamedGeometry prepared,
        int generation)
    {
        if (_device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is unavailable.");
        }

        var resources =
            new PreparedStreamedGpuResources();

        try
        {
            void ThrowIfStreamingSuperseded()
            {
                if (IsDisposed ||
                    generation !=
                        Volatile.Read(
                            ref _streamedWorldPreparationGeneration))
                {
                    throw new OperationCanceledException(
                        "Streamed GPU preparation was superseded.");
                }
            }

            async Task PaceNextUploadAsync(
                long frameBeforeUpload)
            {
                // D3D11 resource creation is legal off the UI thread, but a burst
                // of large CreateBuffer calls can still serialize in the driver
                // against rendering. Give the render loop a chance to present a
                // frame between logical streamed-world uploads. If rendering is
                // temporarily paused/minimized, cap the wait so streaming cannot
                // deadlock behind the presentation cadence.
                for (var wait = 0;
                     wait < 8 &&
                     !IsDisposed &&
                     Volatile.Read(
                         ref _renderFrameSequence) <=
                         frameBeforeUpload;
                     wait++)
                {
                    await Task.Delay(1)
                        .ConfigureAwait(false);
                }

                ThrowIfStreamingSuperseded();
            }

            ThrowIfStreamingSuperseded();

            if (prepared.TileVertices.Length >
                0)
            {
                var frameBeforeUpload =
                    Volatile.Read(
                        ref _renderFrameSequence);

                resources.TileVertexBuffer =
                    await Task.Run(
                            () =>
                                _device.CreateBuffer(
                                    prepared.TileVertices.AsSpan(),
                                    BindFlags.VertexBuffer))
                        .ConfigureAwait(false);

                await PaceNextUploadAsync(
                        frameBeforeUpload)
                    .ConfigureAwait(false);
            }

            ThrowIfStreamingSuperseded();

            if (prepared.Terrain.Vertices.Length >
                0)
            {
                var frameBeforeUpload =
                    Volatile.Read(
                        ref _renderFrameSequence);

                resources.TerrainVertexBuffer =
                    await Task.Run(
                            () =>
                                _device.CreateBuffer(
                                    prepared.Terrain.Vertices.AsSpan(),
                                    BindFlags.VertexBuffer))
                        .ConfigureAwait(false);

                await PaceNextUploadAsync(
                        frameBeforeUpload)
                    .ConfigureAwait(false);
            }

            ThrowIfStreamingSuperseded();

            if (prepared.Splines.Vertices.Length >
                0)
            {
                var frameBeforeUpload =
                    Volatile.Read(
                        ref _renderFrameSequence);

                resources.SplineVertexBuffer =
                    await Task.Run(
                            () =>
                                _device.CreateBuffer(
                                    prepared.Splines.Vertices.AsSpan(),
                                    BindFlags.VertexBuffer))
                        .ConfigureAwait(false);

                await PaceNextUploadAsync(
                        frameBeforeUpload)
                    .ConfigureAwait(false);
            }

            ThrowIfStreamingSuperseded();

            if (prepared.Objects.Vertices.Length >
                0)
            {
                var frameBeforeUpload =
                    Volatile.Read(
                        ref _renderFrameSequence);

                resources.ObjectVertexBuffer =
                    await Task.Run(
                            () =>
                                _device.CreateBuffer(
                                    prepared.Objects.Vertices.AsSpan(),
                                    BindFlags.VertexBuffer))
                        .ConfigureAwait(false);

                await PaceNextUploadAsync(
                        frameBeforeUpload)
                    .ConfigureAwait(false);
            }

            ThrowIfStreamingSuperseded();

            foreach (var trafficVehicle in
                     prepared.TrafficVehicleGeometries)
            {
                var frameBeforeUpload =
                    Volatile.Read(
                        ref _renderFrameSequence);

                var buffer =
                    await Task.Run(
                            () =>
                                _device.CreateBuffer(
                                    trafficVehicle.Geometry.Vertices.AsSpan(),
                                    BindFlags.VertexBuffer))
                        .ConfigureAwait(false);

                resources.TrafficVehicles.Add(
                    new PreparedTrafficVehicleGpuResource(
                        trafficVehicle.Path,
                        trafficVehicle.Geometry,
                        buffer));

                await PaceNextUploadAsync(
                        frameBeforeUpload)
                    .ConfigureAwait(false);
            }

            return resources;
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private void ApplyPreparedTrafficVehicleResources(
        PreparedStreamedGpuResources preparedGpu)
    {
        foreach (var resource in
                 preparedGpu.TrafficVehicles)
        {
            if (_trafficVehicleVertexBuffers.ContainsKey(
                    resource.Path))
            {
                resource.Dispose();
                continue;
            }

            if (resource.Buffer is null)
            {
                continue;
            }

            _trafficVehicleGeometries[
                resource.Path] =
                resource.Geometry;

            _trafficVehicleRenderBatches[
                resource.Path] =
                BuildTrafficVehicleRenderBatches(
                    resource.Geometry);

            _trafficVehicleVertexBuffers[
                resource.Path] =
                resource.Buffer;

            resource.Buffer =
                null;

            Console.WriteLine(
                $"[traffic-ai] staged GPU vehicle={Path.GetFileName(resource.Path)}; vertices={resource.Geometry.Vertices.Length}; meshes={resource.Geometry.RenderedMeshCount}");
        }
    }

    private void RetireStreamingVertexBuffer(
        ID3D11Buffer? buffer)
    {
        if (buffer is null)
        {
            return;
        }

        _retiredStreamingVertexBuffers.Enqueue(
            (
                buffer,
                _renderFrameSequence +
                    StreamingBufferRetirementFrames));
    }

    private void ReleaseRetiredStreamingVertexBuffers()
    {
        while (_retiredStreamingVertexBuffers.Count >
                   0 &&
               _retiredStreamingVertexBuffers.Peek()
                   .ReleaseAfterFrame <=
               _renderFrameSequence)
        {
            var retired =
                _retiredStreamingVertexBuffers.Dequeue();

            retired.Buffer.Dispose();
        }

        while (_retiredStreamingTextures.Count >
                   0 &&
               _retiredStreamingTextures.Peek()
                   .ReleaseAfterFrame <=
               _renderFrameSequence)
        {
            var retired =
                _retiredStreamingTextures.Dequeue();

            retired.Texture.Dispose();
        }
    }

    private void RetireStreamingTexture(
        RuntimeGpuTexture? texture)
    {
        if (texture is null)
        {
            return;
        }

        _retiredStreamingTextures.Enqueue(
            (
                texture,
                _renderFrameSequence +
                    StreamingBufferRetirementFrames));
    }

    private void ApplyPreparedStreamedGeometry(
        PreparedStreamedGeometry prepared,
        PreparedStreamedGpuResources preparedGpu)
    {
        RetireStreamingVertexBuffer(
            _tileVertexBuffer);
        _tileVertexBuffer =
            preparedGpu.TileVertexBuffer;
        preparedGpu.TileVertexBuffer =
            null;

        _tileVertexCount =
            (uint)prepared.TileVertices.Length;

        RetireStreamingVertexBuffer(
            _terrainVertexBuffer);
        _terrainVertexBuffer =
            preparedGpu.TerrainVertexBuffer;
        preparedGpu.TerrainVertexBuffer =
            null;

        _terrainGeometry =
            prepared.Terrain;

        _terrainVertexCount =
            (uint)_terrainGeometry.Vertices.Length;

        AppendTerrainAlignmentDiagnostics();

        RetireStreamingVertexBuffer(
            _splineVertexBuffer);
        _splineVertexBuffer =
            preparedGpu.SplineVertexBuffer;
        preparedGpu.SplineVertexBuffer =
            null;

        _splineGeometry =
            prepared.Splines;

        _splineVertexCount =
            (uint)_splineGeometry.Vertices.Length;

        RetireStreamingVertexBuffer(
            _objectVertexBuffer);
        _objectVertexBuffer =
            preparedGpu.ObjectVertexBuffer;
        preparedGpu.ObjectVertexBuffer =
            null;

        _objectGeometry =
            prepared.Objects;

        RebuildObjectRenderPassBatches();

        _objectVertexCount =
            (uint)_objectGeometry.Vertices.Length;

        _vehicle.ReplacePreparedDrivingSurfaces(
            prepared.TerrainSampler,
            prepared.SplineSurfaceSampler,
            prepared.ScenerySurfaceSampler,
            prepared.CollisionScenerySurfaceSampler);

        preparedGpu.Dispose();
    }

    private void RefreshStreamingTextureCache(
        PreparedStreamedGeometry prepared)
    {
        if (_device is null)
        {
            return;
        }

        _objectTextureLoader ??=
            new RuntimeGpuTextureLoader(
                _device,
                _deviceContext);

        var regularPaths =
            new HashSet<string>(
                prepared.RegularTexturePaths,
                StringComparer.OrdinalIgnoreCase);

        regularPaths.UnionWith(
            _pinnedVehicleTexturePaths);

        var maskPaths =
            new HashSet<string>(
                prepared.MaskTexturePaths,
                StringComparer.OrdinalIgnoreCase);

        var requiredPaths =
            new HashSet<string>(
                regularPaths,
                StringComparer.OrdinalIgnoreCase);

        requiredPaths.UnionWith(
            maskPaths);

        _streamingTextureGeneration++;

        foreach (var requiredPath in
                 requiredPaths)
        {
            if (_objectTextureCache.ContainsKey(
                    requiredPath))
            {
                _objectTextureLastUsedGeneration[
                    requiredPath] =
                    _streamingTextureGeneration;
            }
        }

        var inactiveTextureCount =
            0;
        long inactiveGpuBytes =
            0;
        long cachedGpuBytes =
            0;

        foreach (var pair in
                 _objectTextureCache)
        {
            cachedGpuBytes +=
                pair.Value.ApproximateBytes;

            if (requiredPaths.Contains(
                    pair.Key))
            {
                continue;
            }

            inactiveTextureCount++;
            inactiveGpuBytes +=
                pair.Value.ApproximateBytes;
        }

        while (inactiveTextureCount >
                   MaximumInactiveStreamingTextureCacheEntries ||
               inactiveGpuBytes >
                   _maximumInactiveStreamingTextureCacheBytes ||
               cachedGpuBytes >
                   _maximumStreamingTextureCacheBytes)
        {
            string? oldestPath =
                null;
            long oldestGeneration =
                long.MaxValue;

            foreach (var pair in
                     _objectTextureCache)
            {
                if (requiredPaths.Contains(
                        pair.Key))
                {
                    continue;
                }

                var generation =
                    _objectTextureLastUsedGeneration
                        .TryGetValue(
                            pair.Key,
                            out var value)
                            ? value
                            : long.MinValue;

                if (oldestPath is null ||
                    generation <
                        oldestGeneration)
                {
                    oldestPath =
                        pair.Key;
                    oldestGeneration =
                        generation;
                }
            }

            if (oldestPath is null ||
                !_objectTextureCache.TryGetValue(
                    oldestPath,
                    out var cachedTexture))
            {
                break;
            }

            inactiveTextureCount--;
            inactiveGpuBytes -=
                cachedTexture.ApproximateBytes;
            cachedGpuBytes -=
                cachedTexture.ApproximateBytes;

            RetireStreamingTexture(
                cachedTexture);

            _objectTextureCache.Remove(
                oldestPath);

            _objectTextureLastUsedGeneration.Remove(
                oldestPath);
        }

        _currentStreamingTextureCacheBytes =
            Math.Max(
                0L,
                cachedGpuBytes);

        Console.WriteLine(
            $"[streaming-textures] cache entries={_objectTextureCache.Count:N0}; gpuMB={Math.Max(0L, cachedGpuBytes) / (1024.0 * 1024.0):0.0}/{_maximumStreamingTextureCacheBytes / (1024.0 * 1024.0):0}; inactive={Math.Max(0, inactiveTextureCount):N0}/{MaximumInactiveStreamingTextureCacheEntries:N0}; inactiveGpuMB={Math.Max(0L, inactiveGpuBytes) / (1024.0 * 1024.0):0.0}/{_maximumInactiveStreamingTextureCacheBytes / (1024.0 * 1024.0):0}");

        _failedObjectTexturePaths.IntersectWith(
            requiredPaths);

        _pendingStreamingTextureLoads.Clear();
        _pendingStreamingTexturePaths.Clear();

        void QueueTexture(
            string texturePath,
            bool alphaMask)
        {
            if (_objectTextureCache.ContainsKey(
                    texturePath) ||
                !_pendingStreamingTexturePaths.Add(
                    texturePath))
            {
                return;
            }

            _pendingStreamingTextureLoads.Enqueue(
                (
                    texturePath,
                    AlphaMask:
                        alphaMask));
        }

        foreach (var pinnedPath in
                 _pinnedVehicleTexturePaths)
        {
            if (regularPaths.Contains(
                    pinnedPath))
            {
                QueueTexture(
                    pinnedPath,
                    alphaMask:
                        false);
            }
        }

        var focusX =
            _streamingTileX ??
            prepared.TileWork.FirstOrDefault()?.X ??
            0;

        var focusY =
            _streamingTileY ??
            prepared.TileWork.FirstOrDefault()?.Y ??
            0;

        foreach (var tile in
                 prepared.TileWork
                     .OrderBy(
                         tile =>
                             Math.Abs(
                                 tile.X -
                                 focusX) +
                             Math.Abs(
                                 tile.Y -
                                 focusY)))
        {
            foreach (var texturePath in
                     tile.RegularTexturePaths)
            {
                if (regularPaths.Contains(
                        texturePath))
                {
                    QueueTexture(
                        texturePath,
                        alphaMask:
                            false);
                }
            }

            foreach (var maskPath in
                     tile.MaskTexturePaths)
            {
                if (maskPaths.Contains(
                        maskPath))
                {
                    QueueTexture(
                        maskPath,
                        alphaMask:
                            true);
                }
            }
        }

        foreach (var texturePath in
                 regularPaths)
        {
            QueueTexture(
                texturePath,
                alphaMask:
                    false);
        }

        foreach (var maskPath in
                 maskPaths)
        {
            QueueTexture(
                maskPath,
                alphaMask:
                    true);
        }

        if (_pendingStreamingTextureLoads.Count >
            0)
        {
            Console.WriteLine(
                $"[streaming-textures] queued {_pendingStreamingTextureLoads.Count:N0} GPU uploads.");
        }
    }

    private void ProcessStreamingTextureLoadQueue()
    {
        if (_device is null ||
            _objectTextureLoader is null ||
            _pendingStreamingTextureLoads.Count ==
                0)
        {
            return;
        }

        var startMilliseconds =
            _frameClock.Elapsed.TotalMilliseconds;

        var pressure =
            _lastObservedFrameMilliseconds >
                    0.0
                ? _lastObservedFrameMilliseconds /
                  Math.Max(
                      _targetFrameMilliseconds,
                      1.0)
                : 1.0;

        if (pressure >=
            1.5)
        {
            _currentStreamingTextureUploadLimit =
                1;
            _currentStreamingTextureUploadBudgetMilliseconds =
                0.5;
        }
        else if (pressure >=
                 1.15)
        {
            _currentStreamingTextureUploadLimit =
                2;
            _currentStreamingTextureUploadBudgetMilliseconds =
                1.0;
        }
        else if (pressure <=
                 0.85)
        {
            _currentStreamingTextureUploadLimit =
                MaximumStreamingTextureUploadsPerFrame;
            _currentStreamingTextureUploadBudgetMilliseconds =
                MaximumStreamingTextureUploadBudgetMilliseconds;
        }
        else
        {
            _currentStreamingTextureUploadLimit =
                3;
            _currentStreamingTextureUploadBudgetMilliseconds =
                1.5;
        }

        var processed =
            0;

        while (_pendingStreamingTextureLoads.Count >
                   0 &&
               processed <
                   _currentStreamingTextureUploadLimit)
        {
            var pending =
                _pendingStreamingTextureLoads.Dequeue();

            _pendingStreamingTexturePaths.Remove(
                pending.Path);

            if (_objectTextureCache.ContainsKey(
                    pending.Path))
            {
                continue;
            }

            var texture =
                pending.AlphaMask
                    ? _objectTextureLoader
                        .TryLoadAlphaMask(
                            pending.Path)
                    : _objectTextureLoader
                        .TryLoad(
                            pending.Path);

            if (texture is not null)
            {
                _objectTextureCache[
                    pending.Path] =
                    texture;

                _currentStreamingTextureCacheBytes +=
                    texture.ApproximateBytes;

                _objectTextureLastUsedGeneration[
                    pending.Path] =
                    _streamingTextureGeneration;

                _failedObjectTexturePaths.Remove(
                    pending.Path);
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    pending.Path);
            }

            processed++;

            if (processed >
                    0 &&
                _frameClock.Elapsed.TotalMilliseconds -
                    startMilliseconds >=
                _currentStreamingTextureUploadBudgetMilliseconds)
            {
                break;
            }
        }
    }

    public void PrepareForDisplay()
    {
        if (_graphicsPrepared)
        {
            return;
        }

        InitializeGraphics();

        // sound.cfg must exist before {init} because OMSI scripts may emit
        // T.L/T.F sound events during initialization.
        InitializeOmsiAudio();
        InitializeVehicleScripts();
        InitializeOmsiGameControllers();
        UpdateCaption();

        _graphicsPrepared =
            true;
    }

    private void InitializeOmsiAudio()
    {
        if (_vehiclePreviewMode ||
            _omsiAudio is not null)
        {
            return;
        }

        _omsiAudio =
            RuntimeOmsiAudioHost.TryCreate(
                _windowInfo.Vehicle?
                    .SoundConfigPath,
                _masterVolume,
                _maximumSoundCount);

        if (_omsiAudio is not null)
        {
            Console.WriteLine(
                $"[audio] lead: {_omsiAudio.ExistingFileCount}/{_omsiAudio.SoundCount} OMSI sound files resolved.");

            try
            {
                var lines =
                    new List<string>
                    {
                        $"timestamp={DateTimeOffset.Now:O}",
                        $"vehicle={_windowInfo.Vehicle?.DisplayName ?? "<none>"}",
                        $"soundConfig={_windowInfo.Vehicle?.SoundConfigPath ?? "<none>"}",
                        ""
                    };

                lines.AddRange(
                    _omsiAudio.BuildConfigurationDiagnostics());

                File.WriteAllLines(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "vehicle-audio-config.log"),
                    lines);
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[audio] unable to write vehicle-audio-config.log: {exception.Message}");
            }
        }

        _articulatedOmsiAudio.Clear();

        foreach (var section in
                 _windowInfo.Vehicle?.Sections ??
                 Array.Empty<RuntimeVehicleSectionInfo>())
        {
            if (string.IsNullOrWhiteSpace(
                    section.SoundConfigPath))
            {
                continue;
            }

            var audio =
                RuntimeOmsiAudioHost.TryCreate(
                    section.SoundConfigPath,
                    _masterVolume,
                    _maximumSoundCount);

            if (audio is null)
            {
                continue;
            }

            _articulatedOmsiAudio[
                section.Index] =
                audio;

            Console.WriteLine(
                $"[audio] section={section.Index}: {audio.ExistingFileCount}/{audio.SoundCount} OMSI sound files resolved from {Path.GetFileName(section.SoundConfigPath)}.");

            try
            {
                var sectionLines =
                    new List<string>
                    {
                        $"timestamp={DateTimeOffset.Now:O}",
                        $"vehicle={_windowInfo.Vehicle?.DisplayName ?? "<none>"}",
                        $"section={section.Index}",
                        $"soundConfig={section.SoundConfigPath}",
                        ""
                    };

                sectionLines.AddRange(
                    audio.BuildConfigurationDiagnostics());

                File.WriteAllLines(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        $"vehicle-audio-section-{section.Index}.log"),
                    sectionLines);
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[audio] unable to write section audio diagnostics: {exception.Message}");
            }
        }
    }

    private void InitializeOmsiGameControllers()
    {
        if (_vehiclePreviewMode ||
            !_gameControllerEnabled ||
            _omsiGameController is not null)
        {
            return;
        }

        _omsiGameController =
            RuntimeOmsiGameControllerHost.TryCreate(
                _windowInfo.ContentRoot,
                Handle);

        if (_omsiGameController is not null)
        {
            Console.WriteLine(
                $"[input] {_omsiGameController.ConnectedDeviceCount} OMSI game controller(s) connected through DirectInput.");
        }
    }

    private void OnWindowShown(object? sender, EventArgs e)
    {
        try
        {
            PrepareForDisplay();

            // Present a complete frame immediately. This avoids exposing
            // the default black WinForms/DXGI surface between Show() and
            // the first timer tick.
            RenderFrame();

            _renderTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Unable to initialize Direct3D 11.\n\n{ex}",
                "OMSI Compatible Runtime",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private void InitializeGraphics()
    {
        _factory = CreateDXGIFactory1<IDXGIFactory2>();

        using var adapter = GetHardwareAdapter(_factory);

        var result = D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            RequestedFeatureLevels,
            out var device,
            out _featureLevel,
            out var context);

        if (result.Failure)
        {
            result = D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                RequestedFeatureLevels,
                out device,
                out _featureLevel,
                out context);
        }

        result.CheckError();

        _device = device;
        _deviceContext = context;

        CreateSwapChain();
        CreateBackBufferResources();
        CreatePostProcessResources();
        CreateSkyResources();
        CreateTileOverviewResources();
        CreateTerrainResources();
        CreateSplineResources();
        CreateObjectResources();
        CreateVehicleResources();
        EnsureTrafficVehicleResources();
    }

    private static IDXGIAdapter1 GetHardwareAdapter(IDXGIFactory2 factory)
    {
        for (uint index = 0;
             factory.EnumAdapters1(index, out var adapter).Success;
             index++)
        {
            if (adapter is null)
            {
                continue;
            }

            if ((adapter.Description1.Flags & AdapterFlags.Software) ==
                AdapterFlags.None)
            {
                return adapter;
            }

            adapter.Dispose();
        }

        throw new InvalidOperationException(
            "No Direct3D 11 hardware adapter was found.");
    }

    private void CreateSwapChain()
    {
        if (_factory is null || _device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is not initialized.");
        }

        var description = new SwapChainDescription1
        {
            Width = (uint)Math.Max(ClientSize.Width, 1),
            Height = (uint)Math.Max(ClientSize.Height, 1),
            Format = Format.R8G8B8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = SampleDescription.Default,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore
        };

        var fullscreenDescription = new SwapChainFullscreenDescription
        {
            Windowed = true
        };

        _swapChain = _factory.CreateSwapChainForHwnd(
            _device,
            Handle,
            description,
            fullscreenDescription);

        _factory.MakeWindowAssociation(
            Handle,
            WindowAssociationFlags.IgnoreAltEnter);
    }

    private void CreateBackBufferResources()
    {
        if (_swapChain is null ||
            _device is null)
        {
            return;
        }

        var width =
            (uint)Math.Max(
                ClientSize.Width,
                1);

        var height =
            (uint)Math.Max(
                ClientSize.Height,
                1);

        _backBuffer =
            _swapChain.GetBuffer<
                ID3D11Texture2D>(
                    0);

        var usePostProcess =
            _sharpenStrength >
            0.0001f;

        if (usePostProcess)
        {
            _postProcessTexture =
                _device.CreateTexture2D(
                    Format.R8G8B8A8_UNorm,
                    width,
                    height,
                    mipLevels:
                        1,
                    bindFlags:
                        BindFlags.RenderTarget |
                        BindFlags.ShaderResource);

            _postProcessShaderResourceView =
                _device.CreateShaderResourceView(
                    _postProcessTexture);

            _postProcessBackBufferView =
                _device.CreateRenderTargetView(
                    _backBuffer);
        }

        _activeMsaaSamples =
            0;

        if (_requestedMsaaSamples >=
            2)
        {
            foreach (var sampleCount in
                     _requestedMsaaSamples >=
                             4
                         ? new[]
                           {
                               4,
                               2
                           }
                         : new[]
                           {
                               2
                           })
            {
                try
                {
                    _multisampleColorTexture =
                        _device
                            .CreateTexture2DMultisample(
                                Format.R8G8B8A8_UNorm,
                                width,
                                height,
                                (uint)sampleCount,
                                bindFlags:
                                    BindFlags.RenderTarget);

                    _depthTexture =
                        _device
                            .CreateTexture2DMultisample(
                                Format.D32_Float,
                                width,
                                height,
                                (uint)sampleCount,
                                bindFlags:
                                    BindFlags.DepthStencil);

                    _renderTargetView =
                        _device.CreateRenderTargetView(
                            _multisampleColorTexture);

                    _depthStencilView =
                        _device.CreateDepthStencilView(
                            _depthTexture);

                    _activeMsaaSamples =
                        sampleCount;

                    Console.WriteLine(
                        $"[graphics] MSAA {sampleCount}x enabled.");

                    break;
                }
                catch (Exception exception)
                {
                    _renderTargetView?.Dispose();
                    _renderTargetView =
                        null;

                    _depthStencilView?.Dispose();
                    _depthStencilView =
                        null;

                    _depthTexture?.Dispose();
                    _depthTexture =
                        null;

                    _multisampleColorTexture?.Dispose();
                    _multisampleColorTexture =
                        null;

                    Console.WriteLine(
                        $"[graphics] MSAA {sampleCount}x unavailable: {exception.Message}");
                }
            }
        }

        if (_activeMsaaSamples ==
            0)
        {
            _renderTargetView =
                usePostProcess &&
                _postProcessTexture is not null
                    ? _device.CreateRenderTargetView(
                        _postProcessTexture)
                    : _device.CreateRenderTargetView(
                        _backBuffer);

            _depthTexture =
                _device.CreateTexture2D(
                    Format.D32_Float,
                    width,
                    height,
                    mipLevels:
                        1,
                    bindFlags:
                        BindFlags.DepthStencil);

            _depthStencilView =
                _device.CreateDepthStencilView(
                    _depthTexture);

            if (_requestedMsaaSamples >
                0)
            {
                Console.WriteLine(
                    "[graphics] MSAA unavailable; using single-sample rendering.");
            }
        }
    }

    private void CreatePostProcessResources()
    {
        if (_device is null ||
            _sharpenStrength <=
                0.0001f)
        {
            return;
        }

        var shaderFile =
            ShaderPath(
                "RuntimePostProcess.hlsl");

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMain",
                "vs_4_0");

        ReadOnlyMemory<byte> pixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSMain",
                "ps_4_0");

        _postProcessVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);

        _postProcessPixelShader =
            _device.CreatePixelShader(
                pixelShaderByteCode.Span);

        _postProcessSampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearClamp);

        _postProcessConstantsBuffer =
            _device.CreateConstantBuffer<
                RuntimePostProcessConstants>();
    }

    private void CreateSkyResources()
    {
        if (_device is null)
        {
            return;
        }

        var shaderFile =
            ShaderPath(
                "RuntimeSky.hlsl");

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMain",
                "vs_4_0");

        ReadOnlyMemory<byte> pixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSMain",
                "ps_4_0");

        _skyVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);

        _skyPixelShader =
            _device.CreatePixelShader(
                pixelShaderByteCode.Span);

        _skySampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearWrap);

        _skyConstantsBuffer =
            _device.CreateConstantBuffer<
                RuntimeSkyConstants>();

        _objectTextureLoader ??=
            new RuntimeGpuTextureLoader(
                _device,
                _deviceContext);

        var skyPath =
            Path.Combine(
                _windowInfo.ContentRoot,
                "Texture",
                "himmel01.bmp");

        _skyTexture =
            _objectTextureLoader.TryLoad(
                skyPath);
    }

    private Vector4 ResolveSkyViewParameters()
    {
        float yaw;
        float pitch;
        float verticalFieldOfViewRadians;

        var aspect =
            Math.Max(
                ClientSize.Width,
                1) /
            (float)Math.Max(
                ClientSize.Height,
                1);

        if (_vehiclePreviewMode)
        {
            yaw =
                _previewYaw +
                MathF.PI;
            pitch =
                -_previewPitch;
            verticalFieldOfViewRadians =
                MathF.PI /
                4.0f;
        }
        else if (!_driveMode)
        {
            yaw =
                _camera.Yaw;
            pitch =
                _camera.Pitch;
            verticalFieldOfViewRadians =
                MathF.PI /
                3.0f;
        }
        else
        {
            var vehicle =
                _windowInfo.Vehicle;

            if (_vehicleViewMode ==
                    RuntimeVehicleViewMode.Driver &&
                vehicle?.DriverCameras.Count > 0)
            {
                var index =
                    Math.Clamp(
                        _driverCameraIndex,
                        0,
                        vehicle.DriverCameras.Count - 1);

                var camera =
                    vehicle.DriverCameras[index];

                yaw =
                    _vehicle.HeadingRadians +
                    DegreesToRadians(
                        camera.HeadingDegrees) +
                    _interiorCameraYawOffsetRadians;

                pitch =
                    DegreesToRadians(
                        camera.PitchDegrees) +
                    _interiorCameraPitchOffsetRadians;

                verticalFieldOfViewRadians =
                    DegreesToRadians(
                        Math.Clamp(
                            camera.FieldOfViewDegrees *
                            Math.Clamp(
                                _interiorCameraFieldOfViewScale,
                                0.35f,
                                2.0f),
                            18.0,
                            120.0));
            }
            else if (_vehicleViewMode ==
                         RuntimeVehicleViewMode.Passenger &&
                     vehicle?.PassengerCameras.Count > 0)
            {
                var index =
                    Math.Clamp(
                        _passengerCameraIndex,
                        0,
                        vehicle.PassengerCameras.Count - 1);

                var camera =
                    vehicle.PassengerCameras[index];

                yaw =
                    _vehicle.HeadingRadians +
                    DegreesToRadians(
                        camera.HeadingDegrees) +
                    _interiorCameraYawOffsetRadians;

                pitch =
                    DegreesToRadians(
                        camera.PitchDegrees) +
                    _interiorCameraPitchOffsetRadians;

                verticalFieldOfViewRadians =
                    DegreesToRadians(
                        Math.Clamp(
                            camera.FieldOfViewDegrees *
                            Math.Clamp(
                                _interiorCameraFieldOfViewScale,
                                0.35f,
                                2.0f),
                            18.0,
                            120.0));
            }
            else
            {
                yaw =
                    _vehicle.HeadingRadians +
                    _exteriorCameraYawOffsetRadians;

                var basePitch =
                    MathF.Atan2(
                        4.4f,
                        14.0f);

                pitch =
                    -Math.Clamp(
                        basePitch +
                        _exteriorCameraPitchOffsetRadians,
                        -1.15f,
                        1.25f);

                verticalFieldOfViewRadians =
                    MathF.PI /
                    3.0f;
            }
        }

        return _skyViewParametersOverride ??
            new Vector4(
                yaw,
                pitch,
                MathF.Tan(
                    verticalFieldOfViewRadians *
                    0.5f),
                MathF.Max(
                    aspect,
                    0.1f));
    }

    private void DrawSky()
    {
        if (_deviceContext is null ||
            CurrentRenderTargetView is null ||
            _skyVertexShader is null ||
            _skyPixelShader is null ||
            _skySampler is null ||
            _skyConstantsBuffer is null ||
            _skyTexture is null)
        {
            return;
        }

        _deviceContext.OMSetRenderTargets(
            CurrentRenderTargetView,
            null);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            null);

        _deviceContext.VSSetShader(
            _skyVertexShader);

        _deviceContext.PSSetShader(
            _skyPixelShader);

        Span<RuntimeSkyConstants> skyConstants =
            stackalloc RuntimeSkyConstants[1];

        skyConstants[0] =
            new RuntimeSkyConstants
            {
                ViewParameters =
                    ResolveSkyViewParameters()
            };

        _skyConstantsBuffer.SetData(
            _deviceContext,
            skyConstants,
            MapMode.WriteDiscard);

        _deviceContext.PSSetConstantBuffer(
            0,
            _skyConstantsBuffer);

        _deviceContext.PSSetSampler(
            0,
            _skySampler);

        _deviceContext.PSSetShaderResource(
            0,
            _skyTexture.View);

        _deviceContext.Draw(
            3,
            0);

        _deviceContext.PSUnsetShaderResource(
            0);
    }

    private void CreateTileOverviewResources()
    {
        if (_device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is not initialized.");
        }

        var vertices = BuildTileVertices(_windowInfo.Tiles);
        if (vertices.Length == 0)
        {
            return;
        }

        _tileVertexBuffer = _device.CreateBuffer(
            vertices.AsSpan(),
            BindFlags.VertexBuffer);

        var shaderFile = ShaderPath("RuntimeGrid.hlsl");

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMain",
                "vs_4_0");

        ReadOnlyMemory<byte> pixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSMain",
                "ps_4_0");

        _tileVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);
        _tilePixelShader =
            _device.CreatePixelShader(
                pixelShaderByteCode.Span);
        _tileInputLayout =
            _device.CreateInputLayout(
                CreateInputElements(),
                vertexShaderByteCode.Span);

        _tileVertexCount = (uint)vertices.Length;
    }

    private void CreateTerrainResources()
    {
        if (_device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is not initialized.");
        }

        _terrainGeometry =
            RuntimeTerrainGeometryBuilder.Build(
                _windowInfo.Tiles,
                _windowInfo.GroundTextures,
                _windowInfo.Splines);

        if (_terrainGeometry.Vertices.Length == 0)
        {
            return;
        }

        _terrainVertexBuffer =
            _device.CreateBuffer(
                _terrainGeometry.Vertices.AsSpan(),
                BindFlags.VertexBuffer);

        var shaderFile =
            ShaderPath(
                "RuntimeTerrain.hlsl");

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMain",
                "vs_4_0");

        ReadOnlyMemory<byte> colorPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSColor",
                "ps_4_0");

        ReadOnlyMemory<byte> texturedPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSTextured",
                "ps_4_0");

        ReadOnlyMemory<byte> baseDetailPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSBaseDetail",
                "ps_4_0");

        ReadOnlyMemory<byte> layerPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSLayer",
                "ps_4_0");

        ReadOnlyMemory<byte> layerDetailPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSLayerDetail",
                "ps_4_0");

        ReadOnlyMemory<byte> lightmapPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSLightmap",
                "ps_4_0");

        _terrainVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);

        _terrainPixelShader =
            _device.CreatePixelShader(
                colorPixelShaderByteCode.Span);

        _terrainTexturedPixelShader =
            _device.CreatePixelShader(
                texturedPixelShaderByteCode.Span);

        _terrainBaseDetailPixelShader =
            _device.CreatePixelShader(
                baseDetailPixelShaderByteCode.Span);

        _terrainLayerPixelShader =
            _device.CreatePixelShader(
                layerPixelShaderByteCode.Span);

        _terrainLayerDetailPixelShader =
            _device.CreatePixelShader(
                layerDetailPixelShaderByteCode.Span);

        _terrainLightmapPixelShader =
            _device.CreatePixelShader(
                lightmapPixelShaderByteCode.Span);

        _terrainInputLayout =
            _device.CreateInputLayout(
                CreateTerrainInputElements(),
                vertexShaderByteCode.Span);

        _terrainTextureSampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearWrap);

        _terrainTextureSamplerPerformance =
            _device.CreateSamplerState(
                new SamplerDescription(
                    Filter.MinMagMipLinear,
                    TextureAddressMode.Wrap,
                    mipLODBias:
                        1.0f));

        _terrainMaskSampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearClamp);

        _terrainAlphaBlendState =
            _device.CreateBlendState(
                BlendDescription.NonPremultiplied);

        _terrainAdditiveBlendState =
            _device.CreateBlendState(
                BlendDescription.Additive);

        _terrainCameraBuffer =
            _device.CreateConstantBuffer<
                RuntimeCameraConstants>();

        _terrainRasterizerState =
            _device.CreateRasterizerState(
                RasterizerDescription.CullNone);

        _terrainVertexCount =
            (uint)_terrainGeometry.Vertices.Length;

        AppendTerrainAlignmentDiagnostics();

        _camera.Reset(
            _terrainGeometry);
    }

    private void AppendTerrainAlignmentDiagnostics()
    {
        var alignedSplines =
            _windowInfo.Splines
                .Where(
                    static spline =>
                        spline.TerrainAlignMode is
                        > 0)
                .ToArray();

        var modeSummary =
            alignedSplines
                .GroupBy(
                    static spline =>
                        spline.TerrainAlignMode!.Value)
                .OrderBy(
                    static group =>
                        group.Key)
                .Select(
                    static group =>
                        $"{group.Key}:{group.Count()}")
                .ToArray();

        var signature =
            $"splines={alignedSplines.Length};segments={_terrainGeometry.AlignedSplineSegmentCount};vertices={_terrainGeometry.Vertices.Length};height={_terrainGeometry.MinimumHeight:0.###}..{_terrainGeometry.MaximumHeight:0.###};modes={(modeSummary.Length == 0 ? "<none>" : string.Join(",", modeSummary))}";

        if (string.Equals(
                _lastTerrainAlignmentDiagnosticSignature,
                signature,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastTerrainAlignmentDiagnosticSignature =
            signature;

        Console.WriteLine(
            $"[terrain-align] {signature}");

        var diagnosticLine =
            $"{DateTimeOffset.Now:O}|{signature}{Environment.NewLine}";

        _ =
            Task.Run(
                () =>
                {
                    try
                    {
                        File.AppendAllText(
                            Path.Combine(
                                AppContext.BaseDirectory,
                                "terrain-alignment.log"),
                            diagnosticLine);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[terrain-align] unable to append diagnostics: {ex.Message}");
                    }
                });
    }

    private void CreateSplineResources()
    {
        if (_device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is not initialized.");
        }

        _splineGeometry =
            RuntimeSplineGeometryBuilder.Build(
                _windowInfo.Splines);

        _vehicle.ReplaceSplineSurfaceGeometry(
            _splineGeometry);

        if (_splineGeometry.Vertices.Length == 0)
        {
            return;
        }

        _splineVertexBuffer =
            _device.CreateBuffer(
                _splineGeometry.Vertices.AsSpan(),
                BindFlags.VertexBuffer);

        _splineVertexCount =
            (uint)_splineGeometry.Vertices.Length;
    }

    private void CreateObjectResources()
    {
        if (_device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is not initialized.");
        }

        _objectGeometry =
            RuntimeObjectGeometryBuilder.Build(
                _windowInfo.Tiles,
                _windowInfo.Objects,
                _windowInfo.SceneryAssets,
                useNativeOmsiModelSpace: true,
                isolatedObjectIds:
                    _windowInfo.DynamicSceneryObjectIds);

        RebuildObjectRenderPassBatches();

        if (_objectGeometry.Vertices.Length == 0 &&
            _splineGeometry.Vertices.Length == 0 &&
            _terrainGeometry.TexturedBatchCount == 0 &&
            _terrainGeometry.MaskedLayerCount == 0)
        {
            return;
        }

        if (_objectGeometry.Vertices.Length > 0)
        {
            _objectVertexBuffer =
                _device.CreateBuffer(
                    _objectGeometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        var shaderFile =
            ShaderPath("RuntimeObject.hlsl");

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMain",
                "vs_4_0");

        ReadOnlyMemory<byte> colorPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSColor",
                "ps_4_0");

        ReadOnlyMemory<byte> texturedPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSTextured",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaCutoutPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaCutout",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaBlendPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaBlend",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaCutoutTransMapPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaCutoutTransMap",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaBlendTransMapPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaBlendTransMap",
                "ps_4_0");

        _objectVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);

        _objectColorPixelShader =
            _device.CreatePixelShader(
                colorPixelShaderByteCode.Span);

        _objectTexturedPixelShader =
            _device.CreatePixelShader(
                texturedPixelShaderByteCode.Span);

        _objectAlphaCutoutPixelShader =
            _device.CreatePixelShader(
                alphaCutoutPixelShaderByteCode.Span);

        _objectAlphaBlendPixelShader =
            _device.CreatePixelShader(
                alphaBlendPixelShaderByteCode.Span);

        _objectAlphaCutoutTransMapPixelShader =
            _device.CreatePixelShader(
                alphaCutoutTransMapPixelShaderByteCode.Span);

        _objectAlphaBlendTransMapPixelShader =
            _device.CreatePixelShader(
                alphaBlendTransMapPixelShaderByteCode.Span);

        _objectAlphaBlendState =
            _device.CreateBlendState(
                BlendDescription.NonPremultiplied);

        _objectDepthReadState =
            _device.CreateDepthStencilState(
                DepthStencilDescription.DepthRead);

        _objectDepthDisabledState =
            _device.CreateDepthStencilState(
                DepthStencilDescription.None);

        _objectInputLayout =
            _device.CreateInputLayout(
                CreateObjectInputElements(),
                vertexShaderByteCode.Span);

        _objectSampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearWrap);

        _objectSamplerPerformance =
            _device.CreateSamplerState(
                new SamplerDescription(
                    Filter.MinMagMipLinear,
                    TextureAddressMode.Wrap,
                    mipLODBias:
                        1.0f));

        _objectTextureLoader =
            new RuntimeGpuTextureLoader(
                _device,
                _deviceContext);

        foreach (var texturePath in
            _objectGeometry.Batches
                .Concat(
                    _splineGeometry.Batches)
                .SelectMany(
                    static batch =>
                        new[]
                        {
                            batch.TexturePath,
                            batch.TransMapTexturePath,
                            batch.LightMapTexturePath,
                            batch.MaterialChangeTexturePath,
                            batch.EnvMapTexturePath,
                            batch.EnvMapMaskTexturePath,
                            batch.BumpMapTexturePath
                        })
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    static path =>
                        path!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase))
        {
            var texture =
                _objectTextureLoader.TryLoad(
                    texturePath);

            if (texture is not null)
            {
                _objectTextureCache[
                    texturePath] =
                    texture;
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    texturePath);
            }
        }

        foreach (var texturePath in
            _terrainGeometry.Batches
                .SelectMany(
                    static batch =>
                        new[]
                        {
                            batch.TexturePath,
                            batch.DetailTexturePath
                        })
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    static path =>
                        path!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase))
        {
            if (_objectTextureCache.ContainsKey(
                    texturePath))
            {
                continue;
            }

            var texture =
                _objectTextureLoader.TryLoad(
                    texturePath);

            if (texture is not null)
            {
                _objectTextureCache[
                    texturePath] =
                    texture;
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    texturePath);
            }
        }

        foreach (var maskPath in
            _terrainGeometry.Batches
                .Select(
                    static batch =>
                        batch.MaskTexturePath)
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    static path =>
                        path!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase))
        {
            if (_objectTextureCache.ContainsKey(
                    maskPath))
            {
                continue;
            }

            var mask =
                _objectTextureLoader.TryLoadAlphaMask(
                    maskPath);

            if (mask is not null)
            {
                _objectTextureCache[
                    maskPath] =
                    mask;
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    maskPath);
            }
        }

        _objectVertexCount =
            (uint)_objectGeometry.Vertices.Length;
    }

    private void EnsureTrafficVehicleResources()
    {
        if (_device is null ||
            _windowInfo.TrafficVehicleAssets is null ||
            _windowInfo.TrafficVehicleAssets.Count ==
                0)
        {
            return;
        }

        foreach (var pair in
                 _windowInfo.TrafficVehicleAssets)
        {
            if (_trafficVehicleVertexBuffers.ContainsKey(
                    pair.Key))
            {
                continue;
            }

            var geometry =
                RuntimeVehicleGeometry.Build(
                    pair.Value,
                    viewpointBit:
                        4,
                    forceMaterialAlphaOpaque:
                        true);

            if (geometry.Vertices.Length ==
                0)
            {
                continue;
            }

            var buffer =
                _device.CreateBuffer(
                    geometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);

            _trafficVehicleGeometries[
                pair.Key] =
                geometry;

            _trafficVehicleRenderBatches[
                pair.Key] =
                BuildTrafficVehicleRenderBatches(
                    geometry);

            _trafficVehicleVertexBuffers[
                pair.Key] =
                buffer;

            Console.WriteLine(
                $"[traffic-ai] GPU vehicle={Path.GetFileName(pair.Key)}; vertices={geometry.Vertices.Length}; meshes={geometry.RenderedMeshCount}");
        }
    }

    private void CreateVehicleResources()
    {
        if (_device is null)
        {
            throw new InvalidOperationException(
                "D3D11 device is not initialized.");
        }

        _terrainCameraBuffer ??=
            _device.CreateConstantBuffer<
                RuntimeCameraConstants>();

        _terrainRasterizerState ??=
            _device.CreateRasterizerState(
                RasterizerDescription.CullNone);

        _vehicleExteriorGeometry =
            RuntimeVehicleGeometry.Build(
                _windowInfo.Vehicle,
                viewpointBit: 1);

        _vehicleInteriorGeometry =
            RuntimeVehicleGeometry.Build(
                _windowInfo.Vehicle,
                viewpointBit: 2);

        _pinnedVehicleTexturePaths =
            _vehicleExteriorGeometry.Batches
                .Concat(
                    _vehicleInteriorGeometry.Batches)
                .SelectMany(
                    static batch =>
                        new[]
                        {
                            batch.TexturePath,
                            batch.TransMapTexturePath,
                            batch.LightMapTexturePath,
                            batch.MaterialChangeTexturePath
                        })
                .Where(
                    static texturePath =>
                        !string.IsNullOrWhiteSpace(
                            texturePath))
                .Select(
                    static texturePath =>
                        texturePath!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _vehicleAnimationParentBatches.Clear();
        _vehicleMeshOrdinalBatches.Clear();

        foreach (var batch in
                 _vehicleExteriorGeometry.Batches
                     .Concat(
                         _vehicleInteriorGeometry.Batches))
        {
            if (batch.ModelOrdinal >= 0)
            {
                _vehicleMeshOrdinalBatches.TryAdd(
                    (
                        batch.SectionIndex,
                        batch.ModelOrdinal),
                    batch);
            }

            if (!string.IsNullOrWhiteSpace(
                    batch.MeshIdentifier))
            {
                _vehicleAnimationParentBatches.TryAdd(
                    batch.MeshIdentifier,
                    batch);
            }
        }

        if (_vehiclePreviewMode)
        {
            ConfigureVehiclePreviewBounds();
        }

        Console.WriteLine(
            $"[vehicle-geometry] exteriorVertices={_vehicleExteriorGeometry.Vertices.Length}; " +
            $"exteriorMeshes={_vehicleExteriorGeometry.RenderedMeshCount}; " +
            $"interiorVertices={_vehicleInteriorGeometry.Vertices.Length}; " +
            $"interiorMeshes={_vehicleInteriorGeometry.RenderedMeshCount}; " +
            $"encrypted={Math.Max(_vehicleExteriorGeometry.ProtectedMeshCount, _vehicleInteriorGeometry.ProtectedMeshCount)}; " +
            $"failed={Math.Max(_vehicleExteriorGeometry.MissingMeshCount, _vehicleInteriorGeometry.MissingMeshCount)}");

        AppendVehicleGeometryDiagnostics();

        var hasPlayerVehicleLights =
            _windowInfo.Vehicle?.Meshes.Any(
                static mesh =>
                    mesh.LightEffects is
                        { Count: > 0 }) ==
            true;

        var hasTrafficVehicleLights =
            _windowInfo.TrafficVehicleAssets?.Values.Any(
                static vehicle =>
                    vehicle.Meshes.Any(
                        static mesh =>
                            mesh.LightEffects is
                                { Count: > 0 })) ==
            true;

        var hasVehicleLights =
            hasPlayerVehicleLights ||
            hasTrafficVehicleLights;

        var hasTrafficVehicleAssets =
            _windowInfo.TrafficVehicleAssets is
                { Count: > 0 };

        if (_vehicleExteriorGeometry.Vertices.Length == 0 &&
            _vehicleInteriorGeometry.Vertices.Length == 0 &&
            !hasVehicleLights &&
            !hasTrafficVehicleAssets)
        {
            Console.WriteLine(
                "[vehicle-geometry] No real player or AI vehicle geometry or light effects could be built.");
            return;
        }

        if (_vehicleExteriorGeometry.Vertices.Length > 0)
        {
            _vehicleExteriorVertexBuffer =
                _device.CreateBuffer(
                    _vehicleExteriorGeometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        if (_vehicleInteriorGeometry.Vertices.Length > 0)
        {
            _vehicleInteriorVertexBuffer =
                _device.CreateBuffer(
                    _vehicleInteriorGeometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        if (hasVehicleLights)
        {
            RuntimeObjectVertex[] lightVertices =
            [
                new(
                    new Vector3(-0.5f, 0.5f, 0.0f),
                    new Color4(1.0f, 1.0f, 1.0f, 1.0f),
                    new Vector2(0.0f, 0.0f),
                    Vector3.UnitZ),
                new(
                    new Vector3(0.5f, 0.5f, 0.0f),
                    new Color4(1.0f, 1.0f, 1.0f, 1.0f),
                    new Vector2(1.0f, 0.0f),
                    Vector3.UnitZ),
                new(
                    new Vector3(0.5f, -0.5f, 0.0f),
                    new Color4(1.0f, 1.0f, 1.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                    Vector3.UnitZ),
                new(
                    new Vector3(-0.5f, 0.5f, 0.0f),
                    new Color4(1.0f, 1.0f, 1.0f, 1.0f),
                    new Vector2(0.0f, 0.0f),
                    Vector3.UnitZ),
                new(
                    new Vector3(0.5f, -0.5f, 0.0f),
                    new Color4(1.0f, 1.0f, 1.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                    Vector3.UnitZ),
                new(
                    new Vector3(-0.5f, -0.5f, 0.0f),
                    new Color4(1.0f, 1.0f, 1.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    Vector3.UnitZ)
            ];

            _vehicleLightVertexBuffer =
                _device.CreateBuffer(
                    lightVertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        var shaderFile =
            ShaderPath(
                "RuntimeVehicle.hlsl");

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMain",
                "vs_4_0");

        ReadOnlyMemory<byte> instancedVertexShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "VSMainInstanced",
                "vs_4_0");

        ReadOnlyMemory<byte> colorPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSColor",
                "ps_4_0");

        ReadOnlyMemory<byte> texturedPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSTextured",
                "ps_4_0");

        ReadOnlyMemory<byte> lightPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSLightEffect",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaCutoutPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaCutout",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaBlendPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaBlend",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaCutoutTransMapPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaCutoutTransMap",
                "ps_4_0");

        ReadOnlyMemory<byte> alphaBlendTransMapPixelShaderByteCode =
            Compiler.CompileFromFile(
                shaderFile,
                "PSAlphaBlendTransMap",
                "ps_4_0");

        _vehicleVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);

        _trafficInstancedVertexShader =
            _device.CreateVertexShader(
                instancedVertexShaderByteCode.Span);

        _vehicleColorPixelShader =
            _device.CreatePixelShader(
                colorPixelShaderByteCode.Span);

        _vehicleLightPixelShader =
            _device.CreatePixelShader(
                lightPixelShaderByteCode.Span);

        _vehicleTexturedPixelShader =
            _device.CreatePixelShader(
                texturedPixelShaderByteCode.Span);

        _vehicleAlphaCutoutPixelShader =
            _device.CreatePixelShader(
                alphaCutoutPixelShaderByteCode.Span);

        _vehicleAlphaBlendPixelShader =
            _device.CreatePixelShader(
                alphaBlendPixelShaderByteCode.Span);

        _vehicleAlphaCutoutTransMapPixelShader =
            _device.CreatePixelShader(
                alphaCutoutTransMapPixelShaderByteCode.Span);

        _vehicleAlphaBlendTransMapPixelShader =
            _device.CreatePixelShader(
                alphaBlendTransMapPixelShaderByteCode.Span);

        _vehicleInputLayout =
            _device.CreateInputLayout(
                CreateVehicleInputElements(),
                vertexShaderByteCode.Span);

        _trafficInstancedInputLayout =
            _device.CreateInputLayout(
                CreateTrafficVehicleInstancedInputElements(),
                instancedVertexShaderByteCode.Span);

        _vehicleSampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearWrap);

        _vehicleAlphaBlendState =
            _device.CreateBlendState(
                BlendDescription.NonPremultiplied);

        _vehicleDepthReadState =
            _device.CreateDepthStencilState(
                DepthStencilDescription.DepthRead);

        _vehicleDepthDisabledState =
            _device.CreateDepthStencilState(
                DepthStencilDescription.None);

        _vehicleModelBuffer =
            _device.CreateConstantBuffer<
                RuntimeModelConstants>();

        _vehicleMaterialBuffer =
            _device.CreateConstantBuffer<
                RuntimeVehicleMaterialConstants>();

        _vehicleSkinBuffer =
            _device.CreateConstantBuffer<
                RuntimeVehicleSkinConstants>();

        _objectTextureLoader ??=
            new RuntimeGpuTextureLoader(
                _device,
                _deviceContext);

        _vehicleTextTextureRenderer ??=
            new RuntimeOmsiTextTextureRenderer(
                _windowInfo.ContentRoot,
                _objectTextureLoader);

        CreateReflectionResources();

        static IEnumerable<string?>
            EnumerateKnownVehicleTexturePaths(
                RuntimeObjectBatch batch)
        {
            yield return
                batch.TexturePath;
            yield return
                batch.TransMapTexturePath;
            yield return
                batch.LightMapTexturePath;
            yield return
                batch.MaterialChangeTexturePath;
            yield return
                batch.EnvMapTexturePath;
            yield return
                batch.EnvMapMaskTexturePath;
            yield return
                batch.BumpMapTexturePath;

            if (batch.MaterialChangeSets is not
                { Count: > 0 } changeSets)
            {
                yield break;
            }

            foreach (var changeSet in
                     changeSets)
            {
                foreach (var item in
                         changeSet.Items)
                {
                    yield return
                        item.TransMapTexturePath;
                    yield return
                        item.LightMapTexturePath;
                    yield return
                        item.MaterialChangeTexturePath;
                    yield return
                        item.EnvMapTexturePath;
                    yield return
                        item.EnvMapMaskTexturePath;
                    yield return
                        item.BumpMapTexturePath;
                }
            }
        }

        var vehicleTexturePaths =
            _vehicleExteriorGeometry.Batches
                .Concat(
                    _vehicleInteriorGeometry.Batches)
                .SelectMany(
                    EnumerateKnownVehicleTexturePaths)
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    static path =>
                        path!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var texturePath in
            vehicleTexturePaths)
        {
            if (_objectTextureCache.ContainsKey(
                    texturePath) ||
                _reflectionTargets.ContainsKey(
                    texturePath))
            {
                continue;
            }

            var texture =
                _objectTextureLoader.TryLoad(
                    texturePath);

            if (texture is not null)
            {
                _objectTextureCache[
                    texturePath] =
                    texture;
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    texturePath);
            }
        }

        AppendVehicleTextureDiagnostics(
            vehicleTexturePaths);

        if (!_vehiclePreviewMode)
        {
            _vehicle.Reset(
                _windowInfo.Splines,
                _terrainGeometry,
                _windowInfo.Spawn);
            ResetArticulatedSections();
        }
    }

    private void AppendVehicleTextureDiagnostics(
        IReadOnlyList<string> vehicleTexturePaths)
    {
        try
        {
            var logPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-load.log");

            var failed =
                vehicleTexturePaths
                    .Where(
                        _failedObjectTexturePaths.Contains)
                    .ToArray();

            var batches =
                _vehicleExteriorGeometry.Batches
                    .Concat(
                        _vehicleInteriorGeometry.Batches)
                    .ToArray();

            var lines =
                new List<string>
                {
                    "",
                    "vehicleTextures:",
                    $"required={vehicleTexturePaths.Count}",
                    $"failed={failed.Length}",
                    $"alphaBatches={batches.Count(static batch => batch.AlphaCutout || batch.AlphaBlend)}",
                    $"noZWriteBatches={batches.Count(static batch => batch.NoZWrite)}",
                    $"noZCheckBatches={batches.Count(static batch => batch.NoZCheck)}"
                };

            foreach (var failedPath in
                     failed.Take(40))
            {
                lines.Add(
                    $"failedTexture={failedPath}");
            }

            File.AppendAllLines(
                logPath,
                lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[vehicle-texture] unable to append diagnostics: {ex.Message}");
        }
    }

    private void ReportMissingTransMap(
        string scope,
        string? diffusePath,
        string? transMapPath)
    {
        var key =
            $"{scope}|{diffusePath}|{transMapPath}";

        if (!_reportedMissingTransMaps.Add(
                key))
        {
            return;
        }

        var message =
            $"scope={scope}|diffuse={diffusePath ?? "<none>"}|transmap={transMapPath ?? "<unresolved>"}";

        Console.WriteLine(
            $"[transmap] {message}");

        try
        {
            File.AppendAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "transmap-missing.log"),
                $"{DateTimeOffset.Now:O}|{message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void AppendVehicleGeometryDiagnostics()
    {
        try
        {
            var logPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-load.log");

            static string BoundsOf(
                RuntimeObjectGeometry geometry)
            {
                if (geometry.Vertices.Length == 0)
                {
                    return "<empty>";
                }

                var min =
                    new Vector3(
                        float.PositiveInfinity);
                var max =
                    new Vector3(
                        float.NegativeInfinity);

                foreach (var vertex in
                         geometry.Vertices)
                {
                    min =
                        Vector3.Min(
                            min,
                            vertex.Position);
                    max =
                        Vector3.Max(
                            max,
                            vertex.Position);
                }

                return
                    $"min=({min.X:F3},{min.Y:F3},{min.Z:F3}) " +
                    $"max=({max.X:F3},{max.Y:F3},{max.Z:F3})";
            }

            var viewpointGroups =
                _windowInfo.Vehicle?.Meshes
                    .GroupBy(
                        static mesh =>
                            mesh.ViewpointFlag)
                    .OrderBy(
                        static group =>
                            group.Key)
                    .Select(
                        static group =>
                            $"{group.Key}:{group.Count()}")
                    .ToArray() ??
                Array.Empty<string>();

            var lines =
                new[]
                {
                    "",
                    "runtimeGeometry:",
                    $"exteriorVertices={_vehicleExteriorGeometry.Vertices.Length}",
                    $"exteriorMeshes={_vehicleExteriorGeometry.RenderedMeshCount}",
                    $"exteriorBounds={BoundsOf(_vehicleExteriorGeometry)}",
                    $"interiorVertices={_vehicleInteriorGeometry.Vertices.Length}",
                    $"interiorMeshes={_vehicleInteriorGeometry.RenderedMeshCount}",
                    $"interiorBounds={BoundsOf(_vehicleInteriorGeometry)}",
                    $"viewpointFlags={(viewpointGroups.Length == 0 ? "<none>" : string.Join(", ", viewpointGroups))}",
                    $"spawn={_windowInfo.Spawn?.X:F3},{_windowInfo.Spawn?.Y:F3},{_windowInfo.Spawn?.Z:F3}",
                    $"spawnHeading={_windowInfo.Spawn?.HeadingDegrees:F3}"
                };

            File.AppendAllLines(
                logPath,
                lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[vehicle-geometry] unable to append diagnostics: {ex.Message}");
        }
    }

    private void CreateReflectionResources()
    {
        if (_device is null ||
            _windowInfo.Vehicle?.ReflectionCameras.Count is
                not > 0)
        {
            return;
        }

        foreach (var camera in
                 _windowInfo.Vehicle.ReflectionCameras)
        {
            if (_reflectionTargets.ContainsKey(
                    camera.RuntimeTextureKey))
            {
                continue;
            }

            _reflectionTargets[
                camera.RuntimeTextureKey] =
                new RuntimeReflectionTarget(
                    _device,
                    camera,
                    _reflectionTextureSize);
        }

        if (_reflectionDepthTexture is null)
        {
            _reflectionDepthTexture =
                _device.CreateTexture2D(
                    Format.D32_Float,
                    _reflectionTextureSize,
                    _reflectionTextureSize,
                    mipLevels: 1,
                    bindFlags:
                        BindFlags.DepthStencil);

            _reflectionDepthStencilView =
                _device.CreateDepthStencilView(
                    _reflectionDepthTexture);
        }
    }

    private static InputElementDescription[]
        CreateInputElements() =>
    [
        new InputElementDescription(
            "POSITION",
            0,
            Format.R32G32B32_Float,
            0,
            0),
        new InputElementDescription(
            "COLOR",
            0,
            Format.R32G32B32A32_Float,
            12,
            0)
    ];

    private static InputElementDescription[]
        CreateTerrainInputElements() =>
    [
        new InputElementDescription(
            "POSITION",
            0,
            Format.R32G32B32_Float,
            0,
            0),
        new InputElementDescription(
            "COLOR",
            0,
            Format.R32G32B32A32_Float,
            12,
            0),
        new InputElementDescription(
            "TEXCOORD",
            0,
            Format.R32G32_Float,
            28,
            0),
        new InputElementDescription(
            "TEXCOORD",
            1,
            Format.R32G32_Float,
            36,
            0),
        new InputElementDescription(
            "TEXCOORD",
            2,
            Format.R32G32_Float,
            44,
            0)
    ];

    private static InputElementDescription[]
        CreateObjectInputElements() =>
    [
        new InputElementDescription(
            "POSITION",
            0,
            Format.R32G32B32_Float,
            0,
            0),
        new InputElementDescription(
            "COLOR",
            0,
            Format.R32G32B32A32_Float,
            12,
            0),
        new InputElementDescription(
            "TEXCOORD",
            0,
            Format.R32G32_Float,
            28,
            0)
    ];

    private static InputElementDescription[]
        CreateVehicleInputElements() =>
    [
        new InputElementDescription(
            "POSITION",
            0,
            Format.R32G32B32_Float,
            0,
            0),
        new InputElementDescription(
            "COLOR",
            0,
            Format.R32G32B32A32_Float,
            12,
            0),
        new InputElementDescription(
            "TEXCOORD",
            0,
            Format.R32G32_Float,
            28,
            0),
        new InputElementDescription(
            "NORMAL",
            0,
            Format.R32G32B32_Float,
            36,
            0),
        new InputElementDescription(
            "BLENDWEIGHT",
            0,
            Format.R32G32B32A32_Float,
            48,
            0)
    ];

    private static InputElementDescription[]
        CreateTrafficVehicleInstancedInputElements() =>
    [
        new InputElementDescription(
            "POSITION",
            0,
            Format.R32G32B32_Float,
            0,
            0),
        new InputElementDescription(
            "COLOR",
            0,
            Format.R32G32B32A32_Float,
            12,
            0),
        new InputElementDescription(
            "TEXCOORD",
            0,
            Format.R32G32_Float,
            28,
            0),
        new InputElementDescription(
            "NORMAL",
            0,
            Format.R32G32B32_Float,
            36,
            0),
        new InputElementDescription(
            "BLENDWEIGHT",
            0,
            Format.R32G32B32A32_Float,
            48,
            0),
        new InputElementDescription(
            "INSTANCEWORLD",
            0,
            Format.R32G32B32A32_Float,
            0,
            1,
            InputClassification.PerInstanceData,
            1),
        new InputElementDescription(
            "INSTANCEWORLD",
            1,
            Format.R32G32B32A32_Float,
            16,
            1,
            InputClassification.PerInstanceData,
            1),
        new InputElementDescription(
            "INSTANCEWORLD",
            2,
            Format.R32G32B32A32_Float,
            32,
            1,
            InputClassification.PerInstanceData,
            1),
        new InputElementDescription(
            "INSTANCEWORLD",
            3,
            Format.R32G32B32A32_Float,
            48,
            1,
            InputClassification.PerInstanceData,
            1)
    ];

    private static string ShaderPath(string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Shaders",
            fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Runtime shader was not copied to the output directory.",
                path);
        }

        return path;
    }

    private static RuntimeVertex[] BuildTileVertices(
        IReadOnlyList<RuntimeTileInfo> tiles)
    {
        if (tiles.Count == 0)
        {
            return [];
        }

        var minimumX = tiles.Min(static tile => tile.X);
        var maximumX = tiles.Max(static tile => tile.X);
        var minimumY = tiles.Min(static tile => tile.Y);
        var maximumY = tiles.Max(static tile => tile.Y);

        var width = Math.Max(
            maximumX - minimumX + 1,
            1);
        var height = Math.Max(
            maximumY - minimumY + 1,
            1);
        var cellSize = MathF.Min(
            1.8f / width,
            1.8f / height);
        var halfSize = cellSize * 0.43f;

        var middleX =
            (minimumX + maximumX) * 0.5f;
        var middleY =
            (minimumY + maximumY) * 0.5f;

        var vertices =
            new List<RuntimeVertex>(
                tiles.Count * 6);

        foreach (var tile in tiles)
        {
            var centerX =
                (tile.X - middleX) *
                cellSize;
            var centerY =
                -(tile.Y - middleY) *
                cellSize;

            var density = MathF.Min(
                1.0f,
                (tile.ObjectCount +
                 tile.SplineCount) /
                250.0f);

            var color = new Color4(
                0.18f + density * 0.35f,
                0.38f + density * 0.22f,
                0.72f,
                1.0f);

            var left = centerX - halfSize;
            var right = centerX + halfSize;
            var top = centerY + halfSize;
            var bottom = centerY - halfSize;

            vertices.Add(
                new RuntimeVertex(
                    new Vector3(
                        left,
                        top,
                        0.0f),
                    color));
            vertices.Add(
                new RuntimeVertex(
                    new Vector3(
                        right,
                        top,
                        0.0f),
                    color));
            vertices.Add(
                new RuntimeVertex(
                    new Vector3(
                        right,
                        bottom,
                        0.0f),
                    color));

            vertices.Add(
                new RuntimeVertex(
                    new Vector3(
                        left,
                        top,
                        0.0f),
                    color));
            vertices.Add(
                new RuntimeVertex(
                    new Vector3(
                        right,
                        bottom,
                        0.0f),
                    color));
            vertices.Add(
                new RuntimeVertex(
                    new Vector3(
                        left,
                        bottom,
                        0.0f),
                    color));
        }

        return vertices.ToArray();
    }

    protected override bool ProcessCmdKey(
        ref Message msg,
        Keys keyData)
    {
        if (!_vehiclePreviewMode &&
            (keyData & Keys.KeyCode) ==
            Keys.Menu)
        {
            ToggleOmsiMenu();
            return true;
        }

        if (!_vehiclePreviewMode &&
            _busSelectorPanel?.Visible ==
                true &&
            (keyData & Keys.KeyCode) ==
            Keys.Escape)
        {
            _busSelectorPanel.HideSelector();
            return true;
        }

        if (!_vehiclePreviewMode &&
            _omsiMenuBar?.Visible ==
                true &&
            (keyData & Keys.KeyCode) ==
            Keys.Escape)
        {
            _omsiMenuBar.HideMenu();
            return true;
        }

        return base.ProcessCmdKey(
            ref msg,
            keyData);
    }

    private void ToggleOmsiMenu()
    {
        if (_omsiMenuBar is null)
        {
            return;
        }

        _omsiMenuBar.ToggleMenu();

        if (_omsiMenuBar.Visible)
        {
            LayoutOmsiMenuBar();
            _omsiMenuBar.BringToFront();
        }

        SyncOmsiMenuState();
        UpdateCaption();
    }

    private void LayoutOmsiMenuBar()
    {
        if (_omsiMenuBar is null)
        {
            return;
        }

        _omsiMenuBar.MaximumSize =
            new System.Drawing.Size(
                Math.Max(
                    ClientSize.Width -
                    24,
                    320),
                0);

        _omsiMenuBar.Location =
            new System.Drawing.Point(
                Math.Max(
                    12,
                    ClientSize.Width -
                    _omsiMenuBar.Width -
                    12),
                Math.Max(
                    12,
                    ClientSize.Height -
                    _omsiMenuBar.Height -
                    12));
    }

    private void LayoutRuntimeBusSelector()
    {
        if (_busSelectorPanel is null)
        {
            return;
        }

        _busSelectorPanel.Left =
            Math.Max(
                12,
                (ClientSize.Width -
                 _busSelectorPanel.Width) /
                2);

        _busSelectorPanel.Top =
            Math.Max(
                12,
                (ClientSize.Height -
                 _busSelectorPanel.Height) /
                2);
    }

    private void OnRuntimeBusSelectionConfirmed(
        string relativePath,
        string? hofPath)
    {
        var hofName =
            string.IsNullOrWhiteSpace(
                hofPath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(
                    hofPath);

        Console.WriteLine(
            $"[runtime-select-bus]|{relativePath}|{hofName}");

        _busSelectorPanel?.HideSelector();
        Close();
    }

    private void SyncOmsiMenuState()
    {
        if (_omsiMenuBar is null)
        {
            return;
        }

        _omsiMenuBar.SetPaused(
            _simulationPaused);
        _omsiMenuBar.SetMouseSteeringActive(
            _mouseDriveMode);
        _omsiMenuBar.SetControllerActive(
            _controllerInputEnabled);
    }

    private void OnOmsiMenuCommandInvoked(
        RuntimeOmsiMenuCommand command)
    {
        switch (command)
        {
            case RuntimeOmsiMenuCommand.Close:
                _omsiMenuBar?.HideMenu();
                break;

            case RuntimeOmsiMenuCommand.StartMenu:
                Close();
                return;

            case RuntimeOmsiMenuCommand.NewBus:
                _omsiMenuBar?.HideMenu();

                if (_busSelectorPanel is not null)
                {
                    LayoutRuntimeBusSelector();
                    _busSelectorPanel.ShowSelector();
                    return;
                }

                Console.WriteLine(
                    "[runtime-select-bus]");
                Close();
                return;

            case RuntimeOmsiMenuCommand.RemoveBus:
                RemoveCurrentVehicle();
                break;

            case RuntimeOmsiMenuCommand.Schedule:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ScheduleView);
                break;

            case RuntimeOmsiMenuCommand.Pause:
                _simulationPaused =
                    !_simulationPaused;
                SynchronizeOmsiPauseSystemVariable();
                break;

            case RuntimeOmsiMenuCommand.DriverView:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.DriverView);
                break;

            case RuntimeOmsiMenuCommand.PassengerView:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.PassengerView);
                break;

            case RuntimeOmsiMenuCommand.ExteriorView:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ExteriorView);
                break;

            case RuntimeOmsiMenuCommand.FreeMapCamera:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.FreeCameraView);
                break;

            case RuntimeOmsiMenuCommand.MouseSteering:
                if (_driveMode)
                {
                    ToggleMouseDriveMode();
                }

                break;

            case RuntimeOmsiMenuCommand.GameController:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ControllerToggle);
                break;

            case RuntimeOmsiMenuCommand.ResetVehicle:
                if (!_vehicleRemoved &&
                    _terrainGeometry.Vertices.Length >
                    0)
                {
                    _vehicle.Reset(
                        _windowInfo.Splines,
                        _terrainGeometry,
                        _windowInfo.Spawn);
                    ResetArticulatedSections();
                }

                break;
        }

        SyncOmsiMenuState();
        UpdateCaption();
    }

    private void RemoveCurrentVehicle()
    {
        if (_vehicleRemoved ||
            _windowInfo.Vehicle is null)
        {
            return;
        }

        if (_mouseDriveMode)
        {
            DisableMouseDriveMode();
        }

        var freeCameraPosition =
            _vehicle.GetChaseCameraPosition(
                _windowInfo.Vehicle
                    .OutsideCameraCenter,
                distanceScale:
                    0.75f);

        var freeCameraTarget =
            _vehicle.Position +
            new Vector3(
                0.0f,
                1.6f,
                0.0f);

        _camera.SetLookAt(
            freeCameraPosition,
            freeCameraTarget,
            moveSpeed:
                Math.Clamp(
                    14.0f +
                    Math.Abs(
                        _vehicle.SpeedMetersPerSecond) *
                    2.0f,
                    10.0f,
                    80.0f));

        _vehicle.SetEngineRunning(
            false);

        _vehicleRemoved =
            true;
        _driveMode =
            false;
        _reflectionRenderingEnabled =
            false;

        _omsiAudio?.Dispose();
        _omsiAudio =
            null;

        foreach (var audio in
                 _articulatedOmsiAudio.Values)
        {
            audio.Dispose();
        }

        _articulatedOmsiAudio.Clear();

        Console.WriteLine(
            "[vehicle] current bus removed from map");
    }

    private void OnClientSizeChanged(
        object? sender,
        EventArgs e)
    {
        LayoutOmsiMenuBar();
        LayoutRuntimeBusSelector();

        if (_swapChain is null ||
            ClientSize.Width <= 0 ||
            ClientSize.Height <= 0)
        {
            return;
        }

        _renderTimer.Stop();

        _deviceContext?.UnsetRenderTargets();
        ReleaseBackBufferResources();

        _swapChain.ResizeBuffers(
            2,
            (uint)ClientSize.Width,
            (uint)ClientSize.Height,
            Format.R8G8B8A8_UNorm,
            SwapChainFlags.None)
            .CheckError();

        CreateBackBufferResources();
        _renderTimer.Start();
    }

    private void ReleaseBackBufferResources()
    {
        _depthStencilView?.Dispose();
        _depthStencilView = null;

        _depthTexture?.Dispose();
        _depthTexture = null;

        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _multisampleColorTexture?.Dispose();
        _multisampleColorTexture = null;

        _postProcessShaderResourceView?.Dispose();
        _postProcessShaderResourceView = null;

        _postProcessTexture?.Dispose();
        _postProcessTexture = null;

        _postProcessBackBufferView?.Dispose();
        _postProcessBackBufferView = null;

        _backBuffer?.Dispose();
        _backBuffer = null;

        _activeMsaaSamples =
            0;
    }

    private void RenderTimerOnTick(
        object? sender,
        EventArgs e)
    {
        var tickNowSeconds =
            _frameClock.Elapsed.TotalSeconds;

        var tickDeltaSeconds =
            tickNowSeconds -
            _previousRenderTickSeconds;

        _previousRenderTickSeconds =
            tickNowSeconds;

        if (tickDeltaSeconds >
                0.0 &&
            tickDeltaSeconds <
                1.0)
        {
            _lastObservedFrameMilliseconds =
                tickDeltaSeconds *
                1000.0;
        }

        if (!_simulationPaused)
        {
            UpdateSimulation();
            CheckStreamingCenter();
        }

        ProcessStreamingTextureLoadQueue();
        RenderFrame();
        UpdateFpsOverlay();

        if (_omsiMenuBar?.Visible ==
            true)
        {
            _omsiMenuBar.BringToFront();
        }

        _fpsLabel?.BringToFront();

        _captionFrame++;
        if (_captionFrame >= 15)
        {
            _captionFrame = 0;
            UpdateCaption();
        }
    }

    private void CheckStreamingCenter()
    {
        if (!_windowInfo.ActiveTileRadius.HasValue)
        {
            return;
        }

        // Match OMSI's streamed world behaviour: while driving the
        // vehicle is the streaming focus; in free/map camera mode the
        // camera itself becomes the streaming focus.
        var streamingPosition =
            _driveMode
                ? _vehicle.Position
                : _camera.Position;

        var tileX =
            (int)Math.Floor(
                streamingPosition.X /
                300.0f);

        var tileY =
            (int)Math.Floor(
                streamingPosition.Z /
                300.0f);

        if (_streamingTileX == tileX &&
            _streamingTileY == tileY)
        {
            return;
        }

        var hadCenter =
            _streamingTileX.HasValue &&
            _streamingTileY.HasValue;

        _streamingTileX = tileX;
        _streamingTileY = tileY;

        if (hadCenter)
        {
            StreamingCenterChanged?.Invoke(
                tileX,
                tileY);
        }
    }

    private void ResolveMainSceneTarget()
    {
        if (_activeMsaaSamples <
                2 ||
            _deviceContext is null ||
            _multisampleColorTexture is null)
        {
            return;
        }

        var destination =
            _sharpenStrength >
                    0.0001f &&
                _postProcessTexture is not null
                ? _postProcessTexture
                : _backBuffer;

        if (destination is null)
        {
            return;
        }

        _deviceContext.UnsetRenderTargets();

        _deviceContext.ResolveSubresource(
            destination,
            0,
            _multisampleColorTexture,
            0,
            Format.R8G8B8A8_UNorm);
    }

    private void DrawPostProcess()
    {
        if (_sharpenStrength <=
                0.0001f ||
            _deviceContext is null ||
            _postProcessBackBufferView is null ||
            _postProcessShaderResourceView is null ||
            _postProcessVertexShader is null ||
            _postProcessPixelShader is null ||
            _postProcessSampler is null ||
            _postProcessConstantsBuffer is null)
        {
            return;
        }

        _deviceContext.UnsetRenderTargets();

        _deviceContext.OMSetRenderTargets(
            _postProcessBackBufferView,
            null);

        _deviceContext.RSSetViewport(
            0,
            0,
            (uint)Math.Max(
                ClientSize.Width,
                1),
            (uint)Math.Max(
                ClientSize.Height,
                1));

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            null);

        _deviceContext.VSSetShader(
            _postProcessVertexShader);

        _deviceContext.PSSetShader(
            _postProcessPixelShader);

        Span<RuntimePostProcessConstants> constants =
            stackalloc RuntimePostProcessConstants[1];

        constants[0] =
            new RuntimePostProcessConstants
            {
                TexelSize =
                    new Vector2(
                        1.0f /
                        Math.Max(
                            ClientSize.Width,
                            1),
                        1.0f /
                        Math.Max(
                            ClientSize.Height,
                            1)),
                SharpenStrength =
                    _sharpenStrength,
                Padding =
                    0.0f
            };

        _postProcessConstantsBuffer.SetData(
            _deviceContext,
            constants,
            MapMode.WriteDiscard);

        _deviceContext.PSSetConstantBuffer(
            0,
            _postProcessConstantsBuffer);

        _deviceContext.PSSetSampler(
            0,
            _postProcessSampler);

        _deviceContext.PSSetShaderResource(
            0,
            _postProcessShaderResourceView);

        _deviceContext.Draw(
            3,
            0);

        _deviceContext.PSUnsetShaderResource(
            0);
    }

    private void BeginSceneCameraCache()
    {
        _renderSceneViewProjection =
            CreateViewProjection();
        _renderSceneCameraPosition =
            CurrentCameraPosition;
    }

    private void EndSceneCameraCache()
    {
        _renderSceneViewProjection =
            null;
        _renderSceneCameraPosition =
            null;
    }

    private Matrix4x4 CurrentSceneViewProjection =>
        _renderSceneViewProjection ??
        CreateViewProjection();

    private Vector3 CurrentSceneCameraPosition =>
        _renderSceneCameraPosition ??
        CurrentCameraPosition;

    private void RenderFrame()
    {
        _renderFrameSequence++;
        ReleaseRetiredStreamingVertexBuffers();

        if (_deviceContext is null ||
            _renderTargetView is null ||
            _swapChain is null)
        {
            return;
        }

        if (_reflectionRenderingEnabled)
        {
            RenderReflectionTargets();
        }

        _deviceContext.ClearRenderTargetView(
            _renderTargetView,
            new Color4(
                0.38f,
                0.58f,
                0.78f,
                1.0f));

        if (_depthStencilView is not null)
        {
            _deviceContext.ClearDepthStencilView(
                _depthStencilView,
                DepthStencilClearFlags.Depth,
                1.0f,
                0);
        }

        _deviceContext.RSSetViewport(
            0,
            0,
            (uint)Math.Max(ClientSize.Width, 1),
            (uint)Math.Max(ClientSize.Height, 1));

        if (_vehiclePreviewMode)
        {
            UpdateVehiclePreviewCameraConstants();
            DrawVehicle();

            ResolveMainSceneTarget();
        DrawPostProcess();

            _swapChain.Present(
                _vsync
                    ? 1u
                    : 0u,
                PresentFlags.None)
                .CheckError();

            return;
        }

        BeginSceneCameraCache();

        DrawSky();

        if (CanDrawTerrain())
        {
            // OMSI scenery objects have an explicit global render order.
            // Respect it instead of drawing every .sco in one late pass:
            // presurface -> terrain -> surface -> splines -> on_surface ->
            // 1 -> 2(default) -> 3 -> vehicles -> 4.
            DrawObjects(
                RuntimeSceneryRenderPass.PreSurface);
            DrawTerrain();
            DrawObjects(
                RuntimeSceneryRenderPass.Surface);
            DrawSplines();
            DrawObjects(
                RuntimeSceneryRenderPass.OnSurface);
            DrawObjects(
                RuntimeSceneryRenderPass.One);
            DrawObjects(
                RuntimeSceneryRenderPass.Two);
            DrawObjects(
                RuntimeSceneryRenderPass.Three);

            DrawTrafficVehicles();
            DrawTrafficVehicleLights();

            var exteriorVehicle =
                UseExteriorVehicleView();

            if (exteriorVehicle)
            {
                DrawVehicle();
                DrawVehicleLights();
            }

            DrawObjects(
                RuntimeSceneryRenderPass.Four);

            if (!exteriorVehicle)
            {
                DrawVehicle();
                DrawVehicleLights();
            }
        }
        else
        {
            DrawTileOverview();
        }

        EndSceneCameraCache();

        ResolveMainSceneTarget();
            DrawPostProcess();

        _swapChain.Present(
            _vsync
                ? 1u
                : 0u,
            PresentFlags.None)
            .CheckError();
    }

    private void RenderReflectionTargets()
    {
        if (_deviceContext is null ||
            _reflectionDepthStencilView is null ||
            _reflectionTargets.Count == 0 ||
            !CanDrawTerrain())
        {
            return;
        }

        _reflectionFrameIndex++;

        try
        {
            foreach (var target in
                     _reflectionTargets.Values)
            {
                if (!ShouldRenderReflectionTarget(
                        target))
                {
                    continue;
                }

                _deviceContext.PSUnsetShaderResource(0);
                _deviceContext.PSUnsetShaderResource(1);
                _deviceContext.PSUnsetShaderResource(2);

                _activeRenderTargetView =
                    target.RenderTargetView;
                _activeDepthStencilView =
                    _reflectionDepthStencilView;
                _viewProjectionOverride =
                    _vehicle.CreateReflectionViewProjection(
                        target.Camera,
                        1.0f,
                        _terrainGeometry);

                _cameraPositionOverride =
                    _vehicle.GetDriverCameraPosition(
                        new RuntimeDriverCameraInfo(
                            target.Camera.X,
                            target.Camera.Y,
                            target.Camera.Z,
                            target.Camera.EyeDistance,
                            target.Camera.FieldOfViewDegrees,
                            target.Camera.HeadingDegrees,
                            target.Camera.PitchDegrees));

                BeginSceneCameraCache();

                var reflectionFovRadians =
                    DegreesToRadians(
                        Math.Clamp(
                            target.Camera.FieldOfViewDegrees,
                            18.0,
                            120.0));

                _skyViewParametersOverride =
                    new Vector4(
                        _vehicle.HeadingRadians +
                        DegreesToRadians(
                            target.Camera.HeadingDegrees),
                        DegreesToRadians(
                            target.Camera.PitchDegrees),
                        MathF.Tan(
                            reflectionFovRadians *
                            0.5f),
                        1.0f);

                _deviceContext.OMSetRenderTargets(
                    target.RenderTargetView,
                    _reflectionDepthStencilView);

                _deviceContext.ClearRenderTargetView(
                    target.RenderTargetView,
                    new Color4(
                        0.025f,
                        0.035f,
                        0.055f,
                        1.0f));

                _deviceContext.ClearDepthStencilView(
                    _reflectionDepthStencilView,
                    DepthStencilClearFlags.Depth,
                    1.0f,
                    0);

                _deviceContext.RSSetViewport(
                    0,
                    0,
                    _reflectionTextureSize,
                    _reflectionTextureSize);

                DrawSky();
                DrawObjects(
                    RuntimeSceneryRenderPass.PreSurface);
                DrawTerrain();
                DrawObjects(
                    RuntimeSceneryRenderPass.Surface);
                DrawSplines();
                DrawObjects(
                    RuntimeSceneryRenderPass.OnSurface);
                DrawObjects(
                    RuntimeSceneryRenderPass.One);
                DrawObjects(
                    RuntimeSceneryRenderPass.Two);
                DrawObjects(
                    RuntimeSceneryRenderPass.Three);
                DrawTrafficVehicles();
                DrawTrafficVehicleLights();
                DrawObjects(
                    RuntimeSceneryRenderPass.Four);

                _renderingReflectionPass =
                    true;

                try
                {
                    DrawVehicle();
                }
                finally
                {
                    _renderingReflectionPass =
                        false;
                }

                target.HasRendered =
                    true;

                EndSceneCameraCache();
            }
        }
        finally
        {
            _renderingReflectionPass =
                false;
            _deviceContext.PSUnsetShaderResource(0);
            _deviceContext.PSUnsetShaderResource(1);
            _deviceContext.PSUnsetShaderResource(2);
            _activeRenderTargetView = null;
            _activeDepthStencilView = null;
            _viewProjectionOverride = null;
            _cameraPositionOverride = null;
            _skyViewParametersOverride = null;
            EndSceneCameraCache();
        }
    }

    private bool IsReflectionTargetVisibleInMainView(
        RuntimeReflectionTarget target)
    {
        if (_vehiclePreviewMode)
        {
            return true;
        }

        var camera =
            target.Camera;

        var center =
            _vehicle.GetDriverCameraPosition(
                new RuntimeDriverCameraInfo(
                    camera.X,
                    camera.Y,
                    camera.Z,
                    camera.EyeDistance,
                    camera.FieldOfViewDegrees,
                    camera.HeadingDegrees,
                    camera.PitchDegrees));

        var radius =
            (float)Math.Max(
                camera.VisibilityThreshold ??
                    0.0,
                0.0);

        var viewProjection =
            CreateViewProjection(
                ignoreOverride: true);

        var centerClip =
            Vector4.Transform(
                new Vector4(
                    center,
                    1.0f),
                viewProjection);

        if (!float.IsFinite(
                centerClip.X) ||
            !float.IsFinite(
                centerClip.Y) ||
            !float.IsFinite(
                centerClip.W) ||
            centerClip.W <=
                0.001f)
        {
            return false;
        }

        var clipRadiusX =
            0.0f;
        var clipRadiusY =
            0.0f;

        if (radius >
            0.0001f)
        {
            Span<Vector3> offsets =
                stackalloc Vector3[3]
                {
                    Vector3.UnitX * radius,
                    Vector3.UnitY * radius,
                    Vector3.UnitZ * radius
                };

            foreach (var offset in
                     offsets)
            {
                var edgeClip =
                    Vector4.Transform(
                        new Vector4(
                            center + offset,
                            1.0f),
                        viewProjection);

                if (!float.IsFinite(
                        edgeClip.X) ||
                    !float.IsFinite(
                        edgeClip.Y) ||
                    !float.IsFinite(
                        edgeClip.W) ||
                    edgeClip.W <=
                        0.001f)
                {
                    continue;
                }

                var centerNdcX =
                    centerClip.X /
                    centerClip.W;
                var centerNdcY =
                    centerClip.Y /
                    centerClip.W;
                var edgeNdcX =
                    edgeClip.X /
                    edgeClip.W;
                var edgeNdcY =
                    edgeClip.Y /
                    edgeClip.W;

                clipRadiusX =
                    Math.Max(
                        clipRadiusX,
                        Math.Abs(
                            edgeNdcX -
                            centerNdcX));
                clipRadiusY =
                    Math.Max(
                        clipRadiusY,
                        Math.Abs(
                            edgeNdcY -
                            centerNdcY));
            }
        }

        // Give the mirror sphere a small guard band so camera vibration at
        // the edge of the picture cannot make its texture flicker.
        const float guardBand =
            0.08f;

        var ndcX =
            centerClip.X /
            centerClip.W;
        var ndcY =
            centerClip.Y /
            centerClip.W;

        return ndcX >=
                   -1.0f -
                       clipRadiusX -
                       guardBand &&
               ndcX <=
                   1.0f +
                       clipRadiusX +
                       guardBand &&
               ndcY >=
                   -1.0f -
                       clipRadiusY -
                       guardBand &&
               ndcY <=
                   1.0f +
                       clipRadiusY +
                       guardBand;
    }

    private bool ShouldRenderReflectionTarget(
        RuntimeReflectionTarget target)
    {
        if (_reflectionMode is
            "off" or
            "none" or
            "disabled")
        {
            return false;
        }

        // [add_camera_reflexion_2]'s optional value is the radius of the
        // mirror-visibility sphere. OMSI/openOMSI skip the reflection redraw
        // when that sphere is outside the main camera frustum. This avoids a
        // complete secondary scene pass for mirrors the player cannot see.
        if (!IsReflectionTargetVisibleInMainView(
                target))
        {
            return false;
        }

        if (!target.HasRendered ||
            target.Camera.ContinuousRendering ||
            _reflectionMode is
                "full" or
                "complete")
        {
            return true;
        }

        if (_reflectionMode !=
            "economy")
        {
            return true;
        }

        var interval =
            Math.Clamp(
                (target.Camera.Index +
                 1) *
                    2,
                2,
                12);

        var framePressure =
            _lastObservedFrameMilliseconds >
                    0.0
                ? _lastObservedFrameMilliseconds /
                    Math.Max(
                        _targetFrameMilliseconds,
                        1.0)
                : 1.0;

        if (framePressure >=
            1.50)
        {
            interval =
                Math.Min(
                    interval *
                        2,
                    24);
        }
        else if (framePressure >=
                 1.15)
        {
            interval =
                Math.Min(
                    interval +
                        2,
                    18);
        }

        return (_reflectionFrameIndex +
                target.Camera.Index) %
               interval ==
               0;
    }

    private ID3D11RenderTargetView?
        CurrentRenderTargetView =>
            _activeRenderTargetView ??
            _renderTargetView;

    private ID3D11DepthStencilView?
        CurrentDepthStencilView =>
            _activeDepthStencilView ??
            _depthStencilView;

    private Vector3 CurrentCameraPosition =>
        _cameraPositionOverride ??
        ResolveActiveCameraPosition();

    private bool CanDrawTerrain() =>
        _terrainVertexBuffer is not null &&
        _terrainCameraBuffer is not null &&
        _terrainVertexShader is not null &&
        _terrainPixelShader is not null &&
        _terrainInputLayout is not null &&
        _terrainVertexCount > 0;

    private void DrawTerrain()
    {
        if (_deviceContext is null ||
            CurrentRenderTargetView is null ||
            _terrainVertexBuffer is null ||
            _terrainCameraBuffer is null ||
            _terrainVertexShader is null ||
            _terrainPixelShader is null ||
            _terrainTexturedPixelShader is null ||
            _terrainBaseDetailPixelShader is null ||
            _terrainLayerPixelShader is null ||
            _terrainLayerDetailPixelShader is null ||
            _terrainLightmapPixelShader is null ||
            _terrainInputLayout is null ||
            _terrainTextureSampler is null ||
            _terrainMaskSampler is null)
        {
            return;
        }

        _deviceContext.OMSetRenderTargets(
            CurrentRenderTargetView,
            CurrentDepthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            _terrainInputLayout);

        _deviceContext.IASetVertexBuffer(
            0,
            _terrainVertexBuffer,
            RuntimeTerrainVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _terrainVertexShader);

        _deviceContext.RSSetState(
            _terrainRasterizerState);

        Span<RuntimeCameraConstants> constants =
            stackalloc RuntimeCameraConstants[1];

        constants[0] =
            new RuntimeCameraConstants
            {
                ViewProjection =
                    CurrentSceneViewProjection,
                CameraPosition =
                    CurrentSceneCameraPosition,
                CameraPadding =
                    0.0f
            };

        _terrainCameraBuffer.SetData(
            _deviceContext,
            constants,
            MapMode.WriteDiscard);

        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);

        var usePerformanceTextureLod =
            _lastObservedFrameMilliseconds >
                _targetFrameMilliseconds *
                1.20;

        _deviceContext.PSSetSampler(
            0,
            usePerformanceTextureLod &&
                    _terrainTextureSamplerPerformance is not null
                ? _terrainTextureSamplerPerformance
                : _terrainTextureSampler);

        _deviceContext.PSSetSampler(
            1,
            _terrainMaskSampler);

        _deviceContext.OMSetBlendState(
            null);
        _deviceContext.PSUnsetShaderResource(
            0);
        _deviceContext.PSUnsetShaderResource(
            1);
        _deviceContext.PSUnsetShaderResource(
            2);

        ID3D11BlendState? activeBlendState =
            null;
        ID3D11PixelShader? activePixelShader =
            null;
        ID3D11ShaderResourceView? activeTextureView =
            null;
        ID3D11ShaderResourceView? activeMaskView =
            null;
        ID3D11ShaderResourceView? activeDetailView =
            null;

        foreach (var batch in
            _terrainGeometry.Batches)
        {
            if (batch.VertexCount ==
                0)
            {
                continue;
            }

            RuntimeGpuTexture? texture =
                null;
            RuntimeGpuTexture? mask =
                null;
            RuntimeGpuTexture? detail =
                null;

            var hasTexture =
                batch.TexturePath is
                    { Length: > 0 } texturePath &&
                _objectTextureCache.TryGetValue(
                    texturePath,
                    out texture);

            var hasMask =
                batch.MaskTexturePath is
                    { Length: > 0 } maskPath &&
                _objectTextureCache.TryGetValue(
                    maskPath,
                    out mask);

            var hasDetail =
                batch.DetailTexturePath is
                    { Length: > 0 } detailPath &&
                _objectTextureCache.TryGetValue(
                    detailPath,
                    out detail);

            ID3D11BlendState? desiredBlendState =
                null;
            ID3D11PixelShader desiredPixelShader;
            ID3D11ShaderResourceView? desiredTextureView =
                null;
            ID3D11ShaderResourceView? desiredMaskView =
                null;
            ID3D11ShaderResourceView? desiredDetailView =
                null;

            if (batch.AdditiveLightmap &&
                hasTexture)
            {
                desiredBlendState =
                    _terrainAdditiveBlendState;
                desiredPixelShader =
                    _terrainLightmapPixelShader;
                desiredTextureView =
                    texture!.View;
            }
            else if (hasMask &&
                     hasTexture)
            {
                desiredBlendState =
                    _terrainAlphaBlendState;
                desiredPixelShader =
                    hasDetail
                        ? _terrainLayerDetailPixelShader
                        : _terrainLayerPixelShader;
                desiredTextureView =
                    texture!.View;
                desiredMaskView =
                    mask!.View;
                desiredDetailView =
                    hasDetail
                        ? detail!.View
                        : null;
            }
            else if (hasTexture)
            {
                desiredPixelShader =
                    hasDetail
                        ? _terrainBaseDetailPixelShader
                        : _terrainTexturedPixelShader;
                desiredTextureView =
                    texture!.View;
                desiredDetailView =
                    hasDetail
                        ? detail!.View
                        : null;
            }
            else
            {
                desiredPixelShader =
                    _terrainPixelShader;
            }

            if (!ReferenceEquals(
                    activeBlendState,
                    desiredBlendState))
            {
                _deviceContext.OMSetBlendState(
                    desiredBlendState);
                activeBlendState =
                    desiredBlendState;
            }

            if (!ReferenceEquals(
                    activePixelShader,
                    desiredPixelShader))
            {
                _deviceContext.PSSetShader(
                    desiredPixelShader);
                activePixelShader =
                    desiredPixelShader;
            }

            if (!ReferenceEquals(
                    activeTextureView,
                    desiredTextureView))
            {
                if (desiredTextureView is null)
                {
                    _deviceContext.PSUnsetShaderResource(
                        0);
                }
                else
                {
                    _deviceContext.PSSetShaderResource(
                        0,
                        desiredTextureView);
                }

                activeTextureView =
                    desiredTextureView;
            }

            if (!ReferenceEquals(
                    activeMaskView,
                    desiredMaskView))
            {
                if (desiredMaskView is null)
                {
                    _deviceContext.PSUnsetShaderResource(
                        1);
                }
                else
                {
                    _deviceContext.PSSetShaderResource(
                        1,
                        desiredMaskView);
                }

                activeMaskView =
                    desiredMaskView;
            }

            if (!ReferenceEquals(
                    activeDetailView,
                    desiredDetailView))
            {
                if (desiredDetailView is null)
                {
                    _deviceContext.PSUnsetShaderResource(
                        2);
                }
                else
                {
                    _deviceContext.PSSetShaderResource(
                        2,
                        desiredDetailView);
                }

                activeDetailView =
                    desiredDetailView;
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.OMSetBlendState(
            null);
        _deviceContext.OMSetDepthStencilState(
            null);
        _deviceContext.PSUnsetShaderResource(
            0);
        _deviceContext.PSUnsetShaderResource(
            1);
        _deviceContext.PSUnsetShaderResource(
            2);
        _deviceContext.PSUnsetShaderResource(
            3);
        _deviceContext.RSSetState(
            null);
    }

    private void DrawSplines()
    {
        DrawTexturedGeometry(
            _splineVertexBuffer,
            _splineVertexCount,
            _splineGeometry.Batches);
    }

    private void DrawObjects(
        RuntimeSceneryRenderPass renderPass)
    {
        if (!_objectBatchesByRenderPass.TryGetValue(
                renderPass,
                out var batches) ||
            batches.Length ==
                0)
        {
            return;
        }

        DrawTexturedGeometry(
            _objectVertexBuffer,
            _objectVertexCount,
            batches,
            renderPass,
            batchesMatchRenderPass: true);
    }

    private void RebuildObjectRenderPassBatches()
    {
        _objectBatchesByRenderPass.Clear();

        foreach (RuntimeSceneryRenderPass renderPass in
                 Enum.GetValues<
                     RuntimeSceneryRenderPass>())
        {
            _objectBatchesByRenderPass[
                renderPass] =
                _objectGeometry.Batches
                    .Where(
                        batch =>
                            batch.VertexCount >
                                0 &&
                            MatchesSceneryRenderPass(
                                batch.RenderType,
                                renderPass))
                    .ToArray();
        }
    }

    private static long ResolveStreamingTextureBudgetBytes()
    {
        const long megabyte =
            1024L *
            1024L;

        var environmentValue =
            Environment.GetEnvironmentVariable(
                "OMSI_TEXTURE_MEMORY");

        if (long.TryParse(
                environmentValue,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var configuredMegabytes) &&
            configuredMegabytes >
                0)
        {
            return Math.Clamp(
                configuredMegabytes *
                    megabyte,
                256L *
                    megabyte,
                16_384L *
                    megabyte);
        }

        var availableMemory =
            GC.GetGCMemoryInfo()
                .TotalAvailableMemoryBytes;

        if (availableMemory <=
            0)
        {
            return DefaultMaximumStreamingTextureCacheBytes;
        }

        // Mirror openOMSI's automatic policy: texture memory gets a
        // conservative fraction of system memory, with a sane cap when
        // adapter-specific memory is unavailable.
        return Math.Clamp(
            availableMemory /
                8L,
            512L *
                megabyte,
            1600L *
                megabyte);
    }

    private static bool MatchesSceneryRenderPass(
        string? renderType,
        RuntimeSceneryRenderPass renderPass)
    {
        var normalized =
            renderType?
                .Trim()
                .ToLowerInvariant();

        return renderPass switch
        {
            RuntimeSceneryRenderPass.PreSurface =>
                normalized ==
                    "presurface",
            RuntimeSceneryRenderPass.Surface =>
                normalized ==
                    "surface",
            RuntimeSceneryRenderPass.OnSurface =>
                normalized ==
                    "on_surface",
            RuntimeSceneryRenderPass.One =>
                normalized ==
                    "1",
            RuntimeSceneryRenderPass.Three =>
                normalized ==
                    "3",
            RuntimeSceneryRenderPass.Four =>
                normalized ==
                    "4",
            _ =>
                string.IsNullOrWhiteSpace(
                    normalized) ||
                normalized ==
                    "2" ||
                normalized is not
                    ("presurface" or
                     "surface" or
                     "on_surface" or
                     "1" or
                     "3" or
                     "4")
        };
    }

    private bool IsSceneryBatchVisible(
        RuntimeObjectBatch batch,
        Vector3 cameraPosition,
        Matrix4x4 viewProjection)
    {
        if (!batch.BoundsCenter.HasValue)
        {
            return true;
        }

        var center =
            batch.BoundsCenter.Value;
        var radius =
            MathF.Max(
                batch.BoundsRadius,
                0.0f);
        var offset =
            center -
            cameraPosition;
        var distanceSquared =
            offset.LengthSquared();
        var maximumDistance =
            (float)_maximumObjectVisibilityMeters +
            radius;

        if (distanceSquared >
            maximumDistance *
            maximumDistance)
        {
            return false;
        }

        if (distanceSquared <=
            (TrafficFrustumCullNearDistanceMeters +
             radius) *
            (TrafficFrustumCullNearDistanceMeters +
             radius))
        {
            return true;
        }

        var clip =
            Vector4.Transform(
                new Vector4(
                    center,
                    1.0f),
                viewProjection);

        if (!float.IsFinite(
                clip.X) ||
            !float.IsFinite(
                clip.Y) ||
            !float.IsFinite(
                clip.W))
        {
            return true;
        }

        if (clip.W <=
            0.001f)
        {
            return false;
        }

        var framePressure =
            _lastObservedFrameMilliseconds >
                    0.0
                ? _lastObservedFrameMilliseconds /
                    Math.Max(
                        _targetFrameMilliseconds,
                        1.0)
                : 1.0;

        if (distanceSquared >
                180.0f *
                180.0f &&
            radius >
                0.0f)
        {
            var projectionScale =
                MathF.Max(
                    MathF.Abs(
                        viewProjection.M11),
                    MathF.Abs(
                        viewProjection.M22));

            var projectedRadiusNdc =
                radius *
                projectionScale /
                MathF.Abs(
                    clip.W);

            var viewportPixels =
                _viewProjectionOverride.HasValue
                    ? (int)Math.Max(
                        _reflectionTextureSize,
                        1u)
                    : Math.Max(
                        1,
                        Math.Min(
                            ClientSize.Width,
                            ClientSize.Height));

            var projectedDiameterPixels =
                projectedRadiusNdc *
                viewportPixels;

            // openOMSI applies a minimum projected object size even before
            // the frame is overloaded. Keep our baseline deliberately
            // sub-pixel so distant poles/details disappear only after they
            // are no longer resolvable, then become more aggressive under
            // actual frame pressure.
            var minimumVisiblePixels =
                framePressure >=
                        1.50
                    ? 2.5f
                    : framePressure >=
                            1.15
                        ? 1.5f
                        : 0.65f;

            if (projectedDiameterPixels <
                minimumVisiblePixels)
            {
                return false;
            }
        }

        var margin =
            MathF.Abs(
                clip.W) *
            SceneryFrustumCullMargin +
            radius *
            SceneryFrustumRadiusScale;

        return clip.X >=
                   -margin &&
               clip.X <=
                   margin &&
               clip.Y >=
                   -margin &&
               clip.Y <=
                   margin;
    }

    private void DrawTexturedGeometry(
        ID3D11Buffer? vertexBuffer,
        uint vertexCount,
        IReadOnlyList<RuntimeObjectBatch> batches,
        RuntimeSceneryRenderPass? sceneryRenderPass = null,
        bool batchesMatchRenderPass = false)
    {
        if (_deviceContext is null ||
            CurrentRenderTargetView is null ||
            vertexBuffer is null ||
            _terrainCameraBuffer is null ||
            _objectVertexShader is null ||
            _objectColorPixelShader is null ||
            _objectTexturedPixelShader is null ||
            _objectAlphaCutoutPixelShader is null ||
            _objectAlphaBlendPixelShader is null ||
            _objectAlphaCutoutTransMapPixelShader is null ||
            _objectAlphaBlendTransMapPixelShader is null ||
            _objectInputLayout is null ||
            _objectSampler is null ||
            vertexCount == 0)
        {
            return;
        }

        _deviceContext.OMSetRenderTargets(
            CurrentRenderTargetView,
            CurrentDepthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);
        _deviceContext.IASetInputLayout(
            _objectInputLayout);
        _deviceContext.IASetVertexBuffer(
            0,
            vertexBuffer,
            RuntimeObjectVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _objectVertexShader);
        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);
        var usePerformanceTextureLod =
            _lastObservedFrameMilliseconds >
                _targetFrameMilliseconds *
                1.20;

        _deviceContext.PSSetSampler(
            0,
            _objectSampler);
        _deviceContext.RSSetState(
            _terrainRasterizerState);

        var cullScenery =
            sceneryRenderPass.HasValue;
        var cameraPosition =
            cullScenery
                ? CurrentSceneCameraPosition
                : Vector3.Zero;
        var viewProjection =
            cullScenery
                ? CurrentSceneViewProjection
                : Matrix4x4.Identity;

        // Every scenery pass starts from a known state, then only changes
        // D3D11 bindings when the next batch actually needs a different
        // state. Large OMSI maps commonly contain thousands of adjacent
        // batches sharing the same depth/blend/shader setup, so avoiding
        // redundant driver calls cuts CPU overhead without changing draw
        // order or material semantics.
        _deviceContext.OMSetBlendState(null);
        _deviceContext.OMSetDepthStencilState(null);
        _deviceContext.PSUnsetShaderResource(0);
        _deviceContext.PSUnsetShaderResource(1);
        _deviceContext.PSUnsetShaderResource(2);

        ID3D11BlendState? activeBlendState =
            null;
        ID3D11DepthStencilState? activeDepthState =
            null;
        ID3D11PixelShader? activePixelShader =
            null;
        ID3D11ShaderResourceView? activeDiffuseView =
            null;
        ID3D11ShaderResourceView? activeTransMapView =
            null;
        ID3D11SamplerState activeObjectSampler =
            _objectSampler;

        foreach (var batch in batches)
        {
            if (batch.VertexCount == 0 ||
                (sceneryRenderPass.HasValue &&
                 !batchesMatchRenderPass &&
                 !MatchesSceneryRenderPass(
                     batch.RenderType,
                     sceneryRenderPass.Value)) ||
                (cullScenery &&
                 !IsSceneryBatchVisible(
                     batch,
                     cameraPosition,
                     viewProjection)) ||
                !IsDynamicSceneryBatchVisible(
                    batch))
            {
                continue;
            }

            var textureBudgetPressure =
                _maximumStreamingTextureCacheBytes >
                        0
                    ? _currentStreamingTextureCacheBytes /
                        (double)_maximumStreamingTextureCacheBytes
                    : 0.0;

            var useFarTextureLod =
                false;

            if (cullScenery &&
                _objectSamplerPerformance is not null &&
                batch.BoundsCenter.HasValue &&
                textureBudgetPressure >=
                    0.90)
            {
                var distanceSquared =
                    Vector3.DistanceSquared(
                        batch.BoundsCenter.Value,
                        cameraPosition);

                useFarTextureLod =
                    distanceSquared >=
                    150.0f *
                    150.0f;
            }

            var desiredObjectSampler =
                (usePerformanceTextureLod ||
                 useFarTextureLod) &&
                        _objectSamplerPerformance is not null
                    ? _objectSamplerPerformance
                    : _objectSampler;

            if (!ReferenceEquals(
                    activeObjectSampler,
                    desiredObjectSampler))
            {
                _deviceContext.PSSetSampler(
                    0,
                    desiredObjectSampler);

                activeObjectSampler =
                    desiredObjectSampler;
            }

            var desiredBlendState =
                batch.AlphaBlend
                    ? _objectAlphaBlendState
                    : null;

            if (!ReferenceEquals(
                    activeBlendState,
                    desiredBlendState))
            {
                _deviceContext.OMSetBlendState(
                    desiredBlendState);
                activeBlendState =
                    desiredBlendState;
            }

            var desiredDepthState =
                batch.NoZCheck
                    ? _objectDepthDisabledState
                    : batch.NoZWrite ||
                      batch.AlphaBlend
                        ? _objectDepthReadState
                        : null;

            if (!ReferenceEquals(
                    activeDepthState,
                    desiredDepthState))
            {
                _deviceContext.OMSetDepthStencilState(
                    desiredDepthState);
                activeDepthState =
                    desiredDepthState;
            }

            if (!string.IsNullOrWhiteSpace(
                    batch.TexturePath) &&
                _objectTextureCache.TryGetValue(
                    batch.TexturePath,
                    out var texture))
            {
                RuntimeGpuTexture? transMap =
                    null;

                var requiresExternalTransMap =
                    batch.RequiresExternalTransMap ||
                    !string.IsNullOrWhiteSpace(
                        batch.TransMapTexturePath);

                var hasTransMap =
                    !string.IsNullOrWhiteSpace(
                        batch.TransMapTexturePath) &&
                    _objectTextureCache.TryGetValue(
                        batch.TransMapTexturePath!,
                        out transMap);

                if (requiresExternalTransMap &&
                    !hasTransMap)
                {
                    // Native OMSI treats an external [matl_transmap] as the
                    // authoritative transparency source. Falling back to the
                    // diffuse texture alpha when that mask failed to load
                    // turns signs, arrows and decals into opaque rectangles.
                    ReportMissingTransMap(
                        "scenery",
                        batch.TexturePath,
                        batch.TransMapTexturePath);
                    continue;
                }

                var desiredTransMapView =
                    hasTransMap
                        ? transMap!.View
                        : null;

                if (!ReferenceEquals(
                        activeTransMapView,
                        desiredTransMapView))
                {
                    if (desiredTransMapView is null)
                    {
                        _deviceContext.PSUnsetShaderResource(
                            1);
                    }
                    else
                    {
                        _deviceContext.PSSetShaderResource(
                            1,
                            desiredTransMapView);
                    }

                    activeTransMapView =
                        desiredTransMapView;
                }

                var desiredPixelShader =
                    batch.AlphaCutout
                        ? hasTransMap
                            ? _objectAlphaCutoutTransMapPixelShader
                            : _objectAlphaCutoutPixelShader
                        : batch.AlphaBlend
                            ? hasTransMap
                                ? _objectAlphaBlendTransMapPixelShader
                                : _objectAlphaBlendPixelShader
                            : _objectTexturedPixelShader;

                if (!ReferenceEquals(
                        activePixelShader,
                        desiredPixelShader))
                {
                    _deviceContext.PSSetShader(
                        desiredPixelShader);
                    activePixelShader =
                        desiredPixelShader;
                }

                if (!ReferenceEquals(
                        activeDiffuseView,
                        texture.View))
                {
                    _deviceContext.PSSetShaderResource(
                        0,
                        texture.View);
                    activeDiffuseView =
                        texture.View;
                }
            }
            else if (!string.IsNullOrWhiteSpace(
                         batch.TexturePath))
            {
                // A textured OMSI surface with an unresolved texture is not
                // an untextured white polygon. Path markings, decals and
                // transparent helper surfaces otherwise become large white
                // rectangles. Keep the missing asset visible in diagnostics,
                // but do not fabricate a color fallback.
                continue;
            }
            else
            {
                if (activeDiffuseView is not null)
                {
                    _deviceContext.PSUnsetShaderResource(
                        0);
                    activeDiffuseView =
                        null;
                }

                if (activeTransMapView is not null)
                {
                    _deviceContext.PSUnsetShaderResource(
                        1);
                    activeTransMapView =
                        null;
                }

                if (!ReferenceEquals(
                        activePixelShader,
                        _objectColorPixelShader))
                {
                    _deviceContext.PSSetShader(
                        _objectColorPixelShader);
                    activePixelShader =
                        _objectColorPixelShader;
                }
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.OMSetBlendState(null);
        _deviceContext.OMSetDepthStencilState(null);
        _deviceContext.PSUnsetShaderResource(0);
        _deviceContext.PSUnsetShaderResource(1);
        _deviceContext.PSUnsetShaderResource(2);
        _deviceContext.RSSetState(null);
    }

    private void RebuildRuntimeTrafficSegmentLookup()
    {
        _runtimeTrafficSegmentByIndex.Clear();
        _runtimeTrafficPathConflictCache.Clear();

        foreach (var segment in
                 _windowInfo.TrafficPaths.Segments)
        {
            _runtimeTrafficSegmentByIndex[
                segment.Index] =
                segment;
        }
    }

    private void RebuildTrafficSignalStateLookup()
    {
        _trafficSignalStateBySegmentIndex.Clear();

        foreach (var state in
                 _trafficSignalStates)
        {
            _trafficSignalStateBySegmentIndex[
                state.SegmentIndex] =
                state;
        }
    }

    private void RebuildRailSignalRuntimeLookup()
    {
        _railSignalRuntimeByObjectId.Clear();

        foreach (var state in
                 _railSignalRouteStates)
        {
            if (state.ScriptRuntime is null)
            {
                continue;
            }

            _railSignalRuntimeByObjectId[
                state.SignalObjectId] =
                state.ScriptRuntime;
        }
    }

    private bool IsDynamicSceneryBatchVisible(
        RuntimeObjectBatch batch)
    {
        var conditions =
            batch.VisibilityConditions;

        if (conditions is null ||
            conditions.Count ==
                0 ||
            batch.ObjectId <
                0)
        {
            return true;
        }

        if (!_railSignalRuntimeByObjectId.TryGetValue(
                batch.ObjectId,
                out var runtime))
        {
            return true;
        }

        foreach (var condition in
                 conditions)
        {
            if (!runtime.HasLocalVariable(
                    condition.VariableName))
            {
                return false;
            }

            var value =
                runtime.GetLocal(
                    condition.VariableName);

            if (Math.Abs(
                    value -
                    condition.Value) >
                0.000001)
            {
                return false;
            }
        }

        return true;
    }

    private bool UseExteriorVehicleView() =>
        !_driveMode ||
        _vehicleViewMode ==
            RuntimeVehicleViewMode.Exterior ||
        (_vehicleViewMode ==
             RuntimeVehicleViewMode.Driver &&
         _windowInfo.Vehicle?.DriverCameras.Count is
             not > 0) ||
        (_vehicleViewMode ==
             RuntimeVehicleViewMode.Passenger &&
         _windowInfo.Vehicle?.PassengerCameras.Count is
             not > 0 &&
         _windowInfo.Vehicle?.DriverCameras.Count is
             not > 0);

    private static RuntimeObjectBatch[]
        BuildTrafficVehicleRenderBatches(
            RuntimeObjectGeometry geometry) =>
        geometry.Batches
            .Where(
                static batch =>
                    batch.VertexCount >
                        0)
            .OrderBy(
                static batch =>
                    batch.AlphaBlend
                        ? 1
                        : 0)
            .ToArray();

    private RuntimeObjectMeshInfo[]
        ResolveTrafficVehicleLightMeshes(
            string vehiclePath,
            RuntimeVehicleInfo vehicleInfo)
    {
        if (_trafficVehicleLightMeshes.TryGetValue(
                vehiclePath,
                out var cached))
        {
            return cached;
        }

        var allLightMeshes =
            vehicleInfo.Meshes
                .Where(
                    static mesh =>
                        mesh.LightEffects is
                            { Count: > 0 })
                .ToArray();

        var viewpointMeshes =
            allLightMeshes
                .Where(
                    static mesh =>
                        IsVehicleMeshVisibleFromViewpoint(
                            mesh.ViewpointFlag,
                            4))
                .ToArray();

        var selected =
            viewpointMeshes.Length >
                0
                ? viewpointMeshes
                : allLightMeshes;

        _trafficVehicleLightMeshes[
            vehiclePath] =
            selected;

        return selected;
    }

    private bool IsTrafficAgentVisible(
        RuntimeTrafficAgentInfo agent,
        Vector3 cameraPosition,
        Matrix4x4 viewProjection)
    {
        var dx =
            agent.X -
            cameraPosition.X;
        var dy =
            agent.Y -
            cameraPosition.Y;
        var dz =
            agent.Z -
            cameraPosition.Z;

        var distanceSquared =
            dx * dx +
            dy * dy +
            dz * dz;

        var maximumDistanceSquared =
            _maximumObjectVisibilityMeters *
            _maximumObjectVisibilityMeters;

        if (distanceSquared >
            maximumDistanceSquared)
        {
            return false;
        }

        if (distanceSquared <=
            TrafficFrustumCullNearDistanceMeters *
            TrafficFrustumCullNearDistanceMeters)
        {
            return true;
        }

        var clip =
            Vector4.Transform(
                new Vector4(
                    (float)agent.X,
                    (float)agent.Y,
                    (float)agent.Z,
                    1.0f),
                viewProjection);

        if (!float.IsFinite(
                clip.X) ||
            !float.IsFinite(
                clip.Y) ||
            !float.IsFinite(
                clip.W) ||
            clip.W <=
                0.001f)
        {
            return false;
        }

        var margin =
            clip.W *
            TrafficFrustumCullMargin;

        return clip.X >=
                   -margin &&
               clip.X <=
                   margin &&
               clip.Y >=
                   -margin &&
               clip.Y <=
                   margin;
    }

    private void ResolveTrafficRenderPose(
        RuntimeTrafficAgentInfo agent,
        out float x,
        out float z,
        out float heading)
    {
        x =
            (float)agent.X;
        z =
            (float)agent.Z;
        heading =
            (float)agent.HeadingRadians;

        if (agent.AgentIndex >=
                2_000_000 ||
            _lastTrafficSimulationStepSeconds <=
                0.0 ||
            !double.IsFinite(
                agent.SpeedMetersPerSecond) ||
            agent.SpeedMetersPerSecond <=
                0.001)
        {
            return;
        }

        var elapsed =
            _frameClock.Elapsed.TotalSeconds -
            _lastTrafficSimulationStepSeconds;

        if (!double.IsFinite(
                elapsed) ||
            elapsed <=
                0.0)
        {
            return;
        }

        // Keep the authoritative AI state on its lower-rate simulation tick,
        // but render a short prediction between ticks. This mirrors the
        // openOMSI separation of simulation work from presentation: rules,
        // collision and scripts still use the exact snapshot.
        elapsed =
            Math.Clamp(
                elapsed,
                0.0,
                0.060);

        var travel =
            (float)(
                agent.SpeedMetersPerSecond *
                elapsed);

        if (travel <=
            0.0001f)
        {
            return;
        }

        var curvature =
            double.IsFinite(
                    agent.PathCurvaturePerMeter)
                ? (float)agent.PathCurvaturePerMeter
                : 0.0f;

        if (MathF.Abs(
                curvature) <
            0.00001f)
        {
            x +=
                MathF.Sin(
                    heading) *
                travel;
            z +=
                MathF.Cos(
                    heading) *
                travel;
            return;
        }

        var nextHeading =
            heading +
            curvature *
                travel;

        x +=
            (
                MathF.Cos(
                    heading) -
                MathF.Cos(
                    nextHeading)
            ) /
            curvature;

        z +=
            (
                MathF.Sin(
                    nextHeading) -
                MathF.Sin(
                    heading)
            ) /
            curvature;

        heading =
            nextHeading;
    }

    private bool TryUploadTrafficInstances(
        IReadOnlyList<TrafficVehicleDrawItem> drawItems,
        RuntimeObjectBatch? animationBatch = null,
        RuntimeVehicleInfo? vehicleInfo = null)
    {
        if (_device is null ||
            _deviceContext is null ||
            drawItems.Count ==
                0)
        {
            return false;
        }

        var requiredCount =
            drawItems.Count;

        if (_trafficInstanceScratch.Length <
            requiredCount)
        {
            var scratchCapacity =
                Math.Max(
                    64,
                    _trafficInstanceScratch.Length);

            while (scratchCapacity <
                   requiredCount)
            {
                scratchCapacity *=
                    2;
            }

            Array.Resize(
                ref _trafficInstanceScratch,
                scratchCapacity);
        }

        for (var index = 0;
             index < requiredCount;
             index++)
        {
            var drawItem =
                drawItems[
                    index];

            var world =
                drawItem.VehicleWorld;

            if (animationBatch is not null &&
                vehicleInfo is not null)
            {
                world =
                    CreateTrafficVehicleAnimationMatrix(
                        animationBatch,
                        drawItem.Agent,
                        vehicleInfo) *
                    world;
            }

            _trafficInstanceScratch[
                index] =
                new RuntimeTrafficInstanceData
                {
                    World =
                        world
                };
        }

        if (_trafficInstanceBuffer is null ||
            _trafficInstanceBufferCapacity <
                requiredCount)
        {
            var capacity =
                Math.Max(
                    64,
                    _trafficInstanceBufferCapacity);

            while (capacity <
                   requiredCount)
            {
                capacity *=
                    2;
            }

            _trafficInstanceBuffer?.Dispose();

            _trafficInstanceBuffer =
                _device.CreateBuffer(
                    new BufferDescription(
                        (uint)(
                            capacity *
                            RuntimeTrafficInstanceData.SizeInBytes),
                        BindFlags.VertexBuffer,
                        ResourceUsage.Dynamic,
                        CpuAccessFlags.Write));

            _trafficInstanceBufferCapacity =
                capacity;
        }

        _trafficInstanceBuffer.SetData(
            _deviceContext,
            _trafficInstanceScratch
                .AsSpan(
                    0,
                    requiredCount),
            MapMode.WriteDiscard);

        return true;
    }

    private void DrawTrafficVehicles()
    {
        if (_trafficAgents.Count ==
                0 ||
            _trafficVehicleGeometries.Count ==
                0 ||
            _deviceContext is null ||
            CurrentRenderTargetView is null ||
            CurrentDepthStencilView is null ||
            _vehicleModelBuffer is null ||
            _vehicleMaterialBuffer is null ||
            _vehicleSkinBuffer is null ||
            _vehicleVertexShader is null ||
            _vehicleColorPixelShader is null ||
            _vehicleTexturedPixelShader is null ||
            _vehicleAlphaCutoutPixelShader is null ||
            _vehicleAlphaBlendPixelShader is null ||
            _vehicleAlphaCutoutTransMapPixelShader is null ||
            _vehicleAlphaBlendTransMapPixelShader is null ||
            _vehicleInputLayout is null ||
            _vehicleSampler is null ||
            _terrainCameraBuffer is null ||
            _windowInfo.TrafficVehicleAssets is null)
        {
            return;
        }

        foreach (var drawItems in
                 _trafficVisibleDrawItemsByVehiclePath.Values)
        {
            drawItems.Clear();
        }

        _trafficVisibleDrawItems.Clear();

        var cameraPosition =
            CurrentSceneCameraPosition;
        var viewProjection =
            CurrentSceneViewProjection;

        foreach (var agent in
                 _trafficAgents)
        {
            if (!IsTrafficAgentVisible(
                    agent,
                    cameraPosition,
                    viewProjection) ||
                !_trafficVehicleGeometries.ContainsKey(
                    agent.VehiclePath) ||
                !_trafficVehicleVertexBuffers.ContainsKey(
                    agent.VehiclePath) ||
                !_windowInfo.TrafficVehicleAssets.TryGetValue(
                    agent.VehiclePath,
                    out var vehicleInfo))
            {
                continue;
            }

            var heightOffset =
                (float)(
                    vehicleInfo.Physics.AiDeltaHeightMeters ??
                    0.0);

            ResolveTrafficRenderPose(
                agent,
                out var renderX,
                out var renderZ,
                out var renderHeading);

            var drawItem =
                new TrafficVehicleDrawItem(
                    agent,
                    Matrix4x4.CreateRotationY(
                        renderHeading) *
                    Matrix4x4.CreateTranslation(
                        renderX,
                        (float)agent.Y +
                            heightOffset,
                        renderZ));

            _trafficVisibleDrawItems.Add(
                drawItem);

            if (!_trafficVisibleDrawItemsByVehiclePath.TryGetValue(
                    agent.VehiclePath,
                    out var vehicleDrawItems))
            {
                vehicleDrawItems =
                    [];
                _trafficVisibleDrawItemsByVehiclePath[
                    agent.VehiclePath] =
                    vehicleDrawItems;
            }

            vehicleDrawItems.Add(
                drawItem);
        }

        if (_trafficVisibleDrawItems.Count ==
            0)
        {
            return;
        }

        Span<RuntimeModelConstants> model =
            stackalloc RuntimeModelConstants[1];

        Span<RuntimeVehicleMaterialConstants> materialConstants =
            stackalloc RuntimeVehicleMaterialConstants[1];

        Span<RuntimeVehicleSkinConstants> skinConstants =
            stackalloc RuntimeVehicleSkinConstants[1];

        skinConstants[0] =
            new RuntimeVehicleSkinConstants
            {
                Bone0 = Matrix4x4.Identity,
                Bone1 = Matrix4x4.Identity,
                Bone2 = Matrix4x4.Identity,
                Bone3 = Matrix4x4.Identity
            };

        _deviceContext.OMSetRenderTargets(
            CurrentRenderTargetView,
            CurrentDepthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            _vehicleInputLayout);

        _deviceContext.VSSetShader(
            _vehicleVertexShader);

        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);

        _deviceContext.VSSetConstantBuffer(
            1,
            _vehicleModelBuffer);

        _deviceContext.VSSetConstantBuffer(
            3,
            _vehicleSkinBuffer);

        _vehicleSkinBuffer.SetData(
            _deviceContext,
            skinConstants,
            MapMode.WriteDiscard);

        _deviceContext.PSSetSampler(
            0,
            _vehicleSampler);

        _deviceContext.PSSetConstantBuffer(
            2,
            _vehicleMaterialBuffer);

        _deviceContext.RSSetState(
            _terrainRasterizerState);

        var trafficBindings =
            default(TrafficVehicleBindingState);

        _deviceContext.OMSetBlendState(
            null);
        _deviceContext.OMSetDepthStencilState(
            null);

        for (var slot = 0;
             slot <= 6;
             slot++)
        {
            _deviceContext.PSUnsetShaderResource(
                (uint)slot);
        }

        // Opaque/cutout geometry is safe to batch by material. Static AI
        // batches use a per-instance world matrix stream. For animated
        // wheel/steering batches, vehicles farther than the animation detail
        // radius use the same instanced base pose: at that distance the
        // wheel motion is below useful screen detail, while near vehicles
        // keep the full OMSI animation path.
        foreach (var pair in
                 _trafficVisibleDrawItemsByVehiclePath)
        {
            if (pair.Value.Count ==
                    0 ||
                !_trafficVehicleGeometries.TryGetValue(
                    pair.Key,
                    out var geometry) ||
                !_trafficVehicleVertexBuffers.TryGetValue(
                    pair.Key,
                    out var vertexBuffer) ||
                !_windowInfo.TrafficVehicleAssets.TryGetValue(
                    pair.Key,
                    out var vehicleInfo))
            {
                continue;
            }

            if (!_trafficVehicleRenderBatches.TryGetValue(
                    pair.Key,
                    out var renderBatches))
            {
                renderBatches =
                    BuildTrafficVehicleRenderBatches(
                        geometry);
                _trafficVehicleRenderBatches[
                    pair.Key] =
                    renderBatches;
            }

            if (!_trafficVehicleRenderBatchSummaries.TryGetValue(
                    pair.Key,
                    out var batchSummary))
            {
                var hasStaticOpaque =
                    false;
                var hasAnimatedOpaque =
                    false;

                foreach (var batch in
                         renderBatches)
                {
                    if (batch.AlphaBlend)
                    {
                        continue;
                    }

                    if (batch.Animations is
                        { Count: > 0 })
                    {
                        hasAnimatedOpaque =
                            true;
                    }
                    else
                    {
                        hasStaticOpaque =
                            true;
                    }

                    if (hasStaticOpaque &&
                        hasAnimatedOpaque)
                    {
                        break;
                    }
                }

                batchSummary =
                    new TrafficVehicleRenderBatchSummary(
                        hasStaticOpaque,
                        hasAnimatedOpaque);

                _trafficVehicleRenderBatchSummaries[
                    pair.Key] =
                    batchSummary;
            }

            var hasStaticOpaqueBatches =
                batchSummary.HasStaticOpaque;

            var hasAnimatedOpaqueBatches =
                batchSummary.HasAnimatedOpaque;

            var canInstance =
                pair.Value.Count >
                    1 &&
                _trafficInstancedVertexShader is not null &&
                _trafficInstancedInputLayout is not null;

            var staticInstanced =
                false;

            if (canInstance &&
                hasStaticOpaqueBatches &&
                TryUploadTrafficInstances(
                    pair.Value) &&
                _trafficInstanceBuffer is not null)
            {
                _deviceContext.IASetInputLayout(
                    _trafficInstancedInputLayout);

                _deviceContext.VSSetShader(
                    _trafficInstancedVertexShader);

                _deviceContext.IASetVertexBuffer(
                    0,
                    vertexBuffer,
                    RuntimeObjectVertex.SizeInBytes);

                _deviceContext.IASetVertexBuffer(
                    1,
                    _trafficInstanceBuffer,
                    RuntimeTrafficInstanceData.SizeInBytes);

                foreach (var batch in
                         renderBatches)
                {
                    if (batch.AlphaBlend ||
                        batch.Animations is
                            { Count: > 0 } ||
                        !TryPrepareTrafficVehicleBatch(
                            batch,
                            materialConstants,
                            ref trafficBindings))
                    {
                        continue;
                    }

                    _deviceContext.DrawInstanced(
                        batch.VertexCount,
                        (uint)pair.Value.Count,
                        batch.StartVertex,
                        0);
                }

                staticInstanced =
                    true;
            }

            var animatedInstanced =
                false;

            if (canInstance &&
                hasAnimatedOpaqueBatches)
            {
                _deviceContext.IASetInputLayout(
                    _trafficInstancedInputLayout);

                _deviceContext.VSSetShader(
                    _trafficInstancedVertexShader);

                _deviceContext.IASetVertexBuffer(
                    0,
                    vertexBuffer,
                    RuntimeObjectVertex.SizeInBytes);

                foreach (var batch in
                         renderBatches)
                {
                    if (batch.AlphaBlend ||
                        batch.Animations is not
                            { Count: > 0 })
                    {
                        continue;
                    }

                    if (!TryUploadTrafficInstances(
                            pair.Value,
                            batch,
                            vehicleInfo) ||
                        _trafficInstanceBuffer is null)
                    {
                        continue;
                    }

                    _deviceContext.IASetVertexBuffer(
                        1,
                        _trafficInstanceBuffer,
                        RuntimeTrafficInstanceData.SizeInBytes);

                    if (!TryPrepareTrafficVehicleBatch(
                            batch,
                            materialConstants,
                            ref trafficBindings))
                    {
                        continue;
                    }

                    _deviceContext.DrawInstanced(
                        batch.VertexCount,
                        (uint)pair.Value.Count,
                        batch.StartVertex,
                        0);

                    animatedInstanced =
                        true;
                }
            }

            _deviceContext.IASetInputLayout(
                _vehicleInputLayout);

            _deviceContext.VSSetShader(
                _vehicleVertexShader);

            _deviceContext.IASetVertexBuffer(
                0,
                vertexBuffer,
                RuntimeObjectVertex.SizeInBytes);

            foreach (var batch in
                     renderBatches)
            {
                var animated =
                    batch.Animations is
                        { Count: > 0 };

                if (batch.AlphaBlend ||
                    (staticInstanced &&
                     !animated) ||
                    (animatedInstanced &&
                     animated) ||
                    !TryPrepareTrafficVehicleBatch(
                        batch,
                        materialConstants,
                        ref trafficBindings))
                {
                    continue;
                }

                foreach (var drawItem in
                         pair.Value)
                {
                    model[0] =
                        new RuntimeModelConstants
                        {
                            World =
                                CreateTrafficVehicleAnimationMatrix(
                                    batch,
                                    drawItem.Agent,
                                    vehicleInfo) *
                                drawItem.VehicleWorld
                        };

                    _vehicleModelBuffer.SetData(
                        _deviceContext,
                        model,
                        MapMode.WriteDiscard);

                    _deviceContext.Draw(
                        batch.VertexCount,
                        batch.StartVertex);
                }
            }
        }

        _deviceContext.IASetInputLayout(
            _vehicleInputLayout);

        _deviceContext.VSSetShader(
            _vehicleVertexShader);

        // Keep blended meshes agent-major so windows and other transparent
        // layers retain their existing relative draw order. They run after
        // every opaque AI surface, matching the renderer's normal depth flow.
        string? activeVehiclePath =
            null;
        RuntimeVehicleInfo? activeVehicleInfo =
            null;
        RuntimeObjectBatch[]? activeRenderBatches =
            null;

        foreach (var drawItem in
                 _trafficVisibleDrawItems)
        {
            var vehiclePath =
                drawItem.Agent.VehiclePath;

            if (!string.Equals(
                    activeVehiclePath,
                    vehiclePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!_trafficVehicleGeometries.TryGetValue(
                        vehiclePath,
                        out var geometry) ||
                    !_trafficVehicleVertexBuffers.TryGetValue(
                        vehiclePath,
                        out var vertexBuffer) ||
                    !_windowInfo.TrafficVehicleAssets.TryGetValue(
                        vehiclePath,
                        out activeVehicleInfo))
                {
                    activeVehiclePath =
                        null;
                    activeRenderBatches =
                        null;
                    continue;
                }

                if (!_trafficVehicleRenderBatches.TryGetValue(
                        vehiclePath,
                        out activeRenderBatches))
                {
                    activeRenderBatches =
                        BuildTrafficVehicleRenderBatches(
                            geometry);
                    _trafficVehicleRenderBatches[
                        vehiclePath] =
                        activeRenderBatches;
                }

                _deviceContext.IASetVertexBuffer(
                    0,
                    vertexBuffer,
                    RuntimeObjectVertex.SizeInBytes);

                activeVehiclePath =
                    vehiclePath;
            }

            if (activeVehicleInfo is null ||
                activeRenderBatches is null)
            {
                continue;
            }

            foreach (var batch in
                     activeRenderBatches)
            {
                if (!batch.AlphaBlend ||
                    !TryPrepareTrafficVehicleBatch(
                        batch,
                        materialConstants,
                        ref trafficBindings))
                {
                    continue;
                }

                model[0] =
                    new RuntimeModelConstants
                    {
                        World =
                            CreateTrafficVehicleAnimationMatrix(
                                batch,
                                drawItem.Agent,
                                activeVehicleInfo) *
                            drawItem.VehicleWorld
                    };

                _vehicleModelBuffer.SetData(
                    _deviceContext,
                    model,
                    MapMode.WriteDiscard);

                _deviceContext.Draw(
                    batch.VertexCount,
                    batch.StartVertex);
            }
        }

        _deviceContext.OMSetBlendState(
            null);

        _deviceContext.OMSetDepthStencilState(
            null);

        for (var slot = 0;
             slot <= 6;
             slot++)
        {
            _deviceContext.PSUnsetShaderResource(
                (uint)slot);
        }

        _deviceContext.RSSetState(
            null);
    }

    private bool TryPrepareTrafficVehicleBatch(
        RuntimeObjectBatch batch,
        Span<RuntimeVehicleMaterialConstants> materialConstants,
        ref TrafficVehicleBindingState bindingState)
    {
        if (_deviceContext is null ||
            _vehicleMaterialBuffer is null ||
            _vehicleColorPixelShader is null ||
            _vehicleTexturedPixelShader is null ||
            _vehicleAlphaCutoutPixelShader is null ||
            _vehicleAlphaBlendPixelShader is null ||
            _vehicleAlphaCutoutTransMapPixelShader is null ||
            _vehicleAlphaBlendTransMapPixelShader is null)
        {
            return false;
        }

        var desiredMaterial =
            new RuntimeVehicleMaterialConstants
            {
                AlphaScale = 1.0f,
                LightMapStrength =
                    _materialLightMapEnabled &&
                    !string.IsNullOrWhiteSpace(
                        batch.LightMapTexturePath)
                        ? 1.0f
                        : 0.0f,
                EnvMapStrength =
                    ResolveVehicleEnvMapStrength(
                        batch.EnvMapTexturePath,
                        batch.EnvMapStrength),
                EnvMapMaskEnabled =
                    ResolveVehicleEnvMapMaskEnabled(
                        batch.EnvMapMaskTexturePath,
                        batch.HasTransMapDirective &&
                        string.IsNullOrWhiteSpace(
                            batch.TransMapTexturePath)),
                BumpMapStrength =
                    ResolveVehicleBumpMapStrength(
                        batch.BumpMapTexturePath,
                        batch.BumpMapStrength),
                BaseEmissive =
                    ResolveVehicleAllColorEmissive(
                        batch.BaseAllColor)
            };

        if (!bindingState.HasMaterial ||
            !VehicleMaterialConstantsEqual(
                bindingState.Material,
                desiredMaterial))
        {
            materialConstants[0] =
                desiredMaterial;

            _vehicleMaterialBuffer.SetData(
                _deviceContext,
                materialConstants,
                MapMode.WriteDiscard);

            bindingState.Material =
                desiredMaterial;
            bindingState.HasMaterial =
                true;
        }

        var desiredBlendState =
            batch.AlphaBlend
                ? _vehicleAlphaBlendState
                : null;

        if (!ReferenceEquals(
                bindingState.BlendState,
                desiredBlendState))
        {
            _deviceContext.OMSetBlendState(
                desiredBlendState);
            bindingState.BlendState =
                desiredBlendState;
        }

        var desiredDepthState =
            batch.AlphaBlend
                ? _vehicleDepthReadState
                : batch.NoZCheck
                    ? _vehicleDepthDisabledState
                    : batch.NoZWrite
                        ? _vehicleDepthReadState
                        : null;

        if (!ReferenceEquals(
                bindingState.DepthState,
                desiredDepthState))
        {
            _deviceContext.OMSetDepthStencilState(
                desiredDepthState);
            bindingState.DepthState =
                desiredDepthState;
        }

        ID3D11ShaderResourceView? envMapView =
            null;
        ID3D11ShaderResourceView? envMapMaskView =
            null;
        ID3D11ShaderResourceView? bumpMapView =
            null;

        if (_materialReflectionMapEnabled)
        {
            TryGetVehicleTextureView(
                batch.EnvMapTexturePath,
                out envMapView);

            TryGetVehicleTextureView(
                batch.EnvMapMaskTexturePath,
                out envMapMaskView);
        }

        if (_materialBumpMapEnabled)
        {
            TryGetVehicleTextureView(
                batch.BumpMapTexturePath,
                out bumpMapView);
        }

        SetVehicleShaderResource(
            4,
            envMapView,
            ref bindingState.EnvMapView);
        SetVehicleShaderResource(
            5,
            envMapMaskView,
            ref bindingState.EnvMapMaskView);
        SetVehicleShaderResource(
            6,
            bumpMapView,
            ref bindingState.BumpMapView);

        var hasDiffuseTexture =
            TryGetVehicleTextureView(
                batch.TexturePath,
                out var textureView);

        if (hasDiffuseTexture)
        {
            var requiresExternalTransMap =
                !string.IsNullOrWhiteSpace(
                    batch.TransMapTexturePath);

            var hasTransMap =
                TryGetVehicleTextureView(
                    batch.TransMapTexturePath,
                    out var transMapView);

            if (requiresExternalTransMap &&
                !hasTransMap)
            {
                ReportMissingTransMap(
                    "vehicle",
                    batch.TexturePath,
                    batch.TransMapTexturePath);
                return false;
            }

            ID3D11ShaderResourceView? lightMapView =
                null;

            if (_materialLightMapEnabled)
            {
                TryGetVehicleTextureView(
                    batch.LightMapTexturePath,
                    out lightMapView);
            }

            SetVehicleShaderResource(
                0,
                textureView,
                ref bindingState.DiffuseView);
            SetVehicleShaderResource(
                1,
                hasTransMap
                    ? transMapView
                    : null,
                ref bindingState.TransMapView);
            SetVehicleShaderResource(
                2,
                lightMapView,
                ref bindingState.LightMapView);
            SetVehicleShaderResource(
                3,
                null,
                ref bindingState.MaterialChangeView);

            var desiredPixelShader =
                batch.AlphaCutout
                    ? hasTransMap
                        ? _vehicleAlphaCutoutTransMapPixelShader
                        : _vehicleAlphaCutoutPixelShader
                    : batch.AlphaBlend
                        ? hasTransMap
                            ? _vehicleAlphaBlendTransMapPixelShader
                            : _vehicleAlphaBlendPixelShader
                        : _vehicleTexturedPixelShader;

            if (!ReferenceEquals(
                    bindingState.PixelShader,
                    desiredPixelShader))
            {
                _deviceContext.PSSetShader(
                    desiredPixelShader);
                bindingState.PixelShader =
                    desiredPixelShader;
            }

            return true;
        }

        if (batch.AlphaCutout ||
            batch.AlphaBlend)
        {
            return false;
        }

        SetVehicleShaderResource(
            0,
            null,
            ref bindingState.DiffuseView);
        SetVehicleShaderResource(
            1,
            null,
            ref bindingState.TransMapView);
        SetVehicleShaderResource(
            2,
            null,
            ref bindingState.LightMapView);
        SetVehicleShaderResource(
            3,
            null,
            ref bindingState.MaterialChangeView);

        if (!ReferenceEquals(
                bindingState.PixelShader,
                _vehicleColorPixelShader))
        {
            _deviceContext.PSSetShader(
                _vehicleColorPixelShader);
            bindingState.PixelShader =
                _vehicleColorPixelShader;
        }

        return true;
    }

    private void DrawTrafficVehicleLights()
    {
        if (_trafficVisibleDrawItems.Count ==
                0 ||
            _windowInfo.TrafficVehicleAssets is null ||
            _vehicleLightVertexBuffer is null ||
            _deviceContext is null ||
            CurrentRenderTargetView is null ||
            CurrentDepthStencilView is null ||
            _vehicleModelBuffer is null ||
            _vehicleMaterialBuffer is null ||
            _vehicleSkinBuffer is null ||
            _vehicleVertexShader is null ||
            _vehicleLightPixelShader is null ||
            _vehicleInputLayout is null ||
            _terrainCameraBuffer is null ||
            _terrainAdditiveBlendState is null ||
            _vehicleDepthReadState is null)
        {
            return;
        }

        Span<RuntimeModelConstants> model =
            stackalloc RuntimeModelConstants[1];

        Span<RuntimeVehicleMaterialConstants> material =
            stackalloc RuntimeVehicleMaterialConstants[1];

        Span<RuntimeVehicleSkinConstants> lightSkin =
            stackalloc RuntimeVehicleSkinConstants[1];

        lightSkin[0] =
            new RuntimeVehicleSkinConstants
            {
                Bone0 = Matrix4x4.Identity,
                Bone1 = Matrix4x4.Identity,
                Bone2 = Matrix4x4.Identity,
                Bone3 = Matrix4x4.Identity
            };

        _vehicleSkinBuffer.SetData(
            _deviceContext,
            lightSkin,
            MapMode.WriteDiscard);

        _deviceContext.OMSetRenderTargets(
            CurrentRenderTargetView,
            CurrentDepthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            _vehicleInputLayout);

        _deviceContext.IASetVertexBuffer(
            0,
            _vehicleLightVertexBuffer,
            RuntimeObjectVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _vehicleVertexShader);

        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);

        _deviceContext.VSSetConstantBuffer(
            1,
            _vehicleModelBuffer);

        _deviceContext.VSSetConstantBuffer(
            3,
            _vehicleSkinBuffer);

        _deviceContext.PSSetShader(
            _vehicleLightPixelShader);

        _deviceContext.PSSetConstantBuffer(
            2,
            _vehicleMaterialBuffer);

        _deviceContext.OMSetBlendState(
            _terrainAdditiveBlendState);

        _deviceContext.OMSetDepthStencilState(
            _vehicleDepthReadState);

        _deviceContext.RSSetState(
            _terrainRasterizerState);

        var cameraPosition =
            CurrentSceneCameraPosition;

        foreach (var drawItem in
                 _trafficVisibleDrawItems)
        {
            var agent =
                drawItem.Agent;

            if (!_windowInfo.TrafficVehicleAssets.TryGetValue(
                    agent.VehiclePath,
                    out var vehicleInfo))
            {
                continue;
            }

            var vehicleWorld =
                drawItem.VehicleWorld;

            var lightMeshes =
                ResolveTrafficVehicleLightMeshes(
                    agent.VehiclePath,
                    vehicleInfo);

            foreach (var mesh in
                     lightMeshes)
            {
                var staticTransform =
                    RuntimeObjectGeometryBuilder
                        .CreateMeshTransform(
                            mesh.Transform);

                var parentTransform =
                    staticTransform *
                    vehicleWorld;

                foreach (var light in
                         mesh.LightEffects!)
                {
                    var brightness =
                        ResolveTrafficVehicleLightValue(
                            agent,
                            light);

                    if (brightness <=
                        0.0001)
                    {
                        continue;
                    }

                    var center =
                        Vector3.Transform(
                            ConvertCfgPosition(
                                light.PositionX,
                                light.PositionY,
                                light.PositionZ),
                            parentTransform);

                    var toCamera =
                        cameraPosition -
                        center;

                    if (toCamera.LengthSquared() <
                        0.000001f)
                    {
                        continue;
                    }

                    var cameraDirection =
                        Vector3.Normalize(
                            toCamera);

                    brightness *=
                        ResolveVehicleLightDirectionalAttenuation(
                            light,
                            parentTransform,
                            cameraDirection);

                    if (brightness <=
                        0.0001)
                    {
                        continue;
                    }

                    center +=
                        cameraDirection *
                        (float)light.CameraOffsetMeters;

                    var size =
                        (float)Math.Clamp(
                            light.SizeMeters,
                            0.005,
                            20.0);

                    model[0] =
                        new RuntimeModelConstants
                        {
                            World =
                                Matrix4x4.CreateScale(
                                    size) *
                                Matrix4x4.CreateBillboard(
                                    center,
                                    cameraPosition,
                                    Vector3.UnitY,
                                    Vector3.UnitZ)
                        };

                    _vehicleModelBuffer.SetData(
                        _deviceContext,
                        model,
                        MapMode.WriteDiscard);

                    var normalizedBrightness =
                        (float)Math.Clamp(
                            brightness,
                            0.0,
                            16.0);

                    material[0] =
                        new RuntimeVehicleMaterialConstants
                        {
                            AlphaScale = 1.0f,
                            MaterialChangeDiffuse =
                                new Vector4(
                                    light.Red / 255.0f *
                                        normalizedBrightness,
                                    light.Green / 255.0f *
                                        normalizedBrightness,
                                    light.Blue / 255.0f *
                                        normalizedBrightness,
                                    1.0f)
                        };

                    _vehicleMaterialBuffer.SetData(
                        _deviceContext,
                        material,
                        MapMode.WriteDiscard);

                    _deviceContext.Draw(
                        6,
                        0);
                }
            }
        }

        _deviceContext.OMSetBlendState(
            null);

        _deviceContext.OMSetDepthStencilState(
            null);

        _deviceContext.RSSetState(
            null);
    }

    private bool IsTrafficBlinkPhaseOn() =>
        _frameClock.Elapsed.TotalSeconds %
            1.0 <
        0.5;

    private double ResolveTrafficVehicleLightValue(
        RuntimeTrafficAgentInfo agent,
        RuntimeVehicleLightEffectInfo light)
    {
        double source;

        if (!double.TryParse(
                light.BrightnessVariable,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out source))
        {
            source =
                light.BrightnessVariable
                    .Trim()
                    .ToLowerInvariant() switch
                {
                    "ai_brakelight" or
                    "lights_brems" or
                    "lights_brakes" =>
                        agent.AiBrakeLight
                            ? 1.0
                            : 0.0,
                    "ai_blinker_l" or
                    "lights_blinker_l" =>
                        agent.AiBlinkerLeft &&
                        IsTrafficBlinkPhaseOn()
                            ? 1.0
                            : 0.0,
                    "ai_blinker_r" or
                    "lights_blinker_r" =>
                        agent.AiBlinkerRight &&
                        IsTrafficBlinkPhaseOn()
                            ? 1.0
                            : 0.0,
                    "lights_blinkgeber" =>
                        (agent.AiBlinkerLeft ||
                         agent.AiBlinkerRight) &&
                        IsTrafficBlinkPhaseOn()
                            ? 1.0
                            : 0.0,
                    _ =>
                        0.0
                };
        }

        var value =
            source *
            light.BrightnessFactor;

        return double.IsFinite(
                   value)
            ? Math.Clamp(
                value,
                0.0,
                16.0)
            : 0.0;
    }

    private void DrawVehicle()
    {
        if (_vehicleRemoved)
        {
            return;
        }

        // Geometry selection must follow the camera that is actually in use.
        // If an OMSI .bus has no valid F1/F2 cameras, CreateViewProjection()
        // falls back to the chase camera; drawing the interior geometry in
        // that case makes the vehicle appear to be missing.
        var useExteriorGeometry =
            _renderingReflectionPass ||
            UseExteriorVehicleView();

        var geometry =
            useExteriorGeometry
                ? _vehicleExteriorGeometry
                : _vehicleInteriorGeometry;

        var vertexBuffer =
            useExteriorGeometry
                ? _vehicleExteriorVertexBuffer
                : _vehicleInteriorVertexBuffer;

        // Some add-ons omit one viewpoint set completely. In that case use
        // the other successfully-built geometry instead of making the bus
        // invisible. Viewpoint filtering is still respected when both sets
        // are available.
        if (geometry.Vertices.Length == 0 ||
            vertexBuffer is null)
        {
            var alternateGeometry =
                useExteriorGeometry
                    ? _vehicleInteriorGeometry
                    : _vehicleExteriorGeometry;

            var alternateBuffer =
                useExteriorGeometry
                    ? _vehicleInteriorVertexBuffer
                    : _vehicleExteriorVertexBuffer;

            if (alternateGeometry.Vertices.Length > 0 &&
                alternateBuffer is not null)
            {
                geometry =
                    alternateGeometry;
                vertexBuffer =
                    alternateBuffer;
            }
        }

        if (_deviceContext is null ||
            CurrentRenderTargetView is null ||
            CurrentDepthStencilView is null ||
            vertexBuffer is null ||
            _vehicleModelBuffer is null ||
            _vehicleMaterialBuffer is null ||
            _vehicleSkinBuffer is null ||
            _vehicleVertexShader is null ||
            _vehicleColorPixelShader is null ||
            _vehicleTexturedPixelShader is null ||
            _vehicleAlphaCutoutPixelShader is null ||
            _vehicleAlphaBlendPixelShader is null ||
            _vehicleAlphaCutoutTransMapPixelShader is null ||
            _vehicleAlphaBlendTransMapPixelShader is null ||
            _vehicleInputLayout is null ||
            _vehicleSampler is null ||
            _terrainCameraBuffer is null ||
            geometry.Vertices.Length == 0)
        {
            return;
        }

        Span<RuntimeModelConstants> model =
            stackalloc RuntimeModelConstants[1];

        Span<RuntimeVehicleMaterialConstants> materialConstants =
            stackalloc RuntimeVehicleMaterialConstants[1];
        Span<RuntimeVehicleSkinConstants> skinConstants =
            stackalloc RuntimeVehicleSkinConstants[1];

        var vehicleWorld =
            _vehicle.CreateWorldMatrix();

        _deviceContext.OMSetRenderTargets(
            CurrentRenderTargetView,
            CurrentDepthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            _vehicleInputLayout);

        _deviceContext.IASetVertexBuffer(
            0,
            vertexBuffer,
            RuntimeObjectVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _vehicleVertexShader);

        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);

        _deviceContext.VSSetConstantBuffer(
            1,
            _vehicleModelBuffer);

        _deviceContext.VSSetConstantBuffer(
            3,
            _vehicleSkinBuffer);

        _deviceContext.PSSetSampler(
            0,
            _vehicleSampler);

        _deviceContext.PSSetConstantBuffer(
            2,
            _vehicleMaterialBuffer);

        _deviceContext.RSSetState(
            _terrainRasterizerState);

        _vehicleDrawItems.Clear();

        if (_vehicleDrawItems.Capacity <
            geometry.Batches.Count)
        {
            _vehicleDrawItems.Capacity =
                geometry.Batches.Count;
        }

        var opaqueCount =
            0;

        foreach (var batch in
                 geometry.Batches)
        {
            if (!IsVehicleBatchVisible(
                    batch) ||
                batch.VertexCount ==
                    0)
            {
                continue;
            }

            var material =
                ResolveVehicleMaterialState(
                    batch);

            var draw =
                (
                    Batch:
                        batch,
                    Material:
                        material);

            if (material.AlphaBlend)
            {
                _vehicleDrawItems.Add(
                    draw);
            }
            else
            {
                _vehicleDrawItems.Insert(
                    opaqueCount,
                    draw);

                opaqueCount++;
            }
        }

        _deviceContext.OMSetBlendState(
            null);
        _deviceContext.OMSetDepthStencilState(
            null);

        ID3D11BlendState? activeVehicleBlendState =
            null;
        ID3D11DepthStencilState? activeVehicleDepthState =
            null;
        ID3D11PixelShader? activeVehiclePixelShader =
            null;
        ID3D11ShaderResourceView? activeVehicleDiffuseView =
            null;
        ID3D11ShaderResourceView? activeVehicleTransMapView =
            null;
        ID3D11ShaderResourceView? activeVehicleLightMapView =
            null;
        ID3D11ShaderResourceView? activeVehicleMaterialChangeView =
            null;
        ID3D11ShaderResourceView? activeVehicleEnvMapView =
            null;
        ID3D11ShaderResourceView? activeVehicleEnvMapMaskView =
            null;
        ID3D11ShaderResourceView? activeVehicleBumpMapView =
            null;

        var hasActiveVehicleWorld =
            false;
        var activeVehicleWorld =
            Matrix4x4.Identity;
        var hasActiveVehicleSkin =
            false;
        var activeVehicleSkin =
            default(RuntimeVehicleSkinConstants);
        var hasActiveVehicleMaterial =
            false;
        var activeVehicleMaterial =
            default(RuntimeVehicleMaterialConstants);

        _deviceContext.PSUnsetShaderResource(0);
        _deviceContext.PSUnsetShaderResource(1);
        _deviceContext.PSUnsetShaderResource(2);
        _deviceContext.PSUnsetShaderResource(3);
        _deviceContext.PSUnsetShaderResource(4);
        _deviceContext.PSUnsetShaderResource(5);
        _deviceContext.PSUnsetShaderResource(6);

        foreach (var draw in
                 _vehicleDrawItems)
        {
            var batch =
                draw.Batch;
            var materialState =
                draw.Material;

            if (_renderingReflectionPass)
            {
                var reflectionDiffuse =
                    ResolveVehicleDiffuseTexturePath(
                        batch,
                        materialState.FreeTextures);

                if (!string.IsNullOrWhiteSpace(
                        reflectionDiffuse) &&
                    reflectionDiffuse.StartsWith(
                        "runtime-reflection://",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Do not feed a mirror render target back into itself.
                    // The mirror surface is omitted from the reflected bus,
                    // while the rest of the exterior remains visible.
                    continue;
                }
            }

            var desiredVehicleWorld =
                CreateVehicleAnimationMatrix(
                    batch) *
                CreateArticulatedSectionMatrix(
                    batch.SectionIndex) *
                vehicleWorld;

            if (!hasActiveVehicleWorld ||
                activeVehicleWorld !=
                    desiredVehicleWorld)
            {
                model[0] =
                    new RuntimeModelConstants
                    {
                        World =
                            desiredVehicleWorld
                    };

                _vehicleModelBuffer.SetData(
                    _deviceContext,
                    model,
                    MapMode.WriteDiscard);

                activeVehicleWorld =
                    desiredVehicleWorld;
                hasActiveVehicleWorld =
                    true;
            }

            var desiredVehicleSkin =
                ResolveVehicleSkinConstants(
                    batch);

            if (!hasActiveVehicleSkin ||
                !VehicleSkinConstantsEqual(
                    activeVehicleSkin,
                    desiredVehicleSkin))
            {
                skinConstants[0] =
                    desiredVehicleSkin;

                _vehicleSkinBuffer.SetData(
                    _deviceContext,
                    skinConstants,
                    MapMode.WriteDiscard);

                activeVehicleSkin =
                    desiredVehicleSkin;
                hasActiveVehicleSkin =
                    true;
            }

            var desiredVehicleMaterial =
                new RuntimeVehicleMaterialConstants
                {
                    AlphaScale =
                        _vehiclePreviewMode
                            ? 1.0f
                            : ResolveVehicleAlphaScale(
                                materialState.AlphaScaleVariable,
                                batch.SectionIndex),
                    LightMapStrength =
                        ResolveVehicleLightMapStrength(
                            materialState.LightMapTexturePath,
                            materialState.LightMapVariable,
                            batch.SectionIndex),
                    MaterialChangeStrength =
                        materialState.HasMaterialChange
                            ? 1.0f
                            : 0.0f,
                    EnvMapStrength =
                        ResolveVehicleEnvMapStrength(
                            materialState.EnvMapTexturePath,
                            materialState.EnvMapStrength),
                    EnvMapMaskEnabled =
                        ResolveVehicleEnvMapMaskEnabled(
                            materialState.EnvMapMaskTexturePath,
                            materialState.UseDiffuseAlphaAsEnvMapMask),
                    BumpMapStrength =
                        ResolveVehicleBumpMapStrength(
                            materialState.BumpMapTexturePath,
                            materialState.BumpMapStrength),
                    MaterialChangeTextureEnabled =
                        ResolveVehicleMaterialChangeTextureEnabled(
                            materialState.MaterialChangeTexturePath),
                    MaterialChangeColorEnabled =
                        materialState.MaterialChangeAllColor is null
                            ? 0.0f
                            : 1.0f,
                    MaterialChangeDiffuse =
                        ResolveVehicleAllColorDiffuse(
                            materialState.MaterialChangeAllColor,
                            Vector4.One),
                    BaseEmissive =
                        ResolveVehicleAllColorEmissive(
                            batch.BaseAllColor),
                    MaterialChangeEmissive =
                        ResolveVehicleAllColorEmissive(
                            materialState.MaterialChangeAllColor)
                };

            if (!hasActiveVehicleMaterial ||
                !VehicleMaterialConstantsEqual(
                    activeVehicleMaterial,
                    desiredVehicleMaterial))
            {
                materialConstants[0] =
                    desiredVehicleMaterial;

                _vehicleMaterialBuffer.SetData(
                    _deviceContext,
                    materialConstants,
                    MapMode.WriteDiscard);

                activeVehicleMaterial =
                    desiredVehicleMaterial;
                hasActiveVehicleMaterial =
                    true;
            }

            var desiredBlendState =
                materialState.AlphaBlend
                    ? _vehicleAlphaBlendState
                    : null;

            if (!ReferenceEquals(
                    activeVehicleBlendState,
                    desiredBlendState))
            {
                _deviceContext.OMSetBlendState(
                    desiredBlendState);

                activeVehicleBlendState =
                    desiredBlendState;
            }

            var desiredDepthState =
                materialState.AlphaBlend
                    ? _vehicleDepthReadState
                    : materialState.NoZCheck
                        ? _vehicleDepthDisabledState
                        : materialState.NoZWrite
                            ? _vehicleDepthReadState
                            : null;

            if (!ReferenceEquals(
                    activeVehicleDepthState,
                    desiredDepthState))
            {
                _deviceContext.OMSetDepthStencilState(
                    desiredDepthState);

                activeVehicleDepthState =
                    desiredDepthState;
            }

            ID3D11ShaderResourceView? envMapView =
                null;
            ID3D11ShaderResourceView? envMapMaskView =
                null;
            ID3D11ShaderResourceView? bumpMapView =
                null;

            if (_materialReflectionMapEnabled)
            {
                TryGetVehicleTextureView(
                    materialState.EnvMapTexturePath,
                    out envMapView);

                TryGetVehicleTextureView(
                    materialState.EnvMapMaskTexturePath,
                    out envMapMaskView);
            }

            if (_materialBumpMapEnabled)
            {
                TryGetVehicleTextureView(
                    materialState.BumpMapTexturePath,
                    out bumpMapView);
            }

            SetVehicleShaderResource(
                4,
                envMapView,
                ref activeVehicleEnvMapView);
            SetVehicleShaderResource(
                5,
                envMapMaskView,
                ref activeVehicleEnvMapMaskView);
            SetVehicleShaderResource(
                6,
                bumpMapView,
                ref activeVehicleBumpMapView);

            ID3D11ShaderResourceView?
                textureView;

            var requiresTextTexture =
                materialState.TextTextureIndex.HasValue;

            var hasDiffuseTexture =
                requiresTextTexture
                    ? TryGetVehicleTextTextureView(
                        materialState.TextTextureIndex,
                        batch.SectionIndex,
                        out textureView)
                    : TryGetVehicleTextureView(
                        ResolveVehicleDiffuseTexturePath(
                            batch,
                            materialState.FreeTextures),
                        out textureView);

            if (hasDiffuseTexture)
            {
                var requiresExternalTransMap =
                    !string.IsNullOrWhiteSpace(
                        materialState.TransMapTexturePath);

                var hasTransMap =
                    TryGetVehicleTextureView(
                        materialState.TransMapTexturePath,
                        out var transMapView);

                if (requiresExternalTransMap &&
                    !hasTransMap &&
                    !_vehiclePreviewMode)
                {
                    continue;
                }

                ID3D11ShaderResourceView? lightMapView =
                    null;
                ID3D11ShaderResourceView? materialChangeView =
                    null;

                if (_materialLightMapEnabled)
                {
                    TryGetVehicleTextureView(
                        materialState.LightMapTexturePath,
                        out lightMapView);
                }

                TryGetVehicleTextureView(
                    materialState.MaterialChangeTexturePath,
                    out materialChangeView);

                SetVehicleShaderResource(
                    1,
                    hasTransMap
                        ? transMapView
                        : null,
                    ref activeVehicleTransMapView);
                SetVehicleShaderResource(
                    2,
                    lightMapView,
                    ref activeVehicleLightMapView);
                SetVehicleShaderResource(
                    3,
                    materialChangeView,
                    ref activeVehicleMaterialChangeView);

                var desiredPixelShader =
                    materialState.AlphaCutout
                        ? hasTransMap
                            ? _vehicleAlphaCutoutTransMapPixelShader
                            : _vehicleAlphaCutoutPixelShader
                        : materialState.AlphaBlend
                            ? hasTransMap
                                ? _vehicleAlphaBlendTransMapPixelShader
                                : _vehicleAlphaBlendPixelShader
                            : _vehicleTexturedPixelShader;

                if (!ReferenceEquals(
                        activeVehiclePixelShader,
                        desiredPixelShader))
                {
                    _deviceContext.PSSetShader(
                        desiredPixelShader);

                    activeVehiclePixelShader =
                        desiredPixelShader;
                }

                SetVehicleShaderResource(
                    0,
                    textureView,
                    ref activeVehicleDiffuseView);
            }
            else
            {
                if (!_vehiclePreviewMode &&
                    (requiresTextTexture ||
                     materialState.AlphaCutout ||
                     materialState.AlphaBlend))
                {
                    continue;
                }

                SetVehicleShaderResource(
                    0,
                    null,
                    ref activeVehicleDiffuseView);
                SetVehicleShaderResource(
                    1,
                    null,
                    ref activeVehicleTransMapView);
                SetVehicleShaderResource(
                    2,
                    null,
                    ref activeVehicleLightMapView);
                SetVehicleShaderResource(
                    3,
                    null,
                    ref activeVehicleMaterialChangeView);

                if (!ReferenceEquals(
                        activeVehiclePixelShader,
                        _vehicleColorPixelShader))
                {
                    _deviceContext.PSSetShader(
                        _vehicleColorPixelShader);

                    activeVehiclePixelShader =
                        _vehicleColorPixelShader;
                }
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.OMSetBlendState(null);
        _deviceContext.OMSetDepthStencilState(null);
        _deviceContext.PSUnsetShaderResource(0);
        _deviceContext.PSUnsetShaderResource(1);
        _deviceContext.PSUnsetShaderResource(2);
        _deviceContext.PSUnsetShaderResource(3);
        _deviceContext.PSUnsetShaderResource(4);
        _deviceContext.PSUnsetShaderResource(5);
        _deviceContext.PSUnsetShaderResource(6);
        _deviceContext.RSSetState(null);
    }

    private static bool VehicleMaterialConstantsEqual(
        RuntimeVehicleMaterialConstants first,
        RuntimeVehicleMaterialConstants second) =>
        first.AlphaScale ==
            second.AlphaScale &&
        first.LightMapStrength ==
            second.LightMapStrength &&
        first.MaterialChangeStrength ==
            second.MaterialChangeStrength &&
        first.EnvMapStrength ==
            second.EnvMapStrength &&
        first.EnvMapMaskEnabled ==
            second.EnvMapMaskEnabled &&
        first.BumpMapStrength ==
            second.BumpMapStrength &&
        first.MaterialChangeTextureEnabled ==
            second.MaterialChangeTextureEnabled &&
        first.MaterialChangeColorEnabled ==
            second.MaterialChangeColorEnabled &&
        first.MaterialChangeDiffuse ==
            second.MaterialChangeDiffuse &&
        first.BaseEmissive ==
            second.BaseEmissive &&
        first.MaterialChangeEmissive ==
            second.MaterialChangeEmissive;

    private static bool VehicleSkinConstantsEqual(
        RuntimeVehicleSkinConstants first,
        RuntimeVehicleSkinConstants second) =>
        first.Bone0 ==
            second.Bone0 &&
        first.Bone1 ==
            second.Bone1 &&
        first.Bone2 ==
            second.Bone2 &&
        first.Bone3 ==
            second.Bone3;

    private void SetVehicleShaderResource(
        uint slot,
        ID3D11ShaderResourceView? desired,
        ref ID3D11ShaderResourceView? active)
    {
        if (_deviceContext is null ||
            ReferenceEquals(
                active,
                desired))
        {
            return;
        }

        if (desired is null)
        {
            _deviceContext.PSUnsetShaderResource(
                slot);
        }
        else
        {
            _deviceContext.PSSetShaderResource(
                slot,
                desired);
        }

        active =
            desired;
    }

    private void DrawVehicleLights()
    {
        if (_vehicleRemoved)
        {
            return;
        }

        var vehicle =
            _windowInfo.Vehicle;

        if (vehicle is null ||
            _deviceContext is null ||
            _renderTargetView is null ||
            _depthStencilView is null ||
            _vehicleLightVertexBuffer is null ||
            _vehicleModelBuffer is null ||
            _vehicleMaterialBuffer is null ||
            _vehicleSkinBuffer is null ||
            _vehicleVertexShader is null ||
            _vehicleLightPixelShader is null ||
            _vehicleInputLayout is null ||
            _terrainCameraBuffer is null ||
            _terrainAdditiveBlendState is null ||
            _vehicleDepthReadState is null)
        {
            return;
        }

        _vehicleLightMeshes.Clear();
        _vehicleViewpointLightMeshes.Clear();

        var viewpointBit =
            UseExteriorVehicleView()
                ? 1
                : 2;

        foreach (var mesh in
                 vehicle.Meshes)
        {
            if (mesh.LightEffects is not
                { Count: > 0 })
            {
                continue;
            }

            _vehicleLightMeshes.Add(
                mesh);

            if (IsVehicleMeshVisibleFromViewpoint(
                    mesh.ViewpointFlag,
                    viewpointBit))
            {
                _vehicleViewpointLightMeshes.Add(
                    mesh);
            }
        }

        if (_vehicleLightMeshes.Count ==
            0)
        {
            return;
        }

        IReadOnlyList<RuntimeObjectMeshInfo>
            selectionSource =
                _vehicleViewpointLightMeshes.Count >
                        0
                    ? _vehicleViewpointLightMeshes
                    : _vehicleLightMeshes;

        var detailedLod =
            double.NaN;

        foreach (var mesh in
                 selectionSource)
        {
            if (!mesh.LodThreshold.HasValue)
            {
                continue;
            }

            detailedLod =
                double.IsNaN(
                    detailedLod)
                    ? mesh.LodThreshold.Value
                    : Math.Max(
                        detailedLod,
                        mesh.LodThreshold.Value);
        }

        var cameraPosition =
            ResolveActiveCameraPosition();

        var vehicleWorld =
            _vehicle.CreateWorldMatrix();

        Span<RuntimeModelConstants> model =
            stackalloc RuntimeModelConstants[1];

        Span<RuntimeVehicleMaterialConstants> material =
            stackalloc RuntimeVehicleMaterialConstants[1];

        _deviceContext.OMSetRenderTargets(
            _renderTargetView,
            _depthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _deviceContext.IASetInputLayout(
            _vehicleInputLayout);

        _deviceContext.IASetVertexBuffer(
            0,
            _vehicleLightVertexBuffer,
            RuntimeObjectVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _vehicleVertexShader);

        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);

        _deviceContext.VSSetConstantBuffer(
            1,
            _vehicleModelBuffer);

        Span<RuntimeVehicleSkinConstants> lightSkin =
            stackalloc RuntimeVehicleSkinConstants[1];

        lightSkin[0] =
            new RuntimeVehicleSkinConstants
            {
                Bone0 = Matrix4x4.Identity,
                Bone1 = Matrix4x4.Identity,
                Bone2 = Matrix4x4.Identity,
                Bone3 = Matrix4x4.Identity
            };

        _vehicleSkinBuffer.SetData(
            _deviceContext,
            lightSkin,
            MapMode.WriteDiscard);

        _deviceContext.VSSetConstantBuffer(
            3,
            _vehicleSkinBuffer);

        _deviceContext.PSSetShader(
            _vehicleLightPixelShader);

        _deviceContext.PSSetConstantBuffer(
            2,
            _vehicleMaterialBuffer);

        _deviceContext.OMSetBlendState(
            _terrainAdditiveBlendState);

        _deviceContext.OMSetDepthStencilState(
            _vehicleDepthReadState);

        _deviceContext.RSSetState(
            _terrainRasterizerState);

        foreach (var mesh in
                 selectionSource)
        {
            if (mesh.LodThreshold.HasValue &&
                !double.IsNaN(
                    detailedLod) &&
                Math.Abs(
                    mesh.LodThreshold.Value -
                    detailedLod) >
                0.000001)
            {
                continue;
            }

            if (!AreVehicleVisibilityConditionsMet(
                    mesh.VisibilityConditions,
                    mesh.SectionIndex))
            {
                continue;
            }

            var staticTransform =
                RuntimeObjectGeometryBuilder
                    .CreateMeshTransform(
                        mesh.Transform);

            var animationTransform =
                CreateVehicleAnimationMatrix(
                    mesh.Animations,
                    mesh.SourceTransform,
                    staticTransform,
                    mesh.SectionIndex);

            var parentTransform =
                staticTransform *
                animationTransform *
                CreateArticulatedSectionMatrix(
                    mesh.SectionIndex) *
                vehicleWorld;

            foreach (var light in
                     mesh.LightEffects!)
            {
                var brightness =
                    ResolveVehicleLightValue(
                        light,
                        mesh.SectionIndex);

                if (brightness <=
                    0.0001)
                {
                    continue;
                }

                var center =
                    Vector3.Transform(
                        ConvertCfgPosition(
                            light.PositionX,
                            light.PositionY,
                            light.PositionZ),
                        parentTransform);

                var toCamera =
                    cameraPosition -
                    center;

                var cameraDistanceSquared =
                    toCamera.LengthSquared();

                if (cameraDistanceSquared <
                    0.000001f)
                {
                    continue;
                }

                var cameraDirection =
                    Vector3.Normalize(
                        toCamera);

                var directionalAttenuation =
                    ResolveVehicleLightDirectionalAttenuation(
                        light,
                        parentTransform,
                        cameraDirection);

                brightness *=
                    directionalAttenuation;

                if (brightness <=
                    0.0001)
                {
                    continue;
                }

                center +=
                    cameraDirection *
                    (float)light.CameraOffsetMeters;

                var size =
                    (float)Math.Clamp(
                        light.SizeMeters,
                        0.005,
                        20.0);

                var billboard =
                    Matrix4x4.CreateScale(
                        size) *
                    Matrix4x4.CreateBillboard(
                        center,
                        cameraPosition,
                        Vector3.UnitY,
                        Vector3.UnitZ);

                model[0] =
                    new RuntimeModelConstants
                    {
                        World =
                            billboard
                    };

                _vehicleModelBuffer.SetData(
                    _deviceContext,
                    model,
                    MapMode.WriteDiscard);

                var normalizedBrightness =
                    (float)Math.Clamp(
                        brightness,
                        0.0,
                        16.0);

                material[0] =
                    new RuntimeVehicleMaterialConstants
                    {
                        AlphaScale = 1.0f,
                        MaterialChangeDiffuse =
                            new Vector4(
                                light.Red / 255.0f *
                                    normalizedBrightness,
                                light.Green / 255.0f *
                                    normalizedBrightness,
                                light.Blue / 255.0f *
                                    normalizedBrightness,
                                1.0f)
                    };

                _vehicleMaterialBuffer.SetData(
                    _deviceContext,
                    material,
                    MapMode.WriteDiscard);

                _deviceContext.Draw(
                    6,
                    0);
            }
        }

        _deviceContext.OMSetBlendState(
            null);

        _deviceContext.OMSetDepthStencilState(
            null);

        _deviceContext.RSSetState(
            null);
    }

    private bool AreVehicleVisibilityConditionsMet(
        IReadOnlyList<RuntimeVehicleVisibilityConditionInfo>? conditions,
        int sectionIndex = 0)
    {
        if (conditions is null ||
            conditions.Count == 0)
        {
            return true;
        }

        foreach (var condition in
                 conditions)
        {
            var value =
                ResolveSectionNumericValue(
                    sectionIndex,
                    condition.VariableName);

            if (Math.Abs(
                    value -
                    condition.Value) >
                0.000001)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsVehicleMeshVisibleFromViewpoint(
        int viewpointFlag,
        int requestedBit) =>
        viewpointFlag is 0 or 7 ||
        (viewpointFlag &
         requestedBit) != 0;

    private static double ResolveVehicleLightDirectionalAttenuation(
        RuntimeVehicleLightEffectInfo light,
        Matrix4x4 parentTransform,
        Vector3 cameraDirection)
    {
        if (light.Omni != 0 ||
            light.Rotating != 0)
        {
            return 1.0;
        }

        var direction =
            new Vector3(
                (float)-light.DirectionX,
                (float)light.DirectionZ,
                (float)light.DirectionY);

        direction =
            Vector3.TransformNormal(
                direction,
                parentTransform);

        if (direction.LengthSquared() <
            0.000001f)
        {
            return 1.0;
        }

        direction =
            Vector3.Normalize(
                direction);

        var alignment =
            Math.Clamp(
                Vector3.Dot(
                    direction,
                    cameraDirection),
                -1.0f,
                1.0f);

        var outer =
            Math.Clamp(
                Math.Abs(
                    light.OuterConeAngleDegrees),
                0.0,
                180.0);

        if (outer >=
            179.999)
        {
            return 1.0;
        }

        var inner =
            Math.Clamp(
                Math.Abs(
                    light.InnerConeAngleDegrees),
                0.0,
                outer);

        var outerCos =
            Math.Cos(
                DegreesToRadians(
                    outer *
                    0.5));

        var innerCos =
            Math.Cos(
                DegreesToRadians(
                    inner *
                    0.5));

        if (alignment <=
            outerCos)
        {
            return 0.0;
        }

        if (alignment >=
                innerCos ||
            Math.Abs(
                innerCos -
                outerCos) <
            0.000001)
        {
            return 1.0;
        }

        return Math.Clamp(
            (alignment - outerCos) /
            (innerCos - outerCos),
            0.0,
            1.0);
    }

    private ResolvedVehicleMaterialState ResolveVehicleMaterialState(
        RuntimeObjectBatch batch)
    {
        RuntimeVehicleMaterialChangeItemInfo? selectedItem =
            null;

        if (batch.MaterialChangeSets is
            { Count: > 0 } changeSets)
        {
            if (!_vehicleOrderedMaterialChangeSets.TryGetValue(
                    batch,
                    out var orderedChangeSets))
            {
                orderedChangeSets =
                    changeSets
                        .OrderBy(
                            static set =>
                                set.GroupIndex)
                        .ToArray();

                _vehicleOrderedMaterialChangeSets[
                    batch] =
                    orderedChangeSets;
            }

            foreach (var changeSet in
                     orderedChangeSets)
            {
                var value =
                    ResolveSectionNumericValue(
                        batch.SectionIndex,
                        changeSet.VariableName);

                if (!double.IsFinite(
                        value))
                {
                    continue;
                }

                var rounded =
                    Math.Round(
                        value,
                        MidpointRounding.ToEven);

                if (rounded < 0.0 ||
                    rounded > int.MaxValue)
                {
                    continue;
                }

                var requestedItem =
                    (int)rounded;

                if (!_vehicleMaterialChangeItems.TryGetValue(
                        changeSet,
                        out var itemsByIndex))
                {
                    itemsByIndex =
                        new Dictionary<
                            int,
                            RuntimeVehicleMaterialChangeItemInfo>();

                    foreach (var candidate in
                             changeSet.Items)
                    {
                        itemsByIndex[
                            candidate.ItemIndex] =
                            candidate;
                    }

                    _vehicleMaterialChangeItems[
                        changeSet] =
                        itemsByIndex;
                }

                if (itemsByIndex.TryGetValue(
                        requestedItem,
                        out var item))
                {
                    selectedItem =
                        item;
                }
            }
        }

        var hasNativeItem =
            selectedItem is not null;

        var legacyMaterialChangeValue =
            !hasNativeItem &&
            (batch.MaterialChangeSets is null ||
             batch.MaterialChangeSets.Count == 0) &&
            !string.IsNullOrWhiteSpace(
                batch.MaterialChangeVariable)
                ? ResolveSectionNumericValue(
                    batch.SectionIndex,
                    batch.MaterialChangeVariable)
                : double.NaN;

        var legacyActive =
            double.IsFinite(
                legacyMaterialChangeValue) &&
            legacyMaterialChangeValue >=
                0.5;

        var alphaMode =
            selectedItem?.AlphaMode ??
            (batch.AlphaCutout
                ? 1
                : batch.AlphaBlend
                    ? 2
                    : 0);

        var hasTransMapDirective =
            selectedItem is null
                ? batch.HasTransMapDirective
                : selectedItem.HasTransMapDirective ||
                  batch.HasTransMapDirective;

        var transMap =
            selectedItem is null
                ? batch.TransMapTexturePath
                : selectedItem.HasTransMapDirective
                    ? selectedItem.TransMapTexturePath
                    : batch.TransMapTexturePath;

        var useDiffuseAlphaAsEnvMapMask =
            hasTransMapDirective &&
            string.IsNullOrWhiteSpace(
                transMap);

        if (useDiffuseAlphaAsEnvMapMask)
        {
            // Native OMSI uses a blank [matl_transmap] to repurpose the
            // diffuse alpha channel as reflection/envmap strength. It is
            // therefore not a transparency mode.
            alphaMode = 0;
        }

        var changeTexture =
            hasNativeItem
                ? selectedItem!.MaterialChangeTexturePath
                : legacyActive
                    ? batch.MaterialChangeTexturePath
                    : null;

        var changeTextureIsNightMap =
            hasNativeItem
                ? selectedItem!.MaterialChangeIsNightMap
                : legacyActive &&
                  batch.MaterialChangeIsNightMap;

        if (changeTextureIsNightMap &&
            !_materialNightMapEnabled)
        {
            changeTexture =
                null;
        }

        var changeColor =
            hasNativeItem
                ? selectedItem!.AllColor
                : legacyActive
                    ? batch.MaterialChangeAllColor
                    : null;

        var itemFreeTextures =
            selectedItem?.FreeTextures;

        return new ResolvedVehicleMaterialState(
            alphaMode == 1,
            alphaMode == 2,
            transMap,
            batch.NoZWrite ||
                (selectedItem?.NoZWrite ?? false),
            batch.NoZCheck ||
                (selectedItem?.NoZCheck ?? false),
            selectedItem?.AlphaScaleVariable ??
                batch.AlphaScaleVariable,
            selectedItem?.LightMapTexturePath ??
                batch.LightMapTexturePath,
            selectedItem?.LightMapVariable ??
                batch.LightMapVariable,
            changeTexture,
            changeColor,
            selectedItem?.EnvMapTexturePath ??
                batch.EnvMapTexturePath,
            selectedItem?.EnvMapTexturePath is not null
                ? selectedItem.EnvMapStrength
                : batch.EnvMapStrength,
            selectedItem?.EnvMapMaskTexturePath ??
                batch.EnvMapMaskTexturePath,
            selectedItem?.BumpMapTexturePath ??
                batch.BumpMapTexturePath,
            selectedItem?.BumpMapTexturePath is not null
                ? selectedItem.BumpMapStrength
                : batch.BumpMapStrength,
            itemFreeTextures is { Count: > 0 }
                ? itemFreeTextures
                : batch.FreeTextures,
            selectedItem?.TextTextureIndex ??
                batch.TextTextureIndex,
            hasNativeItem ||
                legacyActive,
            useDiffuseAlphaAsEnvMapMask);
    }

    private readonly record struct ResolvedVehicleMaterialState(
        bool AlphaCutout,
        bool AlphaBlend,
        string? TransMapTexturePath,
        bool NoZWrite,
        bool NoZCheck,
        string? AlphaScaleVariable,
        string? LightMapTexturePath,
        string? LightMapVariable,
        string? MaterialChangeTexturePath,
        RuntimeVehicleMaterialColorInfo? MaterialChangeAllColor,
        string? EnvMapTexturePath,
        double EnvMapStrength,
        string? EnvMapMaskTexturePath,
        string? BumpMapTexturePath,
        double BumpMapStrength,
        IReadOnlyList<RuntimeVehicleFreeTextureInfo>? FreeTextures,
        int? TextTextureIndex,
        bool HasMaterialChange,
        bool UseDiffuseAlphaAsEnvMapMask);

    private float ResolveVehicleBumpMapStrength(
        string? texturePath,
        double strength)
    {
        if (!_materialBumpMapEnabled ||
            string.IsNullOrWhiteSpace(
                texturePath) ||
            strength <= 0.0 ||
            !TryGetVehicleTextureView(
                texturePath,
                out _))
        {
            return 0.0f;
        }

        return (float)Math.Clamp(
            strength,
            0.0,
            10.0);
    }

    private float ResolveVehicleEnvMapStrength(
        string? texturePath,
        double strength)
    {
        if (!_materialReflectionMapEnabled ||
            string.IsNullOrWhiteSpace(
                texturePath) ||
            strength <= 0.0 ||
            !TryGetVehicleTextureView(
                texturePath,
                out _))
        {
            return 0.0f;
        }

        return (float)Math.Clamp(
            strength,
            0.0,
            100.0);
    }

    private float ResolveVehicleEnvMapMaskEnabled(
        string? texturePath,
        bool useDiffuseAlpha)
    {
        if (!_materialReflectionMapEnabled)
        {
            return 0.0f;
        }

        if (useDiffuseAlpha)
        {
            // 2 = use the diffuse texture alpha channel as the reflection
            // mask, matching an empty OMSI [matl_transmap].
            return 2.0f;
        }

        if (string.IsNullOrWhiteSpace(
                texturePath))
        {
            return 0.0f;
        }

        return TryGetVehicleTextureView(
                   texturePath,
                   out _)
            ? 1.0f
            : 0.0f;
    }

    private float ResolveVehicleMaterialChangeTextureEnabled(
        string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(
                texturePath))
        {
            return 0.0f;
        }

        return TryGetVehicleTextureView(
                   texturePath,
                   out _)
            ? 1.0f
            : 0.0f;
    }

    private static Vector4 ResolveVehicleAllColorDiffuse(
        RuntimeVehicleMaterialColorInfo? color,
        Vector4 fallback)
    {
        if (color is null)
        {
            return fallback;
        }

        return new Vector4(
            (float)Math.Clamp(
                color.DiffuseR,
                0.0,
                1.0),
            (float)Math.Clamp(
                color.DiffuseG,
                0.0,
                1.0),
            (float)Math.Clamp(
                color.DiffuseB,
                0.0,
                1.0),
            (float)Math.Clamp(
                color.DiffuseA,
                0.0,
                1.0));
    }

    private static Vector4 ResolveVehicleAllColorEmissive(
        RuntimeVehicleMaterialColorInfo? color)
    {
        if (color is null)
        {
            return Vector4.Zero;
        }

        return new Vector4(
            (float)Math.Max(
                color.EmissiveR,
                0.0),
            (float)Math.Max(
                color.EmissiveG,
                0.0),
            (float)Math.Max(
                color.EmissiveB,
                0.0),
            0.0f);
    }

    private float ResolveVehicleLightMapStrength(
        string? texturePath,
        string? variableName,
        int sectionIndex = 0)
    {
        if (!_materialLightMapEnabled ||
            string.IsNullOrWhiteSpace(
                texturePath))
        {
            return 0.0f;
        }

        if (string.IsNullOrWhiteSpace(
                variableName))
        {
            return 1.0f;
        }

        var value =
            ResolveSectionNumericValue(
                sectionIndex,
                variableName);

        if (!double.IsFinite(
                value))
        {
            return 0.0f;
        }

        return (float)Math.Max(
            value,
            0.0);
    }

    private float ResolveVehicleAlphaScale(
        string? variableName,
        int sectionIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(
                variableName))
        {
            return 1.0f;
        }

        var value =
            ResolveSectionNumericValue(
                sectionIndex,
                variableName);

        if (!double.IsFinite(
                value))
        {
            return 0.0f;
        }

        return (float)Math.Clamp(
            value,
            0.0,
            1.0);
    }

    private RuntimeVehicleSkinConstants ResolveVehicleSkinConstants(
        RuntimeObjectBatch batch)
    {
        var identity =
            Matrix4x4.Identity;

        var result =
            new RuntimeVehicleSkinConstants
            {
                Bone0 = identity,
                Bone1 = identity,
                Bone2 = identity,
                Bone3 = identity
            };

        var targets =
            batch.SkinBoneMeshOrdinals;

        if (targets is null ||
            targets.Count == 0)
        {
            return result;
        }

        for (var slot = 0;
             slot < Math.Min(
                 targets.Count,
                 4);
             slot++)
        {
            if (!_vehicleMeshOrdinalBatches.TryGetValue(
                    (
                        batch.SectionIndex,
                        targets[slot]),
                    out var boneBatch))
            {
                continue;
            }

            var transform =
                CreateVehicleAnimationMatrix(
                    boneBatch);

            switch (slot)
            {
                case 0:
                    result.Bone0 =
                        transform;
                    break;
                case 1:
                    result.Bone1 =
                        transform;
                    break;
                case 2:
                    result.Bone2 =
                        transform;
                    break;
                case 3:
                    result.Bone3 =
                        transform;
                    break;
            }
        }

        return result;
    }

    private bool IsVehicleBatchVisible(
        RuntimeObjectBatch batch) =>
        AreVehicleVisibilityConditionsMet(
            batch.VisibilityConditions,
            batch.SectionIndex);

    private Matrix4x4 CreateTrafficVehicleAnimationMatrix(
        RuntimeObjectBatch batch,
        RuntimeTrafficAgentInfo agent,
        RuntimeVehicleInfo vehicleInfo)
    {
        if (batch.Animations is null ||
            batch.Animations.Count ==
                0)
        {
            return Matrix4x4.Identity;
        }

        if (!_compiledTrafficAnimations.TryGetValue(
                batch,
                out var compiledAnimations))
        {
            var compiled =
                new List<CompiledTrafficAnimation>(
                    batch.Animations.Count);

            foreach (var animation in
                     batch.Animations)
            {
                if (!_trafficAnimationBindings.TryGetValue(
                        animation,
                        out var binding))
                {
                    binding =
                        BuildTrafficAnimationBinding(
                            animation.VariableName);

                    _trafficAnimationBindings[
                        animation] =
                        binding;
                }

                if (binding.Kind ==
                    TrafficAnimationBindingKind.Unsupported)
                {
                    continue;
                }

                ResolveAnimationFrame(
                    batch.SourceTransform,
                    batch.StaticTransform,
                    animation,
                    out var pivot,
                    out var orientation);

                var axis =
                    Vector3.TransformNormal(
                        Vector3.UnitX,
                        orientation);

                axis =
                    axis.LengthSquared() <
                            0.000001f
                        ? Vector3.UnitX
                        : Vector3.Normalize(
                            axis);

                compiled.Add(
                    new CompiledTrafficAnimation(
                        animation,
                        binding,
                        pivot,
                        axis));
            }

            compiledAnimations =
                compiled.ToArray();

            _compiledTrafficAnimations[
                batch] =
                compiledAnimations;
        }

        if (compiledAnimations.Length ==
            0)
        {
            return Matrix4x4.Identity;
        }

        var result =
            Matrix4x4.Identity;

        var animationPhysics =
            GetTrafficVehicleAnimationPhysics(
                vehicleInfo);

        foreach (var compiled in
                 compiledAnimations)
        {
            if (!TryResolveTrafficVehicleAnimationValue(
                    compiled.Binding,
                    agent,
                    animationPhysics,
                    out var variableValue))
            {
                continue;
            }

            var animation =
                compiled.Animation;

            var amount =
                variableValue *
                animation.Delta +
                animation.Offset;

            if (!double.IsFinite(
                    amount) ||
                Math.Abs(
                    amount) <
                0.0000001)
            {
                continue;
            }

            Matrix4x4 animationTransform;

            if (animation.Kind ==
                RuntimeVehicleAnimationKind.Translation)
            {
                animationTransform =
                    Matrix4x4.CreateTranslation(
                        compiled.Axis *
                        (float)amount);
            }
            else
            {
                animationTransform =
                    Matrix4x4.CreateTranslation(
                        -compiled.Pivot) *
                    Matrix4x4.CreateFromAxisAngle(
                        compiled.Axis,
                        DegreesToRadians(
                            amount)) *
                    Matrix4x4.CreateTranslation(
                        compiled.Pivot);
            }

            result *=
                animationTransform;
        }

        return result;
    }

    private static bool TryResolveTrafficVehicleAnimationValue(
        TrafficAnimationBinding binding,
        RuntimeTrafficAgentInfo agent,
        TrafficVehicleAnimationPhysics physics,
        out double value)
    {
        return binding.Kind switch
        {
            TrafficAnimationBindingKind.Steering =>
                TryResolveTrafficSteeringAnimationValue(
                    binding,
                    agent,
                    physics,
                    out value),
            TrafficAnimationBindingKind.WheelRotation =>
                TryResolveTrafficWheelRotationAnimationValue(
                    binding,
                    agent,
                    physics,
                    out value),
            _ =>
                FailTrafficAnimationValue(
                    out value)
        };
    }

    private static TrafficAnimationBinding BuildTrafficAnimationBinding(
        string variableName)
    {
        const string steeringPrefix =
            "Axle_Steering_";
        const string wheelPrefix =
            "Wheel_Rotation_";

        var kind =
            variableName.StartsWith(
                steeringPrefix,
                StringComparison.OrdinalIgnoreCase)
                ? TrafficAnimationBindingKind.Steering
                : variableName.StartsWith(
                    wheelPrefix,
                    StringComparison.OrdinalIgnoreCase)
                    ? TrafficAnimationBindingKind.WheelRotation
                    : TrafficAnimationBindingKind.Unsupported;

        if (kind ==
            TrafficAnimationBindingKind.Unsupported)
        {
            return new TrafficAnimationBinding(
                kind,
                -1,
                false);
        }

        var prefixLength =
            kind ==
                    TrafficAnimationBindingKind.Steering
                ? steeringPrefix.Length
                : wheelPrefix.Length;

        var suffix =
            variableName[
                prefixLength..];

        var separator =
            suffix.IndexOf(
                '_');

        if (separator <=
                0 ||
            !int.TryParse(
                suffix[
                    ..separator],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var axleIndex))
        {
            return new TrafficAnimationBinding(
                TrafficAnimationBindingKind.Unsupported,
                -1,
                false);
        }

        var side =
            suffix[
                (separator + 1)..];

        var isLeft =
            side.Equals(
                "L",
                StringComparison.OrdinalIgnoreCase);

        var isRight =
            side.Equals(
                "R",
                StringComparison.OrdinalIgnoreCase);

        if (!isLeft &&
            !isRight)
        {
            return new TrafficAnimationBinding(
                TrafficAnimationBindingKind.Unsupported,
                -1,
                false);
        }

        if (kind ==
                TrafficAnimationBindingKind.Steering &&
            axleIndex !=
                0)
        {
            return new TrafficAnimationBinding(
                TrafficAnimationBindingKind.Unsupported,
                axleIndex,
                isLeft);
        }

        return new TrafficAnimationBinding(
            kind,
            axleIndex,
            isLeft);
    }

    private TrafficVehicleAnimationPhysics
        GetTrafficVehicleAnimationPhysics(
            RuntimeVehicleInfo vehicleInfo)
    {
        if (_trafficVehicleAnimationPhysics.TryGetValue(
                vehicleInfo,
                out var cached))
        {
            return cached;
        }

        var wheelBase =
            vehicleInfo
                .Physics
                .WheelBaseMeters;

        if ((!wheelBase.HasValue ||
             !double.IsFinite(
                 wheelBase.Value) ||
             wheelBase.Value <=
                 0.0) &&
            vehicleInfo.Physics.FrontAxleLongitudinalMeters.HasValue &&
            vehicleInfo.Physics.RearAxleLongitudinalMeters.HasValue)
        {
            wheelBase =
                Math.Abs(
                    vehicleInfo.Physics.FrontAxleLongitudinalMeters.Value -
                    vehicleInfo.Physics.RearAxleLongitudinalMeters.Value);
        }

        if (wheelBase.HasValue &&
            (!double.IsFinite(
                 wheelBase.Value) ||
             wheelBase.Value <=
                 0.0))
        {
            wheelBase =
                null;
        }

        var trackWidth =
            vehicleInfo
                .Physics
                .TrackWidthMeters;

        if ((!trackWidth.HasValue ||
             !double.IsFinite(
                 trackWidth.Value) ||
             trackWidth.Value <=
                 0.0) &&
            vehicleInfo.Physics.Axles is
                { Count: > 0 })
        {
            trackWidth =
                vehicleInfo
                    .Physics
                    .Axles[0]
                    .MaximumWidthMeters;
        }

        if (trackWidth.HasValue &&
            (!double.IsFinite(
                 trackWidth.Value) ||
             trackWidth.Value <=
                 0.0))
        {
            trackWidth =
                null;
        }

        double? maximumSteeringRadians =
            null;

        var maximumSteeringDegrees =
            vehicleInfo
                .Physics
                .MaximumSteeringAngleDegrees;

        if (maximumSteeringDegrees.HasValue &&
            double.IsFinite(
                maximumSteeringDegrees.Value) &&
            maximumSteeringDegrees.Value >
                0.0)
        {
            maximumSteeringRadians =
                DegreesToRadians(
                    maximumSteeringDegrees.Value);
        }

        var axleCount =
            vehicleInfo.Physics.Axles?
                .Count ??
            0;

        var wheelRadii =
            new double[
                axleCount];

        for (var index = 0;
             index < axleCount;
             index++)
        {
            var diameter =
                vehicleInfo
                    .Physics
                    .Axles![
                        index]
                    .WheelDiameterMeters;

            if (!diameter.HasValue ||
                !double.IsFinite(
                    diameter.Value) ||
                diameter.Value <=
                    0.0)
            {
                continue;
            }

            wheelRadii[
                index] =
                Math.Clamp(
                    diameter.Value *
                        0.5,
                    0.05,
                    2.0);
        }

        double? averageWheelRadius =
            null;

        var averageDiameter =
            vehicleInfo
                .Physics
                .AverageWheelDiameterMeters;

        if (averageDiameter.HasValue &&
            double.IsFinite(
                averageDiameter.Value) &&
            averageDiameter.Value >
                0.0)
        {
            averageWheelRadius =
                Math.Clamp(
                    averageDiameter.Value *
                        0.5,
                    0.05,
                    2.0);
        }

        var result =
            new TrafficVehicleAnimationPhysics(
                wheelBase,
                trackWidth,
                maximumSteeringRadians,
                averageWheelRadius,
                wheelRadii);

        _trafficVehicleAnimationPhysics[
            vehicleInfo] =
            result;

        return result;
    }

    private static bool FailTrafficAnimationValue(
        out double value)
    {
        value =
            0.0;

        return false;
    }

    private static bool TryResolveTrafficSteeringAnimationValue(
        TrafficAnimationBinding binding,
        RuntimeTrafficAgentInfo agent,
        TrafficVehicleAnimationPhysics physics,
        out double value)
    {
        value =
            0.0;

        var curvature =
            agent.PathCurvaturePerMeter;

        if (!double.IsFinite(
                curvature))
        {
            return false;
        }

        var wheelBase =
            physics.WheelBaseMeters;

        if (!wheelBase.HasValue ||
            !double.IsFinite(
                wheelBase.Value) ||
            wheelBase.Value <=
                0.0)
        {
            return false;
        }

        if (Math.Abs(
                curvature) <
            0.000001)
        {
            value =
                0.0;
            return true;
        }

        var direction =
            Math.Sign(
                curvature);

        var absoluteCurvature =
            Math.Abs(
                curvature);

        var centerSteering =
            Math.Atan(
                wheelBase.Value *
                absoluteCurvature);

        var trackWidth =
            physics.TrackWidthMeters;

        var steering =
            centerSteering;

        if (trackWidth.HasValue &&
            double.IsFinite(
                trackWidth.Value) &&
            trackWidth.Value >
                0.0)
        {
            var centerRadius =
                1.0 /
                absoluteCurvature;

            var halfTrack =
                trackWidth.Value *
                0.5;

            var innerRadius =
                Math.Max(
                    centerRadius -
                        halfTrack,
                    0.05);

            var outerRadius =
                centerRadius +
                halfTrack;

            var innerSteering =
                Math.Atan(
                    wheelBase.Value /
                    innerRadius);

            var outerSteering =
                Math.Atan(
                    wheelBase.Value /
                    outerRadius);

            var innerWheel =
                direction >
                    0.0
                    ? !binding.IsLeft
                    : binding.IsLeft;

            steering =
                innerWheel
                    ? innerSteering
                    : outerSteering;
        }

        steering *=
            direction;

        var maximumSteeringRadians =
            physics.MaximumSteeringRadians;

        if (maximumSteeringRadians.HasValue)
        {
            steering =
                Math.Clamp(
                    steering,
                    -maximumSteeringRadians.Value,
                    maximumSteeringRadians.Value);
        }

        value =
            steering;

        return true;
    }

    private static bool TryResolveTrafficWheelRotationAnimationValue(
        TrafficAnimationBinding binding,
        RuntimeTrafficAgentInfo agent,
        TrafficVehicleAnimationPhysics physics,
        out double value)
    {
        value =
            0.0;

        double? radius =
            null;

        if (binding.AxleIndex >=
                0 &&
            binding.AxleIndex <
                physics.WheelRadiiMeters.Length)
        {
            var candidate =
                physics.WheelRadiiMeters[
                    binding.AxleIndex];

            if (candidate >
                0.0)
            {
                radius =
                    candidate;
            }
        }

        radius ??=
            physics.AverageWheelRadiusMeters;

        if (!radius.HasValue ||
            !double.IsFinite(
                radius.Value) ||
            radius.Value <=
                0.0)
        {
            return false;
        }

        value =
            agent.TraveledDistanceMeters /
            radius.Value;

        if (!double.IsFinite(
                value))
        {
            value =
                0.0;
            return false;
        }

        value =
            Math.IEEERemainder(
                value,
                Math.PI *
                2.0);

        return true;
    }

    private Matrix4x4 CreateVehicleAnimationMatrix(
        RuntimeObjectBatch batch)
    {
        var visited =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(
                batch.MeshIdentifier))
        {
            visited.Add(
                batch.MeshIdentifier);
        }

        return CreateVehicleAnimationMatrixWithParents(
            batch,
            visited);
    }

    private Matrix4x4 CreateVehicleAnimationMatrixWithParents(
        RuntimeObjectBatch batch,
        ISet<string> visited)
    {
        var result =
            CreateVehicleAnimationMatrix(
                batch.Animations,
                batch.SourceTransform,
                batch.StaticTransform,
                batch.SectionIndex);

        if (string.IsNullOrWhiteSpace(
                batch.AnimationParent) ||
            !visited.Add(
                batch.AnimationParent) ||
            !_vehicleAnimationParentBatches.TryGetValue(
                batch.AnimationParent,
                out var parent))
        {
            return result;
        }

        // OMSI [animparent] attaches the child to the animation of the
        // previously identified [mesh_ident]. With row-vector matrices the
        // child's own animation is applied first, then the parent chain.
        return result *
               CreateVehicleAnimationMatrixWithParents(
                   parent,
                   visited);
    }

    private Matrix4x4 CreateVehicleAnimationMatrix(
        IReadOnlyList<RuntimeVehicleAnimationInfo>? animations,
        Matrix4x4? sourceTransform,
        Matrix4x4? staticTransform,
        int sectionIndex = 0)
    {
        if (animations is null ||
            animations.Count == 0)
        {
            return Matrix4x4.Identity;
        }

        var result =
            Matrix4x4.Identity;

        foreach (var animation in
                 animations)
        {
            var variableValue =
                ResolveVehicleAnimationValue(
                    animation,
                    sectionIndex);

            var amount =
                variableValue *
                animation.Delta +
                animation.Offset;

            if (!double.IsFinite(
                    amount) ||
                Math.Abs(
                    amount) <
                0.0000001)
            {
                continue;
            }

            ResolveAnimationFrame(
                sourceTransform,
                staticTransform,
                animation,
                out var pivot,
                out var orientation);

            var axis =
                Vector3.TransformNormal(
                    Vector3.UnitX,
                    orientation);

            if (axis.LengthSquared() <
                0.000001f)
            {
                axis =
                    Vector3.UnitX;
            }
            else
            {
                axis =
                    Vector3.Normalize(
                        axis);
            }

            Matrix4x4 animationTransform;

            if (animation.Kind ==
                RuntimeVehicleAnimationKind.Translation)
            {
                animationTransform =
                    Matrix4x4.CreateTranslation(
                        axis *
                        (float)amount);
            }
            else
            {
                animationTransform =
                    Matrix4x4.CreateTranslation(
                        -pivot) *
                    Matrix4x4.CreateFromAxisAngle(
                        axis,
                        DegreesToRadians(
                            amount)) *
                    Matrix4x4.CreateTranslation(
                        pivot);
            }

            result *=
                animationTransform;
        }

        return result;
    }

    private static void ResolveAnimationFrame(
        Matrix4x4? sourceTransform,
        Matrix4x4? staticTransform,
        RuntimeVehicleAnimationInfo animation,
        out Vector3 pivot,
        out Matrix4x4 orientation)
    {
        orientation =
            Matrix4x4.Identity;

        if (animation.OriginFromMesh &&
            sourceTransform is
                Matrix4x4 source)
        {
            var mirror =
                Matrix4x4.CreateScale(
                    -1.0f,
                    1.0f,
                    1.0f);

            var converted =
                mirror *
                source *
                mirror;

            if (staticTransform is
                Matrix4x4 localTransform)
            {
                converted *=
                    localTransform;
            }

            pivot =
                new Vector3(
                    converted.M41,
                    converted.M42,
                    converted.M43);

            // Do not use Matrix4x4.Decompose here. OMSI O3D origins can
            // legitimately contain a reflected/negative object scale
            // (the MEP steering wheel is one real example). Decompose can
            // move that reflection into an arbitrary scale axis and flip
            // the animation axis. Preserve the authored local basis and
            // normalize its rows instead.
            var axisX =
                new Vector3(
                    converted.M11,
                    converted.M12,
                    converted.M13);
            var axisY =
                new Vector3(
                    converted.M21,
                    converted.M22,
                    converted.M23);
            var axisZ =
                new Vector3(
                    converted.M31,
                    converted.M32,
                    converted.M33);

            axisX =
                axisX.LengthSquared() >
                    0.000001f
                    ? Vector3.Normalize(
                        axisX)
                    : Vector3.UnitX;
            axisY =
                axisY.LengthSquared() >
                    0.000001f
                    ? Vector3.Normalize(
                        axisY)
                    : Vector3.UnitY;
            axisZ =
                axisZ.LengthSquared() >
                    0.000001f
                    ? Vector3.Normalize(
                        axisZ)
                    : Vector3.UnitZ;

            // An animation origin is a rotation frame, so it must not
            // carry a mirror/reflection. Some exported O3D meshes (the MEP
            // Quadbus steering wheel is a real example) have a negative
            // determinant in their source transform. OMSI still evaluates
            // anim_rot around a proper local X rotation axis. Keep mirrored
            // bases intact for anim_trans, but remove the X reflection for
            // rotations only. This changes the visual steering-wheel
            // direction without touching Axle_Steering_* or Ackermann
            // physics used by the road wheels.
            var handedness =
                Vector3.Dot(
                    Vector3.Cross(
                        axisX,
                        axisY),
                    axisZ);

            if (animation.Kind ==
                    RuntimeVehicleAnimationKind.Rotation &&
                handedness <
                    0.0f)
            {
                axisX =
                    -axisX;
            }

            orientation =
                new Matrix4x4(
                    axisX.X,
                    axisX.Y,
                    axisX.Z,
                    0.0f,
                    axisY.X,
                    axisY.Y,
                    axisY.Z,
                    0.0f,
                    axisZ.X,
                    axisZ.Y,
                    axisZ.Z,
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    1.0f);
        }
        else
        {
            pivot =
                ConvertCfgPosition(
                    animation.OriginX,
                    animation.OriginY,
                    animation.OriginZ);
        }

        var originRotation =
            CreateCfgOriginRotation(
                animation.OriginRotationX,
                animation.OriginRotationY,
                animation.OriginRotationZ);

        // origin_rot_* rotates the animation origin after the mesh-authored
        // origin has been adopted. With row-vector matrices that means
        // appending the CFG origin rotation to the mesh basis. Prepending it
        // collapses compound pivots such as the MEP Quadbus II steering wheel
        // to an almost vertical axis.
        orientation *=
            originRotation;
    }

    private static Vector3 ConvertCfgPosition(
        double x,
        double y,
        double z) =>
        new(
            (float)-x,
            (float)z,
            (float)y);

    private static Matrix4x4 CreateCfgOriginRotation(
        double xDegrees,
        double yDegrees,
        double zDegrees)
    {
        // OMSI animation origins use intrinsic X/Y/Z orientation.
        // System.Numerics applies row-vector matrices left-to-right, so the
        // equivalent composition is Z * Y * X. The previous X * Y * Z order
        // made a stock MAN steering-wheel origin such as X=-10, Y=90 lose
        // the X tilt entirely, leaving the steering axis vertical.
        //
        // Single-axis animations are unchanged; only compound origins are
        // corrected.
        var sourceRotation =
            Matrix4x4.CreateRotationZ(
                DegreesToRadians(
                    zDegrees)) *
            Matrix4x4.CreateRotationY(
                DegreesToRadians(
                    yDegrees)) *
            Matrix4x4.CreateRotationX(
                DegreesToRadians(
                    xDegrees));

        var basis =
            new Matrix4x4(
                -1, 0, 0, 0,
                 0, 0, 1, 0,
                 0, 1, 0, 0,
                 0, 0, 0, 1);

        return basis *
               sourceRotation *
               basis;
    }

    private bool TryGetVehicleTextTextureView(
        int? textTextureIndex,
        int sectionIndex,
        out ID3D11ShaderResourceView? view)
    {
        view =
            null;

        if (!textTextureIndex.HasValue ||
            _vehicleTextTextureRenderer is null ||
            _windowInfo.Vehicle is null)
        {
            return false;
        }

        var definition =
            _windowInfo.Vehicle.TextTextures
                .FirstOrDefault(
                    texture =>
                        texture.Index ==
                        textTextureIndex.Value);

        if (definition is null)
        {
            return false;
        }

        var value =
            ResolveSectionStringValue(
                sectionIndex,
                definition.StringVariable);

        var texture =
            _vehicleTextTextureRenderer
                .GetOrCreate(
                    definition,
                    value);

        if (texture is null)
        {
            if (_reportedMissingVehicleFonts.Add(
                    definition.FontName))
            {
                Console.WriteLine(
                    $"[texttexture] OMSI font unavailable: {definition.FontName}");
            }

            return false;
        }

        view =
            texture.View;

        return true;
    }

    private string? ResolveVehicleDiffuseTexturePath(
        RuntimeObjectBatch batch,
        IReadOnlyList<RuntimeVehicleFreeTextureInfo>? bindings)
    {

        if (bindings is null ||
            bindings.Count == 0)
        {
            return batch.TexturePath;
        }

        var baseFileName =
            Path.GetFileName(
                batch.TexturePath);

        foreach (var binding in
                 bindings)
        {
            if (!string.IsNullOrWhiteSpace(
                    binding.SourceTextureName) &&
                !string.Equals(
                    Path.GetFileName(
                        binding.SourceTextureName),
                    baseFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value =
                ResolveSectionStringValue(
                    batch.SectionIndex,
                    binding.VariableName);

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                continue;
            }

            if (TryResolveVehicleDynamicTexturePath(
                    value,
                    out var resolved))
            {
                return resolved;
            }
        }

        return batch.TexturePath;
    }

    private bool TryResolveVehicleDynamicTexturePath(
        string value,
        out string? resolved)
    {
        resolved =
            null;

        var normalized =
            value
                .Trim()
                .Trim('"')
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        if (normalized.Length == 0)
        {
            return false;
        }

        string root;

        try
        {
            root =
                EnsureTrailingDirectorySeparator(
                    Path.GetFullPath(
                        _windowInfo.ContentRoot));
        }
        catch
        {
            return false;
        }

        var candidates =
            new List<string>();

        if (Path.IsPathRooted(
                normalized))
        {
            candidates.Add(
                normalized);
        }
        else
        {
            candidates.Add(
                Path.Combine(
                    _windowInfo.ContentRoot,
                    normalized));

            var relativeBus =
                _windowInfo.Vehicle?
                    .RelativePath;

            if (!string.IsNullOrWhiteSpace(
                    relativeBus))
            {
                try
                {
                    var busPath =
                        Path.GetFullPath(
                            Path.Combine(
                                _windowInfo.ContentRoot,
                                "Vehicles",
                                relativeBus
                                    .Replace(
                                        '/',
                                        Path.DirectorySeparatorChar)
                                    .Replace(
                                        '\\',
                                        Path.DirectorySeparatorChar)));

                    var vehicleDirectory =
                        Path.GetDirectoryName(
                            busPath);

                    if (!string.IsNullOrWhiteSpace(
                            vehicleDirectory))
                    {
                        candidates.Add(
                            Path.Combine(
                                vehicleDirectory,
                                normalized));

                        if (!normalized.StartsWith(
                                "Texture" +
                                Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add(
                                Path.Combine(
                                    vehicleDirectory,
                                    "Texture",
                                    normalized));
                        }
                    }
                }
                catch
                {
                }
            }
        }

        foreach (var candidate in
                 candidates)
        {
            try
            {
                var fullPath =
                    Path.GetFullPath(
                        candidate);

                if (!fullPath.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(
                        fullPath))
                {
                    continue;
                }

                resolved =
                    fullPath;
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static string EnsureTrailingDirectorySeparator(
        string path) =>
        path.EndsWith(
            Path.DirectorySeparatorChar) ||
        path.EndsWith(
            Path.AltDirectorySeparatorChar)
            ? path
            : path +
              Path.DirectorySeparatorChar;

    private bool TryGetVehicleTextureView(
        string? texturePath,
        out ID3D11ShaderResourceView? view)
    {
        view = null;

        if (string.IsNullOrWhiteSpace(
                texturePath))
        {
            return false;
        }

        if (_reflectionRenderingEnabled &&
            _reflectionTargets.TryGetValue(
                texturePath,
                out var reflection))
        {
            // Never sample a reflection texture while rendering a reflection
            // target. A mirror surface can otherwise read from the same
            // resource currently bound as the render target, producing a
            // D3D11 read/write hazard and recursive/black mirror output.
            if (_renderingReflectionPass)
            {
                return false;
            }

            view =
                reflection.ShaderResourceView;
            return true;
        }

        if (_objectTextureCache.TryGetValue(
                texturePath,
                out var texture))
        {
            view =
                texture.View;
            return true;
        }

        if (_failedObjectTexturePaths.Contains(
                texturePath) ||
            _objectTextureLoader is null ||
            !File.Exists(
                texturePath))
        {
            return false;
        }

        var loaded =
            _objectTextureLoader.TryLoad(
                texturePath);

        if (loaded is null)
        {
            _failedObjectTexturePaths.Add(
                texturePath);
            return false;
        }

        _objectTextureCache[
            texturePath] =
            loaded;

        view =
            loaded.View;
        return true;
    }

    private Vector3 ResolveActiveCameraPosition()
    {
        if (_vehiclePreviewMode)
        {
            return ResolveVehiclePreviewCameraPosition();
        }

        if (!_driveMode)
        {
            return _camera.Position;
        }

        var vehicle =
            _windowInfo.Vehicle;

        if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Driver &&
            vehicle?.DriverCameras.Count > 0)
        {
            var index =
                Math.Clamp(
                    _driverCameraIndex,
                    0,
                    vehicle.DriverCameras.Count - 1);

            return _vehicle.GetDriverCameraPosition(
                vehicle.DriverCameras[index]);
        }

        if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Passenger &&
            vehicle?.PassengerCameras.Count > 0)
        {
            var index =
                Math.Clamp(
                    _passengerCameraIndex,
                    0,
                    vehicle.PassengerCameras.Count - 1);

            return _vehicle.GetPassengerCameraPosition(
                vehicle.PassengerCameras[index]);
        }

        return _vehicle.GetChaseCameraPosition(
            vehicle?.OutsideCameraCenter,
            _exteriorCameraYawOffsetRadians,
            _exteriorCameraPitchOffsetRadians,
            _exteriorCameraDistanceScale);
    }

    private Matrix4x4 CreateViewProjection(
        bool ignoreOverride = false)
    {
        if (!ignoreOverride &&
            _viewProjectionOverride.HasValue)
        {
            return _viewProjectionOverride.Value;
        }

        var aspect =
            Math.Max(ClientSize.Width, 1) /
            (float)Math.Max(
                ClientSize.Height,
                1);

        if (_vehiclePreviewMode)
        {
            return CreateVehiclePreviewViewProjection(
                aspect);
        }

        if (!_driveMode)
        {
            return _camera.CreateViewProjection(
                aspect,
                _terrainGeometry);
        }

        if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Driver &&
            _windowInfo.Vehicle?.DriverCameras.Count > 0)
        {
            _driverCameraIndex =
                Math.Clamp(
                    _driverCameraIndex,
                    0,
                    _windowInfo.Vehicle.DriverCameras.Count - 1);

            return _vehicle.CreateDriverViewProjection(
                _windowInfo.Vehicle.DriverCameras[
                    _driverCameraIndex],
                aspect,
                _terrainGeometry,
                _interiorCameraYawOffsetRadians,
                _interiorCameraPitchOffsetRadians,
                _interiorCameraFieldOfViewScale);
        }

        if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Passenger &&
            _windowInfo.Vehicle?.PassengerCameras.Count > 0)
        {
            _passengerCameraIndex =
                Math.Clamp(
                    _passengerCameraIndex,
                    0,
                    _windowInfo.Vehicle.PassengerCameras.Count - 1);

            return _vehicle.CreatePassengerViewProjection(
                _windowInfo.Vehicle.PassengerCameras[
                    _passengerCameraIndex],
                aspect,
                _terrainGeometry,
                _interiorCameraYawOffsetRadians,
                _interiorCameraPitchOffsetRadians,
                _interiorCameraFieldOfViewScale);
        }

        if (_vehicleViewMode !=
                RuntimeVehicleViewMode.Exterior &&
            _windowInfo.Vehicle?.DriverCameras.Count > 0)
        {
            _driverCameraIndex =
                Math.Clamp(
                    _windowInfo.Vehicle.StandardDriverCameraIndex,
                    0,
                    _windowInfo.Vehicle.DriverCameras.Count - 1);

            return _vehicle.CreateDriverViewProjection(
                _windowInfo.Vehicle.DriverCameras[
                    _driverCameraIndex],
                aspect,
                _terrainGeometry,
                _interiorCameraYawOffsetRadians,
                _interiorCameraPitchOffsetRadians,
                _interiorCameraFieldOfViewScale);
        }

        return _vehicle.CreateChaseViewProjection(
            aspect,
            _terrainGeometry,
            _windowInfo.Vehicle?.OutsideCameraCenter,
            _exteriorCameraYawOffsetRadians,
            _exteriorCameraPitchOffsetRadians,
            _exteriorCameraDistanceScale);
    }

    private double ResolveTrafficStepIntervalSeconds()
    {
        // Borrow the performance-guard strategy already proven in NavBR:
        // expensive AI work does not need to run at the render cadence.
        // Keep light traffic near 60 Hz, medium traffic near 30 Hz and
        // heavy traffic near 20 Hz. Under frame pressure, never increase
        // AI cadence until rendering has recovered.
        var intervalSeconds =
            _trafficAgents.Count switch
            {
                >= 80 =>
                    0.050,
                >= 40 =>
                    0.033,
                _ =>
                    0.016
            };

        var framePressure =
            _lastObservedFrameMilliseconds >
                    0.0
                ? _lastObservedFrameMilliseconds /
                    Math.Max(
                        _targetFrameMilliseconds,
                        1.0)
                : 1.0;

        if (framePressure >=
            1.5)
        {
            intervalSeconds =
                Math.Max(
                    intervalSeconds,
                    0.050);
        }
        else if (framePressure >=
                 1.15)
        {
            intervalSeconds =
                Math.Max(
                    intervalSeconds,
                    0.033);
        }

        return intervalSeconds;
    }

    private void UpdateSimulation()
    {
        if (_vehiclePreviewMode)
        {
            return;
        }

        var now =
            _frameClock.Elapsed.TotalSeconds;

        var elapsed =
            _lastFrameTimeSeconds <= 0.0
                ? 0.0
                : now - _lastFrameTimeSeconds;

        _lastFrameTimeSeconds = now;

        if (!CanDrawTerrain() ||
            elapsed <= 0.0)
        {
            return;
        }

        var deltaSeconds =
            (float)Math.Clamp(
                elapsed,
                0.0,
                0.1);

        var trafficStepped =
            false;

        if (_trafficStep is not null)
        {
            _trafficStepAccumulatedSeconds +=
                deltaSeconds;

            var trafficStepIntervalSeconds =
                ResolveTrafficStepIntervalSeconds();

            if (_trafficStepAccumulatedSeconds >=
                trafficStepIntervalSeconds)
            {
                var trafficDeltaSeconds =
                    Math.Clamp(
                        _trafficStepAccumulatedSeconds,
                        0.0,
                        0.1);

                _trafficStepAccumulatedSeconds =
                    0.0;

                _trafficAgents =
                    _trafficStep(
                        trafficDeltaSeconds) ??
                    Array.Empty<RuntimeTrafficAgentInfo>();

                _lastTrafficSimulationStepSeconds =
                    now;

                trafficStepped =
                    true;
            }
        }

        // Traffic/signal state changes only when the simulation advances.
        // Do not allocate fresh snapshots and rebuild lookups at render FPS
        // when the AI loop intentionally runs at 20-60 Hz.
        if ((trafficStepped ||
             _trafficStep is null) &&
            _trafficSignalStateProvider is not null)
        {
            _trafficSignalStates =
                _trafficSignalStateProvider() ??
                Array.Empty<RuntimeTrafficSignalStateInfo>();

            RebuildTrafficSignalStateLookup();
        }

        if ((trafficStepped ||
             _trafficStep is null) &&
            _railSignalStateProvider is not null)
        {
            _railSignalRouteStates =
                _railSignalStateProvider() ??
                Array.Empty<RuntimeRailSignalRouteStateInfo>();

            RebuildRailSignalRuntimeLookup();
        }

        var controllerFrame =
            _controllerInputEnabled
                ? _omsiGameController?.Poll() ??
                  RuntimeOmsiControllerFrame.Empty
                : RuntimeOmsiControllerFrame.Empty;

        foreach (var trigger in
                 controllerFrame.Triggered)
        {
            DispatchOmsiScriptTrigger(
                trigger);
        }

        foreach (var trigger in
                 controllerFrame.Pressed)
        {
            PressControllerHostAction(
                trigger);
        }

        foreach (var trigger in
                 controllerFrame.Released)
        {
            DispatchOmsiScriptTrigger(
                ReleaseTriggerName(
                    trigger));

            ReleaseControllerHostAction(
                trigger);
        }

        _controllerClutchInput =
            controllerFrame.Clutch ??
            0.0f;

        var acceleratorHeld =
            IsHostActionHeld(
                RuntimeOmsiHostInputAction.Accelerate);

        var brakeIncreaseHeld =
            IsHostActionHeld(
                RuntimeOmsiHostInputAction.BrakeIncrease);

        var brakeReleaseHeld =
            IsHostActionHeld(
                RuntimeOmsiHostInputAction.BrakeRelease);

        var steerRightHeld =
            IsHostActionHeld(
                RuntimeOmsiHostInputAction.SteerRight);

        var steerLeftHeld =
            IsHostActionHeld(
                RuntimeOmsiHostInputAction.SteerLeft);

        var centerSteeringHeld =
            IsHostActionHeld(
                RuntimeOmsiHostInputAction.SteerCenter);

        if (_driveMode)
        {
            if (_mouseDriveMode)
            {
                _vehicle.UpdateOmsiMouseControls(
                    _mouseDriveAccelerator,
                    _mouseDriveBrake,
                    _mouseDriveSteering,
                    deltaSeconds);
            }
            else if (controllerFrame.HasDrivingAxis)
            {
                _vehicle.UpdateOmsiControllerControls(
                    accelerator:
                        controllerFrame.Accelerator ??
                        (acceleratorHeld
                            ? 1.0f
                            : 0.0f),
                    brake:
                        controllerFrame.Brake ??
                        (brakeIncreaseHeld
                            ? 1.0f
                            : brakeReleaseHeld
                                ? 0.0f
                                : _vehicle.BrakeLevel),
                    steering:
                        controllerFrame.Steering ??
                        (steerRightHeld ||
                         steerLeftHeld
                            ? Math.Clamp(
                                (steerRightHeld
                                    ? 1.0f
                                    : 0.0f) -
                                (steerLeftHeld
                                    ? 1.0f
                                    : 0.0f),
                                -1.0f,
                                1.0f)
                            : centerSteeringHeld
                                ? 0.0f
                                : _vehicle.SteeringInput),
                    deltaSeconds:
                        deltaSeconds);
            }
            else
            {
                var steeringDirection =
                    (steerRightHeld
                        ? 1.0f
                        : 0.0f) -
                    (steerLeftHeld
                        ? 1.0f
                        : 0.0f);

                _vehicle.UpdateOmsiControls(
                    acceleratorHeld:
                        acceleratorHeld,
                    brakeIncreaseHeld:
                        brakeIncreaseHeld,
                    brakeReleaseHeld:
                        brakeReleaseHeld,
                    steeringDirection:
                        steeringDirection,
                    centerSteeringHeld:
                        centerSteeringHeld,
                    automaticSteeringCenter:
                        _automaticSteeringCenter,
                    deltaSeconds:
                        deltaSeconds);
            }
        }
        else
        {
            var forward =
                (IsFreeCameraKeyHeld(Keys.W) ? 1.0f : 0.0f) -
                (IsFreeCameraKeyHeld(Keys.Down) ? 1.0f : 0.0f);

            var right =
                (IsFreeCameraKeyHeld(Keys.D) ? 1.0f : 0.0f) -
                (IsFreeCameraKeyHeld(Keys.A) ? 1.0f : 0.0f);

            var up =
                (IsFreeCameraKeyHeld(Keys.E) ? 1.0f : 0.0f) -
                (IsFreeCameraKeyHeld(Keys.Q) ? 1.0f : 0.0f);

            _camera.Move(
                forward,
                right,
                up,
                deltaSeconds,
                _pressedKeys.Contains(Keys.ShiftKey),
                _pressedKeys.Contains(Keys.ControlKey));
        }

        if (_driveMode)
        {
            UpdateArticulatedSections(
                deltaSeconds);
        }

        UpdateVehicleScripts(
            deltaSeconds,
            now);

        if (_driveMode &&
            now -
                _lastVehiclePhysicsDiagnosticsSeconds >=
            1.0)
        {
            WriteVehiclePhysicsDiagnostics(
                now);
        }

        var listenerPosition =
            ResolveActiveCameraPosition();

        if (!_vehicleRemoved)
        {
            _omsiAudio?.Update(
                _scriptRuntime,
                IsInteriorSoundView(),
                ResolveLeadEngineRunning(),
                listenerPosition,
                _vehicle.Position,
                _vehicle.HeadingRadians);
        }

        if (!_vehicleRemoved)
        {
            foreach (var pair in
                     _articulatedOmsiAudio)
            {
                var section =
                    _windowInfo.Vehicle?.Sections?
                        .FirstOrDefault(
                            item =>
                                item.Index ==
                                pair.Key);

                if (section is null)
                {
                    continue;
                }

                ResolveArticulatedSectionAudioPose(
                    section,
                    out var sectionPosition,
                    out var sectionHeading);

                var sectionRuntime =
                    ResolveScriptRuntimeForSection(
                        section.Index);

                pair.Value.Update(
                    sectionRuntime,
                    IsInteriorSoundView() &&
                        section.OpenForSound,
                    ResolveSectionEngineRunning(
                        sectionRuntime),
                    listenerPosition,
                    sectionPosition,
                    sectionHeading);
            }
        }

        _trafficAudioStepAccumulatedSeconds +=
            deltaSeconds;

        var trafficAudioIntervalSeconds =
            _trafficAgents.Count >=
                    40 ||
                _lastObservedFrameMilliseconds >
                    _targetFrameMilliseconds *
                    1.20
                ? 0.050
                : 0.033;

        if (_trafficAudioStepAccumulatedSeconds >=
            trafficAudioIntervalSeconds)
        {
            _trafficAudioStepAccumulatedSeconds =
                0.0;

            UpdateTrafficOmsiAudio(
                listenerPosition);
        }

        UpdateTrafficCollisionAndRules(
            now,
            deltaSeconds);

        UpdateVehicleAnimationStates(
            deltaSeconds);

        UpdateVehicleLightStates(
            deltaSeconds);
    }

    private void UpdateTrafficOmsiAudio(
        Vector3 listenerPosition)
    {
        if (!_aiVehicleSoundsEnabled)
        {
            foreach (var state in
                     _trafficOmsiAudio.Values)
            {
                state.Audio.Dispose();
            }

            _trafficOmsiAudio.Clear();
            return;
        }

        _activeTrafficAudioAgentIds.Clear();

        foreach (var agent in
                 _trafficAgents)
        {
            _activeTrafficAudioAgentIds.Add(
                agent.AgentIndex);
        }

        _staleTrafficAudioAgentIds.Clear();

        foreach (var agentId in
                 _trafficOmsiAudio.Keys)
        {
            if (!_activeTrafficAudioAgentIds.Contains(
                    agentId))
            {
                _staleTrafficAudioAgentIds.Add(
                    agentId);
            }
        }

        foreach (var staleAgentId in
                 _staleTrafficAudioAgentIds)
        {
            _trafficOmsiAudio[
                staleAgentId]
                .Audio
                .Dispose();

            _trafficOmsiAudio.Remove(
                staleAgentId);
        }

        if (_trafficAgents.Count ==
                0 ||
            _windowInfo.TrafficVehicleAssets is
                null)
        {
            return;
        }

        var perAgentVoiceBudget =
            Math.Max(
                4,
                _maximumSoundCount /
                Math.Max(
                    _trafficAgents.Count,
                    1));

        var activationDistanceSquared =
            TrafficAudioActivationDistanceMeters *
            TrafficAudioActivationDistanceMeters;

        var deactivationDistanceSquared =
            TrafficAudioDeactivationDistanceMeters *
            TrafficAudioDeactivationDistanceMeters;

        foreach (var agent in
                 _trafficAgents)
        {
            var dx =
                (float)agent.X -
                listenerPosition.X;
            var dy =
                (float)agent.Y -
                listenerPosition.Y;
            var dz =
                (float)agent.Z -
                listenerPosition.Z;

            var distanceSquared =
                dx *
                    dx +
                dy *
                    dy +
                dz *
                    dz;

            _trafficOmsiAudio.TryGetValue(
                agent.AgentIndex,
                out var state);

            if (distanceSquared >
                deactivationDistanceSquared)
            {
                if (state is not null)
                {
                    state.Audio.Dispose();

                    _trafficOmsiAudio.Remove(
                        agent.AgentIndex);
                }

                continue;
            }

            if (!_windowInfo.TrafficVehicleAssets.TryGetValue(
                    agent.VehiclePath,
                    out var vehicleInfo) ||
                string.IsNullOrWhiteSpace(
                    vehicleInfo.SoundConfigPath))
            {
                if (state is not null)
                {
                    state.Audio.Dispose();

                    _trafficOmsiAudio.Remove(
                        agent.AgentIndex);
                }

                continue;
            }

            var needsNewAudio =
                state is null ||
                !state.VehiclePath.Equals(
                    agent.VehiclePath,
                    StringComparison.OrdinalIgnoreCase);

            if (needsNewAudio)
            {
                state?.Audio.Dispose();

                if (distanceSquared >
                    activationDistanceSquared)
                {
                    _trafficOmsiAudio.Remove(
                        agent.AgentIndex);
                    continue;
                }

                var audio =
                    RuntimeOmsiAudioHost.TryCreate(
                        vehicleInfo.SoundConfigPath,
                        _masterVolume,
                        perAgentVoiceBudget);

                if (audio is null)
                {
                    _trafficOmsiAudio.Remove(
                        agent.AgentIndex);
                    continue;
                }

                state =
                    new TrafficOmsiAudioState(
                        agent.VehiclePath,
                        audio);

                _trafficOmsiAudio[
                    agent.AgentIndex] =
                    state;

                Console.WriteLine(
                    $"[traffic-ai] audio agent={agent.AgentIndex}; vehicle={Path.GetFileName(agent.VehiclePath)}; sounds={audio.ExistingFileCount}/{audio.SoundCount}");
            }

            if (state is null)
            {
                continue;
            }

            var engineRunning =
                ResolveTrafficEngineRunning(
                    agent.ScriptRuntime);

            state.Audio.Update(
                agent.ScriptRuntime,
                interiorView:
                    false,
                engineRunning:
                    engineRunning,
                listenerPosition,
                new Vector3(
                    (float)agent.X,
                    (float)agent.Y,
                    (float)agent.Z),
                (float)agent.HeadingRadians,
                forceVehicleSpatial:
                    true);

            var activeLoops =
                state.Audio
                    .BuildActiveLoopDiagnostics();

            var diagnosticSignature =
                $"engineRunning={engineRunning};activeLoops={(activeLoops.Count == 0 ? "<none>" : string.Join(",", activeLoops))}";

            if (!string.Equals(
                    state.LastDiagnosticSignature,
                    diagnosticSignature,
                    StringComparison.Ordinal))
            {
                state.LastDiagnosticSignature =
                    diagnosticSignature;

                WriteTrafficAudioDiagnostics(
                    agent,
                    diagnosticSignature);
            }
        }
    }

    private static void WriteTrafficAudioDiagnostics(
        RuntimeTrafficAgentInfo agent,
        string diagnosticSignature)
    {
        RuntimeDiagnosticLogWriter.Enqueue(
            "traffic-audio.log",
            $"{DateTimeOffset.Now:O}|agent={agent.AgentIndex}|vehicle={agent.VehiclePath}|position={agent.X:0.00},{agent.Y:0.00},{agent.Z:0.00}|{diagnosticSignature}{Environment.NewLine}");
    }

    private static bool ResolveTrafficEngineRunning(
        OmsiScriptRuntime? runtime)
    {
        // Never invent an active AI engine. Some lightweight AI vehicles do
        // not expose engine_on/engine_injection_on at all; treating that as
        // true makes an engine loop start immediately when the map opens and
        // sounds like the player's bus is already running. Only an explicit
        // OMSI script state may enable the propulsion loop.
        if (runtime is null)
        {
            return false;
        }

        if (runtime.HasLocalVariable(
                "engine_on"))
        {
            return runtime.GetLocal(
                       "engine_on") >
                   0.5;
        }

        if (runtime.HasLocalVariable(
                "engine_injection_on"))
        {
            return runtime.GetLocal(
                       "engine_injection_on") >
                   0.5;
        }

        return false;
    }

    private void UpdateTrafficCollisionAndRules(
        double nowSeconds,
        float deltaSeconds)
    {
        if (!_driveMode ||
            _vehicleRemoved ||
            _windowInfo.Vehicle is null)
        {
            _activeTrafficCollisionAgents.Clear();
            _speedingSeconds =
                0.0;
            return;
        }

        UpdateSceneryCollisionState();

        UpdateTrafficCollisionState(
            nowSeconds);

        _trafficRuleSampleSeconds +=
            Math.Max(
                deltaSeconds,
                0.0f);

        if (_trafficRuleSampleSeconds <
            0.25)
        {
            return;
        }

        var sampledSeconds =
            _trafficRuleSampleSeconds;

        _trafficRuleSampleSeconds =
            0.0;

        UpdateRedLightRule(
            nowSeconds);

        UpdatePriorityRule(
            nowSeconds);

        var speedLimit =
            ResolveNearestRoadSpeedLimit();

        if (!speedLimit.HasValue)
        {
            _speedingSeconds =
                0.0;
            _lastSpeedLimitKilometersPerHour =
                null;
            return;
        }

        if (!_lastSpeedLimitKilometersPerHour.HasValue ||
            Math.Abs(
                _lastSpeedLimitKilometersPerHour.Value -
                speedLimit.Value) >
            0.1)
        {
            _speedingSeconds =
                0.0;
            _lastSpeedLimitKilometersPerHour =
                speedLimit.Value;
        }

        var speedKph =
            Math.Abs(
                _vehicle.SpeedKph);

        if (speedKph >
            speedLimit.Value +
                5.0)
        {
            _speedingSeconds +=
                sampledSeconds;

            if (_speedingSeconds >=
                    3.0 &&
                nowSeconds -
                    _lastSpeedViolationSeconds >=
                8.0)
            {
                _lastSpeedViolationSeconds =
                    nowSeconds;

                _speedViolationCount++;

                var speedOverLimit =
                    Math.Max(
                        speedKph -
                            speedLimit.Value,
                        0.0);

                var penaltyPoints =
                    speedOverLimit >=
                            30.0
                        ? 5
                        : speedOverLimit >=
                                20.0
                            ? 4
                            : speedOverLimit >=
                                    10.0
                                ? 2
                                : 1;

                var fineCredits =
                    Math.Max(
                        25,
                        (int)Math.Ceiling(
                            speedOverLimit *
                            5.0));

                RegisterTrafficViolation(
                    nowSeconds,
                    "speeding",
                    penaltyPoints,
                    fineCredits,
                    $"speed={speedKph:0.0} km/h; limit={speedLimit.Value:0.0} km/h; over={speedOverLimit:0.0} km/h");

                _speedingSeconds =
                    0.0;
            }
        }
        else
        {
            _speedingSeconds =
                0.0;
        }
    }

    private static IReadOnlyList<RuntimeSceneryCollisionVolume>
        BuildSceneryCollisionVolumes(
            RuntimeWindowInfo windowInfo,
            RuntimeTerrainSampler terrainSurfaceSampler)
    {
        if (windowInfo.Objects.Count ==
                0 ||
            windowInfo.SceneryAssets.Count ==
                0)
        {
            return Array.Empty<RuntimeSceneryCollisionVolume>();
        }

        const double tileSizeMeters =
            300.0;

        var volumes =
            new List<RuntimeSceneryCollisionVolume>();

        var key =
            0;

        foreach (var instance in
                 windowInfo.Objects)
        {
            if (windowInfo.DynamicSceneryObjectIds?.Contains(
                    instance.ObjectId) ==
                true)
            {
                continue;
            }

            if (!windowInfo.SceneryAssets.TryGetValue(
                    instance.AssetPath,
                    out var asset) ||
                asset.NoCollision ||
                asset.Surface ||
                (asset.CollisionBounds is null &&
                 asset.BoundingBox is null))
            {
                continue;
            }

            // OMSI [surface] scenery (crossings, road plates, bridge decks,
            // etc.) contributes its real triangles to the driving-surface
            // sampler in RuntimeDriveVehicle. Treating the bounds of its
            // collision mesh as a solid OBB creates invisible walls across
            // otherwise drivable streets, so surface objects never become
            // blocking scenery volumes here.
            //
            // Do not let a fallback [boundingbox] create an invisible wall
            // for missing/protected/unrenderable scenery. Explicit collision
            // meshes remain authoritative even when the visible asset itself
            // cannot be rendered.
            var hasRenderableVisual =
                asset.Tree is not null ||
                asset.Meshes.Any(
                    static mesh =>
                        string.IsNullOrWhiteSpace(
                            mesh.ErrorCode) &&
                        mesh.Positions.Length >= 3 &&
                        mesh.Indices.Length >= 3);

            if (asset.CollisionBounds is null &&
                !hasRenderableVisual)
            {
                continue;
            }

            var worldX =
                instance.TileX *
                    tileSizeMeters +
                instance.X;

            var worldZ =
                instance.TileY *
                    tileSizeMeters +
                instance.Z;

            var terrainOffset =
                0.0f;

            if (!asset.UsesAbsoluteHeight &&
                terrainSurfaceSampler.TrySample(
                    worldX,
                    worldZ,
                    out var groundHeight))
            {
                terrainOffset =
                    groundHeight;
            }

            var heading =
                (float)(
                    instance.HeadingDegrees *
                    Math.PI /
                    180.0);

            var rotation =
                Matrix4x4.CreateFromYawPitchRoll(
                    heading,
                    (float)(
                        instance.PitchDegrees *
                        Math.PI /
                        180.0),
                    (float)(
                        instance.BankDegrees *
                        Math.PI /
                        180.0));

            var headingRotation =
                Matrix4x4.CreateRotationY(
                    heading);

            Vector3 localCenter;
            float halfLength;
            float halfWidth;
            float halfHeight;

            if (asset.CollisionBounds is
                    { } collisionBounds)
            {
                // Dedicated collision meshes are O3D model-space geometry:
                // X/Z form the ground plane and Y is vertical. Mirror X to
                // match the scenery rendering transform.
                localCenter =
                    new Vector3(
                        (float)(-
                            (collisionBounds.MinimumX +
                             collisionBounds.MaximumX) *
                            0.5),
                        (float)(
                            (collisionBounds.MinimumY +
                             collisionBounds.MaximumY) *
                            0.5),
                        (float)(
                            (collisionBounds.MinimumZ +
                             collisionBounds.MaximumZ) *
                            0.5));

                halfWidth =
                    (float)Math.Max(
                        (collisionBounds.MaximumX -
                         collisionBounds.MinimumX) *
                            0.5,
                        0.05);

                halfHeight =
                    (float)Math.Max(
                        (collisionBounds.MaximumY -
                         collisionBounds.MinimumY) *
                            0.5,
                        0.05);

                halfLength =
                    (float)Math.Max(
                        (collisionBounds.MaximumZ -
                         collisionBounds.MinimumZ) *
                            0.5,
                        0.05);
            }
            else
            {
                var box =
                    asset.BoundingBox!;

                // OMSI [boundingbox]: X/Y are the horizontal object plane and
                // Z is height. Mirror CenterX to match scenery rendering.
                localCenter =
                    new Vector3(
                        (float)-box.CenterX,
                        (float)box.CenterZ,
                        (float)box.CenterY);

                halfWidth =
                    (float)Math.Max(
                        box.LengthX *
                            0.5,
                        0.05);

                halfHeight =
                    (float)Math.Max(
                        box.HeightZ *
                            0.5,
                        0.05);

                halfLength =
                    (float)Math.Max(
                        box.WidthY *
                            0.5,
                        0.05);
            }

            var forward3 =
                Vector3.TransformNormal(
                    Vector3.UnitZ,
                    headingRotation);

            var right3 =
                Vector3.TransformNormal(
                    -Vector3.UnitX,
                    headingRotation);

            var forward =
                Vector2.Normalize(
                    new Vector2(
                        forward3.X,
                        forward3.Z));

            var right =
                Vector2.Normalize(
                    new Vector2(
                        right3.X,
                        right3.Z));

            var minimumForward =
                float.PositiveInfinity;
            var maximumForward =
                float.NegativeInfinity;
            var minimumRight =
                float.PositiveInfinity;
            var maximumRight =
                float.NegativeInfinity;
            var minimumVertical =
                float.PositiveInfinity;
            var maximumVertical =
                float.NegativeInfinity;

            for (var xSign = -1;
                 xSign <=
                     1;
                 xSign +=
                     2)
            {
                for (var ySign = -1;
                     ySign <=
                         1;
                     ySign +=
                         2)
                {
                    for (var zSign = -1;
                         zSign <=
                             1;
                         zSign +=
                             2)
                    {
                        var localCorner =
                            localCenter +
                            new Vector3(
                                xSign *
                                    halfWidth,
                                ySign *
                                    halfHeight,
                                zSign *
                                    halfLength);

                        var rotatedCorner =
                            Vector3.TransformNormal(
                                localCorner,
                                rotation);

                        var horizontalCorner =
                            new Vector2(
                                rotatedCorner.X,
                                rotatedCorner.Z);

                        var forwardProjection =
                            Vector2.Dot(
                                horizontalCorner,
                                forward);

                        var rightProjection =
                            Vector2.Dot(
                                horizontalCorner,
                                right);

                        minimumForward =
                            Math.Min(
                                minimumForward,
                                forwardProjection);

                        maximumForward =
                            Math.Max(
                                maximumForward,
                                forwardProjection);

                        minimumRight =
                            Math.Min(
                                minimumRight,
                                rightProjection);

                        maximumRight =
                            Math.Max(
                                maximumRight,
                                rightProjection);

                        minimumVertical =
                            Math.Min(
                                minimumVertical,
                                rotatedCorner.Y);

                        maximumVertical =
                            Math.Max(
                                maximumVertical,
                                rotatedCorner.Y);
                    }
                }
            }

            if (!float.IsFinite(
                    minimumForward) ||
                !float.IsFinite(
                    maximumForward) ||
                !float.IsFinite(
                    minimumRight) ||
                !float.IsFinite(
                    maximumRight) ||
                !float.IsFinite(
                    minimumVertical) ||
                !float.IsFinite(
                    maximumVertical))
            {
                continue;
            }

            var centerForward =
                (minimumForward +
                 maximumForward) *
                0.5f;

            var centerRight =
                (minimumRight +
                 maximumRight) *
                0.5f;

            var center =
                new Vector2(
                    (float)worldX,
                    (float)worldZ) +
                forward *
                    centerForward +
                right *
                    centerRight;

            halfLength =
                Math.Max(
                    (maximumForward -
                     minimumForward) *
                        0.5f,
                    0.05f);

            halfWidth =
                Math.Max(
                    (maximumRight -
                     minimumRight) *
                        0.5f,
                    0.05f);

            var baseY =
                (float)instance.Y +
                terrainOffset;

            IReadOnlyList<RuntimeSceneryCollisionTriangle>? collisionTriangles =
                null;

            if (asset.CollisionGeometry is
                    { Positions.Length: >= 9, Indices.Length: >= 3 } collisionGeometry)
            {
                var triangles =
                    new List<RuntimeSceneryCollisionTriangle>(
                        collisionGeometry.Indices.Length /
                        3);

                for (var triangleIndex = 0;
                     triangleIndex + 2 <
                         collisionGeometry.Indices.Length;
                     triangleIndex +=
                         3)
                {
                    var rawI0 =
                        collisionGeometry.Indices[
                            triangleIndex];
                    var rawI1 =
                        collisionGeometry.Indices[
                            triangleIndex +
                            1];
                    var rawI2 =
                        collisionGeometry.Indices[
                            triangleIndex +
                            2];

                    var vertexCount =
                        collisionGeometry.Positions.Length /
                        3;

                    if (rawI0 >
                            int.MaxValue ||
                        rawI1 >
                            int.MaxValue ||
                        rawI2 >
                            int.MaxValue)
                    {
                        continue;
                    }

                    var i0 =
                        (int)rawI0;
                    var i1 =
                        (int)rawI1;
                    var i2 =
                        (int)rawI2;

                    if (i0 >= vertexCount ||
                        i1 >= vertexCount ||
                        i2 >= vertexCount)
                    {
                        continue;
                    }

                    Vector3 ResolveCollisionVertex(
                        int vertexIndex)
                    {
                        var offset =
                            vertexIndex *
                            3;

                        var sourceX =
                            collisionGeometry.Positions[
                                offset];
                        var sourceY =
                            collisionGeometry.Positions[
                                offset +
                                1];
                        var sourceZ =
                            collisionGeometry.Positions[
                                offset +
                                2];

                        if (!float.IsFinite(
                                sourceX) ||
                            !float.IsFinite(
                                sourceY) ||
                            !float.IsFinite(
                                sourceZ))
                        {
                            return new Vector3(
                                float.NaN,
                                float.NaN,
                                float.NaN);
                        }

                        var local =
                            new Vector3(
                                -sourceX,
                                sourceY,
                                sourceZ);

                        var rotated =
                            Vector3.TransformNormal(
                                local,
                                rotation);

                        return new Vector3(
                            (float)worldX +
                                rotated.X,
                            baseY +
                                rotated.Y,
                            (float)worldZ +
                                rotated.Z);
                    }

                    var p0 =
                        ResolveCollisionVertex(
                            i0);
                    var p1 =
                        ResolveCollisionVertex(
                            i1);
                    var p2 =
                        ResolveCollisionVertex(
                            i2);

                    if (!float.IsFinite(
                            p0.X) ||
                        !float.IsFinite(
                            p0.Y) ||
                        !float.IsFinite(
                            p0.Z) ||
                        !float.IsFinite(
                            p1.X) ||
                        !float.IsFinite(
                            p1.Y) ||
                        !float.IsFinite(
                            p1.Z) ||
                        !float.IsFinite(
                            p2.X) ||
                        !float.IsFinite(
                            p2.Y) ||
                        !float.IsFinite(
                            p2.Z))
                    {
                        continue;
                    }

                    triangles.Add(
                        new RuntimeSceneryCollisionTriangle(
                            new Vector2(
                                p0.X,
                                p0.Z),
                            new Vector2(
                                p1.X,
                                p1.Z),
                            new Vector2(
                                p2.X,
                                p2.Z),
                            Math.Min(
                                p0.Y,
                                Math.Min(
                                    p1.Y,
                                    p2.Y)),
                            Math.Max(
                                p0.Y,
                                Math.Max(
                                    p1.Y,
                                    p2.Y))));
                }

                if (triangles.Count >
                    0)
                {
                    collisionTriangles =
                        triangles;
                }
            }

            volumes.Add(
                new RuntimeSceneryCollisionVolume(
                    key++,
                    instance.ObjectId,
                    instance.AssetPath,
                    center,
                    forward,
                    right,
                    halfLength,
                    halfWidth,
                    baseY +
                        minimumVertical,
                    baseY +
                        maximumVertical,
                    asset.Surface,
                    asset.CollisionBounds is not null,
                    collisionTriangles));
        }

        return volumes;
    }

    private void UpdateSceneryCollisionState()
    {
        if (!_vehicleLandscapeCollisionsEnabled ||
            PlayerTrafficObstacle is not
                { } player ||
            _sceneryCollisionVolumes.Count ==
                0)
        {
            _activeSceneryCollisionVolumes.Clear();
            return;
        }

        var collided =
            new HashSet<int>();

        var impactApplied =
            false;

        var heading =
            (float)player.HeadingRadians;

        var playerForward =
            new Vector2(
                MathF.Sin(
                    heading),
                MathF.Cos(
                    heading));

        var playerRight =
            new Vector2(
                playerForward.Y,
                -playerForward.X);

        var playerCenter =
            new Vector2(
                (float)player.X,
                (float)player.Z);

        var playerMinimumY =
            (float)player.Y -
            0.25f;

        var playerMaximumY =
            (float)player.Y +
            3.75f;

        var playerBoundingRadius =
            MathF.Sqrt(
                (float)(
                    player.HalfLengthMeters *
                        player.HalfLengthMeters +
                    player.HalfWidthMeters *
                        player.HalfWidthMeters));

        foreach (var volume in
                 _sceneryCollisionVolumes)
        {
            if (volume.Surface)
            {
                var playerGroundY =
                    (float)player.Y;

                var nearSurfaceTop =
                    playerGroundY >=
                        volume.MaximumY -
                            0.55f &&
                    playerGroundY <=
                        volume.MaximumY +
                            1.00f;

                if (nearSurfaceTop ||
                    _vehicle.IsSupportedByScenerySurface(
                        player.X,
                        player.Z,
                        playerGroundY))
                {
                    continue;
                }
            }

            if (playerMaximumY <
                    volume.MinimumY ||
                playerMinimumY >
                    volume.MaximumY)
            {
                continue;
            }

            var centerDelta =
                volume.Center -
                playerCenter;

            var volumeBoundingRadius =
                MathF.Sqrt(
                    volume.HalfLength *
                        volume.HalfLength +
                    volume.HalfWidth *
                        volume.HalfWidth);

            var maximumCenterDistance =
                playerBoundingRadius +
                volumeBoundingRadius;

            if (centerDelta.LengthSquared() >
                maximumCenterDistance *
                    maximumCenterDistance)
            {
                continue;
            }

            Vector2 correction;

            if (volume.Triangles is
                    { Count: > 0 } triangles)
            {
                if (!TryResolveTriangleMeshCorrection(
                        playerCenter,
                        playerForward,
                        playerRight,
                        (float)player.HalfLengthMeters,
                        (float)player.HalfWidthMeters,
                        playerMinimumY,
                        playerMaximumY,
                        triangles,
                        out correction))
                {
                    continue;
                }
            }
            else if (!TryResolveOrientedRectangleCorrection(
                         centerDelta,
                         playerForward,
                         playerRight,
                         (float)player.HalfLengthMeters,
                         (float)player.HalfWidthMeters,
                         volume.Forward,
                         volume.Right,
                         volume.HalfLength,
                         volume.HalfWidth,
                         out correction))
            {
                continue;
            }

            collided.Add(
                volume.Key);

            var newContact =
                !_activeSceneryCollisionVolumes.Contains(
                    volume.Key);

            if (newContact)
            {
                ReportSceneryCollision(
                    volume,
                    player);
            }

            if (newContact &&
                !impactApplied)
            {
                _vehicle.ApplySceneryCollisionResponse(
                    correction,
                    Math.Abs(
                        _vehicle.SpeedKph));

                impactApplied =
                    true;
            }
            else
            {
                _vehicle.CorrectSceneryCollisionPenetration(
                    correction);

                _vehicle.HoldTrafficCollisionContact();
            }

            playerCenter +=
                correction;
        }

        _activeSceneryCollisionVolumes.RemoveWhere(
            key =>
                !collided.Contains(
                    key));

        foreach (var key in
                 collided)
        {
            _activeSceneryCollisionVolumes.Add(
                key);
        }
    }

    private static bool TryResolveTriangleMeshCorrection(
        Vector2 rectangleCenter,
        Vector2 rectangleForward,
        Vector2 rectangleRight,
        float rectangleHalfLength,
        float rectangleHalfWidth,
        float rectangleMinimumY,
        float rectangleMaximumY,
        IReadOnlyList<RuntimeSceneryCollisionTriangle> triangles,
        out Vector2 correction)
    {
        correction =
            Vector2.Zero;

        var workingCenter =
            rectangleCenter;

        var collided =
            false;

        for (var pass = 0;
             pass <
                 4;
             pass++)
        {
            var passCollision =
                false;

            foreach (var triangle in
                     triangles)
            {
                if (rectangleMaximumY <
                        triangle.MinimumY ||
                    rectangleMinimumY >
                        triangle.MaximumY)
                {
                    continue;
                }

                if (!TryResolveOrientedRectangleTriangleCorrection(
                        workingCenter,
                        rectangleForward,
                        rectangleRight,
                        rectangleHalfLength,
                        rectangleHalfWidth,
                        triangle,
                        out var triangleCorrection))
                {
                    continue;
                }

                passCollision =
                    true;
                collided =
                    true;

                correction +=
                    triangleCorrection;

                workingCenter +=
                    triangleCorrection;
            }

            if (!passCollision)
            {
                break;
            }
        }

        return collided;
    }

    private static bool TryResolveOrientedRectangleTriangleCorrection(
        Vector2 rectangleCenter,
        Vector2 rectangleForward,
        Vector2 rectangleRight,
        float rectangleHalfLength,
        float rectangleHalfWidth,
        RuntimeSceneryCollisionTriangle triangle,
        out Vector2 correction)
    {
        correction =
            Vector2.Zero;

        Span<Vector2> rawAxes =
        [
            rectangleForward,
            rectangleRight,
            new Vector2(
                -(triangle.B -
                  triangle.A).Y,
                (triangle.B -
                 triangle.A).X),
            new Vector2(
                -(triangle.C -
                  triangle.B).Y,
                (triangle.C -
                 triangle.B).X),
            new Vector2(
                -(triangle.A -
                  triangle.C).Y,
                (triangle.A -
                 triangle.C).X)
        ];

        var minimumOverlap =
            float.PositiveInfinity;

        var bestAxis =
            Vector2.Zero;

        foreach (var rawAxis in
                 rawAxes)
        {
            if (rawAxis.LengthSquared() <
                0.000001f)
            {
                continue;
            }

            var axis =
                Vector2.Normalize(
                    rawAxis);

            var rectangleCenterProjection =
                Vector2.Dot(
                    rectangleCenter,
                    axis);

            var rectangleRadius =
                Math.Abs(
                    Vector2.Dot(
                        rectangleForward,
                        axis)) *
                    rectangleHalfLength +
                Math.Abs(
                    Vector2.Dot(
                        rectangleRight,
                        axis)) *
                    rectangleHalfWidth;

            var rectangleMinimum =
                rectangleCenterProjection -
                rectangleRadius;

            var rectangleMaximum =
                rectangleCenterProjection +
                rectangleRadius;

            var a =
                Vector2.Dot(
                    triangle.A,
                    axis);
            var b =
                Vector2.Dot(
                    triangle.B,
                    axis);
            var c =
                Vector2.Dot(
                    triangle.C,
                    axis);

            var triangleMinimum =
                Math.Min(
                    a,
                    Math.Min(
                        b,
                        c));

            var triangleMaximum =
                Math.Max(
                    a,
                    Math.Max(
                        b,
                        c));

            var overlap =
                Math.Min(
                    rectangleMaximum,
                    triangleMaximum) -
                Math.Max(
                    rectangleMinimum,
                    triangleMinimum);

            if (overlap <=
                0.0f)
            {
                return false;
            }

            if (overlap <
                minimumOverlap)
            {
                minimumOverlap =
                    overlap;

                var triangleCenter =
                    (triangle.A +
                     triangle.B +
                     triangle.C) /
                    3.0f;

                var pushDirection =
                    rectangleCenter -
                    triangleCenter;

                bestAxis =
                    Vector2.Dot(
                        pushDirection,
                        axis) >=
                    0.0f
                        ? axis
                        : -axis;
            }
        }

        if (!float.IsFinite(
                minimumOverlap) ||
            bestAxis.LengthSquared() <
                0.000001f)
        {
            return false;
        }

        correction =
            bestAxis *
            (minimumOverlap +
             0.02f);

        return true;
    }

    private static bool TryResolveOrientedRectangleCorrection(
        Vector2 centerDelta,
        Vector2 firstForward,
        Vector2 firstRight,
        float firstHalfLength,
        float firstHalfWidth,
        Vector2 secondForward,
        Vector2 secondRight,
        float secondHalfLength,
        float secondHalfWidth,
        out Vector2 correction)
    {
        correction =
            Vector2.Zero;

        Span<Vector2> axes =
        [
            firstForward,
            firstRight,
            secondForward,
            secondRight
        ];

        var minimumOverlap =
            float.PositiveInfinity;

        var bestAxis =
            Vector2.Zero;

        foreach (var rawAxis in axes)
        {
            if (rawAxis.LengthSquared() <
                0.000001f)
            {
                continue;
            }

            var axis =
                Vector2.Normalize(
                    rawAxis);

            var distance =
                Math.Abs(
                    Vector2.Dot(
                        centerDelta,
                        axis));

            var firstRadius =
                Math.Abs(
                    Vector2.Dot(
                        firstForward,
                        axis)) *
                    firstHalfLength +
                Math.Abs(
                    Vector2.Dot(
                        firstRight,
                        axis)) *
                    firstHalfWidth;

            var secondRadius =
                Math.Abs(
                    Vector2.Dot(
                        secondForward,
                        axis)) *
                    secondHalfLength +
                Math.Abs(
                    Vector2.Dot(
                        secondRight,
                        axis)) *
                    secondHalfWidth;

            var overlap =
                firstRadius +
                secondRadius -
                distance;

            if (overlap <=
                0.0f)
            {
                return false;
            }

            if (overlap <
                minimumOverlap)
            {
                minimumOverlap =
                    overlap;

                var sign =
                    Vector2.Dot(
                        centerDelta,
                        axis) >=
                    0.0f
                        ? -1.0f
                        : 1.0f;

                bestAxis =
                    axis *
                    sign;
            }
        }

        if (!float.IsFinite(
                minimumOverlap) ||
            bestAxis.LengthSquared() <
                0.000001f)
        {
            return false;
        }

        correction =
            bestAxis *
            (minimumOverlap +
             0.02f);

        return true;
    }

    private static void ReportSceneryCollision(
        RuntimeSceneryCollisionVolume volume,
        RuntimeTrafficObstacleInfo player)
    {
        RuntimeDiagnosticLogWriter.Enqueue(
            "scenery-collision.log",
            $"{DateTimeOffset.Now:O}|objectId={volume.ObjectId}|asset={volume.AssetPath}|source={(volume.UsesCollisionMesh ? "collision_mesh" : "boundingbox")}|surface={volume.Surface}|halfLength={volume.HalfLength:0.00}|halfWidth={volume.HalfWidth:0.00}|y={volume.MinimumY:0.00}..{volume.MaximumY:0.00}|position={player.X:0.00},{player.Y:0.00},{player.Z:0.00}|speed={player.SpeedMetersPerSecond * 3.6:0.0} km/h{Environment.NewLine}");
    }

    private void UpdateTrafficCollisionState(
        double nowSeconds)
    {
        if (!_vehicleToVehicleCollisionsEnabled ||
            PlayerTrafficObstacle is not
                { } player)
        {
            _activeTrafficCollisionAgents.Clear();
            _lastTrafficCollisionSeconds.Clear();
            return;
        }

        var collided =
            new HashSet<int>();

        var heading =
            (float)player.HeadingRadians;

        var forward =
            new Vector2(
                MathF.Sin(
                    heading),
                MathF.Cos(
                    heading));

        var right =
            new Vector2(
                forward.Y,
                -forward.X);

        foreach (var agent in
                 _trafficAgents)
        {
            // Rail consists are encoded in a separate high index range.
            if (agent.AgentIndex >=
                2_000_000)
            {
                continue;
            }

            if (Math.Abs(
                    agent.Y -
                    player.Y) >
                3.5)
            {
                continue;
            }

            var delta =
                new Vector2(
                    (float)(agent.X -
                            player.X),
                    (float)(agent.Z -
                            player.Z));

            var aiHeading =
                (float)agent.HeadingRadians;

            var aiForward =
                new Vector2(
                    MathF.Sin(
                        aiHeading),
                    MathF.Cos(
                        aiHeading));

            var aiRight =
                new Vector2(
                    aiForward.Y,
                    -aiForward.X);

            ResolveTrafficVehicleCollisionHalfExtents(
                agent.VehiclePath,
                out var aiHalfLength,
                out var aiHalfWidth);

            if (!OrientedTrafficRectanglesOverlap(
                    delta,
                    forward,
                    right,
                    player.HalfLengthMeters,
                    player.HalfWidthMeters,
                    aiForward,
                    aiRight,
                    aiHalfLength,
                    aiHalfWidth))
            {
                continue;
            }

            collided.Add(
                agent.AgentIndex);

            var playerVelocity =
                forward *
                (float)player.SpeedMetersPerSecond;

            var aiVelocity =
                aiForward *
                (float)agent.SpeedMetersPerSecond;

            var relativeImpactSpeedKph =
                (playerVelocity -
                 aiVelocity)
                    .Length() *
                3.6f;

            var newCollisionContact =
                !_activeTrafficCollisionAgents.Contains(
                    agent.AgentIndex);

            if (newCollisionContact)
            {
                _vehicle
                    .ApplyTrafficCollisionResponse(
                        relativeImpactSpeedKph);

                _trafficCollisionResponse?.Invoke(
                    agent.AgentIndex,
                    relativeImpactSpeedKph);
            }
            else
            {
                _vehicle
                    .HoldTrafficCollisionContact();
            }

            var playerLikelyAtFault =
                false;

            if (delta.LengthSquared() >
                0.0001f)
            {
                var impactNormal =
                    Vector2.Normalize(
                        delta);

                var playerClosingSpeed =
                    Vector2.Dot(
                        playerVelocity,
                        impactNormal);

                var aiClosingSpeed =
                    Vector2.Dot(
                        aiVelocity,
                        -impactNormal);

                // Do not automatically fine a stationary bus that is struck
                // by AI traffic. Attribute a collision infraction only when
                // the player's closing component materially dominates the AI
                // vehicle's own movement into the contact.
                playerLikelyAtFault =
                    playerClosingSpeed >
                        1.0f &&
                    playerClosingSpeed >
                        aiClosingSpeed +
                            0.5f;
            }

            if (playerLikelyAtFault &&
                newCollisionContact &&
                (!_lastTrafficCollisionSeconds.TryGetValue(
                     agent.AgentIndex,
                     out var previousCollisionSeconds) ||
                 nowSeconds -
                     previousCollisionSeconds >=
                 2.0))
            {
                _lastTrafficCollisionSeconds[
                    agent.AgentIndex] =
                    nowSeconds;

                _trafficCollisionCount++;

                var collisionPenaltyPoints =
                    relativeImpactSpeedKph >=
                            40.0f
                        ? 5
                        : relativeImpactSpeedKph >=
                                20.0f
                            ? 3
                            : 1;

                var collisionFineCredits =
                    Math.Max(
                        50,
                        (int)Math.Ceiling(
                            relativeImpactSpeedKph *
                            6.0f));

                RegisterTrafficViolation(
                    nowSeconds,
                    "collision",
                    collisionPenaltyPoints,
                    collisionFineCredits,
                    $"ai={agent.AgentIndex}; speed={_vehicle.SpeedKph:0.0} km/h; relative={relativeImpactSpeedKph:0.0} km/h");
            }
        }

        _activeTrafficCollisionAgents
            .RemoveWhere(
                id =>
                    !collided.Contains(
                        id));

        foreach (var id in
                 collided)
        {
            _activeTrafficCollisionAgents.Add(
                id);
        }
    }

    private void ResolveTrafficVehicleCollisionHalfExtents(
        string vehiclePath,
        out double halfLengthMeters,
        out double halfWidthMeters)
    {
        var isBus =
            !string.IsNullOrWhiteSpace(
                vehiclePath) &&
            Path.GetExtension(
                    vehiclePath)
                .Equals(
                    ".bus",
                    StringComparison.OrdinalIgnoreCase);

        halfLengthMeters =
            isBus
                ? 6.0
                : 2.6;

        halfWidthMeters =
            isBus
                ? 1.30
                : 1.15;

        if (string.IsNullOrWhiteSpace(
                vehiclePath) ||
            !_trafficVehicleGeometries.TryGetValue(
                vehiclePath,
                out var geometry) ||
            geometry.Vertices.Length ==
                0)
        {
            return;
        }

        var minimumX =
            float.PositiveInfinity;
        var maximumX =
            float.NegativeInfinity;
        var minimumZ =
            float.PositiveInfinity;
        var maximumZ =
            float.NegativeInfinity;

        foreach (var vertex in
                 geometry.Vertices)
        {
            minimumX =
                Math.Min(
                    minimumX,
                    vertex.Position.X);
            maximumX =
                Math.Max(
                    maximumX,
                    vertex.Position.X);
            minimumZ =
                Math.Min(
                    minimumZ,
                    vertex.Position.Z);
            maximumZ =
                Math.Max(
                    maximumZ,
                    vertex.Position.Z);
        }

        var meshWidth =
            maximumX -
            minimumX;

        var meshLength =
            maximumZ -
            minimumZ;

        if (float.IsFinite(
                meshWidth) &&
            meshWidth >
                0.5f)
        {
            halfWidthMeters =
                Math.Clamp(
                    meshWidth *
                        0.5,
                    0.65,
                    2.0);
        }

        if (float.IsFinite(
                meshLength) &&
            meshLength >
                1.0f)
        {
            halfLengthMeters =
                Math.Clamp(
                    meshLength *
                        0.5,
                    1.5,
                    15.0);
        }
    }

    private static bool OrientedTrafficRectanglesOverlap(
        Vector2 centerDelta,
        Vector2 firstForward,
        Vector2 firstRight,
        double firstHalfLength,
        double firstHalfWidth,
        Vector2 secondForward,
        Vector2 secondRight,
        double secondHalfLength,
        double secondHalfWidth)
    {
        Span<Vector2> axes =
        [
            firstForward,
            firstRight,
            secondForward,
            secondRight
        ];

        foreach (var axis in
                 axes)
        {
            var axisLengthSquared =
                axis.LengthSquared();

            if (axisLengthSquared <
                0.000001f)
            {
                continue;
            }

            var normalizedAxis =
                axisLengthSquared >
                    0.999f &&
                axisLengthSquared <
                    1.001f
                    ? axis
                    : Vector2.Normalize(
                        axis);

            var centerProjection =
                Math.Abs(
                    Vector2.Dot(
                        centerDelta,
                        normalizedAxis));

            var firstRadius =
                firstHalfLength *
                    Math.Abs(
                        Vector2.Dot(
                            firstForward,
                            normalizedAxis)) +
                firstHalfWidth *
                    Math.Abs(
                        Vector2.Dot(
                            firstRight,
                            normalizedAxis));

            var secondRadius =
                secondHalfLength *
                    Math.Abs(
                        Vector2.Dot(
                            secondForward,
                            normalizedAxis)) +
                secondHalfWidth *
                    Math.Abs(
                        Vector2.Dot(
                            secondRight,
                            normalizedAxis));

            if (centerProjection >
                firstRadius +
                    secondRadius)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateRedLightRule(
        double nowSeconds)
    {
        var segment =
            ResolveNearestRoadSegment(
                out var travelForward);

        if (segment is null)
        {
            _lastRedLightSegmentIndex =
                -1;
            return;
        }

        var connectionIndices =
            travelForward
                ? segment.ForwardConnections
                : segment.ReverseConnections;

        RuntimeTrafficPathSegmentInfo?
            signalSegment =
                null;

        foreach (var connectionIndex in
                 connectionIndices)
        {
            _runtimeTrafficSegmentByIndex.TryGetValue(
                connectionIndex,
                out var candidate);

            if (candidate?.TrafficSignal is
                not null)
            {
                signalSegment =
                    candidate;
                break;
            }
        }

        if (signalSegment?.TrafficSignal is
            not { } signal)
        {
            _lastRedLightSegmentIndex =
                -1;
            return;
        }

        var stopPoint =
            travelForward
                ? segment.Points[^1]
                : segment.Points[0];

        var dx =
            _vehicle.Position.X -
            stopPoint.X;
        var dz =
            _vehicle.Position.Z -
            stopPoint.Z;

        var distance =
            Math.Sqrt(
                dx *
                    dx +
                dz *
                    dz);

        var approachDistance =
            Math.Clamp(
                signal.ApproachDistanceMeters,
                3.0,
                60.0);

        if (distance >
            approachDistance +
                5.0)
        {
            if (_lastRedLightSegmentIndex ==
                signalSegment.Index)
            {
                _lastRedLightSegmentIndex =
                    -1;
            }

            return;
        }

        _trafficSignalStateBySegmentIndex.TryGetValue(
            signalSegment.Index,
            out var dynamicSignalState);

        var currentSignalPhase =
            dynamicSignalState is not null
                ? dynamicSignalState.Phase
                : ResolveRuntimeTrafficSignalPhase(
                    signal,
                    nowSeconds);

        // OMSI TrafficLightPhase semantics:
        // 0..2 = red, 3..5 = red-yellow, 6..8 = green,
        // 9..11 = yellow, other values = off.
        // Only the stop phases 0..5 are eligible for a red-light
        // violation. Yellow is not fined automatically.
        if (currentSignalPhase is
                not (>= 0 and <= 5))
        {
            if (_lastRedLightSegmentIndex ==
                signalSegment.Index)
            {
                _lastRedLightSegmentIndex =
                    -1;
            }

            return;
        }

        var speedMetersPerSecond =
            Math.Abs(
                _vehicle.SpeedMetersPerSecond);

        if (speedMetersPerSecond <
                1.5 ||
            segment.Points.Count <
                2)
        {
            return;
        }

        var approachA =
            travelForward
                ? segment.Points[^2]
                : segment.Points[1];

        var approachB =
            travelForward
                ? segment.Points[^1]
                : segment.Points[0];

        var approachX =
            approachB.X -
            approachA.X;

        var approachZ =
            approachB.Z -
            approachA.Z;

        var approachLength =
            Math.Sqrt(
                approachX *
                    approachX +
                approachZ *
                    approachZ);

        if (approachLength <=
            0.0001)
        {
            return;
        }

        var forwardX =
            approachX /
            approachLength;

        var forwardZ =
            approachZ /
            approachLength;

        var playerHalfLength =
            PlayerTrafficObstacle?
                .HalfLengthMeters ??
            5.5;

        var frontX =
            _vehicle.Position.X +
            forwardX *
                playerHalfLength;

        var frontZ =
            _vehicle.Position.Z +
            forwardZ *
                playerHalfLength;

        var signedFrontDistance =
            (frontX -
             stopPoint.X) *
                forwardX +
            (frontZ -
             stopPoint.Z) *
                forwardZ;

        // Penalize only when the front of the bus has actually crossed the
        // stop-line plane while the signal is red. Being close to the line,
        // but still behind it, is not a violation.
        if (signedFrontDistance <
            0.0)
        {
            return;
        }

        if (_lastRedLightSegmentIndex ==
                signalSegment.Index &&
            nowSeconds -
                _lastRedLightViolationSeconds <
            8.0)
        {
            return;
        }

        _lastRedLightSegmentIndex =
            signalSegment.Index;

        _lastRedLightViolationSeconds =
            nowSeconds;

        _redLightViolationCount++;

        RegisterTrafficViolation(
            nowSeconds,
            "red-light",
            penaltyPoints:
                4,
            fineCredits:
                180,
            details:
                $"signal-path={signalSegment.Index}; speed={_vehicle.SpeedKph:0.0} km/h");
    }

    private void UpdatePriorityRule(
        double nowSeconds)
    {
        var playerSegment =
            ResolveNearestRoadSegment(
                out _);

        if (playerSegment?.SceneryObjectId is
                not long crossingObjectId ||
            playerSegment.TrafficSignal is
                not null ||
            Math.Abs(
                _vehicle.SpeedKph) <
                5.0f)
        {
            _lastPriorityCrossingObjectId =
                null;
            return;
        }

        var playerPriority =
            ResolveCrossingApproachPriority(
                playerSegment,
                _vehicle.HeadingRadians);

        RuntimeTrafficPathSegmentInfo?
            conflictingSegment =
                null;

        RuntimeTrafficAgentInfo?
            conflictingAgent =
                null;

        var conflictingPriority =
            0;

        foreach (var agent in
                 _trafficAgents)
        {
            if (agent.AgentIndex >=
                    2_000_000 ||
                agent.SpeedMetersPerSecond <=
                    0.5 ||
                !TryResolveAgentCrossingConflict(
                    agent,
                    crossingObjectId,
                    playerSegment,
                    out var aiConflictSegment,
                    out var aiPriority,
                    out var secondsToEntry) ||
                aiPriority <=
                    playerPriority ||
                secondsToEntry >
                    4.0)
            {
                continue;
            }

            conflictingSegment =
                aiConflictSegment;
            conflictingAgent =
                agent;
            conflictingPriority =
                aiPriority;
            break;
        }

        if (conflictingSegment is null ||
            conflictingAgent is null)
        {
            if (_lastPriorityCrossingObjectId ==
                crossingObjectId)
            {
                _lastPriorityCrossingObjectId =
                    null;
            }

            return;
        }

        if (_lastPriorityCrossingObjectId ==
                crossingObjectId &&
            nowSeconds -
                _lastPriorityViolationSeconds <
            8.0)
        {
            return;
        }

        _lastPriorityCrossingObjectId =
            crossingObjectId;
        _lastPriorityViolationSeconds =
            nowSeconds;
        _priorityViolationCount++;

        RegisterTrafficViolation(
            nowSeconds,
            "priority",
            penaltyPoints:
                3,
            fineCredits:
                120,
            details:
                $"crossing={crossingObjectId}; playerPath={playerSegment.Index}; playerPriority={playerPriority}; ai={conflictingAgent.AgentIndex}; aiPath={conflictingSegment.Index}; aiPriority={conflictingPriority}; speed={_vehicle.SpeedKph:0.0} km/h");
    }

    private int ResolveCrossingApproachPriority(
        RuntimeTrafficPathSegmentInfo crossingSegment,
        float headingRadians)
    {
        var bestPriority =
            crossingSegment.TrafficPriority;

        var bestAlignment =
            -1.0f;

        var vehicleForward =
            new Vector2(
                MathF.Sin(
                    headingRadians),
                MathF.Cos(
                    headingRadians));

        foreach (var candidate in
                 _windowInfo
                     .TrafficPaths
                     .Segments)
        {
            if (candidate.Type !=
                    0 ||
                candidate.Points.Count <
                    2 ||
                candidate.SceneryObjectId ==
                    crossingSegment.SceneryObjectId)
            {
                continue;
            }

            var connectsForward =
                candidate.ForwardConnections.Contains(
                    crossingSegment.Index);

            var connectsReverse =
                candidate.ReverseConnections.Contains(
                    crossingSegment.Index);

            if (!connectsForward &&
                !connectsReverse)
            {
                continue;
            }

            Vector2 direction;

            if (connectsForward)
            {
                var a =
                    candidate.Points[^2];
                var b =
                    candidate.Points[^1];

                direction =
                    new Vector2(
                        (float)(b.X -
                                a.X),
                        (float)(b.Z -
                                a.Z));
            }
            else
            {
                var a =
                    candidate.Points[1];
                var b =
                    candidate.Points[0];

                direction =
                    new Vector2(
                        (float)(b.X -
                                a.X),
                        (float)(b.Z -
                                a.Z));
            }

            if (direction.LengthSquared() <
                0.000001f)
            {
                continue;
            }

            direction =
                Vector2.Normalize(
                    direction);

            var alignment =
                Vector2.Dot(
                    vehicleForward,
                    direction);

            if (alignment >
                    0.25f &&
                alignment >
                    bestAlignment)
            {
                bestAlignment =
                    alignment;

                bestPriority =
                    candidate.TrafficPriority;
            }
        }

        return bestPriority;
    }

    private bool TryResolveAgentCrossingConflict(
        RuntimeTrafficAgentInfo agent,
        long crossingObjectId,
        RuntimeTrafficPathSegmentInfo playerCrossingSegment,
        out RuntimeTrafficPathSegmentInfo conflictSegment,
        out int approachPriority,
        out double secondsToEntry)
    {
        conflictSegment =
            null!;
        approachPriority =
            0;
        secondsToEntry =
            double.PositiveInfinity;

        _runtimeTrafficSegmentByIndex.TryGetValue(
            agent.SegmentIndex,
            out var current);

        if (current is null ||
            current.Points.Count <
                2)
        {
            return false;
        }

        if (current.SceneryObjectId ==
            crossingObjectId)
        {
            if (!RuntimeTrafficPathsConflictCached(
                    playerCrossingSegment,
                    current))
            {
                return false;
            }

            conflictSegment =
                current;

            approachPriority =
                ResolveCrossingApproachPriority(
                    current,
                    (float)agent.HeadingRadians);

            secondsToEntry =
                0.0;

            return true;
        }

        var agentForward =
            new Vector2(
                (float)Math.Sin(
                    agent.HeadingRadians),
                (float)Math.Cos(
                    agent.HeadingRadians));

        var nearestDistanceSquared =
            double.PositiveInfinity;

        var travelForward =
            true;

        for (var index = 1;
             index <
                 current.Points.Count;
             index++)
        {
            var a =
                current.Points[
                    index -
                    1];

            var b =
                current.Points[
                    index];

            var distanceSquared =
                PointToSegmentDistanceSquared(
                    agent.X,
                    agent.Z,
                    a.X,
                    a.Z,
                    b.X,
                    b.Z);

            if (distanceSquared >=
                nearestDistanceSquared)
            {
                continue;
            }

            var direction =
                new Vector2(
                    (float)(b.X -
                            a.X),
                    (float)(b.Z -
                            a.Z));

            if (direction.LengthSquared() <
                0.000001f)
            {
                continue;
            }

            direction =
                Vector2.Normalize(
                    direction);

            nearestDistanceSquared =
                distanceSquared;

            travelForward =
                Vector2.Dot(
                    agentForward,
                    direction) >=
                0.0f;
        }

        var connectionIndices =
            travelForward
                ? current.ForwardConnections
                : current.ReverseConnections;

        foreach (var connectionIndex in
                 connectionIndices)
        {
            _runtimeTrafficSegmentByIndex.TryGetValue(
                connectionIndex,
                out var candidate);

            if (candidate?.SceneryObjectId !=
                    crossingObjectId ||
                !RuntimeTrafficPathsConflictCached(
                    playerCrossingSegment,
                    candidate))
            {
                continue;
            }

            var entryPoint =
                travelForward
                    ? current.Points[^1]
                    : current.Points[0];

            var dx =
                agent.X -
                entryPoint.X;

            var dz =
                agent.Z -
                entryPoint.Z;

            var distanceToEntry =
                Math.Sqrt(
                    dx *
                        dx +
                    dz *
                        dz);

            conflictSegment =
                candidate;

            approachPriority =
                current.TrafficPriority;

            secondsToEntry =
                distanceToEntry /
                Math.Max(
                    agent.SpeedMetersPerSecond,
                    1.0);

            return true;
        }

        return false;
    }

    private bool RuntimeTrafficPathsConflictCached(
        RuntimeTrafficPathSegmentInfo first,
        RuntimeTrafficPathSegmentInfo second)
    {
        if (first.Index ==
            second.Index)
        {
            return true;
        }

        var key =
            first.Index <
                second.Index
                ? (
                    first.Index,
                    second.Index)
                : (
                    second.Index,
                    first.Index);

        if (_runtimeTrafficPathConflictCache.TryGetValue(
                key,
                out var cached))
        {
            return cached;
        }

        var conflicts =
            RuntimeTrafficPathsConflict(
                first,
                second);

        _runtimeTrafficPathConflictCache[
            key] =
            conflicts;

        return conflicts;
    }

    private static bool RuntimeTrafficPathsConflict(
        RuntimeTrafficPathSegmentInfo first,
        RuntimeTrafficPathSegmentInfo second)
    {
        if (first.Index ==
            second.Index)
        {
            return true;
        }

        if (!first.SceneryObjectId.HasValue ||
            first.SceneryObjectId !=
                second.SceneryObjectId ||
            first.Points.Count <
                2 ||
            second.Points.Count <
                2)
        {
            return false;
        }

        for (var firstIndex = 1;
             firstIndex <
                 first.Points.Count;
             firstIndex++)
        {
            var firstStart =
                first.Points[
                    firstIndex -
                    1];
            var firstEnd =
                first.Points[
                    firstIndex];

            for (var secondIndex = 1;
                 secondIndex <
                     second.Points.Count;
                 secondIndex++)
            {
                var secondStart =
                    second.Points[
                        secondIndex -
                        1];
                var secondEnd =
                    second.Points[
                        secondIndex];

                const double conflictToleranceMeters =
                    0.5;

                if (RuntimeTrafficSegmentDistanceSquared(
                        firstStart,
                        firstEnd,
                        secondStart,
                        secondEnd) <=
                    conflictToleranceMeters *
                        conflictToleranceMeters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double RuntimeTrafficSegmentDistanceSquared(
        RuntimeTrafficPathPointInfo firstStart,
        RuntimeTrafficPathPointInfo firstEnd,
        RuntimeTrafficPathPointInfo secondStart,
        RuntimeTrafficPathPointInfo secondEnd)
    {
        var d1X =
            firstEnd.X -
            firstStart.X;
        var d1Y =
            firstEnd.Y -
            firstStart.Y;
        var d1Z =
            firstEnd.Z -
            firstStart.Z;

        var d2X =
            secondEnd.X -
            secondStart.X;
        var d2Y =
            secondEnd.Y -
            secondStart.Y;
        var d2Z =
            secondEnd.Z -
            secondStart.Z;

        var rX =
            firstStart.X -
            secondStart.X;
        var rY =
            firstStart.Y -
            secondStart.Y;
        var rZ =
            firstStart.Z -
            secondStart.Z;

        var a =
            d1X *
                d1X +
            d1Y *
                d1Y +
            d1Z *
                d1Z;

        var e =
            d2X *
                d2X +
            d2Y *
                d2Y +
            d2Z *
                d2Z;

        var f =
            d2X *
                rX +
            d2Y *
                rY +
            d2Z *
                rZ;

        const double epsilon =
            0.000000001;

        double s;
        double t;

        if (a <=
                epsilon &&
            e <=
                epsilon)
        {
            return RuntimeTrafficPointDistanceSquared(
                firstStart,
                secondStart);
        }

        if (a <=
            epsilon)
        {
            s =
                0.0;

            t =
                Math.Clamp(
                    f /
                        e,
                    0.0,
                    1.0);
        }
        else
        {
            var c =
                d1X *
                    rX +
                d1Y *
                    rY +
                d1Z *
                    rZ;

            if (e <=
                epsilon)
            {
                t =
                    0.0;

                s =
                    Math.Clamp(
                        -c /
                            a,
                        0.0,
                        1.0);
            }
            else
            {
                var b =
                    d1X *
                        d2X +
                    d1Y *
                        d2Y +
                    d1Z *
                        d2Z;

                var denominator =
                    a *
                        e -
                    b *
                        b;

                s =
                    Math.Abs(
                        denominator) >
                    epsilon
                        ? Math.Clamp(
                            (b *
                                 f -
                             c *
                                 e) /
                                denominator,
                            0.0,
                            1.0)
                        : 0.0;

                t =
                    (b *
                         s +
                     f) /
                    e;

                if (t <
                    0.0)
                {
                    t =
                        0.0;

                    s =
                        Math.Clamp(
                            -c /
                                a,
                            0.0,
                            1.0);
                }
                else if (t >
                         1.0)
                {
                    t =
                        1.0;

                    s =
                        Math.Clamp(
                            (b -
                             c) /
                                a,
                            0.0,
                            1.0);
                }
            }
        }

        var firstClosest =
            new RuntimeTrafficPathPointInfo(
                firstStart.X +
                    d1X *
                        s,
                firstStart.Y +
                    d1Y *
                        s,
                firstStart.Z +
                    d1Z *
                        s);

        var secondClosest =
            new RuntimeTrafficPathPointInfo(
                secondStart.X +
                    d2X *
                        t,
                secondStart.Y +
                    d2Y *
                        t,
                secondStart.Z +
                    d2Z *
                        t);

        return RuntimeTrafficPointDistanceSquared(
            firstClosest,
            secondClosest);
    }

    private static double RuntimeTrafficPointDistanceSquared(
        RuntimeTrafficPathPointInfo first,
        RuntimeTrafficPathPointInfo second)
    {
        var x =
            first.X -
            second.X;
        var y =
            first.Y -
            second.Y;
        var z =
            first.Z -
            second.Z;

        return x *
                   x +
               y *
                   y +
               z *
                   z;
    }

    private void RegisterTrafficViolation(
        double nowSeconds,
        string kind,
        int penaltyPoints,
        int fineCredits,
        string details)
    {
        var normalizedPoints =
            Math.Max(
                penaltyPoints,
                0);

        var normalizedFine =
            Math.Max(
                fineCredits,
                0);

        _trafficPenaltyPoints +=
            normalizedPoints;

        _trafficFineCredits +=
            normalizedFine;

        var line =
            $"{DateTimeOffset.Now:O}|sim={nowSeconds:0.000}|kind={kind}|points={normalizedPoints}|fineCredits={normalizedFine}|totalPoints={_trafficPenaltyPoints}|totalFineCredits={_trafficFineCredits}|{details}";

        Console.WriteLine(
            $"[traffic-rule] {line}");

        RuntimeDiagnosticLogWriter.Enqueue(
            "traffic-violations.log",
            line +
            Environment.NewLine);
    }

    private RuntimeTrafficPathSegmentInfo?
        ResolveNearestRoadSegment(
            out bool travelForward)
    {
        travelForward =
            true;

        var position =
            _vehicle.Position;

        var vehicleForward =
            new Vector2(
                MathF.Sin(
                    _vehicle.HeadingRadians),
                MathF.Cos(
                    _vehicle.HeadingRadians));

        var nearestDistanceSquared =
            double.PositiveInfinity;

        RuntimeTrafficPathSegmentInfo?
            nearestSegment =
                null;

        var nearestDirection =
            true;

        foreach (var segment in
                 _windowInfo
                     .TrafficPaths
                     .Segments)
        {
            if (segment.Type !=
                    0 ||
                segment.Points.Count <
                    2)
            {
                continue;
            }

            for (var index = 1;
                 index <
                     segment.Points.Count;
                 index++)
            {
                var a =
                    segment.Points[
                        index -
                        1];

                var b =
                    segment.Points[
                        index];

                if (Math.Abs(
                        position.Y -
                        (a.Y +
                         b.Y) *
                        0.5) >
                    5.0)
                {
                    continue;
                }

                var distanceSquared =
                    PointToSegmentDistanceSquared(
                        position.X,
                        position.Z,
                        a.X,
                        a.Z,
                        b.X,
                        b.Z);

                if (distanceSquared >=
                    nearestDistanceSquared)
                {
                    continue;
                }

                var pathDirection =
                    new Vector2(
                        (float)(
                            b.X -
                            a.X),
                        (float)(
                            b.Z -
                            a.Z));

                if (pathDirection.LengthSquared() <
                    0.000001f)
                {
                    continue;
                }

                pathDirection =
                    Vector2.Normalize(
                        pathDirection);

                var alignment =
                    Vector2.Dot(
                        vehicleForward,
                        pathDirection);

                var candidateTravelForward =
                    alignment >=
                    0.0f;

                var directionAllowed =
                    segment.Direction switch
                    {
                        0 =>
                            candidateTravelForward,
                        1 =>
                            !candidateTravelForward,
                        2 =>
                            true,
                        _ =>
                            true
                    };

                if (!directionAllowed ||
                    Math.Abs(
                        alignment) <
                    0.25f)
                {
                    continue;
                }

                nearestDistanceSquared =
                    distanceSquared;

                nearestSegment =
                    segment;

                nearestDirection =
                    candidateTravelForward;
            }
        }

        if (nearestDistanceSquared >
            64.0)
        {
            return null;
        }

        travelForward =
            nearestDirection;

        return nearestSegment;
    }

    private static bool IsRuntimeTrafficSignalGreen(
        RuntimeTrafficSignalProgramInfo signal,
        double elapsedSeconds) =>
        ResolveRuntimeTrafficSignalPhase(
            signal,
            elapsedSeconds) is
            >= 6 and <= 8;

    private static int? ResolveRuntimeTrafficSignalPhase(
        RuntimeTrafficSignalProgramInfo signal,
        double elapsedSeconds)
    {
        if (signal.Phases.Count ==
                0 ||
            !double.IsFinite(
                elapsedSeconds))
        {
            return null;
        }

        var phaseDuration =
            signal.Phases
                .Where(
                    static phase =>
                        phase.DurationSeconds >
                            0.0 &&
                        double.IsFinite(
                            phase.DurationSeconds))
                .Sum(
                    static phase =>
                        phase.DurationSeconds);

        var cycleSeconds =
            signal.CycleSeconds;

        if (!double.IsFinite(
                cycleSeconds) ||
            cycleSeconds <=
                0.0)
        {
            cycleSeconds =
                phaseDuration;
        }

        if (cycleSeconds <=
                0.0 ||
            phaseDuration <=
                0.0)
        {
            return null;
        }

        var position =
            elapsedSeconds %
            cycleSeconds;

        if (position <
            0.0)
        {
            position +=
                cycleSeconds;
        }

        RuntimeTrafficSignalPhaseInfo?
            lastPhase =
                null;

        foreach (var phase in
                 signal.Phases)
        {
            if (phase.DurationSeconds <=
                    0.0 ||
                !double.IsFinite(
                    phase.DurationSeconds))
            {
                continue;
            }

            lastPhase =
                phase;

            if (position <
                phase.DurationSeconds)
            {
                return phase.Phase;
            }

            position -=
                phase.DurationSeconds;
        }

        return lastPhase?.Phase;
    }

    private double? ResolveNearestRoadSpeedLimit()
    {
        var segment =
            ResolveNearestRoadSegment(
                out _);

        if (segment?.SpeedLimitKilometersPerHour is
                not double speedLimit ||
            !double.IsFinite(
                speedLimit) ||
            speedLimit <=
                0.0)
        {
            return null;
        }

        return speedLimit;
    }

    private static double PointToSegmentDistanceSquared(
        double px,
        double pz,
        double ax,
        double az,
        double bx,
        double bz)
    {
        var dx =
            bx -
            ax;
        var dz =
            bz -
            az;

        var lengthSquared =
            dx *
                dx +
            dz *
                dz;

        if (lengthSquared <=
            0.000001)
        {
            var ex =
                px -
                ax;
            var ez =
                pz -
                az;

            return ex *
                       ex +
                   ez *
                       ez;
        }

        var t =
            Math.Clamp(
                ((px -
                  ax) *
                     dx +
                 (pz -
                  az) *
                     dz) /
                lengthSquared,
                0.0,
                1.0);

        var cx =
            ax +
            dx *
                t;
        var cz =
            az +
            dz *
                t;

        var ox =
            px -
            cx;
        var oz =
            pz -
            cz;

        return ox *
                   ox +
               oz *
                   oz;
    }

    private void ResetArticulatedSections()
    {
        _articulatedSectionAbsoluteHeadingRadians.Clear();
        _articulatedSectionYawRadians.Clear();
        _articulatedSectionYawRateRadiansPerSecond.Clear();
        _articulatedSectionPitchRadians.Clear();
        _articulatedSectionPitchRateRadiansPerSecond.Clear();
        _articulatedSectionJointWorldPosition.Clear();

        foreach (var section in
                 _windowInfo.Vehicle?.Sections ??
                 Array.Empty<RuntimeVehicleSectionInfo>())
        {
            _articulatedSectionAbsoluteHeadingRadians[
                section.Index] =
                _vehicle.HeadingRadians;

            _articulatedSectionYawRadians[
                section.Index] =
                0.0f;

            _articulatedSectionYawRateRadiansPerSecond[
                section.Index] =
                0.0f;

            _articulatedSectionPitchRadians[
                section.Index] =
                0.0f;

            _articulatedSectionPitchRateRadiansPerSecond[
                section.Index] =
                0.0f;
        }

        foreach (var section in
                 _windowInfo.Vehicle?.Sections ??
                 Array.Empty<RuntimeVehicleSectionInfo>())
        {
            _articulatedSectionJointWorldPosition[
                section.Index] =
                ResolveArticulatedJointWorldPosition(
                    section);
        }
    }

    private void UpdateArticulatedSections(
        float deltaSeconds)
    {
        var sections =
            _windowInfo.Vehicle?.Sections;

        if (sections is null ||
            sections.Count == 0 ||
            deltaSeconds <= 0.0f)
        {
            return;
        }

        foreach (var section in
                 sections.OrderBy(
                     static item =>
                         item.Index))
        {
            if (_vehicle.TryGetOdeArticulatedSectionState(
                    section.Index,
                    out var physicalHeading,
                    out var physicalYaw,
                    out var physicalYawRate,
                    out var physicalPitch,
                    out var physicalPitchRate))
            {
                _articulatedSectionAbsoluteHeadingRadians[
                    section.Index] =
                    physicalHeading;

                _articulatedSectionYawRadians[
                    section.Index] =
                    physicalYaw;

                _articulatedSectionYawRateRadiansPerSecond[
                    section.Index] =
                    physicalYawRate;

                _articulatedSectionPitchRadians[
                    section.Index] =
                    physicalPitch;

                _articulatedSectionPitchRateRadiansPerSecond[
                    section.Index] =
                    physicalPitchRate;

                _articulatedSectionJointWorldPosition[
                    section.Index] =
                    ResolveArticulatedJointWorldPosition(
                        section);

                continue;
            }

            var parentHeading =
                section.ParentIndex <= 0
                    ? _vehicle.HeadingRadians
                    : _articulatedSectionAbsoluteHeadingRadians
                        .TryGetValue(
                            section.ParentIndex,
                            out var storedParent)
                        ? storedParent
                        : _vehicle.HeadingRadians;

            if (!_articulatedSectionAbsoluteHeadingRadians
                    .TryGetValue(
                        section.Index,
                        out var sectionHeading))
            {
                sectionHeading =
                    parentHeading;
            }

            var hitchWorld =
                ResolveArticulatedJointWorldPosition(
                    section);

            var previousHitch =
                _articulatedSectionJointWorldPosition
                    .TryGetValue(
                        section.Index,
                        out var storedHitch)
                    ? storedHitch
                    : hitchWorld;

            _articulatedSectionJointWorldPosition[
                section.Index] =
                hitchWorld;

            var hitchVelocity =
                (hitchWorld -
                 previousHitch) /
                Math.Max(
                    deltaSeconds,
                    0.0001f);

            var followerLength =
                Math.Clamp(
                    (float)section.FollowerLengthMeters,
                    0.75f,
                    20.0f);

            // A coupled OMSI section is constrained by its front hitch and
            // its own rotation point/rear axle. For a no-slip follower the
            // yaw rate is the lateral hitch velocity divided by the
            // hitch-to-rotation-point distance. Unlike the old speed/L
            // approximation this also accounts for the parent's yaw rate,
            // off-axis coupling points and reversing.
            var sectionRight =
                new Vector2(
                    MathF.Cos(
                        sectionHeading),
                    -MathF.Sin(
                        sectionHeading));

            var targetYawRate =
                Vector2.Dot(
                    hitchVelocity,
                    sectionRight) /
                followerLength;

            var massKilograms =
                Math.Clamp(
                    (float)(section.MassTonnes ??
                        10.0) *
                    1000.0f,
                    1_000.0f,
                    50_000.0f);

            var yawInertiaKilogramSquareMeters =
                Math.Clamp(
                    (float)(section.YawInertiaTonneSquareMeters ??
                        (massKilograms *
                         followerLength *
                         followerLength /
                         12.0f /
                         1000.0f)) *
                    1000.0f,
                    5_000.0f,
                    8_000_000.0f);

            var inertialRatio =
                Math.Clamp(
                    yawInertiaKilogramSquareMeters /
                    Math.Max(
                        massKilograms *
                        followerLength *
                        followerLength,
                        1.0f),
                    0.03f,
                    1.5f);

            var responseTime =
                Math.Clamp(
                    0.035f +
                    inertialRatio *
                    0.30f,
                    0.04f,
                    0.45f);

            var blend =
                1.0f -
                MathF.Exp(
                    -deltaSeconds /
                    responseTime);

            var yawRate =
                _articulatedSectionYawRateRadiansPerSecond
                    .TryGetValue(
                        section.Index,
                        out var storedYawRate)
                    ? storedYawRate
                    : 0.0f;

            yawRate +=
                (targetYawRate -
                 yawRate) *
                blend;

            // Prevent violent numerical snaps after a hitch crosses a tile
            // or frame-time spike while still allowing realistic jackknife
            // behaviour when reversing.
            yawRate =
                Math.Clamp(
                    yawRate,
                    -2.8f,
                    2.8f);

            sectionHeading =
                NormalizeRadians(
                    sectionHeading +
                    yawRate *
                    deltaSeconds);

            var relativeYaw =
                NormalizeRadians(
                    sectionHeading -
                    parentHeading);

            var maximumYaw =
                DegreesToRadians(
                    Math.Clamp(
                        section.MaximumYawDegrees,
                        5.0,
                        89.0));

            if (relativeYaw >
                maximumYaw)
            {
                relativeYaw =
                    maximumYaw;
                sectionHeading =
                    NormalizeRadians(
                        parentHeading +
                        relativeYaw);
                yawRate =
                    Math.Min(
                        yawRate,
                        _vehicle.YawRateRadiansPerSecond);
            }
            else if (relativeYaw <
                     -maximumYaw)
            {
                relativeYaw =
                    -maximumYaw;
                sectionHeading =
                    NormalizeRadians(
                        parentHeading +
                        relativeYaw);
                yawRate =
                    Math.Max(
                        yawRate,
                        _vehicle.YawRateRadiansPerSecond);
            }

            _articulatedSectionAbsoluteHeadingRadians[
                section.Index] =
                sectionHeading;

            _articulatedSectionYawRadians[
                section.Index] =
                relativeYaw;

            _articulatedSectionYawRateRadiansPerSecond[
                section.Index] =
                yawRate;

            _articulatedSectionPitchRadians[
                section.Index] =
                0.0f;

            _articulatedSectionPitchRateRadiansPerSecond[
                section.Index] =
                0.0f;
        }
    }

    private Vector2 ResolveArticulatedJointWorldPosition(
        RuntimeVehicleSectionInfo section)
    {
        var local =
            new Vector3(
                (float)section.JointX,
                0.0f,
                (float)section.JointZ);

        if (section.ParentIndex > 0)
        {
            local =
                Vector3.Transform(
                    local,
                    CreateArticulatedSectionMatrix(
                        section.ParentIndex));
        }

        var sine =
            MathF.Sin(
                _vehicle.HeadingRadians);
        var cosine =
            MathF.Cos(
                _vehicle.HeadingRadians);

        var rotatedX =
            local.X *
                cosine +
            local.Z *
                sine;

        var rotatedZ =
            -local.X *
                sine +
            local.Z *
                cosine;

        return new Vector2(
            _vehicle.Position.X +
                rotatedX,
            _vehicle.Position.Z +
                rotatedZ);
    }

    private Matrix4x4 CreateArticulatedSectionMatrix(
        int sectionIndex)
    {
        if (sectionIndex <= 0)
        {
            return Matrix4x4.Identity;
        }

        var sections =
            _windowInfo.Vehicle?.Sections;

        if (sections is null ||
            sections.Count == 0)
        {
            return Matrix4x4.Identity;
        }

        return CreateArticulatedSectionMatrix(
            sectionIndex,
            sections,
            new HashSet<int>());
    }

    private Matrix4x4 CreateArticulatedSectionMatrix(
        int sectionIndex,
        IReadOnlyList<RuntimeVehicleSectionInfo> sections,
        ISet<int> visited)
    {
        if (sectionIndex <= 0 ||
            !visited.Add(
                sectionIndex))
        {
            return Matrix4x4.Identity;
        }

        var section =
            sections.FirstOrDefault(
                item =>
                    item.Index ==
                    sectionIndex);

        if (section is null)
        {
            return Matrix4x4.Identity;
        }

        var yaw =
            _articulatedSectionYawRadians.TryGetValue(
                sectionIndex,
                out var storedYaw)
                ? storedYaw
                : 0.0f;

        var pitch =
            _articulatedSectionPitchRadians.TryGetValue(
                sectionIndex,
                out var storedPitch)
                ? storedPitch
                : 0.0f;

        var pivot =
            new Vector3(
                (float)section.JointX,
                (float)section.JointY,
                (float)section.JointZ);

        var local =
            Matrix4x4.CreateTranslation(
                -pivot) *
            Matrix4x4.CreateFromYawPitchRoll(
                yaw,
                pitch,
                0.0f) *
            Matrix4x4.CreateTranslation(
                pivot);

        if (section.ParentIndex <= 0)
        {
            return local;
        }

        return local *
               CreateArticulatedSectionMatrix(
                   section.ParentIndex,
                   sections,
                   visited);
    }

    private static float NormalizeRadians(
        float value)
    {
        while (value >
               MathF.PI)
        {
            value -=
                MathF.PI *
                2.0f;
        }

        while (value <
               -MathF.PI)
        {
            value +=
                MathF.PI *
                2.0f;
        }

        return value;
    }

    private void UpdateVehicleAnimationStates(
        double deltaSeconds)
    {
        if (deltaSeconds <= 0.0)
        {
            return;
        }

        var seen =
            new HashSet<RuntimeVehicleAnimationInfo>(
                ReferenceEqualityComparer.Instance);

        var meshes =
            _windowInfo.Vehicle?.Meshes;

        if (meshes is null)
        {
            return;
        }

        foreach (var mesh in meshes)
        {
            if (mesh.Animations is null)
            {
                continue;
            }

            foreach (var animation in
                     mesh.Animations)
            {
                if (!seen.Add(
                        animation))
                {
                    continue;
                }

                var target =
                    ResolveSectionNumericValue(
                        mesh.SectionIndex,
                        animation.VariableName);

                // The physical steering sign is now correct. Stock OMSI
                // steering-wheel meshes (e.g. MAN SD202 D87_lenkrad.o3d)
                // use the opposite visual rotation sense for the cockpit
                // wheel while the road-wheel Axle_Steering variables stay
                // in physical steering direction. Invert only that cockpit
                // animation target, never the driving/physics variable.
                if (ShouldInvertCockpitSteeringWheelAnimation(
                        mesh,
                        animation))
                {
                    target =
                        -target;
                }

                if (!double.IsFinite(
                        target))
                {
                    target = 0.0;
                }

                if (!_vehicleAnimationValues.TryGetValue(
                        animation,
                        out var current))
                {
                    _vehicleAnimationValues[
                        animation] =
                        target;
                    continue;
                }

                var hasDelay =
                    animation.Delay is > 0.0;

                var hasMaxSpeed =
                    animation.MaxSpeed is > 0.0;

                if (!hasDelay &&
                    !hasMaxSpeed)
                {
                    _vehicleAnimationValues[
                        animation] =
                        target;
                    continue;
                }

                var delta =
                    target -
                    current;

                if (Math.Abs(
                        delta) <
                    0.000000001)
                {
                    _vehicleAnimationValues[
                        animation] =
                        target;
                    continue;
                }

                var step =
                    delta;

                if (hasDelay)
                {
                    // OMSI delay values behave as reciprocal response
                    // values: larger numbers reduce damping. Exponential
                    // convergence reproduces that stable first-order lag
                    // without depending on frame rate.
                    var response =
                        1.0 -
                        Math.Exp(
                            -animation.Delay!.Value *
                            deltaSeconds);

                    step *=
                        Math.Clamp(
                            response,
                            0.0,
                            1.0);
                }

                if (hasMaxSpeed)
                {
                    // OMSI documents maxspeed as 1 / animation duration,
                    // therefore it is a maximum normalized animation rate.
                    var maximumStep =
                        animation.MaxSpeed!.Value *
                        deltaSeconds;

                    step =
                        Math.Clamp(
                            step,
                            -maximumStep,
                            maximumStep);
                }

                var next =
                    current +
                    step;

                if ((delta > 0.0 &&
                     next > target) ||
                    (delta < 0.0 &&
                     next < target))
                {
                    next =
                        target;
                }

                _vehicleAnimationValues[
                    animation] =
                    double.IsFinite(
                        next)
                        ? next
                        : target;
            }
        }
    }

    private static bool ShouldInvertCockpitSteeringWheelAnimation(
        RuntimeObjectMeshInfo mesh,
        RuntimeVehicleAnimationInfo animation)
    {
        if (animation.Kind !=
                RuntimeVehicleAnimationKind.Rotation ||
            !animation.VariableName.StartsWith(
                "Axle_Steering_",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path =
            mesh.DeclaredPath ??
            string.Empty;

        return path.Contains(
                   "lenkrad",
                   StringComparison.OrdinalIgnoreCase) ||
               path.Contains(
                   "steeringwheel",
                   StringComparison.OrdinalIgnoreCase) ||
               path.Contains(
                   "steering_wheel",
                   StringComparison.OrdinalIgnoreCase) ||
               path.Contains(
                   "volante",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateVehicleLightStates(
        double deltaSeconds)
    {
        if (deltaSeconds <= 0.0 ||
            _windowInfo.Vehicle is null)
        {
            return;
        }

        foreach (var mesh in
                 _windowInfo.Vehicle.Meshes)
        {
            if (mesh.LightEffects is null)
            {
                continue;
            }

            foreach (var light in
                     mesh.LightEffects)
            {
                var target =
                    ResolveVehicleLightTarget(
                        light,
                        mesh.SectionIndex);

                if (!_vehicleLightValues.TryGetValue(
                        light,
                        out var current))
                {
                    _vehicleLightValues[
                        light] =
                        target;
                    continue;
                }

                if (light.TimeConstantSeconds <=
                    0.000001)
                {
                    _vehicleLightValues[
                        light] =
                        target;
                    continue;
                }

                var response =
                    1.0 -
                    Math.Exp(
                        -deltaSeconds /
                        light.TimeConstantSeconds);

                var next =
                    current +
                    (target - current) *
                    Math.Clamp(
                        response,
                        0.0,
                        1.0);

                _vehicleLightValues[
                    light] =
                    double.IsFinite(
                        next)
                        ? next
                        : target;
            }
        }
    }

    private double ResolveVehicleLightTarget(
        RuntimeVehicleLightEffectInfo light,
        int sectionIndex = 0)
    {
        double source;

        if (!double.TryParse(
                light.BrightnessVariable,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out source))
        {
            source =
                ResolveSectionNumericValue(
                    sectionIndex,
                    light.BrightnessVariable);
        }

        if (!double.IsFinite(
                source))
        {
            source = 0.0;
        }

        var value =
            source *
            light.BrightnessFactor;

        return double.IsFinite(
                   value)
            ? Math.Clamp(
                value,
                0.0,
                16.0)
            : 0.0;
    }

    private double ResolveVehicleLightValue(
        RuntimeVehicleLightEffectInfo light,
        int sectionIndex = 0)
    {
        if (_vehicleLightValues.TryGetValue(
                light,
                out var value) &&
            double.IsFinite(
                value))
        {
            return value;
        }

        return ResolveVehicleLightTarget(
            light,
            sectionIndex);
    }

    private double ResolveVehicleAnimationValue(
        RuntimeVehicleAnimationInfo animation,
        int sectionIndex = 0)
    {
        if (_vehicleAnimationValues.TryGetValue(
                animation,
                out var value) &&
            double.IsFinite(
                value))
        {
            return value;
        }

        var raw =
            ResolveSectionNumericValue(
                sectionIndex,
                animation.VariableName);

        return double.IsFinite(
                raw)
            ? raw
            : 0.0;
    }

    private bool HandleOmsiSystemMacro(
        string name,
        OmsiScriptCallbackContext context) =>
        HandleOmsiSystemMacro(
            0,
            name,
            context);

    private bool HandleOmsiSectionSystemMacro(
        int sectionIndex,
        string name,
        OmsiScriptCallbackContext context) =>
        HandleOmsiSystemMacro(
            sectionIndex,
            name,
            context);

    private bool HandleOmsiSystemMacro(
        int sectionIndex,
        string name,
        OmsiScriptCallbackContext context)
    {
        if (name.Equals(
                "NrSpecRandom",
                StringComparison.Ordinal))
        {
            var selector =
                (int)Math.Truncate(
                    context.PopFloat());

            context.PushFloat(
                ResolveNrSpecRandom(
                    selector));

            return true;
        }

        if (name.Equals(
                "GetHumanCountOnPathLink",
                StringComparison.Ordinal) ||
            name.Equals(
                "GetHumanCountOnSeat",
                StringComparison.Ordinal))
        {
            _ =
                (int)Math.Truncate(
                    context.PopFloat());

            // The x64 runtime does not spawn passenger agents yet, so the
            // exact current occupancy of every path link and every seat is
            // zero. Keeping these callbacks handled preserves native door/
            // suspension/cabin scripts without inventing phantom occupants.
            context.PushFloat(
                0.0);

            return true;
        }

        if (name.Equals(
                "GetHeightAbovePoint",
                StringComparison.Ordinal))
        {
            var localZ =
                context.PopFloat();

            var localY =
                context.PopFloat();

            var localX =
                context.PopFloat();

            context.PushFloat(
                ResolveHeightAboveOmsiPoint(
                    sectionIndex,
                    localX,
                    localY,
                    localZ));

            return true;
        }

        return sectionIndex ==
                   0 &&
               _previousSystemMacroHandler?.Invoke(
                   name,
                   context) ==
               true;
    }

    private double ResolveHeightAboveOmsiPoint(
        int sectionIndex,
        double omsiX,
        double omsiY,
        double omsiZ)
    {
        if (!double.IsFinite(
                omsiX) ||
            !double.IsFinite(
                omsiY) ||
            !double.IsFinite(
                omsiZ))
        {
            return 0.0;
        }

        // OMSI vehicle coordinates are x=right, y=forward, z=up. Vehicle
        // geometry/cameras are normalized to renderer x=left, y=up,
        // z=forward, therefore x is mirrored and y/z are swapped.
        var localPoint =
            new Vector3(
                -(float)omsiX,
                (float)omsiZ,
                (float)omsiY);

        var vehicleWorld =
            CreateArticulatedSectionMatrix(
                sectionIndex) *
            _vehicle.CreateWorldMatrix();

        var origin =
            Vector3.Transform(
                localPoint,
                vehicleWorld);

        var direction =
            Vector3.TransformNormal(
                -Vector3.UnitY,
                vehicleWorld);

        if (direction.LengthSquared() <
            0.000001f)
        {
            direction =
                -Vector3.UnitY;
        }
        else
        {
            direction =
                Vector3.Normalize(
                    direction);
        }

        const float maximumDistanceMeters =
            100.0f;

        var bestDistance =
            maximumDistanceMeters;

        var found =
            TryRaycastSplineSurface(
                origin,
                direction,
                maximumDistanceMeters,
                out var splineDistance);

        if (found)
        {
            bestDistance =
                Math.Min(
                    bestDistance,
                    splineDistance);
        }

        if (TryRaycastTerrainSurface(
                origin,
                direction,
                bestDistance,
                out var terrainDistance))
        {
            bestDistance =
                Math.Min(
                    bestDistance,
                    terrainDistance);

            found = true;
        }

        return found
            ? Math.Max(
                bestDistance,
                0.0f)
            : 0.0;
    }

    private bool TryRaycastSplineSurface(
        Vector3 origin,
        Vector3 direction,
        float maximumDistanceMeters,
        out float distance)
    {
        distance =
            float.PositiveInfinity;

        var vertices =
            _splineGeometry.Vertices;

        if (vertices.Length <
            3)
        {
            return false;
        }

        var found =
            false;

        for (var index = 0;
             index + 2 <
                 vertices.Length;
             index += 3)
        {
            var a =
                vertices[index]
                    .Position;

            var b =
                vertices[index + 1]
                    .Position;

            var c =
                vertices[index + 2]
                    .Position;

            if (!TryRayTriangleDistance(
                    origin,
                    direction,
                    a,
                    b,
                    c,
                    out var candidate) ||
                candidate <
                    0.0f ||
                candidate >
                    maximumDistanceMeters ||
                candidate >=
                    distance)
            {
                continue;
            }

            distance =
                candidate;

            found = true;
        }

        return found;
    }

    private bool TryRaycastTerrainSurface(
        Vector3 origin,
        Vector3 direction,
        float maximumDistanceMeters,
        out float distance)
    {
        distance =
            0.0f;

        if (maximumDistanceMeters <=
            0.0f)
        {
            return false;
        }

        const float stepMeters =
            0.25f;

        var previousDistance =
            0.0f;

        if (!_terrainSurfaceSampler.TrySample(
                origin.X,
                origin.Z,
                out var previousGround))
        {
            return false;
        }

        var previousClearance =
            origin.Y -
            previousGround;

        if (previousClearance <=
            0.0f)
        {
            distance =
                0.0f;

            return true;
        }

        for (var currentDistance =
                 stepMeters;
             currentDistance <=
                 maximumDistanceMeters +
                 0.0001f;
             currentDistance +=
                 stepMeters)
        {
            var point =
                origin +
                direction *
                currentDistance;

            if (!_terrainSurfaceSampler.TrySample(
                    point.X,
                    point.Z,
                    out var ground))
            {
                previousDistance =
                    currentDistance;
                previousClearance =
                    float.NaN;
                continue;
            }

            var clearance =
                point.Y -
                ground;

            if (clearance <=
                    0.0f &&
                float.IsFinite(
                    previousClearance) &&
                previousClearance >
                    0.0f)
            {
                var low =
                    previousDistance;

                var high =
                    currentDistance;

                for (var iteration = 0;
                     iteration < 12;
                     iteration++)
                {
                    var middle =
                        (low +
                         high) *
                        0.5f;

                    var middlePoint =
                        origin +
                        direction *
                        middle;

                    if (!_terrainSurfaceSampler.TrySample(
                            middlePoint.X,
                            middlePoint.Z,
                            out var middleGround))
                    {
                        low =
                            middle;

                        continue;
                    }

                    if (middlePoint.Y -
                        middleGround >
                        0.0f)
                    {
                        low =
                            middle;
                    }
                    else
                    {
                        high =
                            middle;
                    }
                }

                distance =
                    high;

                return true;
            }

            previousDistance =
                currentDistance;

            previousClearance =
                clearance;
        }

        return false;
    }

    private static bool TryRayTriangleDistance(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        distance =
            0.0f;

        var edge1 =
            b -
            a;

        var edge2 =
            c -
            a;

        var p =
            Vector3.Cross(
                direction,
                edge2);

        var determinant =
            Vector3.Dot(
                edge1,
                p);

        if (Math.Abs(
                determinant) <
            0.000001f)
        {
            return false;
        }

        var inverse =
            1.0f /
            determinant;

        var t =
            origin -
            a;

        var u =
            Vector3.Dot(
                t,
                p) *
            inverse;

        if (u <
                -0.00001f ||
            u >
                1.00001f)
        {
            return false;
        }

        var q =
            Vector3.Cross(
                t,
                edge1);

        var v =
            Vector3.Dot(
                direction,
                q) *
            inverse;

        if (v <
                -0.00001f ||
            u +
                v >
                1.00001f)
        {
            return false;
        }

        var candidate =
            Vector3.Dot(
                edge2,
                q) *
            inverse;

        if (!float.IsFinite(
                candidate) ||
            candidate <
                0.0f)
        {
            return false;
        }

        distance =
            candidate;

        return true;
    }

    private double ResolveNrSpecRandom(
        int selector)
    {
        const uint offsetBasis =
            2166136261;
        const uint prime =
            16777619;

        var hash =
            offsetBasis;

        var identity =
            _windowInfo.Vehicle?.RelativePath ??
            "vehicle";

        foreach (var character in
                 identity)
        {
            var normalized =
                char.ToUpperInvariant(
                    character);

            hash ^=
                (byte)(normalized & 0xFF);
            hash *=
                prime;

            hash ^=
                (byte)(normalized >> 8);
            hash *=
                prime;
        }

        unchecked
        {
            hash ^=
                (uint)selector;
            hash *=
                prime;

            hash ^=
                hash >> 13;
            hash *=
                0x5BD1E995u;
            hash ^=
                hash >> 15;
        }

        return
            (hash & 0x00FFFFFFu) /
            16777216.0;
    }

    private void OnUnhandledSystemMacro(
        string name)
    {
        if (!_reportedUnhandledSystemMacros.Add(
                name))
        {
            return;
        }

        Console.WriteLine(
            $"[script] unhandled OMSI system macro: {name}");
    }

    private static void OnScriptDebugMessage(
        string message)
    {
        Console.WriteLine(
            $"[script:$msg] {message}");
    }

    private static double ResolveInitialOdometerMeters(
        IReadOnlyDictionary<string, double>? initialVariables)
    {
        if (initialVariables is null)
        {
            return 0.0;
        }

        initialVariables.TryGetValue(
            "kmcounter_km",
            out var kilometers);

        initialVariables.TryGetValue(
            "kmcounter_m",
            out var meters);

        kilometers =
            double.IsFinite(
                    kilometers)
                ? Math.Max(
                    Math.Floor(
                        kilometers),
                    0.0)
                : 0.0;

        meters =
            double.IsFinite(
                    meters)
                ? Math.Clamp(
                    meters,
                    0.0,
                    999.999999)
                : 0.0;

        return kilometers *
                   1_000.0 +
               meters;
    }

    private void InitializeVehicleScripts()
    {
        if (_scriptRuntime is null)
        {
            return;
        }

        // OMSI scripts can emit T.L/T.F during {init}. Those initialization
        // triggers must not become audible "engine already running" sounds
        // before the player actually starts the vehicle. Keep sound.cfg
        // loaded so the script host is complete, but suppress trigger output
        // until all lead/section init macros have finished and host state is
        // synchronized from the scripts.
        _suppressVehicleInitAudio =
            true;

        try
        {
            UpdateScriptHostVariables(
                0.0,
                0.0);

            _scriptRuntime.ExecuteInit();

            if (_initialVehicleVariables is
                { Count: > 0 })
            {
                foreach (var pair in
                         _initialVehicleVariables)
                {
                    _scriptRuntime.SetLocal(
                        pair.Key,
                        pair.Value);
                }
            }

            SynchronizeHostVehicleStateFromScripts();
            SynchronizeOmsiScriptDynamics();
            AcknowledgeOmsiStringRefresh();

            foreach (var pair in
                     _sectionScriptRuntimes)
            {
                var section =
                    ResolveVehicleSection(
                        pair.Key);

                if (section is null)
                {
                    continue;
                }

                UpdateSectionScriptHostVariables(
                    pair.Value,
                    section,
                    0.0,
                    0.0);

                pair.Value.ExecuteInit();

                AcknowledgeOmsiStringRefresh(
                    pair.Value);
            }
        }
        finally
        {
            _suppressVehicleInitAudio =
                false;
        }

        WriteVehicleRuntimeStateDiagnostics();
    }

    private void WriteVehiclePhysicsDiagnostics(
        double absoluteSeconds)
    {
        _lastVehiclePhysicsDiagnosticsSeconds =
            absoluteSeconds;

        if (_windowInfo.Vehicle is null)
        {
            return;
        }

        try
        {
            var lines =
                new List<string>
                {
                    $"timestamp={DateTimeOffset.Now:O}",
                    $"vehicle={_windowInfo.Vehicle.DisplayName}",
                    ""
                };

            lines.AddRange(
                _vehicle.BuildPhysicsDiagnostics());

            if (_scriptRuntime is not null)
            {
                static string ScriptValue(
                    OmsiScriptRuntime runtime,
                    string variable) =>
                    runtime.HasLocalVariable(
                        variable)
                        ? runtime.GetLocal(
                                variable)
                            .ToString(
                                "0.###",
                                System.Globalization.CultureInfo.InvariantCulture)
                        : "<missing>";

                lines.Add(
                    "");
                lines.Add(
                    "drivetrainScript:");
                lines.Add(
                    $"engine_on={ScriptValue(_scriptRuntime, "engine_on")}");
                lines.Add(
                    $"engine_n={ScriptValue(_scriptRuntime, "engine_n")}");
                lines.Add(
                    $"engine_M={ScriptValue(_scriptRuntime, "engine_M")}");
                lines.Add(
                    $"M_Wheel={ScriptValue(_scriptRuntime, "M_Wheel")}");
                lines.Add(
                    $"n_Wheel={ScriptValue(_scriptRuntime, "n_Wheel")}");
                lines.Add(
                    $"Brakeforce={ScriptValue(_scriptRuntime, "Brakeforce")}");
                lines.Add(
                    $"gearSelector={ScriptValue(_scriptRuntime, "antrieb_getr_gangwahl")}");
                lines.Add(
                    $"gearSelectorWriter={_scriptRuntime.WritesLocalVariable("antrieb_getr_gangwahl")}");
                lines.Add(
                    $"gearPreselect={ScriptValue(_scriptRuntime, "antrieb_getr_gangvorwahl")}");
                lines.Add(
                    $"gearActual={ScriptValue(_scriptRuntime, "antrieb_getr_gang")}");
                lines.Add(
                    $"gearRatio={ScriptValue(_scriptRuntime, "antrieb_getr_ratio_act")}");
                lines.Add(
                    $"parkingBrake={ScriptValue(_scriptRuntime, "bremse_feststell")}");
                lines.Add(
                    $"parkingBrakeWriter={_scriptRuntime.WritesLocalVariable("bremse_feststell")}");
                lines.Add(
                    $"engineOnWriter={_scriptRuntime.WritesLocalVariable("engine_on")}");
                lines.Add(
                    $"cardanRpm={ScriptValue(_scriptRuntime, "antrieb_n_kardanwelle")}");
            }

            lines.Add(
                $"primaryDrivenSection={_vehicle.PrimaryDrivenSectionIndex}");
            lines.Add(
                $"primaryDrivenOmsiAxle={_vehicle.PrimaryDrivenOmsiAxleIndex}");

            var torqueRuntime =
                ResolveOmsiWheelTorqueRuntime();

            var torqueAuthority =
                ReferenceEquals(
                    torqueRuntime,
                    _scriptRuntime)
                    ? "lead"
                    : _sectionScriptRuntimes
                        .FirstOrDefault(
                            pair =>
                                ReferenceEquals(
                                    pair.Value,
                                    torqueRuntime))
                        .Key is
                            var sectionKey &&
                        sectionKey >
                            0
                            ? $"section:{sectionKey}"
                            : torqueRuntime is null
                                ? "<none>"
                                : "unknown";

            lines.Add(
                $"wheelTorqueAuthority={torqueAuthority}");

            foreach (var pair in
                     _sectionScriptRuntimes
                         .OrderBy(
                             static item =>
                                 item.Key))
            {
                var runtime =
                    pair.Value;

                static string SectionValue(
                    OmsiScriptRuntime sectionRuntime,
                    string variable) =>
                    sectionRuntime.HasLocalVariable(
                        variable)
                        ? sectionRuntime.GetLocal(
                                variable)
                            .ToString(
                                "0.###",
                                System.Globalization.CultureInfo.InvariantCulture)
                        : "<missing>";

                lines.Add(
                    $"sectionScript#{pair.Key}|writesM_Wheel={runtime.WritesLocalVariable("M_Wheel")}|M_Wheel={SectionValue(runtime, "M_Wheel")}|writesBrakeforce={runtime.WritesLocalVariable("Brakeforce")}|Brakeforce={SectionValue(runtime, "Brakeforce")}|n_Wheel={SectionValue(runtime, "n_Wheel")}|engine_on={SectionValue(runtime, "engine_on")}|alpha={SectionValue(runtime, "articulation_0_alpha")}|beta={SectionValue(runtime, "articulation_0_beta")}");
            }

            File.WriteAllLines(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-physics-state.log"),
                lines);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[vehicle-physics-state] unable to write diagnostics: {exception.Message}");
        }
    }

    private void WriteVehicleRuntimeStateDiagnostics()
    {
        if (_scriptRuntime is null ||
            _windowInfo.Vehicle is null)
        {
            return;
        }

        try
        {
            var lines =
                new List<string>
                {
                    $"timestamp={DateTimeOffset.Now:O}",
                    $"vehicle={_windowInfo.Vehicle.DisplayName}",
                    "",
                    "meshRuntimeState:"
                };

            foreach (var mesh in
                     _windowInfo.Vehicle.Meshes)
            {
                var visibility =
                    mesh.VisibilityConditions is
                        { Count: > 0 }
                        ? string.Join(
                            ",",
                            mesh.VisibilityConditions.Select(
                                condition =>
                                    $"{condition.VariableName}:actual={_scriptRuntime.GetLocal(condition.VariableName).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} expected={condition.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"))
                        : "<none>";

                var visible =
                    AreVehicleVisibilityConditionsMet(
                        mesh.VisibilityConditions);

                lines.Add(
                    $"mesh={mesh.DeclaredPath} | viewpoint={mesh.ViewpointFlag} | visibleNow={visible} | visibleConditions={visibility}");

                for (var materialIndex = 0;
                     materialIndex < mesh.Materials.Count;
                     materialIndex++)
                {
                    var material =
                        mesh.Materials[materialIndex];

                    var alphaScaleValue =
                        string.IsNullOrWhiteSpace(
                            material.AlphaScaleVariable)
                            ? "<none>"
                            : _scriptRuntime
                                .GetLocal(
                                    material.AlphaScaleVariable)
                                .ToString(
                                    "0.###",
                                    System.Globalization.CultureInfo.InvariantCulture);

                    var lightMapValue =
                        string.IsNullOrWhiteSpace(
                            material.LightMapVariable)
                            ? "<none>"
                            : _scriptRuntime
                                .GetLocal(
                                    material.LightMapVariable)
                                .ToString(
                                    "0.###",
                                    System.Globalization.CultureInfo.InvariantCulture);

                    var legacyChangeValue =
                        string.IsNullOrWhiteSpace(
                            material.MaterialChangeVariable)
                            ? "<none>"
                            : _scriptRuntime
                                .GetLocal(
                                    material.MaterialChangeVariable)
                                .ToString(
                                    "0.###",
                                    System.Globalization.CultureInfo.InvariantCulture);

                    var changeSets =
                        material.MaterialChangeSets is
                            { Count: > 0 }
                            ? string.Join(
                                ";",
                                material.MaterialChangeSets.Select(
                                    set =>
                                    {
                                        var value =
                                            _scriptRuntime.GetLocal(
                                                set.VariableName);

                                        var rounded =
                                            double.IsFinite(
                                                value)
                                                ? Math.Round(
                                                    value,
                                                    MidpointRounding.ToEven)
                                                : double.NaN;

                                        return $"{set.VariableName}:actual={value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} selected={rounded.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} items=[{string.Join(",", set.Items.Select(static item => item.ItemIndex))}]";
                                    }))
                            : "<none>";

                    if (material.AlphaMode != 0 ||
                        material.HasTransMapDirective ||
                        material.NoZWrite ||
                        material.NoZCheck ||
                        !string.IsNullOrWhiteSpace(
                            material.AlphaScaleVariable) ||
                        !string.IsNullOrWhiteSpace(
                            material.LightMapVariable) ||
                        !string.IsNullOrWhiteSpace(
                            material.MaterialChangeVariable) ||
                        material.MaterialChangeSets is
                            { Count: > 0 })
                    {
                        lines.Add(
                            $"  mat#{materialIndex} tex={Path.GetFileName(material.TexturePath) ?? "<none>"} | alpha={material.AlphaMode} | transmapDirective={material.HasTransMapDirective} | transmap={Path.GetFileName(material.TransMapTexturePath) ?? "<none>"} | noZwrite={material.NoZWrite} | noZcheck={material.NoZCheck} | alphaScale={material.AlphaScaleVariable ?? "<none>"}:{alphaScaleValue} | lightmapVar={material.LightMapVariable ?? "<none>"}:{lightMapValue} | matlChange={material.MaterialChangeVariable ?? "<none>"}:{legacyChangeValue} | changeSets={changeSets}");
                    }
                }
            }

            File.WriteAllLines(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-runtime-state.log"),
                lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[vehicle-runtime-state] unable to write diagnostics: {ex.Message}");
        }
    }

    private void WriteVehiclePanelDiagnostics()
    {
        if (_scriptRuntime is null ||
            _windowInfo.Vehicle is null)
        {
            return;
        }

        try
        {
            var panelMeshes =
                _windowInfo.Vehicle.Meshes
                    .Where(
                        mesh =>
                            mesh.DeclaredPath.Contains(
                                "painel",
                                StringComparison.OrdinalIgnoreCase) ||
                            mesh.DeclaredPath.Contains(
                                "cockpit",
                                StringComparison.OrdinalIgnoreCase) ||
                            mesh.DeclaredPath.Contains(
                                "dashboard",
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            var animationVariables =
                panelMeshes
                    .SelectMany(
                        static mesh =>
                            mesh.Animations ??
                            Array.Empty<RuntimeVehicleAnimationInfo>())
                    .Select(
                        static animation =>
                            animation.VariableName)
                    .Where(
                        static variable =>
                            !string.IsNullOrWhiteSpace(
                                variable))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        static variable =>
                            variable,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var materialVariables =
                panelMeshes
                    .SelectMany(
                        static mesh =>
                            mesh.Materials)
                    .SelectMany(
                        static material =>
                            new[]
                            {
                                material.AlphaScaleVariable,
                                material.LightMapVariable,
                                material.MaterialChangeVariable
                            })
                    .Where(
                        static variable =>
                            !string.IsNullOrWhiteSpace(
                                variable))
                    .Select(
                        static variable =>
                            variable!)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        static variable =>
                            variable,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var lines =
                new List<string>
                {
                    $"timestamp={DateTimeOffset.Now:O}",
                    $"vehicle={_windowInfo.Vehicle.DisplayName}",
                    $"panelMeshes={panelMeshes.Length}",
                    "",
                    "animationVariables:"
                };

            foreach (var variable in
                     animationVariables)
            {
                lines.Add(
                    $"{variable}={_scriptRuntime.GetLocal(variable).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            lines.Add(
                "");
            lines.Add(
                "materialVariables:");

            foreach (var variable in
                     materialVariables)
            {
                lines.Add(
                    $"{variable}={_scriptRuntime.GetLocal(variable).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            lines.Add(
                "");
            lines.Add(
                "mouseEvents:");

            foreach (var mesh in
                     _windowInfo.Vehicle.Meshes
                         .Where(
                             static mesh =>
                                 !string.IsNullOrWhiteSpace(
                                     mesh.MouseEventTrigger))
                         .OrderBy(
                             static mesh =>
                                 mesh.ModelOrdinal))
            {
                lines.Add(
                    $"mesh#{mesh.ModelOrdinal} path={mesh.DeclaredPath} trigger={mesh.MouseEventTrigger} viewpoint={mesh.ViewpointFlag}");
            }

            File.WriteAllLines(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-panel-state.log"),
                lines);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[vehicle-panel] diagnostics unavailable: {exception.Message}");
        }
    }

    private void UpdateVehicleScripts(
        double deltaSeconds,
        double absoluteSeconds)
    {
        if (_scriptRuntime is null)
        {
            return;
        }

        UpdateScriptHostVariables(
            deltaSeconds,
            absoluteSeconds);

        _pluginStep?.Invoke(
            deltaSeconds,
            _scriptRuntime,
            DispatchOmsiScriptTrigger);

        // OMSI scripts own engine/electrical/gearbox state. The host feeds
        // predefined physical/input variables, then the vehicle scripts
        // calculate drivetrain, brake, cockpit and sound state themselves.
        foreach (var binding in
                 _activeOmsiContinuousBindings)
        {
            DispatchOmsiScriptTrigger(
                binding.Trigger);
        }

        if (!string.IsNullOrWhiteSpace(
                _activeVehicleMouseTrigger))
        {
            SetOmsiMouseSystemVariables(
                _activeVehicleMouseSectionIndex,
                _vehicleMouseDeltaX,
                _vehicleMouseDeltaY);

            DispatchOmsiSectionScriptTrigger(
                _activeVehicleMouseSectionIndex,
                _activeVehicleMouseTrigger +
                "_drag");

            _vehicleMouseDeltaX =
                0.0f;
            _vehicleMouseDeltaY =
                0.0f;

            SetOmsiMouseSystemVariables(
                _activeVehicleMouseSectionIndex,
                0.0f,
                0.0f);
        }

        _scriptRuntime.ExecuteFrame();
        SynchronizeHostVehicleStateFromScripts();
        SynchronizeOmsiScriptDynamics();
        AcknowledgeOmsiStringRefresh();

        foreach (var pair in
                 _sectionScriptRuntimes)
        {
            var section =
                ResolveVehicleSection(
                    pair.Key);

            if (section is null)
            {
                continue;
            }

            UpdateSectionScriptHostVariables(
                pair.Value,
                section,
                deltaSeconds,
                absoluteSeconds);

            pair.Value.ExecuteFrame();

            AcknowledgeOmsiStringRefresh(
                pair.Value);
        }

        if (!_vehiclePanelAuditWritten &&
            absoluteSeconds >= 1.0)
        {
            WriteVehiclePanelDiagnostics();
            _vehiclePanelAuditWritten =
                true;
        }
    }

    private void AcknowledgeOmsiStringRefresh()
    {
        if (_scriptRuntime is not null)
        {
            AcknowledgeOmsiStringRefresh(
                _scriptRuntime);
        }
    }

    private static void AcknowledgeOmsiStringRefresh(
        OmsiScriptRuntime runtime)
    {
        if (!runtime.HasLocalVariable(
                "Refresh_Strings"))
        {
            return;
        }

        // OMSI resets this write-only refresh request after the host has
        // consumed the current string-variable values. Text textures in
        // this renderer already compare the live string value on every
        // draw, so acknowledging the flag here preserves the script
        // handshake without delaying the visual update.
        if (Math.Abs(
                runtime.GetLocal(
                    "Refresh_Strings")) >
            0.000001)
        {
            runtime.SetLocal(
                "Refresh_Strings",
                0.0);
        }
    }

    private RuntimeVehicleSectionInfo? ResolveVehicleSection(
        int sectionIndex) =>
        _windowInfo.Vehicle?.Sections?
            .FirstOrDefault(
                section =>
                    section.Index ==
                    sectionIndex);

    private OmsiScriptRuntime? ResolveScriptRuntimeForSection(
        int sectionIndex) =>
        sectionIndex > 0 &&
        _sectionScriptRuntimes.TryGetValue(
            sectionIndex,
            out var runtime)
                ? runtime
                : _scriptRuntime;

    private double ResolveSectionNumericValue(
        int sectionIndex,
        string variableName)
    {
        if (sectionIndex >
                0 &&
            _sectionScriptRuntimes.TryGetValue(
                sectionIndex,
                out var sectionRuntime))
        {
            if (sectionRuntime.WritesLocalVariable(
                    variableName) ||
                IsSectionHostLocalVariable(
                    variableName) ||
                _scriptRuntime is null ||
                !_scriptRuntime.HasLocalVariable(
                    variableName))
            {
                return sectionRuntime.GetLocal(
                    variableName);
            }
        }

        return _scriptRuntime?.GetLocal(
                   variableName) ??
               0.0;
    }

    private string ResolveSectionStringValue(
        int sectionIndex,
        string variableName)
    {
        if (sectionIndex >
                0 &&
            _sectionScriptRuntimes.TryGetValue(
                sectionIndex,
                out var sectionRuntime) &&
            (sectionRuntime.WritesStringLocalVariable(
                 variableName) ||
             _scriptRuntime is null))
        {
            return sectionRuntime.GetStringLocal(
                variableName);
        }

        return _scriptRuntime?.GetStringLocal(
                   variableName) ??
               string.Empty;
    }

    private static bool IsSectionHostLocalVariable(
        string variableName) =>
        variableName.Equals(
            "Throttle",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "Brake",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "Clutch",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "Velocity",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "Velocity_Ground",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "n_Wheel",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "kmcounter_km",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "kmcounter_m",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.Equals(
            "Envir_Brightness",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.StartsWith(
            "A_Trans_",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.StartsWith(
            "Wheel_Rotation_",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.StartsWith(
            "Wheel_RotationSpeed_",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.StartsWith(
            "Axle_Suspension_",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.StartsWith(
            "Axle_Steering_",
            StringComparison.OrdinalIgnoreCase) ||
        variableName.StartsWith(
            "articulation_",
            StringComparison.OrdinalIgnoreCase);

    private void InheritLeadScriptValueWhenPassive(
        OmsiScriptRuntime sectionRuntime,
        string variableName)
    {
        if (_scriptRuntime is null ||
            sectionRuntime.WritesLocalVariable(
                variableName) ||
            !sectionRuntime.HasLocalVariable(
                variableName) ||
            !_scriptRuntime.HasLocalVariable(
                variableName))
        {
            return;
        }

        sectionRuntime.SetLocal(
            variableName,
            _scriptRuntime.GetLocal(
                variableName));
    }

    private bool ResolveLeadEngineRunning()
    {
        // The player vehicle always starts electrically and mechanically
        // off. Script init blocks may seed engine_on/engine_n with non-zero
        // values before the user starts the engine; those values must not
        // make propulsion loops audible by themselves.
        if (!_vehicle.EngineRunning)
        {
            return false;
        }

        if (_scriptRuntime is not null)
        {
            // Presence in the OMSI varlist is enough for engine audio state.
            // Some stock scripts read/update these through macros in ways the
            // static "writes variable" analysis cannot always prove.
            if (_scriptRuntime.HasLocalVariable(
                    "engine_on"))
            {
                return _scriptRuntime.GetLocal(
                           "engine_on") >
                       0.5;
            }

            if (_scriptRuntime.HasLocalVariable(
                    "engine_injection_on"))
            {
                return _scriptRuntime.GetLocal(
                           "engine_injection_on") >
                       0.5;
            }
        }

        return _vehicle.EngineRunning;
    }

    private bool ResolveSectionEngineRunning(
        OmsiScriptRuntime? runtime)
    {
        if (!_vehicle.EngineRunning)
        {
            return false;
        }

        if (runtime is not null &&
            runtime.HasLocalVariable(
                "engine_on") &&
            runtime.WritesLocalVariable(
                "engine_on"))
        {
            return runtime.GetLocal(
                       "engine_on") >
                   0.5;
        }

        return _vehicle.EngineRunning;
    }

    private int ResolveSectionOmsiAxleStartIndex(
        int sectionIndex)
    {
        var start =
            Math.Max(
                _windowInfo.Vehicle?.Physics?.Axles?.Count ??
                0,
                2);

        foreach (var section in
                 _windowInfo.Vehicle?.Sections?
                     .OrderBy(
                         static item =>
                             item.Index) ??
                 Enumerable.Empty<RuntimeVehicleSectionInfo>())
        {
            if (section.Index ==
                sectionIndex)
            {
                return start;
            }

            start +=
                Math.Max(
                    section.Physics?.Axles?.Count ??
                    0,
                    1);
        }

        return start;
    }

    private void UpdateSectionScriptHostVariables(
        OmsiScriptRuntime runtime,
        RuntimeVehicleSectionInfo section,
        double deltaSeconds,
        double absoluteSeconds)
    {
        runtime.SetSystem(
            "Timegap",
            deltaSeconds);

        runtime.SetSystem(
            "GetTime",
            absoluteSeconds);

        var now =
            DateTime.Now;

        runtime.SetSystem(
            "Time",
            now.TimeOfDay.TotalSeconds);

        runtime.SetSystem(
            "Year",
            now.Year);

        runtime.SetSystem(
            "Month",
            now.Month);

        runtime.SetSystem(
            "Day",
            now.Day);

        runtime.SetSystem(
            "DayOfYear",
            now.DayOfYear);

        runtime.SetSystem(
            "Pause",
            _simulationPaused
                ? 1.0
                : 0.0);

        runtime.SetSystem(
            "NoSound",
            _masterVolume <=
                    0.0001f
                ? 1.0
                : 0.0);

        runtime.SetLocal(
            "Envir_Brightness",
            1.0);

        runtime.SetLocal(
            "Throttle",
            _vehicle.AcceleratorLevel);

        runtime.SetLocal(
            "Brake",
            _vehicle.BrakeLevel);

        runtime.SetLocal(
            "Clutch",
            Math.Max(
                _controllerClutchInput,
                IsHostActionHeld(
                    RuntimeOmsiHostInputAction.Clutch)
                    ? 1.0f
                    : 0.0f));

        runtime.SetLocal(
            "Velocity",
            _vehicle.SpeedKph);

        runtime.SetLocal(
            "Velocity_Ground",
            _vehicle.SpeedKph);

        runtime.SetLocal(
            "kmcounter_km",
            Math.Floor(
                _odometerMeters /
                1_000.0));

        runtime.SetLocal(
            "kmcounter_m",
            _odometerMeters %
                1_000.0);

        runtime.SetLocal(
            "A_Trans_X",
            _vehicle.LateralAccelerationMetersPerSecondSquared);

        runtime.SetLocal(
            "A_Trans_Y",
            _vehicle.LongitudinalAccelerationMetersPerSecondSquared);

        runtime.SetLocal(
            "A_Trans_Z",
            _vehicle.VerticalAccelerationMetersPerSecondSquared);

        foreach (var sharedVariable in
                 new[]
                 {
                     "engine_on",
                     "engine_injection_on",
                     "engine_n",
                     "engine_M",
                     "elec_busbar_main",
                     "elec_busbar_main_sw",
                     "elec_bus_main",
                     "Snd_OutsideVol"
                 })
        {
            InheritLeadScriptValueWhenPassive(
                runtime,
                sharedVariable);
        }

        var globalAxleStart =
            ResolveSectionOmsiAxleStartIndex(
                section.Index);

        var localAxleCount =
            Math.Max(
                section.Physics?.Axles?.Count ??
                0,
                1);

        var rpmSum =
            0.0;
        var rpmSamples =
            0;

        for (var axle = 0;
             axle < 8;
             axle++)
        {
            var globalAxle =
                axle <
                    localAxleCount
                    ? globalAxleStart +
                      axle
                    : -1;

            var leftRotation =
                0.0f;
            var rightRotation =
                0.0f;
            var leftRpm =
                0.0f;
            var rightRpm =
                0.0f;

            var hasWheel =
                globalAxle >=
                    0 &&
                _vehicle.TryGetOmsiWheelKinematics(
                    globalAxle,
                    out leftRotation,
                    out rightRotation,
                    out leftRpm,
                    out rightRpm);

            runtime.SetLocal(
                $"Wheel_Rotation_{axle}_L",
                hasWheel
                    ? leftRotation
                    : 0.0);

            runtime.SetLocal(
                $"Wheel_Rotation_{axle}_R",
                hasWheel
                    ? rightRotation
                    : 0.0);

            runtime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_L",
                hasWheel
                    ? leftRpm
                    : 0.0);

            runtime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_R",
                hasWheel
                    ? rightRpm
                    : 0.0);

            if (hasWheel)
            {
                rpmSum +=
                    (leftRpm +
                     rightRpm) *
                    0.5;

                rpmSamples++;
            }

            var leftSuspension =
                0.0f;
            var rightSuspension =
                0.0f;

            var hasSuspension =
                globalAxle >=
                    0 &&
                _vehicle.TryGetOmsiAxleSuspension(
                    globalAxle,
                    out leftSuspension,
                    out rightSuspension);

            runtime.SetLocal(
                $"Axle_Suspension_{axle}_L",
                hasSuspension
                    ? leftSuspension
                    : 0.0);

            runtime.SetLocal(
                $"Axle_Suspension_{axle}_R",
                hasSuspension
                    ? rightSuspension
                    : 0.0);

            runtime.SetLocal(
                $"Axle_Steering_{axle}_L",
                0.0);

            runtime.SetLocal(
                $"Axle_Steering_{axle}_R",
                0.0);
        }

        runtime.SetLocal(
            "n_Wheel",
            rpmSamples >
                0
                ? rpmSum /
                  rpmSamples
                : _vehicle.WheelRotationSpeedRpm);

        if (_vehicle.TryGetOdeArticulatedSectionState(
                section.Index,
                out _,
                out var relativeYaw,
                out var relativeYawRate,
                out var relativePitch,
                out var relativePitchRate))
        {
            runtime.SetLocal(
                "articulation_0_alpha",
                relativeYaw);

            runtime.SetLocal(
                "articulation_0_alpha_vel",
                relativeYawRate);

            runtime.SetLocal(
                "articulation_0_beta",
                relativePitch);

            runtime.SetLocal(
                "articulation_0_beta_vel",
                relativePitchRate);
        }
    }

    private void UpdateScriptHostVariables(
        double deltaSeconds,
        double absoluteSeconds)
    {
        if (_scriptRuntime is null)
        {
            return;
        }

        _scriptRuntime.SetSystem(
            "Timegap",
            deltaSeconds);
        _scriptRuntime.SetSystem(
            "GetTime",
            absoluteSeconds);

        var now =
            DateTime.Now;

        _scriptRuntime.SetSystem(
            "Time",
            now.TimeOfDay.TotalSeconds);
        _scriptRuntime.SetSystem(
            "Year",
            now.Year);
        _scriptRuntime.SetSystem(
            "Month",
            now.Month);
        _scriptRuntime.SetSystem(
            "Day",
            now.Day);
        _scriptRuntime.SetSystem(
            "DayOfYear",
            now.DayOfYear);
        _scriptRuntime.SetSystem(
            "Pause",
            _simulationPaused
                ? 1.0
                : 0.0);
        _scriptRuntime.SetSystem(
            "NoSound",
            _masterVolume <=
                    0.0001f
                ? 1.0
                : 0.0);

        // The current bootstrap renderer is daylight-only. OMSI vehicle
        // materials use Envir_Brightness as an alpha scale for exterior
        // glass/reflection layers; leaving it at the VM default of zero
        // makes those layers disappear completely.
        _scriptRuntime.SetLocal(
            "Envir_Brightness",
            1.0);

        _scriptRuntime.SetLocal(
            "Throttle",
            _vehicle.AcceleratorLevel);
        _scriptRuntime.SetLocal(
            "Brake",
            _vehicle.BrakeLevel);
        _scriptRuntime.SetLocal(
            "Clutch",
            Math.Max(
                _controllerClutchInput,
                IsHostActionHeld(
                    RuntimeOmsiHostInputAction.Clutch)
                    ? 1.0f
                    : 0.0f));
        _scriptRuntime.SetLocal(
            "Velocity",
            _vehicle.SpeedKph);
        _scriptRuntime.SetLocal(
            "Velocity_Ground",
            _vehicle.SpeedKph);

        if (_driveMode &&
            !_simulationPaused &&
            !_vehicleRemoved &&
            deltaSeconds >
                0.0)
        {
            _odometerMeters +=
                Math.Abs(
                    _vehicle.SpeedMetersPerSecond) *
                deltaSeconds;
        }

        var odometerKilometers =
            Math.Floor(
                _odometerMeters /
                1_000.0);

        var odometerRemainderMeters =
            _odometerMeters -
            odometerKilometers *
                1_000.0;

        _scriptRuntime.SetLocal(
            "kmcounter_km",
            odometerKilometers);

        _scriptRuntime.SetLocal(
            "kmcounter_m",
            odometerRemainderMeters);

        _scriptRuntime.SetLocal(
            "n_Wheel",
            _vehicle.WheelRotationSpeedRpm);

        // OMSI vehicle coordinates are x=right, y=forward, z=up.
        _scriptRuntime.SetLocal(
            "A_Trans_X",
            _vehicle.LateralAccelerationMetersPerSecondSquared);
        _scriptRuntime.SetLocal(
            "A_Trans_Y",
            _vehicle.LongitudinalAccelerationMetersPerSecondSquared);
        _scriptRuntime.SetLocal(
            "A_Trans_Z",
            _vehicle.VerticalAccelerationMetersPerSecondSquared);

        // Keep input/physics steering independent from model animation.
        // The OMSI Axle_Steering_* variables use the same signed steering
        // direction as the model.cfg wheel animations. The previous extra
        // negation made the visible front wheels steer opposite the actual
        // vehicle path.
        var omsiSteeringLeft =
            _vehicle.FrontLeftSteeringRadians;
        var omsiSteeringRight =
            _vehicle.FrontRightSteeringRadians;

        _scriptRuntime.SetLocal(
            "Axle_Steering_0_L",
            omsiSteeringLeft);
        _scriptRuntime.SetLocal(
            "Axle_Steering_0_R",
            omsiSteeringRight);

        for (var axle = 0;
             axle < 8;
             axle++)
        {
            var hasWheelKinematics =
                _vehicle.TryGetOmsiWheelKinematics(
                    axle,
                    out var leftWheelRotation,
                    out var rightWheelRotation,
                    out var leftWheelRotationSpeedRpm,
                    out var rightWheelRotationSpeedRpm);

            _scriptRuntime.SetLocal(
                $"Wheel_Rotation_{axle}_L",
                hasWheelKinematics
                    ? leftWheelRotation
                    : 0.0f);

            _scriptRuntime.SetLocal(
                $"Wheel_Rotation_{axle}_R",
                hasWheelKinematics
                    ? rightWheelRotation
                    : 0.0f);

            _scriptRuntime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_L",
                hasWheelKinematics
                    ? leftWheelRotationSpeedRpm
                    : 0.0f);

            _scriptRuntime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_R",
                hasWheelKinematics
                    ? rightWheelRotationSpeedRpm
                    : 0.0f);
        }

        for (var axle = 0;
             axle < 8;
             axle++)
        {
            var hasSuspension =
                _vehicle.TryGetOmsiAxleSuspension(
                    axle,
                    out var leftSuspension,
                    out var rightSuspension);

            _scriptRuntime.SetLocal(
                $"Axle_Suspension_{axle}_L",
                hasSuspension
                    ? leftSuspension
                    : 0.0f);

            _scriptRuntime.SetLocal(
                $"Axle_Suspension_{axle}_R",
                hasSuspension
                    ? rightSuspension
                    : 0.0f);
        }

        // Coupled OMSI .bus models expose the articulation angle as a host
        // variable. The MEP Quadbus II bellows and its articulation.osc
        // consume articulation_0_alpha/beta directly.
        foreach (var section in
                 _windowInfo.Vehicle?.Sections ??
                 Array.Empty<RuntimeVehicleSectionInfo>())
        {
            var couplingIndex =
                Math.Max(
                    section.Index - 1,
                    0);

            var relativeYaw =
                _articulatedSectionYawRadians
                    .TryGetValue(
                        section.Index,
                        out var yaw)
                        ? yaw
                        : 0.0f;

            var alphaDegrees =
                -relativeYaw *
                180.0 /
                Math.PI;

            _scriptRuntime.SetLocal(
                $"articulation_{couplingIndex}_alpha",
                alphaDegrees);

            var relativePitch =
                _articulatedSectionPitchRadians
                    .TryGetValue(
                        section.Index,
                        out var pitch)
                        ? pitch
                        : 0.0f;

            var betaDegrees =
                relativePitch *
                180.0 /
                Math.PI;

            _scriptRuntime.SetLocal(
                $"articulation_{couplingIndex}_beta",
                betaDegrees);
        }

    }

    private void OnRuntimeKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        var firstPress =
            _pressedKeys.Add(
                e.KeyCode);

        if (!firstPress)
        {
            return;
        }

        var matchedOmsiBinding =
            !_vehiclePreviewMode &&
            DispatchOmsiKeyboardKeyDown(
                e);

        if (!matchedOmsiBinding &&
            e.Control &&
            e.Shift &&
            e.KeyCode == Keys.F9)
        {
            _reflectionRenderingEnabled =
                !_reflectionRenderingEnabled;

            UpdateCaption();
            e.SuppressKeyPress =
                true;
            return;
        }

        if (!matchedOmsiBinding &&
            e.Control &&
            e.Shift &&
            e.KeyCode == Keys.F5 &&
            _terrainGeometry.Vertices.Length >
                0)
        {
            _vehicle.Reset(
                _windowInfo.Splines,
                _terrainGeometry,
                _windowInfo.Spawn);
            ResetArticulatedSections();

            UpdateCaption();
            e.SuppressKeyPress =
                true;
            return;
        }

        if (!matchedOmsiBinding &&
            e.Control &&
            e.Shift &&
            e.KeyCode == Keys.R &&
            !_driveMode &&
            _terrainGeometry.Vertices.Length >
                0)
        {
            _camera.Reset(
                _terrainGeometry);

            e.SuppressKeyPress =
                true;
            return;
        }

        if (_omsiKeyboardBindings.Count >
            0)
        {
            if (matchedOmsiBinding)
            {
                UpdateCaption();
                e.SuppressKeyPress =
                    true;
                return;
            }

            // Preserve OMSI's standard drive keys even when a custom or
            // partially parsed keyboard.cfg omits one of them. Explicit
            // bindings always win because this path runs only when no
            // binding matched the physical key.
            if (_driveMode &&
                TryApplyOmsiDefaultDriveKeyFallback(
                    e.KeyCode))
            {
                UpdateCaption();
                e.SuppressKeyPress =
                    true;
            }

            return;
        }

        ApplyLegacyKeyboardFallback(
            e);
    }

    private bool TryApplyOmsiDefaultDriveKeyFallback(
        Keys key)
    {
        switch (key)
        {
            case Keys.E:
                if (DispatchDefaultScriptTriggerIfPresent(
                        key,
                        "cp_batterietrennschalter_toggle") ||
                    DispatchDefaultScriptTriggerIfPresent(
                        key,
                        "kw_batterietrennschalter"))
                {
                    SynchronizeHostVehicleStateFromScripts();
                    return true;
                }

                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ElectricalToggle);
                return true;

            case Keys.N:
                if (!DispatchDefaultScriptTriggerIfPresent(
                        key,
                        "automatic_N"))
                {
                    ApplyOmsiHostActionPress(
                        RuntimeOmsiHostInputAction.GearNeutral);
                }
                else
                {
                    SynchronizeHostVehicleStateFromScripts();
                }

                return true;

            case Keys.D:
                if (!DispatchDefaultScriptTriggerIfPresent(
                        key,
                        "automatic_D"))
                {
                    ApplyOmsiHostActionPress(
                        RuntimeOmsiHostInputAction.GearDrive);
                }
                else
                {
                    SynchronizeHostVehicleStateFromScripts();
                }

                return true;

            case Keys.R:
                if (!DispatchDefaultScriptTriggerIfPresent(
                        key,
                        "automatic_R"))
                {
                    ApplyOmsiHostActionPress(
                        RuntimeOmsiHostInputAction.GearReverse);
                }
                else
                {
                    SynchronizeHostVehicleStateFromScripts();
                }

                return true;

            case Keys.M:
                if (DispatchFirstDefaultScriptTriggerIfPresent(
                        key,
                        "kw_m_enginestart",
                        "kw_m_engine_startbutton"))
                {
                    SynchronizeHostVehicleStateFromScripts();
                }
                else
                {
                    _vehicle.ToggleEngine();
                }

                return true;

            default:
                return false;
        }
    }

    private bool DispatchFirstDefaultScriptTriggerIfPresent(
        Keys key,
        params string[] triggers)
    {
        foreach (var trigger in
                 triggers)
        {
            if (DispatchDefaultScriptTriggerIfPresent(
                    key,
                    trigger))
            {
                return true;
            }
        }

        return false;
    }

    private bool DispatchDefaultScriptTriggerIfPresent(
        Keys key,
        string trigger)
    {
        if (!HasOmsiScriptTrigger(
                trigger))
        {
            return false;
        }

        DispatchOmsiScriptTrigger(
            trigger);

        _fallbackOmsiPressTriggers[
            key] =
            trigger;

        return true;
    }

    private void ApplyLegacyKeyboardFallback(
        KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1 &&
            _windowInfo.Vehicle is not null)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.DriverView);
            e.SuppressKeyPress =
                true;
            return;
        }

        if (e.KeyCode == Keys.F2 &&
            _windowInfo.Vehicle is not null)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.PassengerView);
            e.SuppressKeyPress =
                true;
            return;
        }

        if (e.KeyCode == Keys.F3 &&
            _windowInfo.Vehicle is not null)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.ExteriorView);
            e.SuppressKeyPress =
                true;
            return;
        }

        if (e.KeyCode == Keys.F4)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.FreeCameraView);
            e.SuppressKeyPress =
                true;
            return;
        }

        if (e.KeyCode == Keys.Insert)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.ScheduleView);
            e.SuppressKeyPress =
                true;
            return;
        }

        if (e.KeyCode == Keys.Home)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.TicketSellingView);
            e.SuppressKeyPress =
                true;
            return;
        }

        if (e.KeyCode == Keys.S)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.ScrollViews);
            return;
        }

        if (e.KeyCode == Keys.P)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.PauseToggle);
            return;
        }

        if (e.KeyCode == Keys.C)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.ResetCurrentView);
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.ResetAllViews);
            return;
        }

        if (e.Shift &&
            e.KeyCode == Keys.Z)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.StatusInfoCycle);
            return;
        }

        if (!_driveMode)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.InteriorViewNext);
                break;

            case Keys.Right:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.InteriorViewPrevious);
                break;

            case Keys.O:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.MouseDriveToggle);
                break;

            case Keys.E:
            case Keys.M:
            case Keys.D:
            case Keys.N:
            case Keys.R:
                TryApplyOmsiDefaultDriveKeyFallback(
                    e.KeyCode);
                break;

            case Keys.Decimal:
            case Keys.OemPeriod:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ParkingBrakeToggle);
                break;

            case Keys.Subtract:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.StopBrakeToggle);
                break;

            case Keys.K:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ControllerToggle);
                break;
        }

        UpdateCaption();
    }

    private void CycleOmsiMainView()
    {
        if (_windowInfo.Vehicle is null)
        {
            if (_driveMode)
            {
                _driveMode =
                    false;
            }

            return;
        }

        if (!_driveMode)
        {
            ApplyOmsiHostActionPress(
                RuntimeOmsiHostInputAction.DriverView);
            return;
        }

        switch (_vehicleViewMode)
        {
            case RuntimeVehicleViewMode.Driver:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.PassengerView);
                break;

            case RuntimeVehicleViewMode.Passenger:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ExteriorView);
                break;

            case RuntimeVehicleViewMode.Exterior:
            default:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.FreeCameraView);
                break;
        }
    }

    private void ActivateSpecialDriverCamera(
        int? cameraIndex)
    {
        var vehicle =
            _windowInfo.Vehicle;

        if (vehicle is null ||
            vehicle.DriverCameras.Count == 0)
        {
            return;
        }

        if (!_specialViewActive)
        {
            _specialViewActive =
                true;
            _specialPreviousDriveMode =
                _driveMode;
            _specialPreviousViewMode =
                _vehicleViewMode;
            _specialPreviousDriverCameraIndex =
                _driverCameraIndex;
            _specialPreviousPassengerCameraIndex =
                _passengerCameraIndex;
        }

        _driveMode = true;
        _vehicleViewMode =
            RuntimeVehicleViewMode.Driver;
        _driverCameraIndex =
            Math.Clamp(
                cameraIndex ??
                    vehicle.StandardDriverCameraIndex,
                0,
                vehicle.DriverCameras.Count - 1);
    }

    private void ReleaseSpecialDriverCamera()
    {
        if (!_specialViewActive)
        {
            return;
        }

        _specialViewActive =
            false;
        _driveMode =
            _specialPreviousDriveMode;
        _vehicleViewMode =
            _specialPreviousViewMode;
        _driverCameraIndex =
            _specialPreviousDriverCameraIndex;
        _passengerCameraIndex =
            _specialPreviousPassengerCameraIndex;
    }

    private void ResetCurrentOmsiView()
    {
        var vehicle =
            _windowInfo.Vehicle;

        if (!_driveMode)
        {
            if (_terrainGeometry.Vertices.Length >
                0)
            {
                _camera.Reset(
                    _terrainGeometry);
            }

            return;
        }

        if (vehicle is null)
        {
            return;
        }

        switch (_vehicleViewMode)
        {
            case RuntimeVehicleViewMode.Driver:
                _driverCameraIndex =
                    Math.Clamp(
                        vehicle.StandardDriverCameraIndex,
                        0,
                        Math.Max(
                            vehicle.DriverCameras.Count - 1,
                            0));
                _interiorCameraYawOffsetRadians =
                    0.0f;
                _interiorCameraPitchOffsetRadians =
                    0.0f;
                _interiorCameraFieldOfViewScale =
                    1.0f;
                break;

            case RuntimeVehicleViewMode.Passenger:
                _passengerCameraIndex =
                    0;
                _interiorCameraYawOffsetRadians =
                    0.0f;
                _interiorCameraPitchOffsetRadians =
                    0.0f;
                _interiorCameraFieldOfViewScale =
                    1.0f;
                break;

            case RuntimeVehicleViewMode.Exterior:
                _exteriorCameraYawOffsetRadians =
                    0.0f;
                _exteriorCameraPitchOffsetRadians =
                    0.0f;
                _exteriorCameraDistanceScale =
                    1.0f;
                break;
        }
    }

    private void ResetAllOmsiViews()
    {
        var vehicle =
            _windowInfo.Vehicle;

        if (vehicle is not null)
        {
            _driverCameraIndex =
                Math.Clamp(
                    vehicle.StandardDriverCameraIndex,
                    0,
                    Math.Max(
                        vehicle.DriverCameras.Count - 1,
                        0));
            _passengerCameraIndex =
                0;
        }

        _interiorCameraYawOffsetRadians =
            0.0f;
        _interiorCameraPitchOffsetRadians =
            0.0f;
        _interiorCameraFieldOfViewScale =
            1.0f;
        _exteriorCameraYawOffsetRadians =
            0.0f;
        _exteriorCameraPitchOffsetRadians =
            0.0f;
        _exteriorCameraDistanceScale =
            1.0f;

        if (_terrainGeometry.Vertices.Length >
            0)
        {
            _camera.Reset(
                _terrainGeometry);
        }

        _specialViewActive =
            false;
    }

    private void CycleInteriorCamera(
        int direction)
    {
        var vehicle =
            _windowInfo.Vehicle;

        if (vehicle is null)
        {
            return;
        }

        if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Driver &&
            vehicle.DriverCameras.Count > 0)
        {
            _driverCameraIndex =
                WrapCameraIndex(
                    _driverCameraIndex + direction,
                    vehicle.DriverCameras.Count);
            return;
        }

        if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Passenger &&
            vehicle.PassengerCameras.Count > 0)
        {
            _passengerCameraIndex =
                WrapCameraIndex(
                    _passengerCameraIndex + direction,
                    vehicle.PassengerCameras.Count);
        }
    }

    private static float DegreesToRadians(
        double degrees) =>
        (float)(
            degrees *
            Math.PI /
            180.0);

    private static int WrapCameraIndex(
        int index,
        int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        index %= count;
        return index < 0
            ? index + count
            : index;
    }

    private void OnRuntimeKeyUp(
        object? sender,
        KeyEventArgs e)
    {
        _pressedKeys.Remove(
            e.KeyCode);

        var released =
            _activeOmsiPressedBindings
                .Where(
                    binding =>
                        binding.Key ==
                        e.KeyCode)
                .ToArray();

        if (_fallbackOmsiPressTriggers.Remove(
                e.KeyCode,
                out var fallbackTrigger))
        {
            DispatchOmsiScriptTrigger(
                ReleaseTriggerName(
                    fallbackTrigger));
        }

        foreach (var binding in
                 released)
        {
            DispatchOmsiScriptTrigger(
                ReleaseTriggerName(
                    binding.Trigger));

            if (binding.HostAction is
                { } hostAction)
            {
                ApplyOmsiHostActionRelease(
                    hostAction);
            }

            _activeOmsiPressedBindings.Remove(
                binding);

            _activeOmsiContinuousBindings.Remove(
                binding);
        }

        if (_omsiKeyboardBindings.Count == 0 &&
            e.KeyCode is Keys.Insert or Keys.Home)
        {
            ReleaseSpecialDriverCamera();
        }
    }

    private void ApplyOmsiHostActionRelease(
        RuntimeOmsiHostInputAction action)
    {
        if (action is
            RuntimeOmsiHostInputAction.ScheduleView or
            RuntimeOmsiHostInputAction.TicketSellingView)
        {
            ReleaseSpecialDriverCamera();
        }
    }

    private bool HasOmsiScriptTrigger(
        int sectionIndex,
        string trigger)
    {
        if (string.IsNullOrWhiteSpace(
                trigger))
        {
            return false;
        }

        if (sectionIndex >
                0 &&
            _sectionScriptRuntimes.TryGetValue(
                sectionIndex,
                out var sectionRuntime))
        {
            return sectionRuntime.HasTrigger(
                trigger);
        }

        return _scriptRuntime?.HasTrigger(
                   trigger) ==
               true;
    }

    private void SetOmsiMouseSystemVariables(
        int sectionIndex,
        float mouseX,
        float mouseY)
    {
        var runtime =
            sectionIndex >
                    0 &&
                _sectionScriptRuntimes.TryGetValue(
                    sectionIndex,
                    out var sectionRuntime)
                ? sectionRuntime
                : _scriptRuntime;

        if (runtime is null)
        {
            return;
        }

        // OMSI documents mouse_x/mouse_y as pixel-valued system variables.
        // In mouseevent *_drag scripts they are consumed as incremental
        // movement (e.g. mouse_x / -400 added to a door accumulator).
        // Accumulate WinForms motion between frames and consume it once,
        // reproducing the effective relative-drag behavior without making
        // controls jump to a screen-coordinate-dependent limit.
        runtime.SetSystem(
            "mouse_x",
            mouseX);

        runtime.SetSystem(
            "mouse_y",
            mouseY);
    }

    private void DispatchOmsiSectionScriptTrigger(
        int sectionIndex,
        string trigger)
    {
        if (string.IsNullOrWhiteSpace(
                trigger))
        {
            return;
        }

        var traceStartup =
            IsVehicleStartupTraceTrigger(
                trigger);

        var before =
            traceStartup
                ? DescribeVehicleStartupScriptState()
                : null;

        if (sectionIndex >
                0 &&
            _sectionScriptRuntimes.TryGetValue(
                sectionIndex,
                out var sectionRuntime))
        {
            sectionRuntime.ExecuteTrigger(
                trigger);
        }
        else
        {
            _scriptRuntime?.ExecuteTrigger(
                trigger);
        }

        if (traceStartup)
        {
            AppendVehicleStartupTrace(
                trigger,
                before ??
                    "<no-script-runtime>",
                DescribeVehicleStartupScriptState());
        }
    }

    private bool HasOmsiScriptTrigger(
        string trigger)
    {
        if (string.IsNullOrWhiteSpace(
                trigger))
        {
            return false;
        }

        if (_scriptRuntime?.HasTrigger(
                trigger) ==
            true)
        {
            return true;
        }

        return _sectionScriptRuntimes.Values.Any(
            runtime =>
                runtime.HasTrigger(
                    trigger));
    }

    private bool DispatchOmsiKeyboardKeyDown(
        KeyEventArgs e)
    {
        if (_omsiKeyboardBindings.Count == 0)
        {
            return false;
        }

        var matched =
            false;

        foreach (var binding in
                 _omsiKeyboardBindings)
        {
            if (binding.Key !=
                    e.KeyCode ||
                binding.Shift !=
                    e.Shift ||
                binding.Control !=
                    e.Control)
            {
                continue;
            }

            matched =
                true;

            DispatchOmsiScriptTrigger(
                binding.Trigger);

            _activeOmsiPressedBindings.Add(
                binding);

            if (binding.HostAction is
                { } hostAction &&
                !(HasOmsiScriptTrigger(
                      binding.Trigger) &&
                  IsOmsiScriptAuthoritativeAction(
                      hostAction)))
            {
                ApplyOmsiHostActionPress(
                    hostAction);
            }

            if (binding.Continuous)
            {
                _activeOmsiContinuousBindings.Add(
                    binding);
            }
        }

        return matched;
    }

    private void DispatchOmsiScriptTrigger(
        string trigger)
    {
        if (string.IsNullOrWhiteSpace(
                trigger))
        {
            return;
        }

        var traceStartup =
            IsVehicleStartupTraceTrigger(
                trigger);

        var before =
            traceStartup
                ? DescribeVehicleStartupScriptState()
                : null;

        _scriptRuntime?.ExecuteTrigger(
            trigger);

        foreach (var runtime in
                 _sectionScriptRuntimes.Values)
        {
            if (runtime.HasTrigger(
                    trigger))
            {
                runtime.ExecuteTrigger(
                    trigger);
            }
        }

        if (traceStartup)
        {
            AppendVehicleStartupTrace(
                trigger,
                before ??
                    "<no-script-runtime>",
                DescribeVehicleStartupScriptState());
        }

        TriggerOmsiAudio(
            trigger);
    }

    private static bool IsVehicleStartupTraceTrigger(
        string trigger) =>
        trigger.StartsWith(
            "kw_m_engine",
            StringComparison.OrdinalIgnoreCase) ||
        trigger.Contains(
            "batterietrennschalter",
            StringComparison.OrdinalIgnoreCase) ||
        trigger.StartsWith(
            "automatic_",
            StringComparison.OrdinalIgnoreCase);

    private string DescribeVehicleStartupScriptState()
    {
        if (_scriptRuntime is null)
        {
            return "<no-script-runtime>";
        }

        static string Value(
            OmsiScriptRuntime runtime,
            string name) =>
            runtime.HasLocalVariable(
                name)
                ? runtime.GetLocal(
                        name)
                    .ToString(
                        "0.###",
                        System.Globalization.CultureInfo.InvariantCulture)
                : "<missing>";

        var lead =
            $"lead[main={Value(_scriptRuntime, "elec_busbar_main")};" +
            $"main_sw={Value(_scriptRuntime, "elec_busbar_main_sw")};" +
            $"gear={Value(_scriptRuntime, "antrieb_getr_gangwahl")};" +
            $"pregear={Value(_scriptRuntime, "antrieb_getr_gangvorwahl")};" +
            $"engine_on={Value(_scriptRuntime, "engine_on")};" +
            $"injection={Value(_scriptRuntime, "engine_injection_on")};" +
            $"engine_n={Value(_scriptRuntime, "engine_n")};" +
            $"M_Wheel={Value(_scriptRuntime, "M_Wheel")}]";

        if (_sectionScriptRuntimes.Count == 0)
        {
            return lead;
        }

        var sectionStates =
            _sectionScriptRuntimes
                .OrderBy(static pair => pair.Key)
                .Select(pair =>
                    $"section{pair.Key}[main={Value(pair.Value, "elec_busbar_main")};" +
                    $"main_sw={Value(pair.Value, "elec_busbar_main_sw")};" +
                    $"gear={Value(pair.Value, "antrieb_getr_gangwahl")};" +
                    $"pregear={Value(pair.Value, "antrieb_getr_gangvorwahl")};" +
                    $"engine_on={Value(pair.Value, "engine_on")};" +
                    $"injection={Value(pair.Value, "engine_injection_on")};" +
                    $"engine_n={Value(pair.Value, "engine_n")};" +
                    $"M_Wheel={Value(pair.Value, "M_Wheel")};" +
                    $"writesM_Wheel={pair.Value.WritesLocalVariable("M_Wheel")}]");

        return lead + "|" +
               string.Join("|", sectionStates);
    }

    private static void AppendVehicleStartupTrace(
        string trigger,
        string before,
        string after)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-startup-trace.log"),
                $"{DateTimeOffset.Now:O} | trigger={trigger} | before={before} | after={after}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect vehicle input.
        }
    }

    private void OnScriptSoundTriggerRequested(
        string trigger)
    {
        if (_suppressVehicleInitAudio)
        {
            return;
        }

        TriggerOmsiAudio(
            trigger);
    }

    private void OnScriptFileSoundTriggerRequested(
        string trigger,
        string declaredFile)
    {
        if (_suppressVehicleInitAudio)
        {
            return;
        }

        // OMSI accepts an empty string on T.F as the configured trigger
        // sound, equivalent to T.L. Dynamic filenames remain relative to
        // the lead vehicle's sound directory.
        if (string.IsNullOrWhiteSpace(
                declaredFile))
        {
            TriggerOmsiAudio(
                trigger);
            return;
        }

        _omsiAudio?.TriggerFile(
            trigger,
            declaredFile,
            IsInteriorSoundView());
    }

    private void OnSectionScriptFileSoundTriggerRequested(
        int sectionIndex,
        string trigger,
        string declaredFile)
    {
        if (_suppressVehicleInitAudio)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                declaredFile))
        {
            TriggerSectionOmsiAudio(
                sectionIndex,
                trigger);
            return;
        }

        if (_articulatedOmsiAudio.TryGetValue(
                sectionIndex,
                out var audio))
        {
            audio.TriggerFile(
                trigger,
                declaredFile,
                IsInteriorSoundView());
        }
    }

    private void TriggerSectionOmsiAudio(
        int sectionIndex,
        string trigger)
    {
        if (_suppressVehicleInitAudio ||
            _vehicleRemoved ||
            string.IsNullOrWhiteSpace(
                trigger) ||
            !_articulatedOmsiAudio.TryGetValue(
                sectionIndex,
                out var audio))
        {
            return;
        }

        var section =
            ResolveVehicleSection(
                sectionIndex);

        if (section is null)
        {
            return;
        }

        ResolveArticulatedSectionAudioPose(
            section,
            out var sectionPosition,
            out var sectionHeading);

        audio.Trigger(
            trigger,
            ResolveScriptRuntimeForSection(
                sectionIndex),
            IsInteriorSoundView() &&
                section.OpenForSound,
            ResolveActiveCameraPosition(),
            sectionPosition,
            sectionHeading);
    }

    private void TriggerOmsiAudio(
        string trigger)
    {
        if (_vehicleRemoved ||
            string.IsNullOrWhiteSpace(
                trigger))
        {
            return;
        }

        var listenerPosition =
            ResolveActiveCameraPosition();

        _omsiAudio?.Trigger(
            trigger,
            _scriptRuntime,
            IsInteriorSoundView(),
            listenerPosition,
            _vehicle.Position,
            _vehicle.HeadingRadians);

        foreach (var pair in
                 _articulatedOmsiAudio)
        {
            var section =
                _windowInfo.Vehicle?.Sections?
                    .FirstOrDefault(
                        item =>
                            item.Index ==
                            pair.Key);

            if (section is null)
            {
                continue;
            }

            ResolveArticulatedSectionAudioPose(
                section,
                out var sectionPosition,
                out var sectionHeading);

            pair.Value.Trigger(
                trigger,
                ResolveScriptRuntimeForSection(
                    section.Index),
                IsInteriorSoundView() &&
                    section.OpenForSound,
                listenerPosition,
                sectionPosition,
                sectionHeading);
        }
    }

    private void ResolveArticulatedSectionAudioPose(
        RuntimeVehicleSectionInfo section,
        out Vector3 position,
        out float headingRadians)
    {
        var localOrigin =
            new Vector3(
                (float)section.OriginX,
                (float)section.OriginY,
                (float)section.OriginZ);

        localOrigin =
            Vector3.Transform(
                localOrigin,
                CreateArticulatedSectionMatrix(
                    section.Index));

        var sine =
            MathF.Sin(
                _vehicle.HeadingRadians);
        var cosine =
            MathF.Cos(
                _vehicle.HeadingRadians);

        position =
            _vehicle.Position +
            new Vector3(
                localOrigin.X *
                    cosine +
                localOrigin.Z *
                    sine,
                localOrigin.Y,
                -localOrigin.X *
                    sine +
                localOrigin.Z *
                    cosine);

        headingRadians =
            _articulatedSectionAbsoluteHeadingRadians
                .TryGetValue(
                    section.Index,
                    out var storedHeading)
                    ? storedHeading
                    : _vehicle.HeadingRadians;
    }

    private bool IsInteriorSoundView() =>
        _driveMode &&
        _vehicleViewMode !=
            RuntimeVehicleViewMode.Exterior;

    private void PressControllerHostAction(
        string trigger)
    {
        if (!TryResolveHostAction(
                trigger,
                out var action) ||
            !_activeControllerHostActions.Add(
                action))
        {
            return;
        }

        ApplyOmsiHostActionPress(
            action);
    }

    private void ReleaseControllerHostAction(
        string trigger)
    {
        if (!TryResolveHostAction(
                trigger,
                out var action))
        {
            return;
        }

        _activeControllerHostActions.Remove(
            action);

        ApplyOmsiHostActionRelease(
            action);
    }

    private bool TryResolveHostAction(
        string trigger,
        out RuntimeOmsiHostInputAction action)
    {
        if (_omsiHostActionsByTrigger.TryGetValue(
                trigger,
                out action))
        {
            return true;
        }

        action =
            trigger
                .Trim()
                .ToLowerInvariant() switch
            {
                "automatic_d" =>
                    RuntimeOmsiHostInputAction.GearDrive,
                "automatic_n" =>
                    RuntimeOmsiHostInputAction.GearNeutral,
                "automatic_r" =>
                    RuntimeOmsiHostInputAction.GearReverse,
                "parking_brake_toggle" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeToggle,
                "parking_brake_set" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeSet,
                "parking_brake_release" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeRelease,
                "parking_brake_mouse" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeToggle,
                "kw_batterietrennschalter" or
                "cp_batterietrennschalter_toggle" =>
                    RuntimeOmsiHostInputAction.ElectricalToggle,
                "kw_m_enginestart" or
                "kw_m_engine_startbutton" =>
                    RuntimeOmsiHostInputAction.EngineStart,
                "kw_m_engineshutdown" =>
                    RuntimeOmsiHostInputAction.EngineOff,
                "view_interiorcam_plus" =>
                    RuntimeOmsiHostInputAction.InteriorViewPrevious,
                "view_interiorcam_minus" =>
                    RuntimeOmsiHostInputAction.InteriorViewNext,
                "view_reset_direction" =>
                    RuntimeOmsiHostInputAction.ResetCurrentView,
                "view_schedule" or
                "view_set_schedule" =>
                    RuntimeOmsiHostInputAction.ScheduleView,
                "view_ticketselling" or
                "view_set_ticketselling" =>
                    RuntimeOmsiHostInputAction.TicketSellingView,
                "view_reset_all_directions" =>
                    RuntimeOmsiHostInputAction.ResetAllViews,
                "pause" =>
                    RuntimeOmsiHostInputAction.PauseToggle,
                "view_status" or
                "view_information" or
                "view_info" =>
                    RuntimeOmsiHostInputAction.StatusInfoCycle,
                _ =>
                    default
            };

        return trigger
            .Trim()
            .ToLowerInvariant() is
                "automatic_d" or
                "automatic_n" or
                "automatic_r" or
                "parking_brake_toggle" or
                "parking_brake_set" or
                "parking_brake_release" or
                "parking_brake_mouse" or
                "kw_batterietrennschalter" or
                "cp_batterietrennschalter_toggle" or
                "kw_m_enginestart" or
                "kw_m_engine_startbutton" or
                "kw_m_engineshutdown" or
                "cp_batterietrennschalter_toggle" or
                "view_interiorcam_plus" or
                "view_interiorcam_minus" or
                "view_reset_direction" or
                "view_reset_all_directions" or
                "view_schedule" or
                "view_set_schedule" or
                "view_ticketselling" or
                "view_set_ticketselling" or
                "pause" or
                "view_status" or
                "view_information" or
                "view_info";
    }

    private static bool IsOmsiScriptAuthoritativeAction(
        RuntimeOmsiHostInputAction action) =>
        action is
            RuntimeOmsiHostInputAction.ElectricalToggle or
            RuntimeOmsiHostInputAction.EngineToggle or
            RuntimeOmsiHostInputAction.EngineStart or
            RuntimeOmsiHostInputAction.EngineOff or
            RuntimeOmsiHostInputAction.GearDrive or
            RuntimeOmsiHostInputAction.GearNeutral or
            RuntimeOmsiHostInputAction.GearReverse or
            RuntimeOmsiHostInputAction.ParkingBrakeToggle or
            RuntimeOmsiHostInputAction.ParkingBrakeSet or
            RuntimeOmsiHostInputAction.ParkingBrakeRelease or
            RuntimeOmsiHostInputAction.StopBrakeToggle;

    private void ApplyOmsiHostActionPress(
        RuntimeOmsiHostInputAction action)
    {
        switch (action)
        {
            case RuntimeOmsiHostInputAction.Accelerate:
            case RuntimeOmsiHostInputAction.BrakeIncrease:
            case RuntimeOmsiHostInputAction.BrakeRelease:
            case RuntimeOmsiHostInputAction.SteerLeft:
            case RuntimeOmsiHostInputAction.SteerRight:
            case RuntimeOmsiHostInputAction.SteerCenter:
            case RuntimeOmsiHostInputAction.Clutch:
                return;

            case RuntimeOmsiHostInputAction.ElectricalToggle:
                _vehicle.ToggleElectricalSystem();
                break;

            case RuntimeOmsiHostInputAction.EngineToggle:
                if (_scriptRuntime?.HasLocalVariable(
                        "engine_on") == true)
                {
                    _vehicle.SetEngineRunning(
                        _scriptRuntime.GetLocal(
                            "engine_on") >
                        0.5);
                }
                else
                {
                    _vehicle.ToggleEngine();
                }
                break;

            case RuntimeOmsiHostInputAction.EngineStart:
            case RuntimeOmsiHostInputAction.EngineOff:
                if (_scriptRuntime?.HasLocalVariable(
                        "engine_on") == true)
                {
                    _vehicle.SetEngineRunning(
                        _scriptRuntime.GetLocal(
                            "engine_on") >
                        0.5);
                }
                else
                {
                    _vehicle.SetEngineRunning(
                        action ==
                        RuntimeOmsiHostInputAction.EngineStart);
                }
                break;

            case RuntimeOmsiHostInputAction.GearDrive:
                _vehicle.SelectGear(
                    RuntimeDriveGear.Drive);
                break;

            case RuntimeOmsiHostInputAction.GearNeutral:
                _vehicle.SelectGear(
                    RuntimeDriveGear.Neutral);
                break;

            case RuntimeOmsiHostInputAction.GearReverse:
                _vehicle.SelectGear(
                    RuntimeDriveGear.Reverse);
                break;

            case RuntimeOmsiHostInputAction.ParkingBrakeToggle:
                _vehicle.ToggleParkingBrake();
                break;

            case RuntimeOmsiHostInputAction.ParkingBrakeSet:
                _vehicle.SetParkingBrake(
                    true);
                break;

            case RuntimeOmsiHostInputAction.ParkingBrakeRelease:
                _vehicle.SetParkingBrake(
                    false);
                break;

            case RuntimeOmsiHostInputAction.StopBrakeToggle:
                _vehicle.ToggleStopBrake();
                break;

            case RuntimeOmsiHostInputAction.MouseDriveToggle:
                if (!_vehicleRemoved &&
                    _windowInfo.Vehicle is not null)
                {
                    ToggleMouseDriveMode();
                }

                break;

            case RuntimeOmsiHostInputAction.DriverView:
                if (_vehicleRemoved ||
                    _windowInfo.Vehicle is null)
                {
                    break;
                }

                _driveMode =
                    true;
                _vehicleViewMode =
                    RuntimeVehicleViewMode.Driver;
                _driverCameraIndex =
                    Math.Max(
                        _windowInfo.Vehicle
                            .StandardDriverCameraIndex,
                        0);
                break;

            case RuntimeOmsiHostInputAction.PassengerView:
                if (_vehicleRemoved ||
                    _windowInfo.Vehicle is null)
                {
                    break;
                }

                _driveMode =
                    true;
                _vehicleViewMode =
                    _windowInfo.Vehicle
                        .PassengerCameras.Count >
                    0
                        ? RuntimeVehicleViewMode.Passenger
                        : RuntimeVehicleViewMode.Driver;
                break;

            case RuntimeOmsiHostInputAction.ExteriorView:
                if (_vehicleRemoved ||
                    _windowInfo.Vehicle is null)
                {
                    break;
                }

                _driveMode =
                    true;
                _vehicleViewMode =
                    RuntimeVehicleViewMode.Exterior;
                break;

            case RuntimeOmsiHostInputAction.FreeCameraView:
                if (_windowInfo.Vehicle is not null)
                {
                    var freeCameraPosition =
                        _vehicle.GetChaseCameraPosition(
                            _windowInfo.Vehicle
                                .OutsideCameraCenter);

                    var freeCameraTarget =
                        _vehicle.Position +
                        new Vector3(
                            0.0f,
                            1.6f,
                            0.0f);

                    _camera.SetLookAt(
                        freeCameraPosition,
                        freeCameraTarget,
                        moveSpeed:
                            Math.Clamp(
                                12.0f +
                                Math.Abs(
                                    _vehicle.SpeedMetersPerSecond) *
                                2.0f,
                                8.0f,
                                80.0f));
                }

                _driveMode =
                    false;
                break;

            case RuntimeOmsiHostInputAction.ScheduleView:
                ActivateSpecialDriverCamera(
                    _windowInfo.Vehicle?
                        .ScheduleDriverCameraIndex);
                break;

            case RuntimeOmsiHostInputAction.TicketSellingView:
                ActivateSpecialDriverCamera(
                    _windowInfo.Vehicle?
                        .TicketSellingDriverCameraIndex);
                break;

            case RuntimeOmsiHostInputAction.InteriorViewNext:
                CycleInteriorCamera(
                    1);
                break;

            case RuntimeOmsiHostInputAction.InteriorViewPrevious:
                CycleInteriorCamera(
                    -1);
                break;

            case RuntimeOmsiHostInputAction.ResetDriverView:
            case RuntimeOmsiHostInputAction.ResetCurrentView:
                ResetCurrentOmsiView();
                break;

            case RuntimeOmsiHostInputAction.ScrollViews:
                CycleOmsiMainView();
                break;

            case RuntimeOmsiHostInputAction.PauseToggle:
                _simulationPaused =
                    !_simulationPaused;
                SynchronizeOmsiPauseSystemVariable();
                break;

            case RuntimeOmsiHostInputAction.ResetAllViews:
                ResetAllOmsiViews();
                break;

            case RuntimeOmsiHostInputAction.StatusInfoCycle:
                _statusInfoLevel =
                    (_statusInfoLevel + 1) %
                    4;
                break;

            case RuntimeOmsiHostInputAction.ControllerToggle:
                _controllerInputEnabled =
                    !_controllerInputEnabled;

                if (!_controllerInputEnabled)
                {
                    _activeControllerHostActions.Clear();
                    _controllerClutchInput =
                        0.0f;
                }

                break;
        }
    }

    private void SynchronizeOmsiPauseSystemVariable()
    {
        var value =
            _simulationPaused
                ? 1.0
                : 0.0;

        _scriptRuntime?.SetSystem(
            "Pause",
            value);

        foreach (var runtime in
                 _sectionScriptRuntimes.Values)
        {
            runtime.SetSystem(
                "Pause",
                value);
        }
    }

    private bool IsHostActionHeld(
        RuntimeOmsiHostInputAction action)
    {
        if (_activeOmsiPressedBindings.Any(
                binding =>
                    binding.HostAction ==
                    action) ||
            _activeControllerHostActions.Contains(
                action))
        {
            return true;
        }

        if (_omsiKeyboardBindings.Count > 0)
        {
            return false;
        }

        return action switch
        {
            RuntimeOmsiHostInputAction.Accelerate =>
                _pressedKeys.Contains(
                    Keys.NumPad8),
            RuntimeOmsiHostInputAction.BrakeIncrease =>
                _pressedKeys.Contains(
                    Keys.NumPad2),
            RuntimeOmsiHostInputAction.BrakeRelease =>
                _pressedKeys.Contains(
                    Keys.Add),
            RuntimeOmsiHostInputAction.SteerLeft =>
                _pressedKeys.Contains(
                    Keys.NumPad4),
            RuntimeOmsiHostInputAction.SteerRight =>
                _pressedKeys.Contains(
                    Keys.NumPad6),
            RuntimeOmsiHostInputAction.SteerCenter =>
                _pressedKeys.Contains(
                    Keys.NumPad5),
            RuntimeOmsiHostInputAction.Clutch =>
                _pressedKeys.Contains(
                    Keys.Tab),
            _ =>
                false
        };
    }

    private bool IsFreeCameraKeyHeld(
        Keys key) =>
        _pressedKeys.Contains(
            key) &&
        !_activeOmsiPressedBindings.Any(
            binding =>
                binding.Key ==
                key);

    private void SynchronizeOmsiScriptDynamics()
    {
        var torqueRuntime =
            ResolveOmsiWheelTorqueRuntime();

        if (torqueRuntime is null ||
            !torqueRuntime.HasLocalVariable(
                "M_Wheel"))
        {
            _vehicle.SetOmsiScriptDynamics(
                false,
                0.0,
                0.0);
            return;
        }

        var wheelTorque =
            torqueRuntime.GetLocal(
                "M_Wheel");

        var perWheelBrakeForce =
            0.0;

        var axleBrakeForces =
            new double[8];

        // OMSI numbers physical axles continuously in the coupled vehicle,
        // but a coupled .bus VM sees its own axles from zero. Prefer the
        // local section VM only when its scripts actually write that brake
        // output; otherwise retain the lead-VM continuous-index behavior.
        for (var axle = 0;
             axle < 8;
             axle++)
        {
            foreach (var side in
                     new[] { "L", "R" })
            {
                var wheelBrakeForce =
                    ReadOmsiAxleOutput(
                        axle,
                        "Axle_Brakeforce",
                        side,
                        defaultValue:
                            0.0);

                perWheelBrakeForce +=
                    wheelBrakeForce;

                axleBrakeForces[
                    axle] +=
                    wheelBrakeForce;
            }
        }

        var legacyRuntime =
            torqueRuntime.WritesLocalVariable(
                "Brakeforce")
                ? torqueRuntime
                : _scriptRuntime;

        var legacyBrakeForce =
            legacyRuntime is not null &&
            legacyRuntime.HasLocalVariable(
                "Brakeforce")
                ? Math.Max(
                    0.0,
                    legacyRuntime.GetLocal(
                        "Brakeforce"))
                : 0.0;

        var brakeForce =
            perWheelBrakeForce >
                0.001
                ? perWheelBrakeForce
                : legacyBrakeForce;

        _vehicle.SetOmsiScriptDynamics(
            true,
            wheelTorque,
            brakeForce,
            axleBrakeForces);

        var axleSpringFactorLeft =
            new double[8];

        var axleSpringFactorRight =
            new double[8];

        for (var axle = 0;
             axle < 8;
             axle++)
        {
            axleSpringFactorLeft[
                axle] =
                ReadOmsiAxleOutput(
                    axle,
                    "Axle_Springfactor",
                    "L",
                    defaultValue:
                        1.0,
                    requirePositive:
                        true);

            axleSpringFactorRight[
                axle] =
                ReadOmsiAxleOutput(
                    axle,
                    "Axle_Springfactor",
                    "R",
                    defaultValue:
                        1.0,
                    requirePositive:
                        true);
        }

        _vehicle.SetOmsiAxleSpringFactors(
            axleSpringFactorLeft,
            axleSpringFactorRight);
    }

    private OmsiScriptRuntime? ResolveOmsiWheelTorqueRuntime()
    {
        if (_vehicle.PrimaryDrivenSectionIndex >
                0 &&
            _sectionScriptRuntimes.TryGetValue(
                _vehicle.PrimaryDrivenSectionIndex,
                out var drivenSectionRuntime) &&
            drivenSectionRuntime.WritesLocalVariable(
                "M_Wheel"))
        {
            return drivenSectionRuntime;
        }

        if (_scriptRuntime?.WritesLocalVariable(
                "M_Wheel") ==
            true)
        {
            return _scriptRuntime;
        }

        foreach (var pair in
                 _sectionScriptRuntimes
                     .OrderBy(
                         static pair =>
                             pair.Key))
        {
            if (pair.Value.WritesLocalVariable(
                    "M_Wheel"))
            {
                return pair.Value;
            }
        }

        // A declared M_Wheel without a real script writer is not enough
        // to enable OMSI script dynamics. Treating a stale/default zero as
        // authoritative disables the host compatibility propulsion entirely
        // and leaves the bus unable to move. Fall back to host drivetrain
        // physics until a VM actually writes M_Wheel.
        return null;
    }

    private double ReadOmsiAxleOutput(
        int globalAxleIndex,
        string variablePrefix,
        string side,
        double defaultValue,
        bool requirePositive = false)
    {
        var leadVariable =
            $"{variablePrefix}_{globalAxleIndex}_{side}";

        if (TryResolveSectionScriptAxle(
                globalAxleIndex,
                out var sectionRuntime,
                out var localAxleIndex))
        {
            var localVariable =
                $"{variablePrefix}_{localAxleIndex}_{side}";

            if (sectionRuntime is not null &&
                sectionRuntime.WritesLocalVariable(
                    localVariable))
            {
                return NormalizeOmsiScriptOutput(
                    sectionRuntime.GetLocal(
                        localVariable),
                    defaultValue,
                    requirePositive);
            }
        }

        if (_scriptRuntime is not null &&
            _scriptRuntime.HasLocalVariable(
                leadVariable))
        {
            return NormalizeOmsiScriptOutput(
                _scriptRuntime.GetLocal(
                    leadVariable),
                defaultValue,
                requirePositive);
        }

        return defaultValue;
    }

    private bool TryResolveSectionScriptAxle(
        int globalAxleIndex,
        out OmsiScriptRuntime? sectionRuntime,
        out int localAxleIndex)
    {
        sectionRuntime =
            null;
        localAxleIndex =
            -1;

        var leadAxleCount =
            Math.Max(
                _windowInfo.Vehicle?.Physics?.Axles?.Count ??
                0,
                2);

        if (globalAxleIndex <
            leadAxleCount)
        {
            return false;
        }

        var start =
            leadAxleCount;

        foreach (var section in
                 _windowInfo.Vehicle?.Sections?
                     .OrderBy(
                         static item =>
                             item.Index) ??
                 Enumerable.Empty<RuntimeVehicleSectionInfo>())
        {
            var count =
                Math.Max(
                    section.Physics?.Axles?.Count ??
                    0,
                    1);

            if (globalAxleIndex >=
                    start &&
                globalAxleIndex <
                    start +
                    count)
            {
                localAxleIndex =
                    globalAxleIndex -
                    start;

                _sectionScriptRuntimes.TryGetValue(
                    section.Index,
                    out sectionRuntime);

                return true;
            }

            start +=
                count;
        }

        return false;
    }

    private static double NormalizeOmsiScriptOutput(
        double value,
        double defaultValue,
        bool requirePositive)
    {
        if (!double.IsFinite(
                value))
        {
            return defaultValue;
        }

        if (requirePositive &&
            value <=
                0.0)
        {
            return defaultValue;
        }

        return Math.Max(
            value,
            requirePositive
                ? double.Epsilon
                : 0.0);
    }

    private void SynchronizeHostVehicleStateFromScripts()
    {
        if (_scriptRuntime is null)
        {
            return;
        }

        if (_scriptRuntime.WritesLocalVariable(
                "elec_busbar_main"))
        {
            _vehicle.SetElectricalSystemEnabled(
                _scriptRuntime.GetLocal(
                    "elec_busbar_main") >
                0.01);
        }
        else if (_scriptRuntime.WritesLocalVariable(
                     "elec_busbar_main_sw"))
        {
            _vehicle.SetElectricalSystemEnabled(
                _scriptRuntime.GetLocal(
                    "elec_busbar_main_sw") >
                0.5);
        }

        if (_scriptRuntime.WritesLocalVariable(
                "engine_on"))
        {
            _vehicle.SetEngineRunning(
                _scriptRuntime.GetLocal(
                    "engine_on") >
                0.5);
        }

        if (_scriptRuntime.WritesLocalVariable(
                "bremse_feststell"))
        {
            _vehicle.SetParkingBrake(
                _scriptRuntime.GetLocal(
                    "bremse_feststell") >
                0.5);
        }

        if (_scriptRuntime.WritesLocalVariable(
                "antrieb_getr_gangwahl"))
        {
            var selector =
                _scriptRuntime.GetLocal(
                    "antrieb_getr_gangwahl");

            _vehicle.SelectGear(
                selector <
                    0.5
                    ? RuntimeDriveGear.Reverse
                    : selector <
                        1.5
                        ? RuntimeDriveGear.Neutral
                        : RuntimeDriveGear.Drive);
        }
    }

    private static string ReleaseTriggerName(
        string trigger) =>
        trigger.EndsWith(
            "_off",
            StringComparison.OrdinalIgnoreCase)
            ? trigger
            : trigger +
              "_off";

    private bool TryDispatchVehicleMouseEvent(
        System.Drawing.Point location)
    {
        if ((_scriptRuntime is null &&
             _sectionScriptRuntimes.Count ==
                 0) ||
            !_driveMode ||
            _vehicleViewMode !=
                RuntimeVehicleViewMode.Driver ||
            ClientSize.Width <= 0 ||
            ClientSize.Height <= 0)
        {
            return false;
        }

        var geometry =
            _vehicleInteriorGeometry.Vertices.Length > 0
                ? _vehicleInteriorGeometry
                : _vehicleExteriorGeometry;

        if (geometry.Vertices.Length == 0 ||
            geometry.Batches.Count == 0)
        {
            return false;
        }

        var viewProjection =
            CreateViewProjection();
        var vehicleWorld =
            _vehicle.CreateWorldMatrix();
        var mouse =
            new Vector2(
                location.X,
                location.Y);

        var bestDepth =
            float.MaxValue;
        string? bestTrigger =
            null;
        var bestSectionIndex =
            0;

        foreach (var batch in
                 geometry.Batches)
        {
            if (string.IsNullOrWhiteSpace(
                    batch.MouseEventTrigger) ||
                !IsVehicleBatchVisible(
                    batch) ||
                batch.VertexCount <
                    3)
            {
                continue;
            }

            var world =
                CreateVehicleAnimationMatrix(
                    batch) *
                CreateArticulatedSectionMatrix(
                    batch.SectionIndex) *
                vehicleWorld;

            var skin =
                ResolveVehicleSkinConstants(
                    batch);

            var startVertex =
                checked(
                    (int)batch.StartVertex);
            var endVertex =
                Math.Min(
                    checked(
                        (int)(
                            batch.StartVertex +
                            batch.VertexCount)),
                    geometry.Vertices.Length);

            for (var vertex = startVertex;
                 vertex + 2 < endVertex;
                 vertex += 3)
            {
                if (!TryProjectVehiclePoint(
                        Vector3.Transform(
                            ResolveVehiclePickingPosition(
                                geometry.Vertices[
                                    vertex],
                                skin),
                            world),
                        viewProjection,
                        out var a,
                        out var depthA) ||
                    !TryProjectVehiclePoint(
                        Vector3.Transform(
                            ResolveVehiclePickingPosition(
                                geometry.Vertices[
                                    vertex + 1],
                                skin),
                            world),
                        viewProjection,
                        out var b,
                        out var depthB) ||
                    !TryProjectVehiclePoint(
                        Vector3.Transform(
                            ResolveVehiclePickingPosition(
                                geometry.Vertices[
                                    vertex + 2],
                                skin),
                            world),
                        viewProjection,
                        out var c,
                        out var depthC))
                {
                    continue;
                }

                if (!TryGetScreenTriangleDepth(
                        mouse,
                        a,
                        b,
                        c,
                        depthA,
                        depthB,
                        depthC,
                        out var depth))
                {
                    continue;
                }

                if (depth <
                    bestDepth)
                {
                    bestDepth =
                        depth;
                    bestTrigger =
                        batch.MouseEventTrigger;
                    bestSectionIndex =
                        batch.SectionIndex;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(
                bestTrigger))
        {
            return false;
        }

        _activeVehicleMouseTrigger =
            bestTrigger;
        _activeVehicleMouseSectionIndex =
            bestSectionIndex;
        _vehicleMouseDeltaX =
            0.0f;
        _vehicleMouseDeltaY =
            0.0f;
        _lastMousePosition =
            location;
        Capture =
            true;

        SetOmsiMouseSystemVariables(
            bestSectionIndex,
            0.0f,
            0.0f);

        var scriptOwnsTrigger =
            HasOmsiScriptTrigger(
                bestSectionIndex,
                bestTrigger);

        DispatchOmsiSectionScriptTrigger(
            bestSectionIndex,
            bestTrigger);

        if (TryResolveHostAction(
                bestTrigger,
                out var hostAction) &&
            !(scriptOwnsTrigger &&
              IsOmsiScriptAuthoritativeAction(
                  hostAction)))
        {
            ApplyOmsiHostActionPress(
                hostAction);
        }

        Console.WriteLine(
            $"[cockpit-click] section={bestSectionIndex}; trigger={bestTrigger}; x={location.X}; y={location.Y}");

        return true;
    }

    private static Vector3 ResolveVehiclePickingPosition(
        RuntimeObjectVertex vertex,
        RuntimeVehicleSkinConstants skin)
    {
        var weights =
            new Vector4(
                Math.Max(
                    vertex.SkinWeights.X,
                    0.0f),
                Math.Max(
                    vertex.SkinWeights.Y,
                    0.0f),
                Math.Max(
                    vertex.SkinWeights.Z,
                    0.0f),
                Math.Max(
                    vertex.SkinWeights.W,
                    0.0f));

        var skinSum =
            Math.Clamp(
                weights.X +
                weights.Y +
                weights.Z +
                weights.W,
                0.0f,
                1.0f);

        var result =
            vertex.Position *
            (1.0f -
             skinSum);

        if (weights.X >
            0.0f)
        {
            result +=
                Vector3.Transform(
                    vertex.Position,
                    skin.Bone0) *
                weights.X;
        }

        if (weights.Y >
            0.0f)
        {
            result +=
                Vector3.Transform(
                    vertex.Position,
                    skin.Bone1) *
                weights.Y;
        }

        if (weights.Z >
            0.0f)
        {
            result +=
                Vector3.Transform(
                    vertex.Position,
                    skin.Bone2) *
                weights.Z;
        }

        if (weights.W >
            0.0f)
        {
            result +=
                Vector3.Transform(
                    vertex.Position,
                    skin.Bone3) *
                weights.W;
        }

        return result;
    }

    private bool TryProjectVehiclePoint(
        Vector3 worldPosition,
        Matrix4x4 viewProjection,
        out Vector2 screen,
        out float depth)
    {
        screen =
            default;
        depth =
            0.0f;

        var clip =
            Vector4.Transform(
                new Vector4(
                    worldPosition,
                    1.0f),
                viewProjection);

        if (clip.W <=
            0.000001f)
        {
            return false;
        }

        var inverseW =
            1.0f /
            clip.W;
        var ndcX =
            clip.X *
            inverseW;
        var ndcY =
            clip.Y *
            inverseW;
        var ndcZ =
            clip.Z *
            inverseW;

        if (!float.IsFinite(
                ndcX) ||
            !float.IsFinite(
                ndcY) ||
            !float.IsFinite(
                ndcZ) ||
            ndcZ <
                0.0f ||
            ndcZ >
                1.0f)
        {
            return false;
        }

        screen =
            new Vector2(
                (ndcX + 1.0f) *
                    0.5f *
                    ClientSize.Width,
                (1.0f - ndcY) *
                    0.5f *
                    ClientSize.Height);
        depth =
            ndcZ;

        return true;
    }

    private static bool TryGetScreenTriangleDepth(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        float depthA,
        float depthB,
        float depthC,
        out float depth)
    {
        var v0 =
            c - a;
        var v1 =
            b - a;
        var v2 =
            point - a;

        var dot00 =
            Vector2.Dot(
                v0,
                v0);
        var dot01 =
            Vector2.Dot(
                v0,
                v1);
        var dot02 =
            Vector2.Dot(
                v0,
                v2);
        var dot11 =
            Vector2.Dot(
                v1,
                v1);
        var dot12 =
            Vector2.Dot(
                v1,
                v2);

        var denominator =
            dot00 *
                dot11 -
            dot01 *
                dot01;

        if (Math.Abs(
                denominator) <
            0.000001f)
        {
            depth =
                float.MaxValue;
            return false;
        }

        var inverseDenominator =
            1.0f /
            denominator;
        var u =
            (dot11 *
                 dot02 -
             dot01 *
                 dot12) *
            inverseDenominator;
        var v =
            (dot00 *
                 dot12 -
             dot01 *
                 dot02) *
            inverseDenominator;

        const float edgeTolerance =
            0.015f;

        if (u <
                -edgeTolerance ||
            v <
                -edgeTolerance ||
            u + v >
                1.0f +
                edgeTolerance)
        {
            depth =
                float.MaxValue;
            return false;
        }

        var aWeight =
            1.0f -
            u -
            v;

        depth =
            depthA *
                aWeight +
            depthB *
                v +
            depthC *
                u;

        return float.IsFinite(
            depth);
    }

    private void OnRuntimeMouseDown(
        object? sender,
        MouseEventArgs e)
    {
        if (!_vehiclePreviewMode &&
            _omsiMenuBar?.Visible ==
                true)
        {
            _omsiMenuBar.HideMenu();
        }

        if (_vehiclePreviewMode)
        {
            if (e.Button is
                MouseButtons.Left or
                MouseButtons.Right)
            {
                _mouseLooking = true;
                _lastMousePosition =
                    e.Location;
                Capture = true;
            }

            return;
        }

        if (e.Button ==
                MouseButtons.Left &&
            TryDispatchVehicleMouseEvent(
                e.Location))
        {
            return;
        }

        if (e.Button is not
                (MouseButtons.Right or
                 MouseButtons.Middle))
        {
            return;
        }

        _mouseLooking = true;
        _freeCameraDragButton =
            e.Button;
        _lastMousePosition =
            e.Location;
        Capture = true;

        if (_mouseDriveMode)
        {
            // O remains latched. RMB/MMB only borrows the pointer while
            // held so the driver can look around without leaving mouse
            // steering mode.
            Cursor =
                Cursors.Default;
        }
    }

    private void OnRuntimeMouseUp(
        object? sender,
        MouseEventArgs e)
    {
        if (_vehiclePreviewMode)
        {
            if (e.Button is
                MouseButtons.Left or
                MouseButtons.Right)
            {
                _mouseLooking = false;
                Capture = false;
            }

            return;
        }

        if (e.Button ==
                MouseButtons.Left &&
            !string.IsNullOrWhiteSpace(
                _activeVehicleMouseTrigger))
        {
            var releasedTrigger =
                _activeVehicleMouseTrigger;

            DispatchOmsiSectionScriptTrigger(
                _activeVehicleMouseSectionIndex,
                ReleaseTriggerName(
                    releasedTrigger));

            if (TryResolveHostAction(
                    releasedTrigger,
                    out var releasedHostAction))
            {
                ApplyOmsiHostActionRelease(
                    releasedHostAction);
            }

            SetOmsiMouseSystemVariables(
                _activeVehicleMouseSectionIndex,
                0.0f,
                0.0f);

            _activeVehicleMouseTrigger =
                null;
            _activeVehicleMouseSectionIndex =
                0;
            _vehicleMouseDeltaX =
                0.0f;
            _vehicleMouseDeltaY =
                0.0f;

            if (_mouseDriveMode)
            {
                Capture =
                    true;
                Cursor =
                    Cursors.Cross;

                var center =
                    new System.Drawing.Point(
                        ClientSize.Width / 2,
                        ClientSize.Height / 2);

                _lastMousePosition =
                    center;

                Cursor.Position =
                    PointToScreen(
                        center);
            }
            else
            {
                Capture =
                    false;
            }

            return;
        }

        if (e.Button !=
                _freeCameraDragButton)
        {
            return;
        }

        _mouseLooking = false;
        _freeCameraDragButton =
            MouseButtons.None;

        if (_mouseDriveMode)
        {
            Capture =
                true;
            Cursor =
                Cursors.Cross;

            var center =
                new System.Drawing.Point(
                    ClientSize.Width / 2,
                    ClientSize.Height / 2);

            _lastMousePosition =
                center;
            Cursor.Position =
                PointToScreen(
                    center);
        }
        else
        {
            Capture =
                false;
        }
    }

    private void OnRuntimeMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (_vehiclePreviewMode)
        {
            if (!_mouseLooking)
            {
                return;
            }

            var previewDeltaX =
                e.X - _lastMousePosition.X;
            var previewDeltaY =
                e.Y - _lastMousePosition.Y;

            _lastMousePosition =
                e.Location;

            _previewYaw +=
                previewDeltaX * 0.008f;
            _previewPitch =
                Math.Clamp(
                    _previewPitch -
                    previewDeltaY * 0.006f,
                    -0.35f,
                    0.75f);

            return;
        }

        if (!string.IsNullOrWhiteSpace(
                _activeVehicleMouseTrigger))
        {
            var controlDeltaX =
                e.X -
                _lastMousePosition.X;

            var controlDeltaY =
                e.Y -
                _lastMousePosition.Y;

            _lastMousePosition =
                e.Location;

            _vehicleMouseDeltaX +=
                controlDeltaX;

            _vehicleMouseDeltaY +=
                controlDeltaY;

            return;
        }

        if (_driveMode &&
            _mouseDriveMode &&
            !_mouseLooking)
        {
            UpdateOmsiMouseAxes(e.Location);
            return;
        }

        if (!_mouseLooking)
        {
            return;
        }

        var deltaX =
            e.X - _lastMousePosition.X;
        var deltaY =
            e.Y - _lastMousePosition.Y;

        _lastMousePosition = e.Location;

        if (_driveMode)
        {
            const float vehicleCameraSensitivity =
                0.0045f;

            if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Exterior)
            {
                _exteriorCameraYawOffsetRadians =
                    NormalizeRadians(
                        _exteriorCameraYawOffsetRadians +
                        deltaX *
                        vehicleCameraSensitivity);

                _exteriorCameraPitchOffsetRadians =
                    Math.Clamp(
                        _exteriorCameraPitchOffsetRadians -
                        deltaY *
                        vehicleCameraSensitivity,
                        -1.1f,
                        1.0f);
            }
            else
            {
                _interiorCameraYawOffsetRadians =
                    Math.Clamp(
                        _interiorCameraYawOffsetRadians +
                        deltaX *
                        vehicleCameraSensitivity,
                        -3.0f,
                        3.0f);

                _interiorCameraPitchOffsetRadians =
                    Math.Clamp(
                        _interiorCameraPitchOffsetRadians -
                        deltaY *
                        vehicleCameraSensitivity,
                        -1.35f,
                        1.35f);
            }

            return;
        }

        if (_freeCameraDragButton ==
            MouseButtons.Middle)
        {
            _camera.Pan(
                deltaX,
                deltaY,
                ClientSize.Height);
        }
        else
        {
            _camera.Rotate(
                deltaX,
                deltaY);
        }
    }

    private void OnRuntimeMouseWheel(
        object? sender,
        MouseEventArgs e)
    {
        if (_vehiclePreviewMode)
        {
            var steps =
                e.Delta / 120.0f;

            _previewDistance =
                Math.Clamp(
                    _previewDistance *
                    MathF.Pow(
                        0.90f,
                        steps),
                    Math.Max(
                        _previewRadius * 1.15f,
                        1.5f),
                    Math.Max(
                        _previewRadius * 8.0f,
                        30.0f));

            return;
        }

        if (_driveMode)
        {
            var steps =
                e.Delta /
                120.0f;

            if (_vehicleViewMode ==
                RuntimeVehicleViewMode.Exterior)
            {
                _exteriorCameraDistanceScale =
                    Math.Clamp(
                        _exteriorCameraDistanceScale *
                        MathF.Pow(
                            0.90f,
                            steps),
                        0.35f,
                        4.0f);
            }
            else
            {
                // OMSI cockpit/passenger zoom changes field of view while
                // keeping the eye at the authored camera position.
                _interiorCameraFieldOfViewScale =
                    Math.Clamp(
                        _interiorCameraFieldOfViewScale *
                        MathF.Pow(
                            0.90f,
                            steps),
                        0.35f,
                        1.75f);
            }

            return;
        }

        var wheelSteps =
            e.Delta /
            120.0f;

        if (_pressedKeys.Contains(
                Keys.ControlKey))
        {
            _camera.AdjustSpeed(
                wheelSteps);
        }
        else
        {
            _camera.Dolly(
                wheelSteps);
        }
    }

    private void ToggleMouseDriveMode()
    {
        if (_mouseDriveMode)
        {
            DisableMouseDriveMode();
            return;
        }

        _mouseDriveMode = true;
        _mouseLooking = false;
        _freeCameraDragButton =
            MouseButtons.None;
        _mouseDriveAccelerator = 0.0f;
        _mouseDriveBrake = 0.0f;
        _mouseDriveSteering = 0.0f;
        Capture = true;
        Cursor =
            Cursors.Cross;

        var center =
            new System.Drawing.Point(
                ClientSize.Width / 2,
                ClientSize.Height / 2);

        Cursor.Position =
            PointToScreen(center);

        SyncOmsiMenuState();
    }

    private void DisableMouseDriveMode()
    {
        if (!_mouseDriveMode)
        {
            return;
        }

        _mouseDriveMode = false;
        _freeCameraDragButton =
            MouseButtons.None;
        _mouseDriveAccelerator = 0.0f;
        _mouseDriveBrake = 0.0f;
        _mouseDriveSteering = 0.0f;
        Capture = false;
        Cursor =
            Cursors.Default;

        SyncOmsiMenuState();
    }

    private void UpdateOmsiMouseAxes(
        System.Drawing.Point location)
    {
        var halfWidth =
            Math.Max(
                ClientSize.Width * 0.45f,
                1.0f);
        var halfHeight =
            Math.Max(
                ClientSize.Height * 0.45f,
                1.0f);

        var centerX =
            ClientSize.Width * 0.5f;
        var centerY =
            ClientSize.Height * 0.5f;

        // Physical alpha testing is the authority for the screen-space
        // steering direction. The renderer/vehicle coordinate conversion
        // makes the previous screen-X sign feel reversed in OMSI mouse
        // steering mode, so map cursor-right to the opposite raw axis here.
        var horizontal =
            Math.Clamp(
                (centerX - location.X) /
                halfWidth,
                -1.0f,
                1.0f);

        var vertical =
            Math.Clamp(
                (centerY - location.Y) /
                halfHeight,
                -1.0f,
                1.0f);

        const float deadZone =
            0.07f;

        horizontal =
            ApplyMouseDeadZone(
                horizontal,
                deadZone);

        vertical =
            ApplyMouseDeadZone(
                vertical,
                deadZone);

        // Slight response curve gives finer control around the centre
        // without taking away full lock / full pedal near the edges.
        _mouseDriveSteering =
            MathF.CopySign(
                MathF.Pow(
                    Math.Abs(
                        horizontal),
                    1.28f),
                horizontal);

        _mouseDriveAccelerator =
            Math.Max(
                vertical,
                0.0f);

        _mouseDriveBrake =
            Math.Max(
                -vertical,
                0.0f);
    }

    private static float ApplyMouseDeadZone(
        float value,
        float deadZone)
    {
        var magnitude =
            Math.Abs(
                value);

        if (magnitude <=
            deadZone)
        {
            return 0.0f;
        }

        return MathF.CopySign(
            Math.Clamp(
                (magnitude - deadZone) /
                (1.0f - deadZone),
                0.0f,
                1.0f),
            value);
    }

    private void ConfigureVehiclePreviewBounds()
    {
        var vertices =
            _vehicleExteriorGeometry.Vertices.Length > 0
                ? _vehicleExteriorGeometry.Vertices
                : _vehicleInteriorGeometry.Vertices;

        if (vertices.Length == 0)
        {
            _previewCenter =
                new Vector3(
                    0.0f,
                    1.6f,
                    0.0f);
            _previewRadius =
                5.0f;
            _previewDistance =
                14.0f;
            return;
        }

        var minimum =
            vertices[0].Position;
        var maximum =
            vertices[0].Position;

        foreach (var vertex in
                 vertices)
        {
            minimum =
                Vector3.Min(
                    minimum,
                    vertex.Position);

            maximum =
                Vector3.Max(
                    maximum,
                    vertex.Position);
        }

        _previewCenter =
            (minimum + maximum) *
            0.5f;

        var size =
            maximum - minimum;

        _previewRadius =
            Math.Max(
                size.Length() *
                0.5f,
                1.0f);

        _previewDistance =
            Math.Clamp(
                _previewRadius * 2.35f,
                4.0f,
                40.0f);
    }

    private Vector3 ResolveVehiclePreviewCameraPosition()
    {
        var horizontal =
            MathF.Cos(
                _previewPitch);

        var direction =
            new Vector3(
                MathF.Sin(
                    _previewYaw) *
                horizontal,
                MathF.Sin(
                    _previewPitch),
                MathF.Cos(
                    _previewYaw) *
                horizontal);

        return
            _previewCenter +
            direction *
            _previewDistance;
    }

    private Matrix4x4 CreateVehiclePreviewViewProjection(
        float aspect)
    {
        var eye =
            ResolveVehiclePreviewCameraPosition();

        var view =
            Matrix4x4.CreateLookAt(
                eye,
                _previewCenter,
                Vector3.UnitY);

        var projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 4.0f,
                MathF.Max(
                    aspect,
                    0.1f),
                0.03f,
                Math.Max(
                    250.0f,
                    _previewDistance +
                    _previewRadius *
                    12.0f));

        return
            view *
            projection;
    }

    private void UpdateVehiclePreviewCameraConstants()
    {
        if (_deviceContext is null ||
            _terrainCameraBuffer is null)
        {
            return;
        }

        Span<RuntimeCameraConstants> constants =
            stackalloc RuntimeCameraConstants[1];

        constants[0] =
            new RuntimeCameraConstants
            {
                ViewProjection =
                    CreateViewProjection(),
                CameraPosition =
                    ResolveActiveCameraPosition(),
                CameraPadding =
                    0.0f
            };

        _terrainCameraBuffer.SetData(
            _deviceContext,
            constants,
            MapMode.WriteDiscard);
    }

    private void DrawTileOverview()
    {
        if (_deviceContext is null ||
            _renderTargetView is null ||
            _tileVertexBuffer is null ||
            _tileVertexShader is null ||
            _tilePixelShader is null ||
            _tileInputLayout is null ||
            _tileVertexCount == 0)
        {
            return;
        }

        _deviceContext.OMSetRenderTargets(
            _renderTargetView,
            (ID3D11DepthStencilView?)null);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);
        _deviceContext.IASetInputLayout(
            _tileInputLayout);
        _deviceContext.IASetVertexBuffer(
            0,
            _tileVertexBuffer,
            RuntimeVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _tileVertexShader);
        _deviceContext.PSSetShader(
            _tilePixelShader);
        _deviceContext.Draw(
            _tileVertexCount,
            0);
    }

    private void UpdateFpsOverlay()
    {
        if (!_showFps ||
            _fpsLabel is null)
        {
            return;
        }

        _fpsFrameCount++;

        var nowSeconds =
            _frameClock.Elapsed.TotalSeconds;

        var frameSeconds =
            nowSeconds -
            _fpsPreviousFrameSeconds;

        _fpsPreviousFrameSeconds =
            nowSeconds;

        if (frameSeconds >
                0.0 &&
            frameSeconds <
                1.0)
        {
            _frameTimeSamplesMilliseconds.Enqueue(
                frameSeconds *
                1000.0);

            while (_frameTimeSamplesMilliseconds.Count >
                   MaximumFrameTimeSamples)
            {
                _frameTimeSamplesMilliseconds.Dequeue();
            }
        }

        var elapsedSeconds =
            nowSeconds -
            _fpsSampleStartSeconds;

        if (elapsedSeconds <
            0.5)
        {
            return;
        }

        var frames =
            Math.Max(
                _fpsFrameCount,
                1);

        var fps =
            frames /
            elapsedSeconds;

        var frameMilliseconds =
            elapsedSeconds *
            1000.0 /
            frames;

        var graphicsMode =
            _activeMsaaSamples >=
                    2
                ? $"MSAA {_activeMsaaSamples}x"
                : "MSAA off";

        var sharpenMode =
            _sharpenStrength >
                    0.0001f
                ? $"Sharp {_sharpenStrength:0.00}"
                : "Sharp off";

        var samples =
            _frameTimeSamplesMilliseconds
                .OrderBy(
                    static value =>
                        value)
                .ToArray();

        var percentile99Milliseconds =
            samples.Length >
                    0
                ? samples[
                    Math.Clamp(
                        (int)Math.Ceiling(
                            samples.Length *
                            0.99) -
                        1,
                        0,
                        samples.Length -
                        1)]
                : frameMilliseconds;

        var onePercentLowFps =
            percentile99Milliseconds >
                    0.0
                ? 1000.0 /
                  percentile99Milliseconds
                : fps;

        var worstFrameMilliseconds =
            samples.Length >
                    0
                ? samples[^1]
                : frameMilliseconds;

        var streamingMode =
            _pendingStreamingTextureLoads.Count >
                    0
                ? $"\nStream {_pendingStreamingTextureLoads.Count:N0} tex · {_currentStreamingTextureUploadLimit}/f · {_currentStreamingTextureUploadBudgetMilliseconds:0.0} ms"
                : string.Empty;

        var streamingTiming =
            _lastStreamingPrepareMilliseconds >
                    0.0 ||
                _lastStreamingGpuPrepareMilliseconds >
                    0.0 ||
                _lastStreamingSwapMilliseconds >
                    0.0
                ? $"\nCPU {_lastStreamingPrepareMilliseconds:0.0} · GPU {_lastStreamingGpuPrepareMilliseconds:0.0} · Swap {_lastStreamingSwapMilliseconds:0.0} ms"
                : string.Empty;

        _fpsLabel.Text =
            $"FPS {fps:0.0}  |  {frameMilliseconds:0.0} ms\n1% {onePercentLowFps:0.0} FPS · max {worstFrameMilliseconds:0.0} ms\n{graphicsMode} · {sharpenMode}{streamingMode}{streamingTiming}";

        _fpsFrameCount =
            0;

        _fpsSampleStartSeconds =
            nowSeconds;
    }

    private void UpdateCaption()
    {
        if (_vehiclePreviewMode)
        {
            Text =
                $"Prévia 3D — {_windowInfo.Vehicle?.DisplayName ?? "Veículo"}";
            return;
        }

        var runtimeObjectCount =
            _windowInfo.Objects.Count(
                item =>
                    _windowInfo.SceneryAssets.TryGetValue(
                        item.AssetPath,
                        out var asset) &&
                    (!asset.OnlyEditor ||
                     asset.Tree is not null));

        var sceneryBudget =
            _objectGeometry.HitVertexBudget
                ? " · object-budget"
                : string.Empty;

        var mirrorMode =
            _reflectionRenderingEnabled
                ? $"mirrors ON {_reflectionTargets.Count:N0}"
                : $"mirrors OFF {_reflectionTargets.Count:N0}";

        var antiAliasingMode =
            _activeMsaaSamples >=
                    2
                ? $"MSAA {_activeMsaaSamples}x"
                : "MSAA off";

        var mode = _terrainVertexCount > 0
            ? $"terrain {_terrainVertexCount / 3:N0} triangles · {mirrorMode} · {antiAliasingMode} · ground textures {_terrainGeometry.TexturedBatchCount:N0} · masks {_terrainGeometry.MaskedLayerCount:N0} · roads {_splineGeometry.RenderedSplineCount:N0} · road textures {_splineGeometry.TexturedBatchCount:N0} · runtime objects {_objectGeometry.RenderedObjectCount:N0}/{runtimeObjectCount:N0} · meshes {_objectGeometry.RenderedMeshCount:N0} · trees {_objectGeometry.RenderedTreeCount:N0} · textures {_objectTextureCache.Count:N0} loaded · texture failures {_failedObjectTexturePaths.Count:N0} · encrypted {_objectGeometry.ProtectedMeshCount:N0}{sceneryBudget}"
            : "tile overview";

        var gear = _vehicleRemoved
            ? "—"
            : _vehicle.Gear switch
        {
            RuntimeDriveGear.Drive => "D",
            RuntimeDriveGear.Reverse => "R",
            _ => "N"
        };

        var driveInputMode =
            _mouseDriveMode
                ? "MOUSE LOCKED: ←/→ steer · ↑ throttle · ↓ brake · O desativa · RMB segura câmera"
                : _controllerInputEnabled &&
                  _omsiGameController is
                    { ConnectedDeviceCount: > 0 }
                    ? $"OMSI STEERING: GAME CONTROLLER · {_omsiGameController.ConnectedDeviceCount} ativo(s) · K alterna · O mouse"
                    : "OMSI STEERING: KEYBOARD · Inputs/keyboard.cfg · O ativa mouse · K controller";

        var vehicleView =
            _vehicleViewMode switch
            {
                RuntimeVehicleViewMode.Passenger =>
                    "F2 passenger",
                RuntimeVehicleViewMode.Exterior =>
                    "F3 exterior",
                _ =>
                    "F1 cockpit"
            };

        var pauseState =
            _simulationPaused
                ? "PAUSADO · "
                : string.Empty;

        var control = _driveMode
            ? $"{pauseState}OMSI DRIVE · {vehicleView} · {_vehicle.SpeedKph:0} km/h · gear {gear} · " +
              $"E:{(_vehicle.ElectricalSystemEnabled ? "ON" : "OFF")} " +
              $"M:{(_vehicle.EngineRunning ? "ON" : "OFF")} · " +
              $"brake {_vehicle.BrakeLevel * 100.0f:0}% · " +
              $"park:{(_vehicle.ParkingBrakeEngaged ? "ON" : "OFF")} · " +
              $"{driveInputMode} · RMB drag look/orbit · F3 wheel zoom · S views · F1/F2/F3/F4 cameras · ←/→ perspectives · C/Space reset · P pause · D/N/R · E/M · Tab clutch"
            : $"{pauseState}FREE CAM · RMB look · MMB pan · wheel zoom · Ctrl+wheel speed · S views · F1/F2/F3/F4 cameras · C reset · O:{(_mouseDriveMode ? "LOCKED" : "OFF")}";

        var totalInfractions =
            _trafficCollisionCount +
            _speedViolationCount +
            _redLightViolationCount +
            _priorityViolationCount;

        var trafficRuleStatus =
            totalInfractions >
            0
                ? $" · infrações {totalInfractions} (colisões {_trafficCollisionCount}, velocidade {_speedViolationCount}, vermelho {_redLightViolationCount}, prioridade {_priorityViolationCount}) · penalidade {_trafficPenaltyPoints} pts · multas {_trafficFineCredits} cr"
                : string.Empty;

        control +=
            trafficRuleStatus +
            " · Alt menu";

        Text =
            _statusInfoLevel switch
            {
                0 =>
                    $"OMSI Compatible Runtime — {_windowInfo.WorldName}",
                1 =>
                    $"OMSI Compatible Runtime — {_windowInfo.WorldName} — {control}",
                2 =>
                    $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
                    $"{_windowInfo.TileCount:N0}/{_windowInfo.TotalTileCount:N0} tiles — " +
                    $"{_windowInfo.SplineCount:N0} splines — {control}",
                _ =>
                    $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
                    $"{_windowInfo.TileCount:N0}/{_windowInfo.TotalTileCount:N0} tiles — " +
                    $"{runtimeObjectCount:N0} runtime objects · {_windowInfo.ObjectCount:N0} map entries — " +
                    $"{_windowInfo.SplineCount:N0} splines — " +
                    $"{mode} — {control}"
            };
    }

    private sealed record TrafficOmsiAudioState(
        string VehiclePath,
        RuntimeOmsiAudioHost Audio)
    {
        public string? LastDiagnosticSignature
        {
            get;
            set;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_omsiMenuBar is not null)
            {
                _omsiMenuBar.CommandInvoked -=
                    OnOmsiMenuCommandInvoked;
            }

            if (_busSelectorPanel is not null)
            {
                _busSelectorPanel.SelectionConfirmed -=
                    OnRuntimeBusSelectionConfirmed;
            }

            if (_scriptRuntime is not null)
            {
                _scriptRuntime.SystemMacroHandler =
                    _previousSystemMacroHandler;
                _scriptRuntime.UnhandledSystemMacro -=
                    OnUnhandledSystemMacro;
                _scriptRuntime.DebugMessageRequested -=
                    OnScriptDebugMessage;
                _scriptRuntime.SoundTriggerRequested -=
                    OnScriptSoundTriggerRequested;
                _scriptRuntime.FileSoundTriggerRequested -=
                    OnScriptFileSoundTriggerRequested;
            }

            if (_mouseDriveMode)
            {
                DisableMouseDriveMode();
            }

            _omsiGameController?.Dispose();
            _omsiGameController =
                null;

            _omsiAudio?.Dispose();
            _omsiAudio =
                null;

            foreach (var audio in
                     _articulatedOmsiAudio.Values)
            {
                audio.Dispose();
            }

            _articulatedOmsiAudio.Clear();

            foreach (var state in
                     _trafficOmsiAudio.Values)
            {
                state.Audio.Dispose();
            }

            _trafficOmsiAudio.Clear();
            _activeTrafficAudioAgentIds.Clear();
            _staleTrafficAudioAgentIds.Clear();

            _vehicle.Dispose();

            _renderTimer.Stop();
            _renderTimer.Tick -=
                RenderTimerOnTick;
            _renderTimer.Dispose();

            _deviceContext?.ClearState();
            _deviceContext?.Flush();

            _skyTexture?.Dispose();
            _skyTexture = null;
            _skyConstantsBuffer?.Dispose();
            _skySampler?.Dispose();
            _skyPixelShader?.Dispose();
            _skyVertexShader?.Dispose();

            _terrainRasterizerState?.Dispose();
            _terrainAdditiveBlendState?.Dispose();
            _terrainAlphaBlendState?.Dispose();
            _terrainMaskSampler?.Dispose();
            _terrainTextureSamplerPerformance?.Dispose();
            _terrainTextureSampler?.Dispose();
            _terrainInputLayout?.Dispose();
            _terrainLightmapPixelShader?.Dispose();
            _terrainLayerDetailPixelShader?.Dispose();
            _terrainLayerPixelShader?.Dispose();
            _terrainBaseDetailPixelShader?.Dispose();
            _terrainTexturedPixelShader?.Dispose();
            _terrainPixelShader?.Dispose();
            _terrainVertexShader?.Dispose();
            _terrainCameraBuffer?.Dispose();
            while (_retiredStreamingVertexBuffers.Count >
                   0)
            {
                _retiredStreamingVertexBuffers
                    .Dequeue()
                    .Buffer
                    .Dispose();
            }

            _terrainVertexBuffer?.Dispose();
            _splineVertexBuffer?.Dispose();

            while (_retiredStreamingTextures.Count >
                   0)
            {
                _retiredStreamingTextures
                    .Dequeue()
                    .Texture
                    .Dispose();
            }

            foreach (var texture in
                _objectTextureCache.Values)
            {
                texture.Dispose();
            }

            _objectTextureCache.Clear();
            _objectTextureLastUsedGeneration.Clear();
            _failedObjectTexturePaths.Clear();
            _pendingStreamingTextureLoads.Clear();
            _pendingStreamingTexturePaths.Clear();

            _vehicleTextTextureRenderer?.Dispose();
            _vehicleTextTextureRenderer = null;
            _vehicleAnimationParentBatches.Clear();
            _vehicleDrawItems.Clear();
            _vehicleLightMeshes.Clear();
            _vehicleViewpointLightMeshes.Clear();
            _vehicleOrderedMaterialChangeSets.Clear();
            _vehicleMaterialChangeItems.Clear();

            _objectSamplerPerformance?.Dispose();
            _objectSampler?.Dispose();
            _objectDepthDisabledState?.Dispose();
            _objectDepthReadState?.Dispose();
            _objectAlphaBlendState?.Dispose();
            _objectInputLayout?.Dispose();
            _objectAlphaBlendTransMapPixelShader?.Dispose();
            _objectAlphaCutoutTransMapPixelShader?.Dispose();
            _objectAlphaBlendPixelShader?.Dispose();
            _objectAlphaCutoutPixelShader?.Dispose();
            _objectTexturedPixelShader?.Dispose();
            _objectColorPixelShader?.Dispose();
            _objectVertexShader?.Dispose();
            _objectVertexBuffer?.Dispose();

            foreach (var target in
                _reflectionTargets.Values)
            {
                target.Dispose();
            }

            _reflectionTargets.Clear();

            _reflectionDepthStencilView?.Dispose();
            _reflectionDepthStencilView = null;

            _reflectionDepthTexture?.Dispose();
            _reflectionDepthTexture = null;

            _vehicleDepthDisabledState?.Dispose();
            _vehicleDepthReadState?.Dispose();
            _vehicleAlphaBlendState?.Dispose();
            _vehicleSampler?.Dispose();
            _vehicleInputLayout?.Dispose();
            _trafficInstancedInputLayout?.Dispose();
            _trafficInstancedVertexShader?.Dispose();
            _trafficInstanceBuffer?.Dispose();
            _trafficInstanceBuffer =
                null;
            _trafficInstanceBufferCapacity =
                0;
            _trafficInstanceScratch =
                Array.Empty<RuntimeTrafficInstanceData>();
            _vehicleAlphaBlendTransMapPixelShader?.Dispose();
            _vehicleAlphaCutoutTransMapPixelShader?.Dispose();
            _vehicleAlphaBlendPixelShader?.Dispose();
            _vehicleAlphaCutoutPixelShader?.Dispose();
            _vehicleTexturedPixelShader?.Dispose();
            _vehicleLightPixelShader?.Dispose();
            _vehicleColorPixelShader?.Dispose();
            _vehicleVertexShader?.Dispose();
            _vehicleSkinBuffer?.Dispose();
            _vehicleMaterialBuffer?.Dispose();
            _vehicleModelBuffer?.Dispose();
            _vehicleLightVertexBuffer?.Dispose();
            _vehicleInteriorVertexBuffer?.Dispose();
            _vehicleExteriorVertexBuffer?.Dispose();

            foreach (var buffer in
                     _trafficVehicleVertexBuffers.Values)
            {
                buffer.Dispose();
            }

            _trafficVehicleVertexBuffers.Clear();
            _trafficVehicleGeometries.Clear();
            _trafficVehicleRenderBatches.Clear();
            _trafficVehicleRenderBatchSummaries.Clear();
            _trafficAnimationBindings.Clear();
            _compiledTrafficAnimations.Clear();
            _trafficVehicleAnimationPhysics.Clear();
            _trafficVehicleLightMeshes.Clear();
            _trafficVisibleDrawItemsByVehiclePath.Clear();
            _trafficVisibleDrawItems.Clear();

            _tileInputLayout?.Dispose();
            _tilePixelShader?.Dispose();
            _tileVertexShader?.Dispose();
            _tileVertexBuffer?.Dispose();

            _postProcessConstantsBuffer?.Dispose();
            _postProcessSampler?.Dispose();
            _postProcessPixelShader?.Dispose();
            _postProcessVertexShader?.Dispose();

            ReleaseBackBufferResources();

            _swapChain?.Dispose();
            _deviceContext?.Dispose();
            _device?.Dispose();
            _factory?.Dispose();
        }

        base.Dispose(disposing);
    }

    private readonly struct RuntimeVertex
    {
        public const uint SizeInBytes = 28;

        public RuntimeVertex(
            Vector3 position,
            Color4 color)
        {
            Position = position;
            Color = color;
        }

        public readonly Vector3 Position;

        public readonly Color4 Color;
    }
}
