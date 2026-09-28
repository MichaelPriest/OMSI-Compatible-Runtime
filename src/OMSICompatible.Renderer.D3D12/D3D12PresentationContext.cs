using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Dxc;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace OMSICompatible.Renderer.D3D12;

public sealed class D3D12PresentationContext :
    IDisposable
{
    private const int FrameCount =
        2;

    private readonly IDXGIFactory4 _factory;
    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _queue;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly uint _rtvDescriptorSize;
    private readonly ID3D12DescriptorHeap _dsvHeap;
    private readonly ID3D12Resource _depthStencil;
    private readonly ID3D12Resource[] _renderTargets =
        new ID3D12Resource[
            FrameCount];
    private readonly ID3D12CommandAllocator[] _commandAllocators =
        new ID3D12CommandAllocator[
            FrameCount];
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipelineState;
    private readonly ID3D12Fence _fence;
    private readonly int _width;
    private readonly int _height;
    private readonly AutoResetEvent _fenceEvent =
        new(
            false);

    private ulong _fenceValue;
    private uint _backBufferIndex;
    private bool _disposed;

    private D3D12PresentationContext(
        IDXGIFactory4 factory,
        ID3D12Device device,
        ID3D12CommandQueue queue,
        IDXGISwapChain3 swapChain,
        ID3D12DescriptorHeap rtvHeap,
        uint rtvDescriptorSize,
        ID3D12DescriptorHeap dsvHeap,
        ID3D12Resource depthStencil,
        ID3D12GraphicsCommandList commandList,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipelineState,
        ID3D12Fence fence,
        int width,
        int height)
    {
        _factory =
            factory;
        _device =
            device;
        _queue =
            queue;
        _swapChain =
            swapChain;
        _rtvHeap =
            rtvHeap;
        _rtvDescriptorSize =
            rtvDescriptorSize;
        _dsvHeap =
            dsvHeap;
        _depthStencil =
            depthStencil;
        _commandList =
            commandList;
        _rootSignature =
            rootSignature;
        _pipelineState =
            pipelineState;
        _fence =
            fence;
        _width =
            Math.Max(
                width,
                1);
        _height =
            Math.Max(
                height,
                1);
    }

    public static D3D12PresentationContext Create(
        IntPtr windowHandle,
        int width,
        int height,
        bool preferHardware = true)
    {
        if (windowHandle ==
            IntPtr.Zero)
        {
            throw new ArgumentException(
                "A valid window handle is required.",
                nameof(windowHandle));
        }

        var factory =
            CreateDXGIFactory2<IDXGIFactory4>(
                false);

        ID3D12Device? device =
            null;

        try
        {
            for (uint index = 0;
                 factory.EnumAdapters1(
                         index,
                         out IDXGIAdapter1? adapter)
                     .Success;
                 index++)
            {
                using (adapter)
                {
                    if (adapter is null)
                    {
                        continue;
                    }

                    var description =
                        adapter.Description1;

                    var software =
                        (description.Flags &
                         AdapterFlags.Software) !=
                        AdapterFlags.None;

                    if (preferHardware &&
                        software)
                    {
                        continue;
                    }

                    if (D3D12CreateDevice(
                            adapter,
                            FeatureLevel.Level_11_0,
                            out device)
                        .Success &&
                        device is not
                        null)
                    {
                        break;
                    }
                }
            }

            if (device is null &&
                preferHardware)
            {
                for (uint index = 0;
                     factory.EnumAdapters1(
                             index,
                             out IDXGIAdapter1? adapter)
                         .Success;
                     index++)
                {
                    using (adapter)
                    {
                        if (adapter is null)
                        {
                            continue;
                        }

                        if (D3D12CreateDevice(
                                adapter,
                                FeatureLevel.Level_11_0,
                                out device)
                            .Success &&
                            device is not
                            null)
                        {
                            break;
                        }
                    }
                }
            }

            if (device is null)
            {
                throw new PlatformNotSupportedException(
                    "No Direct3D 12 compatible adapter was found.");
            }

            var queue =
                device.CreateCommandQueue(
                    CommandListType.Direct);

            var swapChainDescription =
                new SwapChainDescription1
                {
                    BufferCount =
                        FrameCount,
                    Width =
                        (uint)Math.Max(
                            width,
                            1),
                    Height =
                        (uint)Math.Max(
                            height,
                            1),
                    Format =
                        Format.R8G8B8A8_UNorm,
                    BufferUsage =
                        Usage.RenderTargetOutput,
                    SwapEffect =
                        SwapEffect.FlipDiscard,
                    SampleDescription =
                        new SampleDescription(
                            1,
                            0)
                };

            IDXGISwapChain3 swapChain;

            using (var swapChain1 =
                   factory.CreateSwapChainForHwnd(
                       queue,
                       windowHandle,
                       swapChainDescription))
            {
                factory.MakeWindowAssociation(
                    windowHandle,
                    WindowAssociationFlags.IgnoreAltEnter);

                swapChain =
                    swapChain1.QueryInterface<
                        IDXGISwapChain3>();
            }

            var rtvHeap =
                device.CreateDescriptorHeap(
                    new DescriptorHeapDescription(
                        DescriptorHeapType.RenderTargetView,
                        FrameCount));

            var rtvDescriptorSize =
                device.GetDescriptorHandleIncrementSize(
                    DescriptorHeapType.RenderTargetView);

            var depthDescription =
                ResourceDescription.Texture2D(
                    Format.D32_Float,
                    (uint)Math.Max(
                        width,
                        1),
                    (uint)Math.Max(
                        height,
                        1),
                    flags:
                        ResourceFlags.AllowDepthStencil);

            var depthStencil =
                device.CreateCommittedResource(
                    HeapType.Default,
                    depthDescription,
                    ResourceStates.DepthWrite,
                    new ClearValue(
                        Format.D32_Float,
                        1.0f,
                        0));

            var dsvHeap =
                device.CreateDescriptorHeap(
                    new DescriptorHeapDescription(
                        DescriptorHeapType.DepthStencilView,
                        1));

            device.CreateDepthStencilView(
                depthStencil,
                new DepthStencilViewDescription
                {
                    Format =
                        Format.D32_Float,
                    ViewDimension =
                        DepthStencilViewDimension.Texture2D
                },
                dsvHeap.GetCPUDescriptorHandleForHeapStart());

            var commandAllocators =
                new ID3D12CommandAllocator[
                    FrameCount];

            for (var index = 0;
                 index <
                     FrameCount;
                 index++)
            {
                commandAllocators[
                    index] =
                    device.CreateCommandAllocator(
                        CommandListType.Direct);
            }

            var rootSignature =
                device.CreateRootSignature(
                    new RootSignatureDescription1(
                        RootSignatureFlags.AllowInputAssemblerInputLayout));

            const string shaderSource =
                """
                struct VsOutput
                {
                    float4 position : SV_Position;
                    float3 color : COLOR0;
                };

                VsOutput VSMain(uint vertexId : SV_VertexID)
                {
                    float2 positions[3] =
                    {
                        float2( 0.0,  0.65),
                        float2( 0.65, -0.55),
                        float2(-0.65, -0.55)
                    };

                    float3 colors[3] =
                    {
                        float3(0.95, 0.30, 0.20),
                        float3(0.20, 0.80, 0.35),
                        float3(0.20, 0.45, 0.95)
                    };

                    VsOutput output;
                    output.position = float4(positions[vertexId], 0.0, 1.0);
                    output.color = colors[vertexId];
                    return output;
                }

                float4 PSMain(VsOutput input) : SV_Target0
                {
                    return float4(input.color, 1.0);
                }
                """;

            using var vertexResult =
                DxcCompiler.Compile(
                    DxcShaderStage.Vertex,
                    shaderSource,
                    "VSMain");

            if (vertexResult.GetStatus().Failure)
            {
                throw new InvalidOperationException(
                    vertexResult.GetErrors());
            }

            using var pixelResult =
                DxcCompiler.Compile(
                    DxcShaderStage.Pixel,
                    shaderSource,
                    "PSMain");

            if (pixelResult.GetStatus().Failure)
            {
                throw new InvalidOperationException(
                    pixelResult.GetErrors());
            }

            var pipelineStateStream =
                new PipelineStateStream
                {
                    RootSignature =
                        rootSignature,
                    VertexShader =
                        vertexResult.GetObjectBytecodeMemory().Span,
                    PixelShader =
                        pixelResult.GetObjectBytecodeMemory().Span,
                    SampleMask =
                        uint.MaxValue,
                    PrimitiveTopology =
                        PrimitiveTopologyType.Triangle,
                    RasterizerState =
                        RasterizerDescription.CullNone,
                    BlendState =
                        BlendDescription.Opaque,
                    DepthStencilState =
                        DepthStencilDescription.Default,
                    RenderTargetFormats =
                        [
                            Format.R8G8B8A8_UNorm
                        ],
                    DepthStencilFormat =
                        Format.D32_Float,
                    SampleDescription =
                        SampleDescription.Default
                };

            var pipelineState =
                device.CreatePipelineState(
                    pipelineStateStream);

            var commandList =
                device.CreateCommandList<
                    ID3D12GraphicsCommandList>(
                    CommandListType.Direct,
                    commandAllocators[
                        0],
                    pipelineState);

            commandList.Close();

            var fence =
                device.CreateFence(
                    0);

            var context =
                new D3D12PresentationContext(
                    factory,
                    device,
                    queue,
                    swapChain,
                    rtvHeap,
                    rtvDescriptorSize,
                    dsvHeap,
                    depthStencil,
                    commandList,
                    rootSignature,
                    pipelineState,
                    fence,
                    width,
                    height);

            for (uint index = 0;
                 index <
                     FrameCount;
                 index++)
            {
                context._commandAllocators[
                    index] =
                    commandAllocators[
                        index];

                var target =
                    swapChain.GetBuffer<
                        ID3D12Resource>(
                        index);

                context._renderTargets[
                    index] =
                    target;

                var handle =
                    new CpuDescriptorHandle(
                        rtvHeap.GetCPUDescriptorHandleForHeapStart(),
                        (int)index,
                        rtvDescriptorSize);

                device.CreateRenderTargetView(
                    target,
                    null,
                    handle);
            }

            context._backBufferIndex =
                swapChain.CurrentBackBufferIndex;

            return context;
        }
        catch
        {
            device?.Dispose();
            factory.Dispose();
            throw;
        }
    }

    public void ClearAndPresent(
        float red,
        float green,
        float blue,
        bool vsync)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        var allocator =
            _commandAllocators[
                _backBufferIndex];

        allocator.Reset();

        _commandList.Reset(
            allocator,
            _pipelineState);

        _commandList.SetGraphicsRootSignature(
            _rootSignature);

        var renderTarget =
            _renderTargets[
                _backBufferIndex];

        _commandList.ResourceBarrierTransition(
            renderTarget,
            ResourceStates.Present,
            ResourceStates.RenderTarget);

        var rtv =
            new CpuDescriptorHandle(
                _rtvHeap.GetCPUDescriptorHandleForHeapStart(),
                (int)_backBufferIndex,
                _rtvDescriptorSize);

        var dsv =
            _dsvHeap.GetCPUDescriptorHandleForHeapStart();

        _commandList.OMSetRenderTargets(
            rtv,
            dsv);

        _commandList.ClearRenderTargetView(
            rtv,
            new Color4(
                red,
                green,
                blue,
                1.0f));

        _commandList.ClearDepthStencilView(
            dsv,
            ClearFlags.Depth,
            1.0f,
            0);

        _commandList.RSSetViewport(
            new Viewport(
                0.0f,
                0.0f,
                _width,
                _height,
                0.0f,
                1.0f));

        _commandList.RSSetScissorRect(
            _width,
            _height);

        _commandList.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _commandList.DrawInstanced(
            3,
            1,
            0,
            0);

        _commandList.ResourceBarrierTransition(
            renderTarget,
            ResourceStates.RenderTarget,
            ResourceStates.Present);

        _commandList.Close();

        _queue.ExecuteCommandList(
            _commandList);

        _swapChain.Present(
            vsync
                ? 1u
                : 0u,
            PresentFlags.None);

        SignalAndThrottle();

        _backBufferIndex =
            _swapChain.CurrentBackBufferIndex;
    }

    private void SignalAndThrottle()
    {
        var fenceValue =
            ++_fenceValue;

        _queue.Signal(
            _fence,
            fenceValue);

        if (_fence.CompletedValue >=
            fenceValue)
        {
            return;
        }

        _fence.SetEventOnCompletion(
            fenceValue,
            _fenceEvent);

        _fenceEvent.WaitOne();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed =
            true;

        try
        {
            SignalAndThrottle();
        }
        catch
        {
        }

        foreach (var target in
                 _renderTargets)
        {
            target?.Dispose();
        }

        foreach (var allocator in
                 _commandAllocators)
        {
            allocator?.Dispose();
        }

        _commandList.Dispose();
        _pipelineState.Dispose();
        _rootSignature.Dispose();
        _depthStencil.Dispose();
        _dsvHeap.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _fence.Dispose();
        _queue.Dispose();
        _device.Dispose();
        _factory.Dispose();
        _fenceEvent.Dispose();
    }
}
