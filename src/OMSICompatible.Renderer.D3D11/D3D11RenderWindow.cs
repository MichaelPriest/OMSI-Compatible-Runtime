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
    private readonly Dictionary<int, float>
        _articulatedSectionAbsoluteHeadingRadians =
            [];
    private readonly Dictionary<int, float>
        _articulatedSectionYawRadians =
            [];

    private double _lastFrameTimeSeconds;
    private bool _graphicsPrepared;
    private bool _mouseLooking;
    private bool _mouseDriveMode;
    private bool _driveMode = true;
    private RuntimeVehicleViewMode _vehicleViewMode =
        RuntimeVehicleViewMode.Driver;
    private int _driverCameraIndex;
    private int _passengerCameraIndex;
    private float _mouseDriveAccelerator;
    private float _mouseDriveBrake;
    private float _mouseDriveSteering;
    private bool _simulationPaused;
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
        bool gameControllerEnabled = true)
    {
        _windowInfo = windowInfo;
        _scriptRuntime = scriptRuntime;
        _initialVehicleVariables =
            initialVehicleVariables is null
                ? null
                : new Dictionary<string, double>(
                    initialVehicleVariables,
                    StringComparer.OrdinalIgnoreCase);
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
        }

        _vehicle = new RuntimeDriveVehicle(
            windowInfo.Tiles,
            windowInfo.Vehicle?.Physics);
        _driveMode =
            windowInfo.Vehicle is not null &&
            !_vehiclePreviewMode;
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
        InitializeVehicleScripts();
        InitializeOmsiGameControllers();
        InitializeOmsiAudio();
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
                $"[audio] {_omsiAudio.ExistingFileCount}/{_omsiAudio.SoundCount} OMSI sound files resolved.");
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
                SamplerDescription.LinearClamp);

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

    private void DrawSky()
    {
        if (_deviceContext is null ||
            CurrentRenderTargetView is null ||
            _skyVertexShader is null ||
            _skyPixelShader is null ||
            _skySampler is null ||
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

        foreach (var batch in
                 _vehicleExteriorGeometry.Batches
                     .Concat(
                         _vehicleInteriorGeometry.Batches))
        {
            if (string.IsNullOrWhiteSpace(
                    batch.MeshIdentifier) ||
                _vehicleAnimationParentBatches.ContainsKey(
                    batch.MeshIdentifier))
            {
                continue;
            }

            _vehicleAnimationParentBatches[
                batch.MeshIdentifier] =
                batch;
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

        if (!_omsiMenuBar.Visible &&
            _mouseDriveMode)
        {
            DisableMouseDriveMode();
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
                if (_terrainGeometry.Vertices.Length >
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

        _deviceContext.PSSetSampler(
            0,
            _vehicleSampler);

        _deviceContext.PSSetConstantBuffer(
            2,
            _vehicleMaterialBuffer);

        _deviceContext.RSSetState(
            _terrainRasterizerState);

        foreach (var batch in
            geometry.Batches)
        {
            if (!IsVehicleBatchVisible(
                    batch) ||
                batch.VertexCount == 0)
            {
                continue;
            }

            var materialState =
                ResolveVehicleMaterialState(
                    batch);

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

            materialConstants[0] =
                new RuntimeVehicleMaterialConstants
                {
                    AlphaScale =
                        ResolveVehicleAlphaScale(
                            materialState.AlphaScaleVariable),
                    LightMapStrength =
                        ResolveVehicleLightMapStrength(
                            materialState.LightMapTexturePath,
                            materialState.LightMapVariable),
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
                materialState.NoZCheck
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
        var vehicle =
            _windowInfo.Vehicle;

        if (vehicle is null ||
            _deviceContext is null ||
            _renderTargetView is null ||
            _depthStencilView is null ||
            _vehicleLightVertexBuffer is null ||
            _vehicleModelBuffer is null ||
            _vehicleMaterialBuffer is null ||
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
                    mesh.VisibilityConditions))
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
                    staticTransform);

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
        IReadOnlyList<RuntimeVehicleVisibilityConditionInfo>? conditions)
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
                _scriptRuntime?.GetLocal(
                    condition.VariableName) ??
                0.0;

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
                    _scriptRuntime?.GetLocal(
                        changeSet.VariableName) ??
                    0.0;

                if (!double.IsFinite(
                        value))
                {
                    continue;
                }

                var rounded =
                    Math.Round(
                        value,
                        MidpointRounding.ToEven);

                if (rounded < 1.0 ||
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
                _scriptRuntime?.GetLocal(
                    batch.MaterialChangeVariable) ??
                0.0) &&
            (_scriptRuntime?.GetLocal(
                 batch.MaterialChangeVariable) ??
             0.0) >= 0.5;

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
        string? variableName)
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
            _scriptRuntime?.GetLocal(
                variableName) ??
            0.0;

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
        string? variableName)
    {
        if (string.IsNullOrWhiteSpace(
                variableName))
        {
            return 1.0f;
        }

        var value =
            _scriptRuntime?.GetLocal(
                variableName) ??
            0.0;

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

    private bool IsVehicleBatchVisible(
        RuntimeObjectBatch batch) =>
        AreVehicleVisibilityConditionsMet(
            batch.VisibilityConditions);

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
                batch.StaticTransform);

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
        Matrix4x4? staticTransform)
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
                    animation);

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

            if (Matrix4x4.Decompose(
                    converted,
                    out _,
                    out var rotation,
                    out pivot))
            {
                orientation =
                    Matrix4x4.CreateFromQuaternion(
                        rotation);
            }
            else
            {
                pivot =
                    new Vector3(
                        converted.M41,
                        converted.M42,
                        converted.M43);
            }
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

        orientation =
            originRotation *
            orientation;
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
        var sourceRotation =
            Matrix4x4.CreateRotationX(
                DegreesToRadians(
                    xDegrees)) *
            Matrix4x4.CreateRotationY(
                DegreesToRadians(
                    yDegrees)) *
            Matrix4x4.CreateRotationZ(
                DegreesToRadians(
                    zDegrees));

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
            _scriptRuntime?.GetStringLocal(
                definition.StringVariable) ??
            string.Empty;

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
            bindings.Count == 0 ||
            _scriptRuntime is null)
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
                _scriptRuntime.GetStringLocal(
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
            vehicle?.OutsideCameraCenter);
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
                _terrainGeometry);
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
                _terrainGeometry);
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
                _terrainGeometry);
        }

        return _vehicle.CreateChaseViewProjection(
            aspect,
            _terrainGeometry,
            _windowInfo.Vehicle?.OutsideCameraCenter);
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

        _omsiAudio?.Update(
            _scriptRuntime,
            IsInteriorSoundView(),
            _vehicle.EngineRunning);

        UpdateVehicleAnimationStates(
            deltaSeconds);

        UpdateVehicleLightStates(
            deltaSeconds);
    }

    private void ResetArticulatedSections()
    {
        _articulatedSectionAbsoluteHeadingRadians.Clear();
        _articulatedSectionYawRadians.Clear();

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

            var followerLength =
                Math.Clamp(
                    (float)section.FollowerLengthMeters,
                    1.0f,
                    15.0f);

            var headingDifference =
                NormalizeRadians(
                    parentHeading -
                    sectionHeading);

            var angularVelocity =
                _vehicle.SpeedMetersPerSecond /
                followerLength *
                MathF.Sin(
                    headingDifference);

            sectionHeading =
                NormalizeRadians(
                    sectionHeading +
                    angularVelocity *
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

            relativeYaw =
                Math.Clamp(
                    relativeYaw,
                    -maximumYaw,
                    maximumYaw);

            sectionHeading =
                NormalizeRadians(
                    parentHeading +
                    relativeYaw);

            _articulatedSectionAbsoluteHeadingRadians[
                section.Index] =
                sectionHeading;

            _articulatedSectionYawRadians[
                section.Index] =
                relativeYaw;
        }
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

        var pivot =
            new Vector3(
                (float)section.JointX,
                (float)section.JointY,
                (float)section.JointZ);

        var local =
            Matrix4x4.CreateTranslation(
                -pivot) *
            Matrix4x4.CreateRotationY(
                yaw) *
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
                    _scriptRuntime?.GetLocal(
                        animation.VariableName) ??
                    0.0;

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
                        light);

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
                _scriptRuntime?.GetLocal(
                    light.BrightnessVariable) ??
                0.0;
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
        RuntimeVehicleLightEffectInfo light)
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
            light);
    }

    private double ResolveVehicleAnimationValue(
        RuntimeVehicleAnimationInfo animation)
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
            _scriptRuntime?.GetLocal(
                animation.VariableName) ??
            0.0;

        return double.IsFinite(
                raw)
            ? raw
            : 0.0;
    }

    private bool HandleOmsiSystemMacro(
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

        return _previousSystemMacroHandler?.Invoke(
                   name,
                   context) ==
               true;
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

        // A freshly spawned OMSI bus starts electrically off, engine off,
        // neutral and with the parking brake applied. Some add-on init
        // scripts leave engine/electrical variables non-zero; normalize the
        // host-owned state before audio and the first frame are evaluated.
        WriteVehicleControlStateToScripts();

        WriteVehicleRuntimeStateDiagnostics();
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

        foreach (var binding in
                 _activeOmsiContinuousBindings)
        {
            DispatchOmsiScriptTrigger(
                binding.Trigger);
        }

        _scriptRuntime.ExecuteFrame();
        WriteVehicleControlStateToScripts();
    }

    private void WriteVehicleControlStateToScripts()
    {
        if (_scriptRuntime is null)
        {
            return;
        }

        var electrical =
            _vehicle.ElectricalSystemEnabled
                ? 1.0
                : 0.0;

        _scriptRuntime.SetLocal(
            "elec_busbar_main",
            electrical);
        _scriptRuntime.SetLocal(
            "elec_busbar_main_sw",
            electrical);
        _scriptRuntime.SetLocal(
            "engine_on",
            _vehicle.EngineRunning
                ? 1.0
                : 0.0);
        _scriptRuntime.SetLocal(
            "bremse_feststell",
            _vehicle.ParkingBrakeEngaged
                ? 1.0
                : 0.0);
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

        // Feed the steering angle with the same left/right sign used
        // by the driving input. model.cfg animation deltas already define
        // each mesh's own rotation direction, so host-side inversion makes
        // steering wheels and axle meshes turn the wrong way on many buses.
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

        var wheelRotation =
            _vehicle.WheelRotationRadians;
        var wheelRotationSpeedRpm =
            _vehicle.WheelRotationSpeedRpm;

        for (var axle = 0;
             axle < 4;
             axle++)
        {
            _scriptRuntime.SetLocal(
                $"Wheel_Rotation_{axle}_L",
                wheelRotation);
            _scriptRuntime.SetLocal(
                $"Wheel_Rotation_{axle}_R",
                wheelRotation);
            _scriptRuntime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_L",
                wheelRotationSpeedRpm);
            _scriptRuntime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_R",
                wheelRotationSpeedRpm);
        }

        _scriptRuntime.SetLocal(
            "Axle_Suspension_0_L",
            _vehicle.FrontLeftSuspensionMeters);
        _scriptRuntime.SetLocal(
            "Axle_Suspension_0_R",
            _vehicle.FrontRightSuspensionMeters);
        _scriptRuntime.SetLocal(
            "Axle_Suspension_1_L",
            _vehicle.RearLeftSuspensionMeters);
        _scriptRuntime.SetLocal(
            "Axle_Suspension_1_R",
            _vehicle.RearRightSuspensionMeters);
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

        var freeCameraMovementKey =
            !_driveMode &&
            e.KeyCode is
                Keys.W or
                Keys.A or
                Keys.D or
                Keys.Q or
                Keys.E or
                Keys.ShiftKey or
                Keys.ControlKey;

        var matchedOmsiBinding =
            !_vehiclePreviewMode &&
            !freeCameraMovementKey &&
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
            }

            return;
        }

        ApplyLegacyKeyboardFallback(
            e);
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

        if (!_driveMode)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.S:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ScrollViews);
                break;

            case Keys.P:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.PauseToggle);
                break;

            case Keys.C:
            case Keys.Space:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ResetAllViews);
                break;

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
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.ElectricalToggle);
                break;

            case Keys.M:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.EngineToggle);
                break;

            case Keys.D:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.GearDrive);
                break;

            case Keys.N:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.GearNeutral);
                break;

            case Keys.R:
                ApplyOmsiHostActionPress(
                    RuntimeOmsiHostInputAction.GearReverse);
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

        foreach (var binding in
                 released)
        {
            DispatchOmsiScriptTrigger(
                ReleaseTriggerName(
                    binding.Trigger));

            _activeOmsiPressedBindings.Remove(
                binding);

            _activeOmsiContinuousBindings.Remove(
                binding);
        }
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
                { } hostAction)
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

        _scriptRuntime?.ExecuteTrigger(
            trigger);

        _omsiAudio?.Trigger(
            trigger,
            _scriptRuntime,
            IsInteriorSoundView());
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
                "kw_m_enginestart" =>
                    RuntimeOmsiHostInputAction.EngineStart,
                "kw_m_engineshutdown" =>
                    RuntimeOmsiHostInputAction.EngineOff,
                "cp_batterietrennschalter_toggle" =>
                    RuntimeOmsiHostInputAction.ElectricalToggle,
                "view_interiorcam_plus" =>
                    RuntimeOmsiHostInputAction.InteriorViewNext,
                "view_interiorcam_minus" =>
                    RuntimeOmsiHostInputAction.InteriorViewPrevious,
                "view_reset_direction" =>
                    RuntimeOmsiHostInputAction.ResetDriverView,
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
                "kw_m_enginestart" or
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
                "pause";
    }

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
                _vehicle.ToggleEngine();
                break;

            case RuntimeOmsiHostInputAction.EngineStart:
                _vehicle.SetEngineRunning(
                    true);
                break;

            case RuntimeOmsiHostInputAction.EngineOff:
                _vehicle.SetEngineRunning(
                    false);
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
                if (_driveMode)
                {
                    ToggleMouseDriveMode();
                }

                break;

            case RuntimeOmsiHostInputAction.DriverView:
                if (_windowInfo.Vehicle is null)
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
                if (_windowInfo.Vehicle is null)
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
                if (_windowInfo.Vehicle is null)
                {
                    break;
                }

                _driveMode =
                    true;
                _vehicleViewMode =
                    RuntimeVehicleViewMode.Exterior;
                break;

            case RuntimeOmsiHostInputAction.FreeCameraView:
                if (_mouseDriveMode)
                {
                    DisableMouseDriveMode();
                }

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
                ActivateSpecialDriverCamera(
                    _windowInfo.Vehicle?
                        .StandardDriverCameraIndex);
                break;

            case RuntimeOmsiHostInputAction.ScrollViews:
                CycleOmsiMainView();
                break;

            case RuntimeOmsiHostInputAction.PauseToggle:
                _simulationPaused =
                    !_simulationPaused;
                break;

            case RuntimeOmsiHostInputAction.ResetAllViews:
                if (_windowInfo.Vehicle is not null)
                {
                    _driveMode =
                        true;
                    _vehicleViewMode =
                        RuntimeVehicleViewMode.Driver;
                    _driverCameraIndex =
                        Math.Max(
                            _windowInfo.Vehicle
                                .StandardDriverCameraIndex,
                            0);
                }
                else if (_terrainGeometry.Vertices.Length >
                         0)
                {
                    _camera.Reset(
                        _terrainGeometry);
                }

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
    }

    private static string ReleaseTriggerName(
        string trigger) =>
        trigger.EndsWith(
            "_off",
            StringComparison.OrdinalIgnoreCase)
            ? trigger
            : trigger +
              "_off";

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

        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        if (_driveMode &&
            _mouseDriveMode)
        {
            DisableMouseDriveMode();
            UpdateCaption();
            return;
        }

        if (_driveMode)
        {
            return;
        }

        _mouseLooking = true;
        _lastMousePosition = e.Location;
        Capture = true;
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

        if (e.Button != MouseButtons.Right ||
            _mouseDriveMode)
        {
            return;
        }

        _mouseLooking = false;

        if (!_driveMode)
        {
            Capture = false;
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

        if (_driveMode &&
            _mouseDriveMode)
        {
            UpdateOmsiMouseAxes(e.Location);
            return;
        }

        if (!_mouseLooking ||
            _driveMode)
        {
            return;
        }

        var deltaX =
            e.X - _lastMousePosition.X;
        var deltaY =
            e.Y - _lastMousePosition.Y;

        _lastMousePosition = e.Location;

        _camera.Rotate(
            deltaX,
            deltaY);
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
            return;
        }

        _camera.AdjustSpeed(
            e.Delta / 120.0f);
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

        // Screen X follows steering direction: moving the cross
        // left steers left; moving it right steers right.
        var horizontal =
            Math.Clamp(
                (location.X - centerX) /
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

        var gear = _vehicle.Gear switch
        {
            RuntimeDriveGear.Drive => "D",
            RuntimeDriveGear.Reverse => "R",
            _ => "N"
        };

        var driveInputMode =
            _mouseDriveMode
                ? "MOUSE: ←/→ steer · ↑ throttle · ↓ brake · RMB exit"
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
              $"{driveInputMode} · F1/F2/F3 view · ←/→ perspectives · Insert schedule · Home tickets · F4 free cam · F9 mirrors · D/N/R · E/M · Num. park · Tab free cam"
            : $"{pauseState}FREE CAM · WASD move · RMB look · Q/E vertical · R reset · F1/F2/F3 OMSI view · Tab OMSI drive";

        control +=
            " · Alt menu";

        Text =
            $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
            $"{_windowInfo.TileCount:N0}/{_windowInfo.TotalTileCount:N0} tiles — " +
            $"{runtimeObjectCount:N0} runtime objects · {_windowInfo.ObjectCount:N0} map entries — " +
            $"{_windowInfo.SplineCount:N0} splines — " +
            $"{mode} — {control}";
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

            _renderTimer.Stop();
            _renderTimer.Tick -=
                RenderTimerOnTick;
            _renderTimer.Dispose();

            _deviceContext?.ClearState();
            _deviceContext?.Flush();

            _skyTexture?.Dispose();
            _skyTexture = null;
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
