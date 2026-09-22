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

    private static readonly FeatureLevel[] RequestedFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    private readonly RuntimeWindowInfo _windowInfo;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly RuntimeFreeCamera _camera = new();
    private readonly HashSet<Keys> _pressedKeys = [];
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    private double _lastFrameTimeSeconds;
    private bool _mouseLooking;
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
    private ID3D11InputLayout? _terrainInputLayout;
    private ID3D11RasterizerState? _terrainRasterizerState;
    private RuntimeTerrainGeometry _terrainGeometry =
        RuntimeTerrainGeometry.Empty;
    private uint _terrainVertexCount;

    private FeatureLevel _featureLevel;

    public D3D11RenderWindow(RuntimeWindowInfo windowInfo)
    {
        _windowInfo = windowInfo;

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
                _windowInfo.Tiles);

        if (_terrainGeometry.Vertices.Length == 0)
        {
            return;
        }

        _terrainVertexBuffer = _device.CreateBuffer(
            _terrainGeometry.Vertices.AsSpan(),
            BindFlags.VertexBuffer);

        var shaderFile = ShaderPath("RuntimeTerrain.hlsl");

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

        _terrainVertexShader =
            _device.CreateVertexShader(
                vertexShaderByteCode.Span);
        _terrainPixelShader =
            _device.CreatePixelShader(
                pixelShaderByteCode.Span);
        _terrainInputLayout =
            _device.CreateInputLayout(
                CreateInputElements(),
                vertexShaderByteCode.Span);

        _terrainCameraBuffer =
            _device.CreateConstantBuffer<
                RuntimeCameraConstants>();

        _terrainRasterizerState =
            _device.CreateRasterizerState(
                RasterizerDescription.CullNone);

        _terrainVertexCount =
            (uint)_terrainGeometry.Vertices.Length;

        _camera.Reset(_terrainGeometry);
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
        UpdateCamera();
        RenderFrame();
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
            _terrainInputLayout is null)
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
        _deviceContext.PSSetShader(
            _terrainPixelShader);
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

        _deviceContext.Draw(
            _terrainVertexCount,
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

        return _camera.CreateViewProjection(
            aspect,
            _terrainGeometry);
    }

    private void UpdateCamera()
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
        _pressedKeys.Add(e.KeyCode);

        if (e.KeyCode == Keys.R &&
            _terrainGeometry.Vertices.Length > 0)
        {
            _camera.Reset(_terrainGeometry);
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

        _mouseLooking = true;
        _lastMousePosition = e.Location;
        Capture = true;
    }

    private void OnRuntimeMouseUp(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        _mouseLooking = false;
        Capture = false;
    }

    private void OnRuntimeMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (!_mouseLooking)
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
        _camera.AdjustSpeed(
            e.Delta / 120.0f);
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
        var mode = _terrainVertexCount > 0
            ? $"terrain {_terrainVertexCount / 3:N0} triangles"
            : "tile overview";

        Text =
            $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
            $"{_windowInfo.TileCount:N0} tiles — " +
            $"{_windowInfo.ObjectCount:N0} objects — " +
            $"{_windowInfo.SplineCount:N0} splines — " +
            $"{mode} — D3D11 {_featureLevel} — " +
            "WASD move · RMB look · Q/E vertical · R reset";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Tick -=
                RenderTimerOnTick;
            _renderTimer.Dispose();

            _deviceContext?.ClearState();
            _deviceContext?.Flush();

            _terrainRasterizerState?.Dispose();
            _terrainInputLayout?.Dispose();
            _terrainPixelShader?.Dispose();
            _terrainVertexShader?.Dispose();
            _terrainCameraBuffer?.Dispose();
            _terrainVertexBuffer?.Dispose();

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
