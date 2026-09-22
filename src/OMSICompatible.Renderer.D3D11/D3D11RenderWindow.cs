using System.Numerics;
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
    private static readonly FeatureLevel[] RequestedFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    private readonly RuntimeWindowInfo _windowInfo;
    private readonly System.Windows.Forms.Timer _renderTimer;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _deviceContext;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11RenderTargetView? _renderTargetView;

    private ID3D11Buffer? _tileVertexBuffer;
    private ID3D11VertexShader? _tileVertexShader;
    private ID3D11PixelShader? _tilePixelShader;
    private ID3D11InputLayout? _tileInputLayout;
    private uint _tileVertexCount;

    private FeatureLevel _featureLevel;

    public D3D11RenderWindow(RuntimeWindowInfo windowInfo)
    {
        _windowInfo = windowInfo;

        Text = $"OMSI Compatible Runtime — {windowInfo.WorldName}";
        ClientSize = new System.Drawing.Size(1280, 720);
        MinimumSize = new System.Drawing.Size(960, 540);
        StartPosition = FormStartPosition.CenterScreen;

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
        CreateBackBuffer();
        CreateTileOverviewResources();
    }

    private static IDXGIAdapter1 GetHardwareAdapter(IDXGIFactory2 factory)
    {
        for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
        {
            if (adapter is null)
            {
                continue;
            }

            if ((adapter.Description1.Flags & AdapterFlags.Software) == AdapterFlags.None)
            {
                return adapter;
            }

            adapter.Dispose();
        }

        throw new InvalidOperationException("No Direct3D 11 hardware adapter was found.");
    }

    private void CreateSwapChain()
    {
        if (_factory is null || _device is null)
        {
            throw new InvalidOperationException("D3D11 device is not initialized.");
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

    private void CreateBackBuffer()
    {
        if (_swapChain is null || _device is null)
        {
            return;
        }

        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(_backBuffer);
    }

    private void CreateTileOverviewResources()
    {
        if (_device is null)
        {
            throw new InvalidOperationException("D3D11 device is not initialized.");
        }

        var vertices = BuildTileVertices(_windowInfo.Tiles);
        if (vertices.Length == 0)
        {
            return;
        }

        _tileVertexBuffer = _device.CreateBuffer(
            vertices.AsSpan(),
            BindFlags.VertexBuffer);

        var shaderFile = Path.Combine(
            AppContext.BaseDirectory,
            "Shaders",
            "RuntimeGrid.hlsl");

        if (!File.Exists(shaderFile))
        {
            throw new FileNotFoundException(
                "Runtime grid shader was not copied to the output directory.",
                shaderFile);
        }

        ReadOnlyMemory<byte> vertexShaderByteCode =
            Compiler.CompileFromFile(shaderFile, "VSMain", "vs_4_0");

        ReadOnlyMemory<byte> pixelShaderByteCode =
            Compiler.CompileFromFile(shaderFile, "PSMain", "ps_4_0");

        _tileVertexShader = _device.CreateVertexShader(vertexShaderByteCode.Span);
        _tilePixelShader = _device.CreatePixelShader(pixelShaderByteCode.Span);

        InputElementDescription[] inputElements =
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

        _tileInputLayout = _device.CreateInputLayout(
            inputElements,
            vertexShaderByteCode.Span);

        _tileVertexCount = (uint)vertices.Length;
    }

    private static RuntimeVertex[] BuildTileVertices(IReadOnlyList<RuntimeTileInfo> tiles)
    {
        if (tiles.Count == 0)
        {
            return [];
        }

        var minimumX = tiles.Min(static tile => tile.X);
        var maximumX = tiles.Max(static tile => tile.X);
        var minimumY = tiles.Min(static tile => tile.Y);
        var maximumY = tiles.Max(static tile => tile.Y);

        var width = Math.Max(maximumX - minimumX + 1, 1);
        var height = Math.Max(maximumY - minimumY + 1, 1);
        var cellSize = MathF.Min(1.8f / width, 1.8f / height);
        var halfSize = cellSize * 0.43f;

        var middleX = (minimumX + maximumX) * 0.5f;
        var middleY = (minimumY + maximumY) * 0.5f;

        var vertices = new List<RuntimeVertex>(tiles.Count * 6);

        foreach (var tile in tiles)
        {
            var centerX = (tile.X - middleX) * cellSize;
            var centerY = -(tile.Y - middleY) * cellSize;

            var density = MathF.Min(
                1.0f,
                (tile.ObjectCount + tile.SplineCount) / 250.0f);

            var color = new Color4(
                0.18f + density * 0.35f,
                0.38f + density * 0.22f,
                0.72f,
                1.0f);

            var left = centerX - halfSize;
            var right = centerX + halfSize;
            var top = centerY + halfSize;
            var bottom = centerY - halfSize;

            vertices.Add(new RuntimeVertex(new Vector3(left, top, 0.0f), color));
            vertices.Add(new RuntimeVertex(new Vector3(right, top, 0.0f), color));
            vertices.Add(new RuntimeVertex(new Vector3(right, bottom, 0.0f), color));

            vertices.Add(new RuntimeVertex(new Vector3(left, top, 0.0f), color));
            vertices.Add(new RuntimeVertex(new Vector3(right, bottom, 0.0f), color));
            vertices.Add(new RuntimeVertex(new Vector3(left, bottom, 0.0f), color));
        }

        return vertices.ToArray();
    }

    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        if (_swapChain is null ||
            ClientSize.Width <= 0 ||
            ClientSize.Height <= 0)
        {
            return;
        }

        _renderTimer.Stop();

        _deviceContext?.UnsetRenderTargets();
        _renderTargetView?.Dispose();
        _renderTargetView = null;
        _backBuffer?.Dispose();
        _backBuffer = null;

        _swapChain.ResizeBuffers(
            2,
            (uint)ClientSize.Width,
            (uint)ClientSize.Height,
            Format.R8G8B8A8_UNorm,
            SwapChainFlags.None).CheckError();

        CreateBackBuffer();
        _renderTimer.Start();
    }

    private void RenderTimerOnTick(object? sender, EventArgs e)
    {
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
            new Color4(0.025f, 0.035f, 0.055f, 1.0f));

        _deviceContext.OMSetRenderTargets(
            _renderTargetView,
            (ID3D11DepthStencilView?)null);

        _deviceContext.RSSetViewport(
            new Viewport(ClientSize.Width, ClientSize.Height));

        if (_tileVertexBuffer is not null &&
            _tileVertexShader is not null &&
            _tilePixelShader is not null &&
            _tileInputLayout is not null &&
            _tileVertexCount > 0)
        {
            _deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _deviceContext.IASetInputLayout(_tileInputLayout);
            _deviceContext.IASetVertexBuffer(
                0,
                _tileVertexBuffer,
                RuntimeVertex.SizeInBytes);

            _deviceContext.VSSetShader(_tileVertexShader);
            _deviceContext.PSSetShader(_tilePixelShader);
            _deviceContext.Draw(_tileVertexCount, 0);
        }

        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    private void UpdateCaption()
    {
        Text =
            $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
            $"{_windowInfo.TileCount:N0} tiles — " +
            $"{_windowInfo.ObjectCount:N0} objects — " +
            $"{_windowInfo.SplineCount:N0} splines — D3D11 {_featureLevel}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Tick -= RenderTimerOnTick;
            _renderTimer.Dispose();

            _deviceContext?.ClearState();
            _deviceContext?.Flush();

            _tileInputLayout?.Dispose();
            _tilePixelShader?.Dispose();
            _tileVertexShader?.Dispose();
            _tileVertexBuffer?.Dispose();

            _renderTargetView?.Dispose();
            _backBuffer?.Dispose();
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

        public RuntimeVertex(Vector3 position, Color4 color)
        {
            Position = position;
            Color = color;
        }

        public readonly Vector3 Position;

        public readonly Color4 Color;
    }
}
