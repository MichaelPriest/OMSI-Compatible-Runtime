using OMSICompatible.Renderer.Common;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D12;

public sealed class D3D12RuntimeTexture :
    IDisposable
{
    private readonly ID3D12Resource _texture;
    private readonly ID3D12DescriptorHeap _descriptorHeap;

    private D3D12RuntimeTexture(
        ID3D12Resource texture,
        ID3D12DescriptorHeap descriptorHeap)
    {
        _texture = texture;
        _descriptorHeap = descriptorHeap;
    }

    public ID3D12DescriptorHeap DescriptorHeap =>
        _descriptorHeap;

    public GpuDescriptorHandle GpuHandle =>
        _descriptorHeap.GetGPUDescriptorHandleForHeapStart();

    public static D3D12RuntimeTexture Create(
        ID3D12Device device,
        ID3D12CommandQueue queue,
        RuntimeDecodedTexture decoded)
    {
        ArgumentNullException.ThrowIfNull(
            device);

        ArgumentNullException.ThrowIfNull(
            queue);

        ArgumentNullException.ThrowIfNull(
            decoded);

        if (decoded.Width <=
                0 ||
            decoded.Height <=
                0 ||
            decoded.Pixels.Length !=
                checked(
                    decoded.Width *
                    decoded.Height *
                    4))
        {
            throw new ArgumentException(
                "Invalid RGBA texture payload.",
                nameof(decoded));
        }

        var description =
            ResourceDescription.Texture2D(
                Format.R8G8B8A8_UNorm,
                (uint)decoded.Width,
                (uint)decoded.Height,
                arraySize:
                    1,
                mipLevels:
                    1);

        var texture =
            device.CreateCommittedResource(
                HeapType.Default,
                description,
                ResourceStates.CopyDest);

        var layouts =
            new PlacedSubresourceFootPrint[
                1];

        var rowCounts =
            new uint[
                1];

        var rowSizes =
            new ulong[
                1];

        device.GetCopyableFootprints(
            description,
            0,
            1,
            0,
            layouts,
            rowCounts,
            rowSizes,
            out var uploadBytes);

        var upload =
            device.CreateCommittedResource(
                HeapType.Upload,
                ResourceDescription.Buffer(
                    uploadBytes),
                ResourceStates.GenericRead);

        try
        {
            var staged =
                new byte[
                    checked(
                        (int)uploadBytes)];

            var sourceRowBytes =
                checked(
                    decoded.Width *
                    4);

            var destinationRowBytes =
                checked(
                    (int)layouts[0]
                        .Footprint
                        .RowPitch);

            var baseOffset =
                checked(
                    (int)layouts[0]
                        .Offset);

            for (var row = 0;
                 row <
                     decoded.Height;
                 row++)
            {
                decoded.Pixels.AsSpan(
                        row *
                            sourceRowBytes,
                        sourceRowBytes)
                    .CopyTo(
                        staged.AsSpan(
                            baseOffset +
                            row *
                                destinationRowBytes,
                            sourceRowBytes));
            }

            upload.SetData(
                staged);

            using var allocator =
                device.CreateCommandAllocator(
                    CommandListType.Direct);

            using var commandList =
                device.CreateCommandList<
                    ID3D12GraphicsCommandList>(
                    CommandListType.Direct,
                    allocator);

            commandList.CopyTextureRegion(
                new TextureCopyLocation(
                    texture,
                    0),
                0,
                0,
                0,
                new TextureCopyLocation(
                    upload,
                    layouts[0]));

            commandList.ResourceBarrierTransition(
                texture,
                ResourceStates.CopyDest,
                ResourceStates.PixelShaderResource);

            commandList.Close();

            queue.ExecuteCommandList(
                commandList);

            using var fence =
                device.CreateFence(
                    0);

            using var fenceEvent =
                new AutoResetEvent(
                    false);

            queue.Signal(
                fence,
                1);

            if (fence.CompletedValue <
                1)
            {
                fence.SetEventOnCompletion(
                    1,
                    fenceEvent);

                fenceEvent.WaitOne();
            }

            var heap =
                device.CreateDescriptorHeap(
                    new DescriptorHeapDescription(
                        DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                        1,
                        DescriptorHeapFlags.ShaderVisible));

            device.CreateShaderResourceView(
                texture,
                null,
                heap.GetCPUDescriptorHandleForHeapStart());

            return new D3D12RuntimeTexture(
                texture,
                heap);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
        finally
        {
            upload.Dispose();
        }
    }

    public void Dispose()
    {
        _descriptorHeap.Dispose();
        _texture.Dispose();
    }
}
