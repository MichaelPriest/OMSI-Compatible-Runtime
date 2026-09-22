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
        public float Padding;
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
    private readonly OmsiSystemMacroHandler? _previousSystemMacroHandler;
    private readonly HashSet<string> _reportedUnhandledSystemMacros =
        new(
            StringComparer.Ordinal);
    private readonly HashSet<Keys> _pressedKeys = [];
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    private double _lastFrameTimeSeconds;
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
    private ID3D11Buffer? _vehicleModelBuffer;
    private ID3D11Buffer? _vehicleMaterialBuffer;
    private ID3D11VertexShader? _vehicleVertexShader;
    private ID3D11PixelShader? _vehicleColorPixelShader;
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

    private FeatureLevel _featureLevel;

    public D3D11RenderWindow(
        RuntimeWindowInfo windowInfo,
        OmsiScriptRuntime? scriptRuntime = null)
    {
        _windowInfo = windowInfo;
        _scriptRuntime = scriptRuntime;
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
        _driverCameraIndex =
            Math.Max(
                windowInfo.Vehicle?.StandardDriverCameraIndex ?? 0,
                0);

        Text = $"OMSI Compatible Runtime — {windowInfo.WorldName}";
        ClientSize = new System.Drawing.Size(1280, 720);
        MinimumSize = new System.Drawing.Size(960, 540);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        KeyDown += OnRuntimeKeyDown;
        KeyUp += OnRuntimeKeyUp;
        MouseDown += OnRuntimeMouseDown;
        MouseUp += OnRuntimeMouseUp;
        MouseMove += OnRuntimeMouseMove;
        MouseWheel += OnRuntimeMouseWheel;

        _renderTimer = new System.Windows.Forms.Timer
        {
            Interval = 16
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
                _windowInfo.SceneryAssets);

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

    private void OnWindowShown(object? sender, EventArgs e)
    {
        try
        {
            InitializeGraphics();
            InitializeVehicleScripts();
            UpdateCaption();
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
                _windowInfo.SceneryAssets);

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

        _vehicleExteriorGeometry =
            RuntimeVehicleGeometry.Build(
                _windowInfo.Vehicle,
                viewpointBit: 1);

        _vehicleInteriorGeometry =
            RuntimeVehicleGeometry.Build(
                _windowInfo.Vehicle,
                viewpointBit: 2);

        Console.WriteLine(
            $"[vehicle-geometry] exteriorVertices={_vehicleExteriorGeometry.Vertices.Length}; " +
            $"exteriorMeshes={_vehicleExteriorGeometry.RenderedMeshCount}; " +
            $"interiorVertices={_vehicleInteriorGeometry.Vertices.Length}; " +
            $"interiorMeshes={_vehicleInteriorGeometry.RenderedMeshCount}; " +
            $"protected={Math.Max(_vehicleExteriorGeometry.ProtectedMeshCount, _vehicleInteriorGeometry.ProtectedMeshCount)}; " +
            $"failed={Math.Max(_vehicleExteriorGeometry.MissingMeshCount, _vehicleInteriorGeometry.MissingMeshCount)}");

        AppendVehicleGeometryDiagnostics();

        if (_vehicleExteriorGeometry.Vertices.Length == 0 &&
            _vehicleInteriorGeometry.Vertices.Length == 0)
        {
            Console.WriteLine(
                "[vehicle-geometry] No real vehicle geometry could be built; exterior proxy fallback will be used.");
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
                CreateObjectInputElements(),
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

        _vehicle.Reset(
            _windowInfo.Splines,
            _terrainGeometry,
            _windowInfo.Spawn);
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

    private void OnClientSizeChanged(
        object? sender,
        EventArgs e)
    {
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
        UpdateSimulation();
        CheckStreamingCenter();
        RenderFrame();

        _captionFrame++;
        if (_captionFrame >= 15)
        {
            _captionFrame = 0;
            UpdateCaption();
        }
    }

    private void CheckStreamingCenter()
    {
        if (!_driveMode ||
            !_windowInfo.ActiveTileRadius.HasValue)
        {
            return;
        }

        var tileX =
            (int)Math.Floor(
                _vehicle.Position.X /
                300.0f);

        var tileY =
            (int)Math.Floor(
                _vehicle.Position.Z /
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
                0.025f,
                0.035f,
                0.055f,
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

        if (CanDrawTerrain())
        {
            DrawTerrain();
            DrawSplines();
            DrawObjects();
            DrawVehicle();
        }
        else
        {
            DrawTileOverview();
        }

        _swapChain.Present(
            1,
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
                    CreateViewProjection()
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

    private void DrawVehicle()
    {
        // Geometry selection must follow the camera that is actually in use.
        // If an OMSI .bus has no valid F1/F2 cameras, CreateViewProjection()
        // falls back to the chase camera; drawing the interior geometry in
        // that case makes the vehicle appear to be missing.
        var useExteriorGeometry =
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

            model[0] =
                new RuntimeModelConstants
                {
                    World =
                        CreateVehicleAnimationMatrix(
                            batch) *
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
                            batch),
                    LightMapStrength =
                        ResolveVehicleLightMapStrength(
                            batch),
                    MaterialChangeStrength =
                        ResolveVehicleMaterialChangeStrength(
                            batch),
                    Padding =
                        0.0f
                };

            _vehicleMaterialBuffer.SetData(
                _deviceContext,
                materialConstants,
                MapMode.WriteDiscard);

            _deviceContext.OMSetBlendState(
                batch.AlphaBlend
                    ? _vehicleAlphaBlendState
                    : null);

            _deviceContext.OMSetDepthStencilState(
                batch.NoZCheck
                    ? _vehicleDepthDisabledState
                    : batch.NoZWrite
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

            if (TryGetVehicleTextureView(
                    batch.TexturePath,
                    out var textureView))
            {
                var hasTransMap =
                    TryGetVehicleTextureView(
                        batch.TransMapTexturePath,
                        out var transMapView);

                if (hasTransMap)
                {
                    _deviceContext.PSSetShaderResource(
                        1,
                        transMapView!);
                }

                if (TryGetVehicleTextureView(
                        batch.LightMapTexturePath,
                        out var lightMapView))
                {
                    _deviceContext.PSSetShaderResource(
                        2,
                        lightMapView!);
                }

                if (TryGetVehicleTextureView(
                        batch.MaterialChangeTexturePath,
                        out var materialChangeView))
                {
                    _deviceContext.PSSetShaderResource(
                        3,
                        materialChangeView!);
                }

                _deviceContext.PSSetShader(
                    batch.AlphaCutout
                        ? hasTransMap
                            ? _vehicleAlphaCutoutTransMapPixelShader
                            : _vehicleAlphaCutoutPixelShader
                        : batch.AlphaBlend
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
                if (batch.AlphaCutout ||
                    batch.AlphaBlend)
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
        _deviceContext.RSSetState(null);
    }

    private float ResolveVehicleMaterialChangeStrength(
        RuntimeObjectBatch batch)
    {
        if (string.IsNullOrWhiteSpace(
                batch.MaterialChangeTexturePath) ||
            string.IsNullOrWhiteSpace(
                batch.MaterialChangeVariable))
        {
            return 0.0f;
        }

        var value =
            _scriptRuntime?.GetLocal(
                batch.MaterialChangeVariable) ??
            0.0;

        return double.IsFinite(
                   value) &&
               value >= 0.5
            ? 1.0f
            : 0.0f;
    }

    private float ResolveVehicleLightMapStrength(
        RuntimeObjectBatch batch)
    {
        if (string.IsNullOrWhiteSpace(
                batch.LightMapTexturePath))
        {
            return 0.0f;
        }

        if (string.IsNullOrWhiteSpace(
                batch.LightMapVariable))
        {
            return 1.0f;
        }

        var value =
            _scriptRuntime?.GetLocal(
                batch.LightMapVariable) ??
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
        RuntimeObjectBatch batch)
    {
        if (string.IsNullOrWhiteSpace(
                batch.AlphaScaleVariable))
        {
            return 1.0f;
        }

        var value =
            _scriptRuntime?.GetLocal(
                batch.AlphaScaleVariable) ??
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
        RuntimeObjectBatch batch)
    {
        var conditions =
            batch.VisibilityConditions;

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

    private Matrix4x4 CreateVehicleAnimationMatrix(
        RuntimeObjectBatch batch)
    {
        var animations =
            batch.Animations;

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
                _scriptRuntime?.GetLocal(
                    animation.VariableName) ??
                0.0;

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
                batch,
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
        RuntimeObjectBatch batch,
        RuntimeVehicleAnimationInfo animation,
        out Vector3 pivot,
        out Matrix4x4 orientation)
    {
        orientation =
            Matrix4x4.Identity;

        if (animation.OriginFromMesh &&
            batch.SourceTransform is
                Matrix4x4 sourceTransform)
        {
            var mirror =
                Matrix4x4.CreateScale(
                    -1.0f,
                    1.0f,
                    1.0f);

            var converted =
                mirror *
                sourceTransform *
                mirror;

            if (batch.StaticTransform is
                Matrix4x4 staticTransform)
            {
                converted *=
                    staticTransform;
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

        return false;
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
            else
            {
                var steeringDirection =
                    (_pressedKeys.Contains(Keys.NumPad6) ? 1.0f : 0.0f) -
                    (_pressedKeys.Contains(Keys.NumPad4) ? 1.0f : 0.0f);

                _vehicle.UpdateOmsiControls(
                    acceleratorHeld:
                        _pressedKeys.Contains(Keys.NumPad8),
                    brakeIncreaseHeld:
                        _pressedKeys.Contains(Keys.NumPad2),
                    brakeReleaseHeld:
                        _pressedKeys.Contains(Keys.Add),
                    steeringDirection:
                        steeringDirection,
                    centerSteeringHeld:
                        _pressedKeys.Contains(Keys.NumPad5),
                    deltaSeconds:
                        deltaSeconds);
            }
        }
        else
        {
            var forward =
                (_pressedKeys.Contains(Keys.W) ? 1.0f : 0.0f) -
                (_pressedKeys.Contains(Keys.S) ? 1.0f : 0.0f);

            var right =
                (_pressedKeys.Contains(Keys.D) ? 1.0f : 0.0f) -
                (_pressedKeys.Contains(Keys.A) ? 1.0f : 0.0f);

            var up =
                (_pressedKeys.Contains(Keys.E) ? 1.0f : 0.0f) -
                (_pressedKeys.Contains(Keys.Q) ? 1.0f : 0.0f);

            _camera.Move(
                forward,
                right,
                up,
                deltaSeconds,
                _pressedKeys.Contains(Keys.ShiftKey),
                _pressedKeys.Contains(Keys.ControlKey));
        }

        UpdateVehicleScripts(
            deltaSeconds,
            now);
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

        _scriptRuntime.ExecuteFrame();
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

        _scriptRuntime.SetLocal(
            "Throttle",
            _vehicle.AcceleratorLevel);
        _scriptRuntime.SetLocal(
            "Brake",
            _vehicle.BrakeLevel);
        _scriptRuntime.SetLocal(
            "Velocity",
            _vehicle.SpeedKph);
        _scriptRuntime.SetLocal(
            "Velocity_Ground",
            _vehicle.SpeedKph);

        _scriptRuntime.SetLocal(
            "Axle_Steering_0_L",
            _vehicle.SteeringAngleRadians);
        _scriptRuntime.SetLocal(
            "Axle_Steering_0_R",
            _vehicle.SteeringAngleRadians);
    }

    private void OnRuntimeKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        var firstPress =
            _pressedKeys.Add(e.KeyCode);

        if (!firstPress)
        {
            return;
        }

        if (e.KeyCode == Keys.F1)
        {
            _driveMode = true;
            _vehicleViewMode =
                RuntimeVehicleViewMode.Driver;

            if (_windowInfo.Vehicle is not null)
            {
                _driverCameraIndex =
                    Math.Max(
                        _windowInfo.Vehicle.StandardDriverCameraIndex,
                        0);
            }

            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.F2)
        {
            _driveMode = true;
            _vehicleViewMode =
                _windowInfo.Vehicle?.PassengerCameras.Count > 0
                    ? RuntimeVehicleViewMode.Passenger
                    : RuntimeVehicleViewMode.Driver;
            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.F3)
        {
            _driveMode = true;
            _vehicleViewMode =
                RuntimeVehicleViewMode.Exterior;
            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.F4)
        {
            if (_mouseDriveMode)
            {
                DisableMouseDriveMode();
            }

            _driveMode = false;
            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.F9)
        {
            _reflectionRenderingEnabled =
                !_reflectionRenderingEnabled;

            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Insert)
        {
            ActivateSpecialDriverCamera(
                _windowInfo.Vehicle?.ScheduleDriverCameraIndex);
            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Home)
        {
            ActivateSpecialDriverCamera(
                _windowInfo.Vehicle?.TicketSellingDriverCameraIndex);
            UpdateCaption();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Tab)
        {
            if (_mouseDriveMode)
            {
                DisableMouseDriveMode();
            }

            _driveMode = !_driveMode;
            e.SuppressKeyPress = true;
            UpdateCaption();
            return;
        }

        if (_driveMode)
        {
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                CycleInteriorCamera(
                    e.KeyCode == Keys.Left
                        ? 1
                        : -1);
                UpdateCaption();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.O)
            {
                ToggleMouseDriveMode();
                UpdateCaption();
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.E:
                    _vehicle.ToggleElectricalSystem();
                    break;

                case Keys.M:
                    _vehicle.ToggleEngine();
                    break;

                case Keys.D:
                    _vehicle.SelectGear(
                        RuntimeDriveGear.Drive);
                    break;

                case Keys.N:
                    _vehicle.SelectGear(
                        RuntimeDriveGear.Neutral);
                    break;

                case Keys.R:
                    _vehicle.SelectGear(
                        RuntimeDriveGear.Reverse);
                    break;

                case Keys.Decimal:
                case Keys.OemPeriod:
                    _vehicle.ToggleParkingBrake();
                    break;

                case Keys.Subtract:
                    _vehicle.ToggleStopBrake();
                    break;

                case Keys.F5:
                    if (_terrainGeometry.Vertices.Length > 0)
                    {
                        _vehicle.Reset(
                            _windowInfo.Splines,
                            _terrainGeometry,
                            _windowInfo.Spawn);
                    }

                    break;
            }

            UpdateCaption();
            return;
        }

        if ((e.KeyCode == Keys.R ||
             e.KeyCode == Keys.F5) &&
            _terrainGeometry.Vertices.Length > 0)
        {
            _camera.Reset(
                _terrainGeometry);
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
        _pressedKeys.Remove(e.KeyCode);
    }

    private void OnRuntimeMouseDown(
        object? sender,
        MouseEventArgs e)
    {
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

        Cursor.Hide();

        var center =
            new System.Drawing.Point(
                ClientSize.Width / 2,
                ClientSize.Height / 2);

        Cursor.Position =
            PointToScreen(center);
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

        Cursor.Show();
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

        _mouseDriveSteering =
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

        const float deadZone = 0.05f;

        if (Math.Abs(_mouseDriveSteering) < deadZone)
        {
            _mouseDriveSteering = 0.0f;
        }

        if (Math.Abs(vertical) < deadZone)
        {
            vertical = 0.0f;
        }

        _mouseDriveAccelerator =
            Math.Max(
                vertical,
                0.0f);

        _mouseDriveBrake =
            Math.Max(
                -vertical,
                0.0f);
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
        var sceneryBudget =
            _objectGeometry.HitVertexBudget
                ? " · object-budget"
                : string.Empty;

        var mirrorMode =
            _reflectionRenderingEnabled
                ? $"mirrors ON {_reflectionTargets.Count:N0}"
                : $"mirrors OFF {_reflectionTargets.Count:N0}";

        var mode = _terrainVertexCount > 0
            ? $"terrain {_terrainVertexCount / 3:N0} triangles · {mirrorMode} · ground textures {_terrainGeometry.TexturedBatchCount:N0} · masks {_terrainGeometry.MaskedLayerCount:N0} · roads {_splineGeometry.RenderedSplineCount:N0} · road textures {_splineGeometry.TexturedBatchCount:N0} · rendered objects {_objectGeometry.RenderedObjectCount:N0}/{_windowInfo.ObjectCount:N0} · meshes {_objectGeometry.RenderedMeshCount:N0} · trees {_objectGeometry.RenderedTreeCount:N0} · textures {_objectTextureCache.Count:N0}/{_objectGeometry.TexturedBatchCount:N0} · protected {_objectGeometry.ProtectedMeshCount:N0}{sceneryBudget}"
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
                : "KEYBOARD: Num8 throttle · Num2 brake · Num+ release · Num4/6 steer · Num5 center · O mouse";

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

        var control = _driveMode
            ? $"OMSI DRIVE · {vehicleView} · {_vehicle.SpeedKph:0} km/h · gear {gear} · " +
              $"E:{(_vehicle.ElectricalSystemEnabled ? "ON" : "OFF")} " +
              $"M:{(_vehicle.EngineRunning ? "ON" : "OFF")} · " +
              $"brake {_vehicle.BrakeLevel * 100.0f:0}% · " +
              $"park:{(_vehicle.ParkingBrakeEngaged ? "ON" : "OFF")} · " +
              $"{driveInputMode} · F1/F2/F3 view · ←/→ perspectives · Insert schedule · Home tickets · F4 free cam · F9 mirrors · D/N/R · E/M · Num. park · Tab free cam"
            : "FREE CAM · WASD move · RMB look · Q/E vertical · R reset · F1/F2/F3 OMSI view · Tab OMSI drive";

        Text =
            $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
            $"{_windowInfo.TileCount:N0}/{_windowInfo.TotalTileCount:N0} tiles — " +
            $"{_windowInfo.ObjectCount:N0} objects — " +
            $"{_windowInfo.SplineCount:N0} splines — " +
            $"{mode} — {control}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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

            _renderTimer.Stop();
            _renderTimer.Tick -=
                RenderTimerOnTick;
            _renderTimer.Dispose();

            _deviceContext?.ClearState();
            _deviceContext?.Flush();

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
            _vehicleColorPixelShader?.Dispose();
            _vehicleVertexShader?.Dispose();
            _vehicleMaterialBuffer?.Dispose();
            _vehicleModelBuffer?.Dispose();
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
