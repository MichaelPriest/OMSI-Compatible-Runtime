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
                ".dds",
                StringComparison.OrdinalIgnoreCase))
        {
            var dds =
                TryLoadDds(path);

            if (dds is not null)
            {
                return dds;
            }
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
    }    private RuntimeGpuTexture? TryLoadDds(
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

            if (stream.Length <
                128)
            {
                return null;
            }

            var header =
                new byte[128];

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

            if (width <=
                    0 ||
                height <=
                    0 ||
                width >
                    16_384 ||
                height >
                    16_384)
            {
                return null;
            }

            var fourCc =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        span.Slice(
                            84,
                            4));

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

            var dataOffset =
                128L;

            if (fourCc ==
                dx10)
            {
                Span<byte> dx10Header =
                    stackalloc byte[20];

                stream.ReadExactly(
                    dx10Header);

                dataOffset +=
                    20;

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

            stream.Position =
                dataOffset;

            byte[]? rgba =
                fourCc switch
                {
                    dxt1 =>
                        DecodeBcTexture(
                            stream,
                            width,
                            height,
                            BcFormat.Bc1),
                    dxt2 or dxt3 =>
                        DecodeBcTexture(
                            stream,
                            width,
                            height,
                            BcFormat.Bc2),
                    dxt4 or dxt5 =>
                        DecodeBcTexture(
                            stream,
                            width,
                            height,
                            BcFormat.Bc3),
                    _ =>
                        null
                };

            if (rgba is null)
            {
                var pixelFormatFlags =
                    BinaryPrimitives
                        .ReadUInt32LittleEndian(
                            span.Slice(
                                80,
                                4));

                var bitCount =
                    BinaryPrimitives
                        .ReadUInt32LittleEndian(
                            span.Slice(
                                88,
                                4));

                const uint ddpfRgb =
                    0x00000040;

                if ((pixelFormatFlags &
                         ddpfRgb) !=
                        0 &&
                    bitCount ==
                        32)
                {
                    var redMask =
                        BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                span.Slice(
                                    92,
                                    4));
                    var greenMask =
                        BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                span.Slice(
                                    96,
                                    4));
                    var blueMask =
                        BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                span.Slice(
                                    100,
                                    4));
                    var alphaMask =
                        BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                span.Slice(
                                    104,
                                    4));

                    rgba =
                        DecodeUncompressedDds32(
                            stream,
                            width,
                            height,
                            redMask,
                            greenMask,
                            blueMask,
                            alphaMask);
                }
            }

            return rgba is null
                ? null
                : CreateRgbaTexture(
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

    private enum BcFormat
    {
        Bc1,
        Bc2,
        Bc3
    }

    private static byte[]? DecodeBcTexture(
        Stream stream,
        int width,
        int height,
        BcFormat format)
    {
        var blockBytes =
            format ==
                BcFormat.Bc1
                ? 8
                : 16;

        var blocksX =
            (width +
             3) /
            4;
        var blocksY =
            (height +
             3) /
            4;

        var requiredBytes =
            checked(
                blocksX *
                blocksY *
                blockBytes);

        if (stream.Length -
                stream.Position <
            requiredBytes)
        {
            return null;
        }

        var rgba =
            new byte[
                checked(
                    width *
                    height *
                    4)];

        Span<byte> block =
            stackalloc byte[16];

        for (var blockY = 0;
             blockY <
                 blocksY;
             blockY++)
        {
            for (var blockX = 0;
                 blockX <
                     blocksX;
                 blockX++)
            {
                stream.ReadExactly(
                    block[
                        ..blockBytes]);

                DecodeBcBlock(
                    block[
                        ..blockBytes],
                    format,
                    rgba,
                    width,
                    height,
                    blockX *
                        4,
                    blockY *
                        4);
            }
        }

        return rgba;
    }

    private static void DecodeBcBlock(
        ReadOnlySpan<byte> block,
        BcFormat format,
        byte[] rgba,
        int width,
        int height,
        int originX,
        int originY)
    {
        Span<byte> alpha =
            stackalloc byte[16];

        alpha.Fill(
            255);

        var colorOffset =
            0;

        if (format ==
            BcFormat.Bc2)
        {
            for (var pixel = 0;
                 pixel <
                     16;
                 pixel++)
            {
                var packed =
                    block[
                        pixel /
                        2];

                var nibble =
                    (pixel &
                     1) ==
                            0
                        ? packed &
                          0x0F
                        : packed >>
                          4;

                alpha[pixel] =
                    (byte)(
                        nibble *
                        17);
            }

            colorOffset =
                8;
        }
        else if (format ==
                 BcFormat.Bc3)
        {
            DecodeBc3Alpha(
                block[
                    ..8],
                alpha);

            colorOffset =
                8;
        }

        var colorBlock =
            block[
                colorOffset..
                (colorOffset +
                 8)];

        var color0 =
            BinaryPrimitives
                .ReadUInt16LittleEndian(
                    colorBlock[
                        0..
                        2]);
        var color1 =
            BinaryPrimitives
                .ReadUInt16LittleEndian(
                    colorBlock[
                        2..
                        4]);

        Span<byte> palette =
            stackalloc byte[
                16];

        DecodeRgb565(
            color0,
            palette,
            0);
        DecodeRgb565(
            color1,
            palette,
            4);

        var forceFourColor =
            format !=
            BcFormat.Bc1;

        if (forceFourColor ||
            color0 >
                color1)
        {
            MixColor(
                palette,
                0,
                4,
                8,
                2,
                1);
            MixColor(
                palette,
                0,
                4,
                12,
                1,
                2);
        }
        else
        {
            MixColor(
                palette,
                0,
                4,
                8,
                1,
                1);

            palette[12] =
                0;
            palette[13] =
                0;
            palette[14] =
                0;
            palette[15] =
                0;
        }

        var indices =
            BinaryPrimitives
                .ReadUInt32LittleEndian(
                    colorBlock[
                        4..
                        8]);

        for (var pixel = 0;
             pixel <
                 16;
             pixel++)
        {
            var x =
                originX +
                pixel %
                    4;
            var y =
                originY +
                pixel /
                    4;

            if (x >=
                    width ||
                y >=
                    height)
            {
                continue;
            }

            var paletteIndex =
                (int)(
                    (indices >>
                     (pixel *
                      2)) &
                    0x03);

            var source =
                paletteIndex *
                4;

            var target =
                (y *
                     width +
                 x) *
                4;

            rgba[target] =
                palette[source];
            rgba[target + 1] =
                palette[source + 1];
            rgba[target + 2] =
                palette[source + 2];

            rgba[target + 3] =
                format ==
                        BcFormat.Bc1 &&
                    color0 <=
                        color1 &&
                    paletteIndex ==
                        3
                    ? (byte)0
                    : alpha[pixel];
        }
    }

    private static void DecodeBc3Alpha(
        ReadOnlySpan<byte> block,
        Span<byte> alpha)
    {
        Span<byte> palette =
            stackalloc byte[8];

        palette[0] =
            block[0];
        palette[1] =
            block[1];

        if (palette[0] >
            palette[1])
        {
            for (var index = 1;
                 index <=
                     6;
                 index++)
            {
                palette[index + 1] =
                    (byte)(
                        ((7 -
                          index) *
                             palette[0] +
                         index *
                             palette[1]) /
                        7);
            }
        }
        else
        {
            for (var index = 1;
                 index <=
                     4;
                 index++)
            {
                palette[index + 1] =
                    (byte)(
                        ((5 -
                          index) *
                             palette[0] +
                         index *
                             palette[1]) /
                        5);
            }

            palette[6] =
                0;
            palette[7] =
                255;
        }

        ulong indices =
            0;

        for (var index = 0;
             index <
                 6;
             index++)
        {
            indices |=
                (ulong)block[
                    2 +
                    index] <<
                (index *
                 8);
        }

        for (var pixel = 0;
             pixel <
                 16;
             pixel++)
        {
            alpha[pixel] =
                palette[
                    (int)(
                        (indices >>
                         (pixel *
                          3)) &
                        0x07)];
        }
    }

    private static void DecodeRgb565(
        ushort packed,
        Span<byte> palette,
        int offset)
    {
        var red =
            (packed >>
             11) &
            0x1F;
        var green =
            (packed >>
             5) &
            0x3F;
        var blue =
            packed &
            0x1F;

        palette[offset] =
            (byte)(
                red *
                255 /
                31);
        palette[offset + 1] =
            (byte)(
                green *
                255 /
                63);
        palette[offset + 2] =
            (byte)(
                blue *
                255 /
                31);
        palette[offset + 3] =
            255;
    }

    private static void MixColor(
        Span<byte> palette,
        int first,
        int second,
        int target,
        int firstWeight,
        int secondWeight)
    {
        var denominator =
            firstWeight +
            secondWeight;

        for (var channel = 0;
             channel <
                 3;
             channel++)
        {
            palette[
                target +
                channel] =
                (byte)(
                    (palette[
                         first +
                         channel] *
                         firstWeight +
                     palette[
                         second +
                         channel] *
                         secondWeight) /
                    denominator);
        }

        palette[target + 3] =
            255;
    }

    private static byte[]? DecodeUncompressedDds32(
        Stream stream,
        int width,
        int height,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask)
    {
        var byteCount =
            checked(
                width *
                height *
                4);

        if (stream.Length -
                stream.Position <
            byteCount)
        {
            return null;
        }

        var source =
            new byte[
                byteCount];

        stream.ReadExactly(
            source);

        var rgba =
            new byte[
                byteCount];

        for (var pixel = 0;
             pixel <
                 width *
                 height;
             pixel++)
        {
            var packed =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        source.AsSpan(
                            pixel *
                                4,
                            4));

            var target =
                pixel *
                4;

            rgba[target] =
                ExtractMaskedChannel(
                    packed,
                    redMask,
                    0);
            rgba[target + 1] =
                ExtractMaskedChannel(
                    packed,
                    greenMask,
                    0);
            rgba[target + 2] =
                ExtractMaskedChannel(
                    packed,
                    blueMask,
                    0);
            rgba[target + 3] =
                ExtractMaskedChannel(
                    packed,
                    alphaMask,
                    255);
        }

        return rgba;
    }

    private static byte ExtractMaskedChannel(
        uint packed,
        uint mask,
        byte fallback)
    {
        if (mask ==
            0)
        {
            return fallback;
        }

        var shift =
            0;

        var shiftedMask =
            mask;

        while ((shiftedMask &
                1) ==
               0)
        {
            shiftedMask >>=
                1;
            shift++;
        }

        var max =
            shiftedMask;

        if (max ==
            0)
        {
            return fallback;
        }

        var value =
            (packed &
             mask) >>
            shift;

        return (byte)Math.Clamp(
            (int)Math.Round(
                value *
                255.0 /
                max),
            0,
            255);
    }

    private RuntimeGpuTexture? TryLoadTga(
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

    public bool TryReadRgba(
        string path,
        out byte[] pixels,
        out int width,
        out int height)
    {
        pixels =
            Array.Empty<byte>();
        width =
            0;
        height =
            0;

        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return false;
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

            converter.Initialize(
                frame,
                WICPixelFormat.Format32bppRGBA);

            var size =
                converter.Size;

            if (size.Width <= 0 ||
                size.Height <= 0 ||
                size.Width > 16_384 ||
                size.Height > 16_384)
            {
                return false;
            }

            var stride =
                checked(
                    (uint)size.Width *
                    4u);

            pixels =
                new byte[
                    checked(
                        size.Width *
                        size.Height *
                        4)];

            converter.CopyPixels(
                stride,
                pixels);

            width =
                size.Width;
            height =
                size.Height;

            return true;
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
            pixels =
                Array.Empty<byte>();
            width =
                0;
            height =
                0;
            return false;
        }
    }

    public RuntimeGpuTexture CreateFromRgba(
        byte[] pixels,
        int width,
        int height) =>
        CreateRgbaTexture(
            pixels,
            width,
            height);

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
