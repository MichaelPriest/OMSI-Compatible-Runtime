using System.Buffers.Binary;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.WIC;
using WICPixelFormat =
    Vortice.WIC.PixelFormat;

namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeGpuTexture :
    IDisposable
{
    public RuntimeGpuTexture(
        ID3D11Texture2D texture,
        ID3D11ShaderResourceView view)
    {
        Texture = texture;
        View = view;
    }

    public ID3D11Texture2D Texture
    {
        get;
    }

    public ID3D11ShaderResourceView View
    {
        get;
    }

    public void Dispose()
    {
        View.Dispose();
        Texture.Dispose();
    }
}

internal sealed class RuntimeGpuTextureLoader
{
    private readonly ID3D11Device _device;

    public RuntimeGpuTextureLoader(
        ID3D11Device device)
    {
        _device =
            device ??
            throw new ArgumentNullException(
                nameof(device));
    }

    public RuntimeGpuTexture? TryLoadAlphaMask(
        string path)
    {
        if (
            string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            if (stream.Length < 128)
            {
                return null;
            }

            var header =
                new byte[128];

            stream.ReadExactly(
                header);

            if (
                header[0] != (byte)'D' ||
                header[1] != (byte)'D' ||
                header[2] != (byte)'S' ||
                header[3] != (byte)' ')
            {
                return null;
            }

            var span =
                header.AsSpan();

            var height =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        span.Slice(
                            12,
                            4));

            var width =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        span.Slice(
                            16,
                            4));

