using System.Numerics;
using OMSICompatible.Renderer.Common;
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
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 RenderOrigin;
    }
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
    private readonly ID3D12PipelineState _alphaBlendPipelineState;
    private readonly ID3D12PipelineState _depthReadPipelineState;
    private readonly ID3D12PipelineState _alphaBlendDepthReadPipelineState;
    private readonly ID3D12PipelineState _depthDisabledPipelineState;
    private readonly ID3D12PipelineState _alphaBlendDepthDisabledPipelineState;
    private readonly D3D12RuntimeGeometryBuffer _geometryBuffer;
    private readonly D3D12RuntimeTexture _fallbackTexture;
    private readonly Dictionary<string, D3D12RuntimeTexture> _textureCache =
        new(
            StringComparer.OrdinalIgnoreCase);
    private readonly ID3D12Fence _fence;
    private readonly int _width;
    private readonly int _height;
    private FrameConstants _frameConstants =
        new()
        {
            ViewProjection =
                Matrix4x4.Identity,
            RenderOrigin =
                Vector4.Zero
        };
    private Matrix4x4 _modelMatrix =
        Matrix4x4.Identity;
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
        ID3D12PipelineState alphaBlendPipelineState,
        ID3D12PipelineState depthReadPipelineState,
        ID3D12PipelineState alphaBlendDepthReadPipelineState,
        ID3D12PipelineState depthDisabledPipelineState,
        ID3D12PipelineState alphaBlendDepthDisabledPipelineState,
        D3D12RuntimeGeometryBuffer geometryBuffer,
        D3D12RuntimeTexture fallbackTexture,
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
        _alphaBlendPipelineState =
            alphaBlendPipelineState;
        _depthReadPipelineState =
            depthReadPipelineState;
        _alphaBlendDepthReadPipelineState =
            alphaBlendDepthReadPipelineState;
        _depthDisabledPipelineState =
            depthDisabledPipelineState;
        _alphaBlendDepthDisabledPipelineState =
            alphaBlendDepthDisabledPipelineState;
        _geometryBuffer =
            geometryBuffer;
        _fallbackTexture =
            fallbackTexture;
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
                        RootSignatureFlags.AllowInputAssemblerInputLayout,
                        [
                            new RootParameter1(
                                new RootConstants(
                                    0,
                                    0,
                                    20),
                                ShaderVisibility.Vertex),
                            new RootParameter1(
                                new RootConstants(
                                    1,
                                    0,
                                    16),
                                ShaderVisibility.Vertex),
                            new RootParameter1(
                                new RootDescriptorTable1(
                                    new DescriptorRange1(
                                        DescriptorRangeType.ShaderResourceView,
                                        1,
                                        0)),
                                ShaderVisibility.Pixel),
                            new RootParameter1(
                                new RootConstants(
                                    2,
                                    0,
                                    1),
                                ShaderVisibility.Pixel)
                        ],
                        [
                            new StaticSamplerDescription(
                                0,
                                shaderVisibility:
                                    ShaderVisibility.Pixel)
                        ]));

            const string shaderSource =
                """
                cbuffer FrameConstants : register(b0)
                {
                    row_major float4x4 ViewProjection;
                    float4 RenderOrigin;
                };

                cbuffer ModelConstants : register(b1)
                {
                    row_major float4x4 Model;
                };

                Texture2D DiffuseTexture : register(t0);
                SamplerState DiffuseSampler : register(s0);

                cbuffer MaterialConstants : register(b2)
                {
                    float AlphaCutoff;
                };

                struct VsInput
                {
                    float3 position : POSITION;
                    float4 color : COLOR0;
                    float2 uv : TEXCOORD0;
                };

                struct VsOutput
                {
                    float4 position : SV_Position;
                    float4 color : COLOR0;
                    float2 uv : TEXCOORD0;
                };

                VsOutput VSMain(VsInput input)
                {
                    VsOutput output;
                    float3 worldPosition =
                        mul(
                            float4(
                                input.position,
                                1.0),
                            Model).xyz;

                    output.position =
                        mul(
                            float4(
                                worldPosition -
                                    RenderOrigin.xyz,
                                1.0),
                            ViewProjection);
                    output.color = input.color;
                    output.uv = input.uv;
                    return output;
                }

                float4 PSMain(VsOutput input) : SV_Target0
                {
                    float4 color =
                        input.color *
                        DiffuseTexture.Sample(
                            DiffuseSampler,
                            input.uv);

                    if (AlphaCutoff >=
                        0.0)
                    {
                        clip(
                            color.a -
                            AlphaCutoff);
                    }

                    return color;
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

            ID3D12PipelineState CreatePipelineState(
                BlendDescription blendState,
                DepthStencilDescription depthStencilState)
            {
                return device.CreateGraphicsPipelineState(
                    new GraphicsPipelineStateDescription
                    {
                        RootSignature =
                            rootSignature,
                        VertexShader =
                            vertexResult.GetObjectBytecodeMemory(),
                        PixelShader =
                            pixelResult.GetObjectBytecodeMemory(),
                        InputLayout =
                            new InputLayoutDescription(
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
                                ]),
                        SampleMask =
                            uint.MaxValue,
                        PrimitiveTopologyType =
                            PrimitiveTopologyType.Triangle,
                        RasterizerState =
                            RasterizerDescription.CullNone,
                        BlendState =
                            blendState,
                        DepthStencilState =
                            depthStencilState,
                        RenderTargetFormats =
                            [
                                Format.R8G8B8A8_UNorm
                            ],
                        DepthStencilFormat =
                            Format.D32_Float,
                        SampleDescription =
                            SampleDescription.Default
                    });
            }

            var pipelineState =
                CreatePipelineState(
                    BlendDescription.Opaque,
                    DepthStencilDescription.Default);

            var alphaBlendPipelineState =
                CreatePipelineState(
                    BlendDescription.NonPremultiplied,
                    DepthStencilDescription.Read);

            var depthReadPipelineState =
                CreatePipelineState(
                    BlendDescription.Opaque,
                    DepthStencilDescription.Read);

            var alphaBlendDepthReadPipelineState =
                CreatePipelineState(
                    BlendDescription.NonPremultiplied,
                    DepthStencilDescription.Read);

            var depthDisabledPipelineState =
                CreatePipelineState(
                    BlendDescription.Opaque,
                    DepthStencilDescription.None);

            var alphaBlendDepthDisabledPipelineState =
                CreatePipelineState(
                    BlendDescription.NonPremultiplied,
                    DepthStencilDescription.None);

            ReadOnlySpan<RuntimeTerrainVertex> vertices =
            [
                new RuntimeTerrainVertex(
                    new Vector3(
                        0.0f,
                        0.65f,
                        0.0f),
                    new Color4(
                        0.95f,
                        0.30f,
                        0.20f,
                        1.0f),
                    Vector2.Zero,
                    Vector2.Zero,
                    Vector2.Zero),
                new RuntimeTerrainVertex(
                    new Vector3(
                        0.65f,
                        -0.55f,
                        0.0f),
                    new Color4(
                        0.20f,
                        0.80f,
                        0.35f,
                        1.0f),
                    Vector2.Zero,
                    Vector2.Zero,
                    Vector2.Zero),
                new RuntimeTerrainVertex(
                    new Vector3(
                        -0.65f,
                        -0.55f,
                        0.0f),
                    new Color4(
                        0.20f,
                        0.45f,
                        0.95f,
                        1.0f),
                    Vector2.Zero,
                    Vector2.Zero,
                    Vector2.Zero)
            ];

            var geometryBuffer =
                D3D12RuntimeGeometryBuffer.Create(
                    device,
                    vertices);

            var fallbackTexture =
                D3D12RuntimeTexture.Create(
                    device,
                    queue,
                    new RuntimeDecodedTexture(
                        [
                            (byte)255,
                            (byte)255,
                            (byte)255,
                            (byte)255
                        ],
                        1,
                        1));

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
                    alphaBlendPipelineState,
                    depthReadPipelineState,
                    alphaBlendDepthReadPipelineState,
                    depthDisabledPipelineState,
                    alphaBlendDepthDisabledPipelineState,
                    geometryBuffer,
                    fallbackTexture,
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

    public D3D12RuntimeTerrainResources CreateTerrainResources(
        RuntimeTerrainGeometry geometry)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        return D3D12RuntimeTerrainResources.Create(
            _device,
            geometry);
    }

    public D3D12RuntimeObjectResources CreateObjectResources(
        ReadOnlySpan<RuntimeObjectVertex> vertices,
        IReadOnlyList<RuntimeObjectDrawBatch>? batches = null)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        return D3D12RuntimeObjectResources.Create(
            _device,
            vertices,
            batches);
    }

    public void SetViewProjection(
        Matrix4x4 viewProjection)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        _frameConstants.ViewProjection =
            viewProjection;
    }

    public void SetRenderOrigin(
        Vector3 renderOrigin)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        _frameConstants.RenderOrigin =
            new Vector4(
                renderOrigin,
                0.0f);
    }

    public void SetModelMatrix(
        Matrix4x4 modelMatrix)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        _modelMatrix =
            modelMatrix;
    }

    public void ClearAndPresent(
        float red,
        float green,
        float blue,
        bool vsync)
    {
        DrawAndPresent(
            _geometryBuffer,
            batches:
                null,
            red,
            green,
            blue,
            vsync);
    }

    public void DrawAndPresent(
        D3D12RuntimeTerrainResources terrain,
        float red,
        float green,
        float blue,
        bool vsync)
    {
        ArgumentNullException.ThrowIfNull(
            terrain);

        DrawAndPresent(
            terrain.Buffer,
            terrain.Batches,
            red,
            green,
            blue,
            vsync);
    }

    public void DrawAndPresent(
        D3D12RuntimeObjectResources objects,
        float red,
        float green,
        float blue,
        bool vsync)
    {
        ArgumentNullException.ThrowIfNull(
            objects);

        DrawAndPresent(
            objects.Buffer,
            batches:
                null,
            red,
            green,
            blue,
            vsync);
    }

    public void DrawSceneAndPresent(
        D3D12RuntimeTerrainResources? terrain,
        D3D12RuntimeObjectResources? splines,
        D3D12RuntimeObjectResources? objects,
        D3D12RuntimeObjectResources? vehicle,
        Matrix4x4 vehicleModel,
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

        _commandList.SetGraphicsRoot32BitConstants(
            0,
            ref _frameConstants);

        var staticModel =
            Matrix4x4.Identity;

        _commandList.SetGraphicsRoot32BitConstants(
            1,
            ref staticModel);

        _commandList.SetGraphicsRoot32BitConstant(
            3,
            -1.0f,
            0);

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

        if (terrain is not null)
        {
            _commandList.IASetVertexBuffers(
                0,
                terrain.Buffer.View);

            foreach (var batch in
                     terrain.Batches)
            {
                if (batch.VertexCount ==
                    0)
                {
                    continue;
                }

                BindTexture(
                    ResolveTexture(
                        batch.TexturePath));

                _commandList.SetGraphicsRoot32BitConstant(
                    3,
                    batch.AlphaCutout
                        ? 0.5f
                        : -1.0f,
                    0);

                _commandList.DrawInstanced(
                    batch.VertexCount,
                    1,
                    batch.StartVertex,
                    0);
            }
        }

        DrawObjectBuffer(
            splines);

        DrawObjectBuffer(
            objects);

        if (vehicle is not null &&
            vehicle.Buffer.VertexCount >
                0)
        {
            _commandList.SetGraphicsRoot32BitConstants(
                1,
                ref vehicleModel);

            DrawObjectBuffer(
                vehicle);
        }

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

    private void DrawObjectBuffer(
        D3D12RuntimeObjectResources? resources)
    {
        if (resources is null ||
            resources.Buffer.VertexCount <=
                0)
        {
            return;
        }

        _commandList.IASetVertexBuffers(
            0,
            resources.Buffer.View);

        if (resources.Batches.Count >
            0)
        {
            foreach (var batch in
                     resources.Batches)
            {
                if (batch.VertexCount ==
                    0)
                {
                    continue;
                }

                _commandList.SetPipelineState(
                    ResolveObjectPipelineState(
                        batch));

                BindTexture(
                    ResolveTexture(
                        batch.TexturePath));

                _commandList.DrawInstanced(
                    batch.VertexCount,
                    1,
                    batch.StartVertex,
                    0);
            }

            _commandList.SetPipelineState(
                _pipelineState);

            _commandList.SetGraphicsRoot32BitConstant(
                3,
                -1.0f,
                0);

            return;
        }

        _commandList.SetPipelineState(
            _pipelineState);

        _commandList.DrawInstanced(
            checked(
                (uint)resources.Buffer.VertexCount),
            1,
            0,
            0);
    }

    private D3D12RuntimeTexture ResolveTexture(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return _fallbackTexture;
        }

        if (_textureCache.TryGetValue(
                path,
                out var cached))
        {
            return cached;
        }

        try
        {
            if (!D3D12RuntimeTexture.TryCreateFromFile(
                    _device,
                    _queue,
                    path,
                    out var created) ||
                created is null)
            {
                return _fallbackTexture;
            }

            _textureCache[path] =
                created;

            return created;
        }
        catch
        {
            return _fallbackTexture;
        }
    }

    private void BindTexture(
        D3D12RuntimeTexture texture)
    {
        _commandList.SetDescriptorHeaps(
            texture.DescriptorHeap);

        _commandList.SetGraphicsRootDescriptorTable(
            2,
            texture.GpuHandle);
    }

    private ID3D12PipelineState ResolveObjectPipelineState(
        RuntimeObjectDrawBatch batch)
    {
        if (batch.NoZCheck)
        {
            return batch.AlphaBlend
                ? _alphaBlendDepthDisabledPipelineState
                : _depthDisabledPipelineState;
        }

        if (batch.AlphaBlend)
        {
            return _alphaBlendDepthReadPipelineState;
        }

        if (batch.NoZWrite)
        {
            return _depthReadPipelineState;
        }

        return _pipelineState;
    }

    private void DrawAndPresent(
        D3D12RuntimeGeometryBuffer geometryBuffer,
        IReadOnlyList<RuntimeTerrainBatch>? batches,
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

        _commandList.SetGraphicsRoot32BitConstants(
            0,
            ref _frameConstants);

        _commandList.SetGraphicsRoot32BitConstants(
            1,
            ref _modelMatrix);

        BindTexture(
            _fallbackTexture);

        _commandList.SetGraphicsRoot32BitConstant(
            3,
            -1.0f,
            0);

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

        _commandList.IASetVertexBuffers(
            0,
            geometryBuffer.View);

        if (batches is
            { Count: > 0 })
        {
            foreach (var batch in
                     batches)
            {
                if (batch.VertexCount ==
                    0)
                {
                    continue;
                }

                _commandList.DrawInstanced(
                    batch.VertexCount,
                    1,
                    batch.StartVertex,
                    0);
            }
        }
        else
        {
            _commandList.DrawInstanced(
                checked(
                    (uint)geometryBuffer.VertexCount),
                1,
                0,
                0);
        }

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
        foreach (var texture in
                 _textureCache.Values)
        {
            texture.Dispose();
        }

        _textureCache.Clear();

        _fallbackTexture.Dispose();
        _geometryBuffer.Dispose();
        _alphaBlendDepthDisabledPipelineState.Dispose();
        _depthDisabledPipelineState.Dispose();
        _alphaBlendDepthReadPipelineState.Dispose();
        _depthReadPipelineState.Dispose();
        _alphaBlendPipelineState.Dispose();
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
