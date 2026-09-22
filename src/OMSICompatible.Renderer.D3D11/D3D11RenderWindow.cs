using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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

    private static readonly FeatureLevel[] RequestedFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    private readonly RuntimeWindowInfo _windowInfo;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly RuntimeFreeCamera _camera = new();
    private readonly RuntimeDriveVehicle _vehicle;
    private readonly HashSet<Keys> _pressedKeys = [];
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    private double _lastFrameTimeSeconds;
    private bool _mouseLooking;
    private bool _mouseDriveMode;
    private bool _driveMode = true;
    private float _mouseDriveAccelerator;
    private float _mouseDriveBrake;
    private float _mouseDriveSteering;
    private int _captionFrame;
    private System.Drawing.Point _lastMousePosition;

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

    private ID3D11Buffer? _vehicleVertexBuffer;
    private ID3D11Buffer? _vehicleModelBuffer;
    private ID3D11VertexShader? _vehicleVertexShader;
    private ID3D11PixelShader? _vehiclePixelShader;
    private ID3D11InputLayout? _vehicleInputLayout;
    private uint _vehicleVertexCount;

    private FeatureLevel _featureLevel;

    public D3D11RenderWindow(RuntimeWindowInfo windowInfo)
    {
        _windowInfo = windowInfo;
        _vehicle = new RuntimeDriveVehicle(windowInfo.Tiles);

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

    private void OnWindowShown(object? sender, EventArgs e)
    {
        try
        {
            InitializeGraphics();
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
                .Select(
                    static batch =>
                        batch.TexturePath)
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

        var vertices =
            RuntimeVehicleGeometry.BuildBusProxy();

        _vehicleVertexBuffer =
            _device.CreateBuffer(
                vertices.AsSpan(),
                BindFlags.VertexBuffer);

        var shaderFile =
            ShaderPath("RuntimeVehicle.hlsl");

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

        _vehicleVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);
        _vehiclePixelShader =
            _device.CreatePixelShader(
                pixelShaderByteCode.Span);
        _vehicleInputLayout =
            _device.CreateInputLayout(
                CreateInputElements(),
                vertexShaderByteCode.Span);

        _vehicleModelBuffer =
            _device.CreateConstantBuffer<
                RuntimeModelConstants>();

        _vehicleVertexCount =
            (uint)vertices.Length;

        _vehicle.Reset(
            _windowInfo.Splines,
            _terrainGeometry);
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
        RenderFrame();

        _captionFrame++;
        if (_captionFrame >= 15)
        {
            _captionFrame = 0;
            UpdateCaption();
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
            _renderTargetView is null ||
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
            _renderTargetView,
            _depthStencilView);

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
        _deviceContext.RSSetState(null);
    }

    private void DrawSplines()
    {
        if (_deviceContext is null ||
            _renderTargetView is null ||
            _splineVertexBuffer is null ||
            _terrainCameraBuffer is null ||
            _objectVertexShader is null ||
            _objectColorPixelShader is null ||
            _objectTexturedPixelShader is null ||
            _objectAlphaCutoutPixelShader is null ||
            _objectInputLayout is null ||
            _objectSampler is null ||
            _splineVertexCount == 0)
        {
            return;
        }

        _deviceContext.OMSetRenderTargets(
            _renderTargetView,
            _depthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);
        _deviceContext.IASetInputLayout(
            _objectInputLayout);
        _deviceContext.IASetVertexBuffer(
            0,
            _splineVertexBuffer,
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

        foreach (var batch in
            _splineGeometry.Batches)
        {
            if (batch.VertexCount == 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(
                    batch.TexturePath) &&
                _objectTextureCache.TryGetValue(
                    batch.TexturePath,
                    out var texture))
            {
                _deviceContext.PSSetShader(
                    batch.AlphaCutout
                        ? _objectAlphaCutoutPixelShader
                        : _objectTexturedPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    texture.View);
            }
            else
            {
                _deviceContext.PSSetShader(
                    _objectColorPixelShader);

                _deviceContext.PSUnsetShaderResource(
                    0);
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.PSUnsetShaderResource(
            0);
        _deviceContext.RSSetState(null);
    }

    private void DrawObjects()
    {
        if (_deviceContext is null ||
            _renderTargetView is null ||
            _objectVertexBuffer is null ||
            _terrainCameraBuffer is null ||
            _objectVertexShader is null ||
            _objectColorPixelShader is null ||
            _objectTexturedPixelShader is null ||
            _objectAlphaCutoutPixelShader is null ||
            _objectInputLayout is null ||
            _objectSampler is null ||
            _objectVertexCount == 0)
        {
            return;
        }

        _deviceContext.OMSetRenderTargets(
            _renderTargetView,
            _depthStencilView);

        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);
        _deviceContext.IASetInputLayout(
            _objectInputLayout);
        _deviceContext.IASetVertexBuffer(
            0,
            _objectVertexBuffer,
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

        foreach (var batch in
            _objectGeometry.Batches)
        {
            if (batch.VertexCount == 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(
                    batch.TexturePath) &&
                _objectTextureCache.TryGetValue(
                    batch.TexturePath,
                    out var texture))
            {
                _deviceContext.PSSetShader(
                    batch.AlphaCutout
                        ? _objectAlphaCutoutPixelShader
                        : _objectTexturedPixelShader);

                _deviceContext.PSSetShaderResource(
                    0,
                    texture.View);
            }
            else
            {
                _deviceContext.PSSetShader(
                    _objectColorPixelShader);

                _deviceContext.PSUnsetShaderResource(
                    0);
            }

            _deviceContext.Draw(
                batch.VertexCount,
                batch.StartVertex);
        }

        _deviceContext.PSUnsetShaderResource(
            0);
        _deviceContext.RSSetState(null);
    }

    private void DrawVehicle()
    {
        if (_deviceContext is null ||
            _renderTargetView is null ||
            _vehicleVertexBuffer is null ||
            _vehicleModelBuffer is null ||
            _vehicleVertexShader is null ||
            _vehiclePixelShader is null ||
            _vehicleInputLayout is null ||
            _terrainCameraBuffer is null ||
            _vehicleVertexCount == 0)
        {
            return;
        }

        Span<RuntimeModelConstants> model =
            stackalloc RuntimeModelConstants[1];

        model[0] =
            new RuntimeModelConstants
            {
                World =
                    _vehicle.CreateWorldMatrix()
            };

        _vehicleModelBuffer.SetData(
            _deviceContext,
            model,
            MapMode.WriteDiscard);

        _deviceContext.OMSetRenderTargets(
            _renderTargetView,
            _depthStencilView);
        _deviceContext.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);
        _deviceContext.IASetInputLayout(
            _vehicleInputLayout);
        _deviceContext.IASetVertexBuffer(
            0,
            _vehicleVertexBuffer,
            RuntimeVehicleVertex.SizeInBytes);

        _deviceContext.VSSetShader(
            _vehicleVertexShader);
        _deviceContext.PSSetShader(
            _vehiclePixelShader);
        _deviceContext.VSSetConstantBuffer(
            0,
            _terrainCameraBuffer);
        _deviceContext.VSSetConstantBuffer(
            1,
            _vehicleModelBuffer);
        _deviceContext.RSSetState(
            _terrainRasterizerState);

        _deviceContext.Draw(
            _vehicleVertexCount,
            0);

        _deviceContext.RSSetState(null);
    }

    private Matrix4x4 CreateViewProjection()
    {
        var aspect =
            Math.Max(ClientSize.Width, 1) /
            (float)Math.Max(
                ClientSize.Height,
                1);

        return _driveMode
            ? _vehicle.CreateChaseViewProjection(
                aspect,
                _terrainGeometry)
            : _camera.CreateViewProjection(
                aspect,
                _terrainGeometry);
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

                return;
            }

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

            return;
        }

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
                            _terrainGeometry);
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

        var mode = _terrainVertexCount > 0
            ? $"terrain {_terrainVertexCount / 3:N0} triangles · ground textures {_terrainGeometry.TexturedBatchCount:N0} · masks {_terrainGeometry.MaskedLayerCount:N0} · roads {_splineGeometry.RenderedSplineCount:N0} · road textures {_splineGeometry.TexturedBatchCount:N0} · rendered objects {_objectGeometry.RenderedObjectCount:N0}/{_windowInfo.ObjectCount:N0} · meshes {_objectGeometry.RenderedMeshCount:N0} · trees {_objectGeometry.RenderedTreeCount:N0} · textures {_objectTextureCache.Count:N0}/{_objectGeometry.TexturedBatchCount:N0} · protected {_objectGeometry.ProtectedMeshCount:N0}{sceneryBudget}"
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

        var control = _driveMode
            ? $"OMSI DRIVE {_vehicle.SpeedKph:0} km/h · gear {gear} · " +
              $"E:{(_vehicle.ElectricalSystemEnabled ? "ON" : "OFF")} " +
              $"M:{(_vehicle.EngineRunning ? "ON" : "OFF")} · " +
              $"brake {_vehicle.BrakeLevel * 100.0f:0}% · " +
              $"park:{(_vehicle.ParkingBrakeEngaged ? "ON" : "OFF")} · " +
              $"{driveInputMode} · D/N/R · E/M · Num. park · Tab free cam"
            : "FREE CAM · WASD move · RMB look · Q/E vertical · R reset · Tab OMSI drive";

        Text =
            $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
            $"{_windowInfo.TileCount:N0} tiles — " +
            $"{_windowInfo.ObjectCount:N0} objects — " +
            $"{_windowInfo.SplineCount:N0} splines — " +
            $"{mode} — {control}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
            _objectInputLayout?.Dispose();
            _objectAlphaCutoutPixelShader?.Dispose();
            _objectTexturedPixelShader?.Dispose();
            _objectColorPixelShader?.Dispose();
            _objectVertexShader?.Dispose();
            _objectVertexBuffer?.Dispose();

            _vehicleInputLayout?.Dispose();
            _vehiclePixelShader?.Dispose();
            _vehicleVertexShader?.Dispose();
            _vehicleModelBuffer?.Dispose();
            _vehicleVertexBuffer?.Dispose();

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