            var pixelFormatFlags =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        span.Slice(
                            80,
                            4));

            var fourCc =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        span.Slice(
                            84,
                            4));

            var bitCount =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        span.Slice(
                            88,
                            4));

            var alphaMask =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        span.Slice(
                            104,
                            4));

            const uint ddpfAlpha =
                0x00000002;

            if (
                width <= 0 ||
                height <= 0 ||
                width > 4096 ||
                height > 4096 ||
                (pixelFormatFlags &
                    ddpfAlpha) == 0 ||
                fourCc != 0 ||
                bitCount != 8 ||
                alphaMask != 0xFF)
            {
                return null;
            }

            var pixelCount =
                checked(
                    width *
                    height);

            if (
                stream.Length <
                128L +
                pixelCount)
            {
                return null;
            }

            var alpha =
                new byte[pixelCount];

            stream.ReadExactly(
                alpha);

            var rgba =
                new byte[
                    checked(
                        pixelCount *
                        4)];

            for (
                var index = 0;
                index < pixelCount;
                index++)
            {
                var target =
                    index *
                    4;

                rgba[target] =
                    255;

                rgba[target + 1] =
                    255;

                rgba[target + 2] =
                    255;

                rgba[target + 3] =
                    alpha[index];
            }

            return CreateRgbaTexture(
                rgba,
                width,
                height);
        }
        catch (
            Exception exception)
            when (
                exception is
                    IOException or
                    UnauthorizedAccessException or
                    ArgumentException or
                    NotSupportedException or
                    OverflowException ||
                exception.GetType()
                    .Namespace?
                    .StartsWith(
                        "SharpGen",
                        StringComparison.Ordinal) ==
                    true)
        {
            return null;
        }
    }

    public RuntimeGpuTexture? TryLoad(
        string path)
    {
        if (
            string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(path))
        {
            return null;
        }

        if (
            string.Equals(
                Path.GetExtension(path),
                ".tga",
                StringComparison.OrdinalIgnoreCase))
        {
            var tga =
                TryLoadTga(path);

            if (tga is not null)
            {
                return tga;
            }
        }

        try
        {
            using var factory =
                new IWICImagingFactory2();

            using var decoder =
                factory
                    .CreateDecoderFromFileName(
                        path);

            using var frame =
                decoder.GetFrame(0);

            using var converter =
                factory.CreateFormatConverter();

            converter
                .Initialize(
                    frame,
                    WICPixelFormat
                        .Format32bppRGBA);

            var size =
                converter.Size;

            if (
                size.Width <= 0 ||
                size.Height <= 0 ||
                size.Width > 16_384 ||
                size.Height > 16_384)
            {
                return null;
            }

            var stride =
                checked(
                    (uint)size.Width *
                    4u);

            var pixels =
                new byte[
                    checked(
                        size.Width *
                        size.Height *
                        4)];

            converter.CopyPixels(
                stride,
                pixels);

            return CreateRgbaTexture(
                pixels,
                size.Width,
                size.Height);
        }
        catch (
            Exception exception)
            when (
                exception is
                    IOException or
                    UnauthorizedAccessException or
                    ArgumentException or
                    NotSupportedException or
                    OverflowException ||
                exception.GetType()
                    .Namespace?
                    .StartsWith(
                        "SharpGen",
                        StringComparison.Ordinal) ==
                    true)
        {
            return null;
        }
    }    private RuntimeGpuTexture? TryLoadTga(
        string path)
    {
        try
        {
            using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            Span<byte> header =
                stackalloc byte[18];

            stream.ReadExactly(
                header);

            var idLength =
                header[0];

            var colorMapType =
                header[1];

            var imageType =
                header[2];

            var width =
                BinaryPrimitives
                    .ReadUInt16LittleEndian(
                        header.Slice(
                            12,
                            2));

            var height =
                BinaryPrimitives
                    .ReadUInt16LittleEndian(
                        header.Slice(
                            14,
                            2));

            var pixelDepth =
                header[16];

            var descriptor =
                header[17];

            if (
                colorMapType != 0 ||
                imageType is not
                    (2 or 10) ||
                width == 0 ||
                height == 0 ||
                width > 16_384 ||
                height > 16_384 ||
                pixelDepth is not
                    (24 or 32))
            {
                return null;
            }

            if (idLength > 0)
            {
                stream.Seek(
                    idLength,
                    SeekOrigin.Current);
            }

            var bytesPerPixel =
                pixelDepth / 8;

            var pixelCount =
                checked(
                    (int)width *
                    (int)height);

            var source =
                new byte[
                    checked(
                        pixelCount *
                        bytesPerPixel)];

            if (imageType == 2)
            {
                stream.ReadExactly(
                    source);
            }
            else if (
                !TryDecodeTgaRle(
                    stream,
                    source,
                    bytesPerPixel,
                    pixelCount))
            {
                return null;
            }

            var rgba =
                new byte[
                    checked(
                        pixelCount *
                        4)];

            var topOrigin =
                (descriptor &
                    0x20) !=
                0;

            var rightOrigin =
                (descriptor &
                    0x10) !=
                0;

            for (
                var sourceIndex = 0;
                sourceIndex <
                    pixelCount;
                sourceIndex++)
            {
                var sourceX =
                    sourceIndex %
                    width;

                var sourceY =
                    sourceIndex /
                    width;

                var targetX =
                    rightOrigin
                        ? width -
                            1 -
                            sourceX
                        : sourceX;

                var targetY =
                    topOrigin
                        ? sourceY
                        : height -
                            1 -
                            sourceY;

                var inputOffset =
                    sourceIndex *
                    bytesPerPixel;

                var outputOffset =
                    (
                        targetY *
                        width +
                        targetX
                    ) *
                    4;

                rgba[
                    outputOffset] =
                    source[
                        inputOffset +
                        2];

                rgba[
                    outputOffset +
                    1] =
                    source[
                        inputOffset +
                        1];

                rgba[
                    outputOffset +
                    2] =
                    source[
                        inputOffset];

                rgba[
                    outputOffset +
                    3] =
                    bytesPerPixel == 4
                        ? source[
                            inputOffset +
                            3]
                        : (byte)255;
            }

            return CreateRgbaTexture(
                rgba,
                width,
                height);
        }
        catch (
            Exception exception)
            when (
                exception is
                    IOException or
                    UnauthorizedAccessException or
                    ArgumentException or
                    NotSupportedException or
                    OverflowException)
        {
            return null;
        }
    }

    private static bool TryDecodeTgaRle(
        Stream stream,
        byte[] destination,
        int bytesPerPixel,
        int pixelCount)
    {
        var pixelIndex = 0;

        Span<byte> pixel =
            stackalloc byte[4];

        while (
            pixelIndex <
                pixelCount)
        {
            var packetHeader =
                stream.ReadByte();

            if (packetHeader < 0)
            {
                return false;
            }

            var count =
                (packetHeader &
                    0x7F) +
                1;

            if (
                pixelIndex +
                    count >
                pixelCount)
            {
                return false;
            }

            if (
                (packetHeader &
                    0x80) !=
                0)
            {
                stream.ReadExactly(
                    pixel[
                        ..bytesPerPixel]);

                for (
                    var repeat = 0;
                    repeat < count;
                    repeat++)
                {
                    pixel[
                        ..bytesPerPixel]
                        .CopyTo(
                            destination
                                .AsSpan(
                                    pixelIndex *
                                        bytesPerPixel,
                                    bytesPerPixel));

                    pixelIndex++;
                }

                continue;
            }

            var byteCount =
                checked(
                    count *
                    bytesPerPixel);

            stream.ReadExactly(
                destination.AsSpan(
                    pixelIndex *
                        bytesPerPixel,
                    byteCount));

            pixelIndex +=
                count;
        }

        return true;
    }

    private RuntimeGpuTexture CreateRgbaTexture(
        byte[] pixels,
        int width,
        int height)
    {
        var texture =
            _device
                .CreateTexture2D(
                    pixels,
                    Format
                        .R8G8B8A8_UNorm,
                    (uint)width,
                    (uint)height,
                    mipLevels: 1,
                    bindFlags:
                        BindFlags
                            .ShaderResource);

        var view =
            _device
                .CreateShaderResourceView(
                    texture);

        return new RuntimeGpuTexture(
            texture,
            view);
    }


}
