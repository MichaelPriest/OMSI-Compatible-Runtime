using System.Drawing;
using System.Windows.Forms;
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
    private FeatureLevel _featureLevel;

    public D3D11RenderWindow(RuntimeWindowInfo windowInfo)
    {
        _windowInfo = windowInfo;

        Text = $"OMSI Compatible Runtime — {windowInfo.WorldName}";
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(960, 540);
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
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None
        };

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _swapChain = _factory.CreateSwapChainForHwnd(dxgiDevice, Handle, description);
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

        _deviceContext.OMSetRenderTargets(_renderTargetView, null);
        _deviceContext.RSSetViewport(new Viewport(ClientSize.Width, ClientSize.Height));
        _deviceContext.ClearRenderTargetView(
            _renderTargetView,
            new Color4(0.025f, 0.035f, 0.055f, 1.0f));

        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    private void UpdateCaption()
    {
        Text =
            $"OMSI Compatible Runtime — {_windowInfo.WorldName} — " +
            $"{_windowInfo.TileCount:N0} tiles — D3D11 {_featureLevel}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Tick -= RenderTimerOnTick;
            _renderTimer.Dispose();

            _deviceContext?.Flush();
            _renderTargetView?.Dispose();
            _backBuffer?.Dispose();
            _swapChain?.Dispose();
            _deviceContext?.Dispose();
            _device?.Dispose();
            _factory?.Dispose();
        }

        base.Dispose(disposing);
    }
}
