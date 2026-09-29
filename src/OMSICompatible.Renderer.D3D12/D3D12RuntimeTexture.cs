using System.Buffers.Binary;
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

    public CpuDescriptorHandle CpuHandle =>
        _descriptorHeap.GetCPUDescriptorHandleForHeapStart();

    public GpuDescriptorHandle GpuHandle =>
        _descriptorHeap.GetGPUDescriptorHandleForHeapStart();

    public static bool TryCreateFromFile(
        ID3D12Device device,
        ID3D12CommandQueue queue,
        string path,
        out D3D12RuntimeTexture? texture)
    {
        texture =
            null;

        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return false;
        }

        if (string.Equals(
                Path.GetExtension(
                    path),
                ".dds",
                StringComparison.OrdinalIgnoreCase) &&
            TryReadBcDds(
                path,
                out var format,
                out var width,
                out var height,
                out var payload,
                out var sourceRowBytes,
                out var sourceRows))
        {
            texture =
                CreatePayload(
                    device,
                    queue,
                    format,
                    width,
                    height,
                    payload,
                    sourceRowBytes,
                    sourceRows);

            return true;
        }

        if (!RuntimeRgbaTextureDecoder.TryRead(
                path,
                out var decoded))
        {
            return false;
        }

        texture =
            Create(
                device,
                queue,
                decoded);

        return true;
    }

    public static D3D12RuntimeTexture Create(
        ID3D12Device device,
        ID3D12CommandQueue queue,
        RuntimeDecodedTexture decoded)
    {
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

        return CreatePayload(
            device,
            queue,
            Format.R8G8B8A8_UNorm,
            decoded.Width,
            decoded.Height,
            decoded.Pixels,
            checked(
                decoded.Width *
                4),
            decoded.Height);
    }

    private static D3D12RuntimeTexture CreatePayload(
        ID3D12Device device,
        ID3D12CommandQueue queue,
        Format format,
        int width,
        int height,
        byte[] payload,
        int sourceRowBytes,
        int sourceRows)
    {
        ArgumentNullException.ThrowIfNull(
            device);

        ArgumentNullException.ThrowIfNull(
            queue);

        if (width <=
                0 ||
            height <=
                0 ||
            sourceRowBytes <=
                0 ||
            sourceRows <=
                0 ||
            payload.Length <
                checked(
                    sourceRowBytes *
                    sourceRows))
        {
            throw new ArgumentException(
                "Invalid texture payload.",
                nameof(payload));
        }

        var description =
            ResourceDescription.Texture2D(
                format,
                (uint)width,
                (uint)height,
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

            var destinationRowBytes =
                checked(
                    (int)layouts[0]
                        .Footprint
                        .RowPitch);

            var baseOffset =
                checked(
                    (int)layouts[0]
                        .Offset);

            var copyRows =
                Math.Min(
                    sourceRows,
                    checked(
                        (int)rowCounts[0]));

            for (var row = 0;
                 row <
                     copyRows;
                 row++)
            {
                payload.AsSpan(
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

    private static bool TryReadBcDds(
        string path,
        out Format format,
        out int width,
        out int height,
        out byte[] payload,
        out int sourceRowBytes,
        out int sourceRows)
    {
        format =
            Format.Unknown;
        width =
            0;
        height =
            0;
        payload =
            Array.Empty<byte>();
        sourceRowBytes =
            0;
        sourceRows =
            0;

        try
        {
            using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            if (stream.Length <
                128)
            {
                return false;
            }

            Span<byte> header =
                stackalloc byte[
                    128];

            stream.ReadExactly(
                header);

            if (header[0] !=
                    (byte)'D' ||
                header[1] !=
                    (byte)'D' ||
                header[2] !=
                    (byte)'S' ||
                header[3] !=
                    (byte)' ')
            {
                return false;
            }

            height =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        header[
                            12..
                            16]);

            width =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        header[
                            16..
                            20]);

            if (width <=
                    0 ||
                height <=
                    0 ||
                width >
                    16_384 ||
                height >
                    16_384)
            {
                return false;
            }

            var fourCc =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        header[
                            84..
                            88]);

            const uint dxt1 =
                0x31545844;
            const uint dxt2 =
                0x32545844;
            const uint dxt3 =
                0x33545844;
            const uint dxt4 =
                0x34545844;
            const uint dxt5 =
                0x35545844;
            const uint dx10 =
                0x30315844;

            if (fourCc ==
                dx10)
            {
                Span<byte> dx10Header =
                    stackalloc byte[
                        20];

                stream.ReadExactly(
                    dx10Header);

                var dxgiFormat =
                    BinaryPrimitives
                        .ReadUInt32LittleEndian(
                            dx10Header[
                                0..
                                4]);

                fourCc =
                    dxgiFormat switch
                    {
                        71 or 72 =>
                            dxt1,
                        74 or 75 =>
                            dxt3,
                        77 or 78 =>
                            dxt5,
                        _ =>
                            fourCc
                    };
            }

            var blockBytes =
                fourCc switch
                {
                    dxt1 =>
                        8,
                    dxt2 or
                    dxt3 or
                    dxt4 or
                    dxt5 =>
                        16,
                    _ =>
                        0
                };

            format =
                fourCc switch
                {
                    dxt1 =>
                        Format.BC1_UNorm,
                    dxt2 or
                    dxt3 =>
                        Format.BC2_UNorm,
                    dxt4 or
                    dxt5 =>
                        Format.BC3_UNorm,
                    _ =>
                        Format.Unknown
                };

            if (blockBytes ==
                    0 ||
                format ==
                    Format.Unknown)
            {
                return false;
            }

            var blocksWide =
                Math.Max(
                    1,
                    (width +
                     3) /
                    4);

            var blocksHigh =
                Math.Max(
                    1,
                    (height +
                     3) /
                    4);

            sourceRowBytes =
                checked(
                    blocksWide *
                    blockBytes);

            sourceRows =
                blocksHigh;

            var byteCount =
                checked(
                    sourceRowBytes *
                    sourceRows);

            if (stream.Length -
                    stream.Position <
                byteCount)
            {
                return false;
            }

            payload =
                new byte[
                    byteCount];

            stream.ReadExactly(
                payload);

            return true;
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException or
                OverflowException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _descriptorHeap.Dispose();
        _texture.Dispose();
    }
}
