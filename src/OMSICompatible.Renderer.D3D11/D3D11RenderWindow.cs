using System.Diagnostics;
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
    private struct RuntimeModelConstants
    {
        public Matrix4x4 World;
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

    private static readonly FeatureLevel[] RequestedFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    private RuntimeWindowInfo _windowInfo;
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
    private RuntimeOmsiGameControllerHost? _omsiGameController;
    private RuntimeOmsiAudioHost? _omsiAudio;
    private readonly Dictionary<int, RuntimeOmsiAudioHost>
        _articulatedOmsiAudio =
            [];
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
    private int _statusInfoLevel = 1;
    private bool _specialViewActive;
    private bool _specialPreviousDriveMode;
    private RuntimeVehicleViewMode _specialPreviousViewMode =
        RuntimeVehicleViewMode.Driver;
    private int _specialPreviousDriverCameraIndex;
    private int _specialPreviousPassengerCameraIndex;
    private readonly RuntimeOmsiMenuBar? _omsiMenuBar;
    private int _captionFrame;
    private int? _streamingTileX;
    private int? _streamingTileY;
    private System.Drawing.Point _lastMousePosition;

    public event Action<int, int>?
        StreamingCenterChanged;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _deviceContext;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11Texture2D? _depthTexture;
    private ID3D11DepthStencilView? _depthStencilView;

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
    private ID3D11SamplerState? _terrainMaskSampler;
    private ID3D11BlendState? _terrainAlphaBlendState;
    private ID3D11BlendState? _terrainAdditiveBlendState;
    private ID3D11RasterizerState? _terrainRasterizerState;
    private RuntimeTerrainGeometry _terrainGeometry =
        RuntimeTerrainGeometry.Empty;
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
    private ID3D11InputLayout? _objectInputLayout;
    private ID3D11SamplerState? _objectSampler;
    private RuntimeGpuTextureLoader? _objectTextureLoader;
    private RuntimeOmsiTextTextureRenderer? _vehicleTextTextureRenderer;
    private readonly Dictionary<string, RuntimeGpuTexture>
        _objectTextureCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string>
        _failedObjectTexturePaths =
            new(
                StringComparer.OrdinalIgnoreCase);
    private RuntimeObjectGeometry _objectGeometry =
        RuntimeObjectGeometry.Empty;
    private uint _objectVertexCount;

    private ID3D11Buffer? _vehicleExteriorVertexBuffer;
    private ID3D11Buffer? _vehicleInteriorVertexBuffer;
    private ID3D11Buffer? _vehicleLightVertexBuffer;
    private ID3D11Buffer? _vehicleModelBuffer;
    private ID3D11Buffer? _vehicleMaterialBuffer;
    private ID3D11Buffer? _vehicleSkinBuffer;
    private ID3D11VertexShader? _vehicleVertexShader;
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

    private const uint ReflectionTextureSize = 512;
    private readonly Dictionary<string, RuntimeReflectionTarget>
        _reflectionTargets =
            new(
                StringComparer.OrdinalIgnoreCase);
    private ID3D11Texture2D? _reflectionDepthTexture;
    private ID3D11DepthStencilView? _reflectionDepthStencilView;
    private ID3D11RenderTargetView? _activeRenderTargetView;
    private ID3D11DepthStencilView? _activeDepthStencilView;
    private Matrix4x4? _viewProjectionOverride;
    private bool _reflectionRenderingEnabled;
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

    public D3D11RenderWindow(
        RuntimeWindowInfo windowInfo,
        OmsiScriptRuntime? scriptRuntime = null,
        int targetFps = 60,
        bool vsync = true,
        bool vehiclePreviewMode = false,
        IReadOnlyDictionary<string, double>? initialVehicleVariables = null,
        string? inputLanguage = null,
        bool gameControllerEnabled = true,
        IReadOnlyDictionary<int, OmsiScriptRuntime>? sectionScriptRuntimes = null)
    {
        _windowInfo = windowInfo;
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
        _vehiclePreviewMode =
            vehiclePreviewMode;
        _gameControllerEnabled =
            gameControllerEnabled;
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
                windowInfo.Tiles);

        _vehicle = new RuntimeDriveVehicle(
            windowInfo.Tiles,
            windowInfo.Vehicle?.Physics,
            windowInfo.Vehicle?.Sections);
        _driveMode =
            windowInfo.Vehicle is not null &&
            !_vehiclePreviewMode;
        _reflectionRenderingEnabled =
            !_vehiclePreviewMode &&
            windowInfo.Vehicle?.ReflectionCameras.Count is
                > 0;
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

    public void ApplyStreamedWorld(
        RuntimeWindowInfo windowInfo)
    {
        ArgumentNullException.ThrowIfNull(
            windowInfo);

        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(
                () =>
                    ApplyStreamedWorld(
                        windowInfo));
            return;
        }

        _windowInfo =
            windowInfo;

        _terrainSurfaceSampler =
            new RuntimeTerrainSampler(
                windowInfo.Tiles);

        _vehicle.ReplaceTerrainTiles(
            windowInfo.Tiles);

        if (_device is null)
        {
            return;
        }

        var wasRunning =
            _renderTimer.Enabled;

        _renderTimer.Stop();

        try
        {
            RebuildStreamedGeometry();
            RefreshStreamingTextureCache();
            UpdateCaption();
        }
        finally
        {
            if (wasRunning)
            {
                _renderTimer.Start();
            }
        }
    }

    private void RebuildStreamedGeometry()
    {
        if (_device is null)
        {
            return;
        }

        _tileVertexBuffer?.Dispose();
        _tileVertexBuffer = null;

        var tileVertices =
            BuildTileVertices(
                _windowInfo.Tiles);

        if (tileVertices.Length > 0)
        {
            _tileVertexBuffer =
                _device.CreateBuffer(
                    tileVertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        _tileVertexCount =
            (uint)tileVertices.Length;

        _terrainVertexBuffer?.Dispose();
        _terrainVertexBuffer = null;

        _terrainGeometry =
            RuntimeTerrainGeometryBuilder.Build(
                _windowInfo.Tiles,
                _windowInfo.GroundTextures);

        if (_terrainGeometry.Vertices.Length > 0)
        {
            _terrainVertexBuffer =
                _device.CreateBuffer(
                    _terrainGeometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        _terrainVertexCount =
            (uint)_terrainGeometry.Vertices.Length;

        _splineVertexBuffer?.Dispose();
        _splineVertexBuffer = null;

        _splineGeometry =
            RuntimeSplineGeometryBuilder.Build(
                _windowInfo.Splines);

        _vehicle.ReplaceSplineSurfaceGeometry(
            _splineGeometry);

        if (_splineGeometry.Vertices.Length > 0)
        {
            _splineVertexBuffer =
                _device.CreateBuffer(
                    _splineGeometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        _splineVertexCount =
            (uint)_splineGeometry.Vertices.Length;

        _objectVertexBuffer?.Dispose();
        _objectVertexBuffer = null;

        _objectGeometry =
            RuntimeObjectGeometryBuilder.Build(
                _windowInfo.Tiles,
                _windowInfo.Objects,
                _windowInfo.SceneryAssets,
                useNativeOmsiModelSpace: true);

        if (_objectGeometry.Vertices.Length > 0)
        {
            _objectVertexBuffer =
                _device.CreateBuffer(
                    _objectGeometry.Vertices.AsSpan(),
                    BindFlags.VertexBuffer);
        }

        _objectVertexCount =
            (uint)_objectGeometry.Vertices.Length;
    }

    private void RefreshStreamingTextureCache()
    {
        if (_device is null)
        {
            return;
        }

        _objectTextureLoader ??=
            new RuntimeGpuTextureLoader(
                _device);

        var regularPaths =
            _objectGeometry.Batches
                .Concat(
                    _splineGeometry.Batches)
                .Concat(
                    _vehicleExteriorGeometry.Batches)
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
                .Concat(
                    _terrainGeometry.Batches
                        .SelectMany(
                            static batch =>
                                new[]
                                {
                                    batch.TexturePath,
                                    batch.DetailTexturePath
                                }))
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    static path =>
                        path!)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var maskPaths =
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
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var requiredPaths =
            new HashSet<string>(
                regularPaths,
                StringComparer.OrdinalIgnoreCase);

        requiredPaths.UnionWith(
            maskPaths);

        foreach (var cachedPath in
            _objectTextureCache.Keys
                .Where(
                    path =>
                        !requiredPaths.Contains(
                            path))
                .ToArray())
        {
            _objectTextureCache[
                cachedPath]
                .Dispose();

            _objectTextureCache.Remove(
                cachedPath);
        }

        _failedObjectTexturePaths.IntersectWith(
            requiredPaths);

        foreach (var texturePath in
            regularPaths)
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

                _failedObjectTexturePaths.Remove(
                    texturePath);
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    texturePath);
            }
        }

        foreach (var maskPath in
            maskPaths)
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

                _failedObjectTexturePaths.Remove(
                    maskPath);
            }
            else
            {
                _failedObjectTexturePaths.Add(
                    maskPath);
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
                    .SoundConfigPath);

        if (_omsiAudio is not null)
        {
            Console.WriteLine(
                $"[audio] lead: {_omsiAudio.ExistingFileCount}/{_omsiAudio.SoundCount} OMSI sound files resolved.");
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
                    section.SoundConfigPath);

            if (audio is null)
            {
                continue;
            }

            _articulatedOmsiAudio[
                section.Index] =
                audio;

            Console.WriteLine(
                $"[audio] section={section.Index}: {audio.ExistingFileCount}/{audio.SoundCount} OMSI sound files resolved from {Path.GetFileName(section.SoundConfigPath)}.");
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
        CreateSkyResources();
        CreateTileOverviewResources();
        CreateTerrainResources();
        CreateSplineResources();
        CreateObjectResources();
        CreateVehicleResources();
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
        if (_swapChain is null || _device is null)
        {
            return;
        }

        var width = (uint)Math.Max(ClientSize.Width, 1);
        var height = (uint)Math.Max(ClientSize.Height, 1);

        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(_backBuffer);

        _depthTexture = _device.CreateTexture2D(
            Format.D32_Float,
            width,
            height,
            mipLevels: 1,
            bindFlags: BindFlags.DepthStencil);

        _depthStencilView = _device.CreateDepthStencilView(
            _depthTexture);
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
                _device);

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

        return new Vector4(
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
                _windowInfo.GroundTextures);

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

        _camera.Reset(
            _terrainGeometry);
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
                useNativeOmsiModelSpace: true);

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

        _objectInputLayout =
            _device.CreateInputLayout(
                CreateObjectInputElements(),
                vertexShaderByteCode.Span);

        _objectSampler =
            _device.CreateSamplerState(
                SamplerDescription.LinearWrap);

        _objectTextureLoader =
            new RuntimeGpuTextureLoader(
                _device);

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

        var hasVehicleLights =
            _windowInfo.Vehicle?.Meshes.Any(
                static mesh =>
                    mesh.LightEffects is
                        { Count: > 0 }) ==
            true;

        if (_vehicleExteriorGeometry.Vertices.Length == 0 &&
            _vehicleInteriorGeometry.Vertices.Length == 0 &&
            !hasVehicleLights)
        {
            Console.WriteLine(
                "[vehicle-geometry] No real vehicle geometry or light effects could be built.");
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
                _device);

        _vehicleTextTextureRenderer ??=
            new RuntimeOmsiTextTextureRenderer(
                _windowInfo.ContentRoot,
                _objectTextureLoader);

        CreateReflectionResources();

        var vehicleTexturePaths =
            _vehicleExteriorGeometry.Batches
                .Concat(
                    _vehicleInteriorGeometry.Batches)
                .SelectMany(
                    static batch =>
                        new[]
                        {
                            batch.TexturePath,
                            batch.TransMapTexturePath
                        })
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
                    ReflectionTextureSize);
        }

        if (_reflectionDepthTexture is null)
        {
            _reflectionDepthTexture =
                _device.CreateTexture2D(
                    Format.D32_Float,
                    ReflectionTextureSize,
                    ReflectionTextureSize,
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

        _backBuffer?.Dispose();
        _backBuffer = null;
    }

    private void RenderTimerOnTick(
        object? sender,
        EventArgs e)
    {
        if (!_simulationPaused)
        {
            UpdateSimulation();
            CheckStreamingCenter();
        }

        RenderFrame();

        if (_omsiMenuBar?.Visible ==
            true)
        {
            _omsiMenuBar.BringToFront();
        }

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

    private void RenderFrame()
    {
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

            _swapChain.Present(
                _vsync
                    ? 1u
                    : 0u,
                PresentFlags.None)
                .CheckError();

            return;
        }

        DrawSky();

        if (CanDrawTerrain())
        {
            DrawTerrain();
            DrawSplines();
            DrawObjects();
            DrawVehicle();
            DrawVehicleLights();
        }
        else
        {
            DrawTileOverview();
        }

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

        try
        {
            foreach (var target in
                     _reflectionTargets.Values)
            {
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
                    ReflectionTextureSize,
                    ReflectionTextureSize);

                DrawSky();
                DrawTerrain();
                DrawSplines();
                DrawObjects();
            }
        }
        finally
        {
            _deviceContext.PSUnsetShaderResource(0);
            _deviceContext.PSUnsetShaderResource(1);
            _deviceContext.PSUnsetShaderResource(2);
            _activeRenderTargetView = null;
            _activeDepthStencilView = null;
            _viewProjectionOverride = null;
        }
    }

    private ID3D11RenderTargetView?
        CurrentRenderTargetView =>
            _activeRenderTargetView ??
            _renderTargetView;

    private ID3D11DepthStencilView?
        CurrentDepthStencilView =>
            _activeDepthStencilView ??
            _depthStencilView;

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

        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);

        _deviceContext.PSSetSampler(
            0,
            _terrainTextureSampler);

        _deviceContext.PSSetSampler(
            1,
            _terrainMaskSampler);

        foreach (var batch in
            _terrainGeometry.Batches)
        {
            if (batch.VertexCount == 0)
            {
                continue;
            }

            RuntimeGpuTexture? texture = null;
            RuntimeGpuTexture? mask = null;
            RuntimeGpuTexture? detail = null;

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

            _deviceContext.PSUnsetShaderResource(0);
            _deviceContext.PSUnsetShaderResource(1);
            _deviceContext.PSUnsetShaderResource(2);
            _deviceContext.OMSetBlendState(null);

            if (batch.AdditiveLightmap &&
                hasTexture)
            {
                _deviceContext.OMSetBlendState(
                    _terrainAdditiveBlendState);

                _deviceContext.PSSetShader(
                    _terrainLightmapPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    texture!.View);
            }
            else if (hasMask &&
                     hasTexture)
            {
                _deviceContext.OMSetBlendState(
                    _terrainAlphaBlendState);

                _deviceContext.PSSetShader(
                    hasDetail
                        ? _terrainLayerDetailPixelShader
                        : _terrainLayerPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    texture!.View);

                _deviceContext.PSSetShaderResource(
                    1,
                    mask!.View);

                if (hasDetail)
                {
                    _deviceContext.PSSetShaderResource(
                        2,
                        detail!.View);
                }
            }
            else if (hasTexture)
            {
                _deviceContext.PSSetShader(
                    hasDetail
                        ? _terrainBaseDetailPixelShader
                        : _terrainTexturedPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    texture!.View);

                if (hasDetail)
                {
                    _deviceContext.PSSetShaderResource(
                        2,
                        detail!.View);
                }
            }
            else
            {
                _deviceContext.PSSetShader(
                    _terrainPixelShader);
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.OMSetBlendState(null);
        _deviceContext.PSUnsetShaderResource(0);
        _deviceContext.PSUnsetShaderResource(1);
        _deviceContext.PSUnsetShaderResource(2);
        _deviceContext.PSUnsetShaderResource(3);
        _deviceContext.RSSetState(null);
    }

    private void DrawSplines()
    {
        DrawTexturedGeometry(
            _splineVertexBuffer,
            _splineVertexCount,
            _splineGeometry.Batches);
    }

    private void DrawObjects()
    {
        DrawTexturedGeometry(
            _objectVertexBuffer,
            _objectVertexCount,
            _objectGeometry.Batches);
    }

    private void DrawTexturedGeometry(
        ID3D11Buffer? vertexBuffer,
        uint vertexCount,
        IReadOnlyList<RuntimeObjectBatch> batches)
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
        _deviceContext.PSSetSampler(
            0,
            _objectSampler);
        _deviceContext.RSSetState(
            _terrainRasterizerState);

        foreach (var batch in batches)
        {
            if (batch.VertexCount == 0)
            {
                continue;
            }

            _deviceContext.OMSetBlendState(
                batch.AlphaBlend
                    ? _objectAlphaBlendState
                    : null);

            _deviceContext.PSUnsetShaderResource(
                0);
            _deviceContext.PSUnsetShaderResource(
                1);

            if (!string.IsNullOrWhiteSpace(
                    batch.TexturePath) &&
                _objectTextureCache.TryGetValue(
                    batch.TexturePath,
                    out var texture))
            {
                RuntimeGpuTexture? transMap =
                    null;

                var hasTransMap =
                    !string.IsNullOrWhiteSpace(
                        batch.TransMapTexturePath) &&
                    _objectTextureCache.TryGetValue(
                        batch.TransMapTexturePath,
                        out transMap);

                if (hasTransMap)
                {
                    _deviceContext.PSSetShaderResource(
                        1,
                        transMap!.View);
                }

                _deviceContext.PSSetShader(
                    batch.AlphaCutout
                        ? hasTransMap
                            ? _objectAlphaCutoutTransMapPixelShader
                            : _objectAlphaCutoutPixelShader
                        : batch.AlphaBlend
                            ? hasTransMap
                                ? _objectAlphaBlendTransMapPixelShader
                                : _objectAlphaBlendPixelShader
                            : _objectTexturedPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    texture.View);
            }
            else
            {
                _deviceContext.PSSetShader(
                    _objectColorPixelShader);
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.OMSetBlendState(null);
        _deviceContext.PSUnsetShaderResource(0);
        _deviceContext.PSUnsetShaderResource(1);
        _deviceContext.PSUnsetShaderResource(2);
        _deviceContext.RSSetState(null);
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
            _renderTargetView is null ||
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
            _renderTargetView,
            _depthStencilView);

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

        var drawBatches =
            geometry.Batches
                .Where(
                    batch =>
                        IsVehicleBatchVisible(
                            batch) &&
                        batch.VertexCount >
                            0)
                .Select(
                    batch =>
                        (
                            Batch: batch,
                            Material:
                                ResolveVehicleMaterialState(
                                    batch)))
                // OMSI relies heavily on model.cfg ordering, but transparent
                // glass/overlays must never be allowed to reveal through an
                // opaque body that has not written depth yet. Stable OrderBy
                // preserves original order inside each pass.
                .OrderBy(
                    item =>
                        item.Material.AlphaBlend
                            ? 1
                            : 0)
                .ToArray();

        foreach (var draw in
                 drawBatches)
        {
            var batch =
                draw.Batch;
            var materialState =
                draw.Material;

            model[0] =
                new RuntimeModelConstants
                {
                    World =
                        CreateVehicleAnimationMatrix(
                            batch) *
                        CreateArticulatedSectionMatrix(
                            batch.SectionIndex) *
                        vehicleWorld
                };

            _vehicleModelBuffer.SetData(
                _deviceContext,
                model,
                MapMode.WriteDiscard);

            skinConstants[0] =
                ResolveVehicleSkinConstants(
                    batch);

            _vehicleSkinBuffer.SetData(
                _deviceContext,
                skinConstants,
                MapMode.WriteDiscard);

            materialConstants[0] =
                new RuntimeVehicleMaterialConstants
                {
                    AlphaScale =
                        ResolveVehicleAlphaScale(
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

            _vehicleMaterialBuffer.SetData(
                _deviceContext,
                materialConstants,
                MapMode.WriteDiscard);

            _deviceContext.OMSetBlendState(
                materialState.AlphaBlend
                    ? _vehicleAlphaBlendState
                    : null);

            _deviceContext.OMSetDepthStencilState(
                materialState.AlphaBlend
                    ? _vehicleDepthReadState
                    : materialState.NoZCheck
                        ? _vehicleDepthDisabledState
                        : materialState.NoZWrite
                            ? _vehicleDepthReadState
                            : null);

            _deviceContext.PSUnsetShaderResource(
                0);

            _deviceContext.PSUnsetShaderResource(
                1);

            _deviceContext.PSUnsetShaderResource(
                2);

            _deviceContext.PSUnsetShaderResource(
                3);

            _deviceContext.PSUnsetShaderResource(
                4);

            _deviceContext.PSUnsetShaderResource(
                5);

            _deviceContext.PSUnsetShaderResource(
                6);

            if (TryGetVehicleTextureView(
                    materialState.EnvMapTexturePath,
                    out var envMapView))
            {
                _deviceContext.PSSetShaderResource(
                    4,
                    envMapView!);
            }

            if (TryGetVehicleTextureView(
                    materialState.EnvMapMaskTexturePath,
                    out var envMapMaskView))
            {
                _deviceContext.PSSetShaderResource(
                    5,
                    envMapMaskView!);
            }

            if (TryGetVehicleTextureView(
                    materialState.BumpMapTexturePath,
                    out var bumpMapView))
            {
                _deviceContext.PSSetShaderResource(
                    6,
                    bumpMapView!);
            }

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
                var hasTransMap =
                    TryGetVehicleTextureView(
                        materialState.TransMapTexturePath,
                        out var transMapView);

                if (hasTransMap)
                {
                    _deviceContext.PSSetShaderResource(
                        1,
                        transMapView!);
                }

                if (TryGetVehicleTextureView(
                        materialState.LightMapTexturePath,
                        out var lightMapView))
                {
                    _deviceContext.PSSetShaderResource(
                        2,
                        lightMapView!);
                }

                if (TryGetVehicleTextureView(
                        materialState.MaterialChangeTexturePath,
                        out var materialChangeView))
                {
                    _deviceContext.PSSetShaderResource(
                        3,
                        materialChangeView!);
                }

                _deviceContext.PSSetShader(
                    materialState.AlphaCutout
                        ? hasTransMap
                            ? _vehicleAlphaCutoutTransMapPixelShader
                            : _vehicleAlphaCutoutPixelShader
                        : materialState.AlphaBlend
                            ? hasTransMap
                                ? _vehicleAlphaBlendTransMapPixelShader
                                : _vehicleAlphaBlendPixelShader
                            : _vehicleTexturedPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    textureView!);
            }
            else
            {
                if (requiresTextTexture ||
                    materialState.AlphaCutout ||
                    materialState.AlphaBlend)
                {
                    continue;
                }

                _deviceContext.PSSetShader(
                    _vehicleColorPixelShader);
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

        var allLightMeshes =
            vehicle.Meshes
                .Where(
                    static mesh =>
                        mesh.LightEffects is
                            { Count: > 0 })
                .ToArray();

        if (allLightMeshes.Length == 0)
        {
            return;
        }

        var viewpointBit =
            UseExteriorVehicleView()
                ? 1
                : 2;

        var viewpointMeshes =
            allLightMeshes
                .Where(
                    mesh =>
                        IsVehicleMeshVisibleFromViewpoint(
                            mesh.ViewpointFlag,
                            viewpointBit))
                .ToArray();

        var selectionSource =
            viewpointMeshes.Length > 0
                ? viewpointMeshes
                : allLightMeshes;

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
            foreach (var changeSet in
                     changeSets.OrderBy(
                         static set =>
                             set.GroupIndex))
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

                var item =
                    changeSet.Items
                        .FirstOrDefault(
                            candidate =>
                                candidate.ItemIndex ==
                                requestedItem);

                if (item is not null)
                {
                    selectedItem =
                        item;
                }
            }
        }

        var hasNativeItem =
            selectedItem is not null;

        var legacyActive =
            !hasNativeItem &&
            (batch.MaterialChangeSets is null ||
             batch.MaterialChangeSets.Count == 0) &&
            !string.IsNullOrWhiteSpace(
                batch.MaterialChangeVariable) &&
            double.IsFinite(
                ResolveSectionNumericValue(
                    batch.SectionIndex,
                    batch.MaterialChangeVariable)) &&
            ResolveSectionNumericValue(
                batch.SectionIndex,
                batch.MaterialChangeVariable) >= 0.5;

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
        if (string.IsNullOrWhiteSpace(
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
        if (string.IsNullOrWhiteSpace(
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
        if (string.IsNullOrWhiteSpace(
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

    private Matrix4x4 CreateViewProjection()
    {
        if (_viewProjectionOverride.HasValue)
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
                _vehicle.EngineRunning,
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

        UpdateVehicleAnimationStates(
            deltaSeconds);

        UpdateVehicleLightStates(
            deltaSeconds);
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

        // From this point the OMSI vehicle script is authoritative.
        // Do not overwrite engine/electrical/gearbox state after {init};
        // synchronize the x64 host from the state the original scripts
        // established, exactly as the runtime frame path does.
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
                    $"gearPreselect={ScriptValue(_scriptRuntime, "antrieb_getr_gangvorwahl")}");
                lines.Add(
                    $"gearActual={ScriptValue(_scriptRuntime, "antrieb_getr_gang")}");
                lines.Add(
                    $"gearRatio={ScriptValue(_scriptRuntime, "antrieb_getr_ratio_act")}");
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

    private bool ResolveSectionEngineRunning(
        OmsiScriptRuntime? runtime)
    {
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
            0.0);

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
            0.0);

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
        TriggerOmsiAudio(
            trigger);
    }

    private void OnScriptFileSoundTriggerRequested(
        string trigger,
        string declaredFile)
    {
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
        if (_vehicleRemoved ||
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

        // Compatibility fallback for older/add-on scripts where the write
        // cannot be statically identified (for example generated VM state).
        if (_scriptRuntime?.HasLocalVariable(
                "M_Wheel") ==
            true)
        {
            return _scriptRuntime;
        }

        return _sectionScriptRuntimes.Values
            .FirstOrDefault(
                static runtime =>
                    runtime.HasLocalVariable(
                        "M_Wheel"));
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

        if (_scriptRuntime.HasLocalVariable(
                "elec_busbar_main"))
        {
            _vehicle.SetElectricalSystemEnabled(
                _scriptRuntime.GetLocal(
                    "elec_busbar_main") >
                0.01);
        }
        else if (_scriptRuntime.HasLocalVariable(
                     "elec_busbar_main_sw"))
        {
            _vehicle.SetElectricalSystemEnabled(
                _scriptRuntime.GetLocal(
                    "elec_busbar_main_sw") >
                0.5);
        }

        if (_scriptRuntime.HasLocalVariable(
                "engine_on"))
        {
            _vehicle.SetEngineRunning(
                _scriptRuntime.GetLocal(
                    "engine_on") >
                0.5);
        }

        if (_scriptRuntime.HasLocalVariable(
                "bremse_feststell"))
        {
            _vehicle.SetParkingBrake(
                _scriptRuntime.GetLocal(
                    "bremse_feststell") >
                0.5);
        }

        if (_scriptRuntime.HasLocalVariable(
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

        // OMSI mouse steering is mirrored relative to runtime's
        // internal steering sign. User-facing behaviour must be:
        // mouse right -> vehicle right, mouse left -> vehicle left.
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

        var mode = _terrainVertexCount > 0
            ? $"terrain {_terrainVertexCount / 3:N0} triangles · {mirrorMode} · ground textures {_terrainGeometry.TexturedBatchCount:N0} · masks {_terrainGeometry.MaskedLayerCount:N0} · roads {_splineGeometry.RenderedSplineCount:N0} · road textures {_splineGeometry.TexturedBatchCount:N0} · runtime objects {_objectGeometry.RenderedObjectCount:N0}/{runtimeObjectCount:N0} · meshes {_objectGeometry.RenderedMeshCount:N0} · trees {_objectGeometry.RenderedTreeCount:N0} · textures {_objectTextureCache.Count:N0} loaded · texture failures {_failedObjectTexturePaths.Count:N0} · encrypted {_objectGeometry.ProtectedMeshCount:N0}{sceneryBudget}"
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

        control +=
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_omsiMenuBar is not null)
            {
                _omsiMenuBar.CommandInvoked -=
                    OnOmsiMenuCommandInvoked;
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
            _terrainVertexBuffer?.Dispose();
            _splineVertexBuffer?.Dispose();

            foreach (var texture in
                _objectTextureCache.Values)
            {
                texture.Dispose();
            }

            _objectTextureCache.Clear();
            _failedObjectTexturePaths.Clear();

            _vehicleTextTextureRenderer?.Dispose();
            _vehicleTextTextureRenderer = null;
            _vehicleAnimationParentBatches.Clear();

            _objectSampler?.Dispose();
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

            _tileInputLayout?.Dispose();
            _tilePixelShader?.Dispose();
            _tileVertexShader?.Dispose();
            _tileVertexBuffer?.Dispose();

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
