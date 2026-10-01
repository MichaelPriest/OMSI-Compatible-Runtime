using System.Buffers.Binary;
using Vortice.WIC;
using WICPixelFormat =
    Vortice.WIC.PixelFormat;

namespace OMSICompatible.Renderer.Common;

public sealed record RuntimeDecodedTexture(
    byte[] Pixels,
    int Width,
    int Height);

public static class RuntimeRgbaTextureDecoder
{
    private sealed record CachedTexture(
        RuntimeDecodedTexture Texture,
        long SourceLength,
        DateTime SourceLastWriteUtc,
        long LastUsedGeneration);

    private static readonly object Gate =
        new();

    private static readonly Dictionary<string, CachedTexture> Cache =
        new(
            StringComparer.OrdinalIgnoreCase);

    private const long MaximumCacheBytes =
        256L * 1024L * 1024L;

    private const long MaximumSingleTextureBytes =
        64L * 1024L * 1024L;

    private static long _generation;
    private static long _cacheBytes;

    public static bool TryRead(
        string path,
        out RuntimeDecodedTexture texture)
    {
        texture =
            new RuntimeDecodedTexture(
                Array.Empty<byte>(),
                0,
                0);

        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return false;
        }

        FileInfo info;

        try
        {
            info =
                new FileInfo(
                    path);
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
        {
            return false;
        }

        lock (Gate)
        {
            _generation++;

            if (Cache.TryGetValue(
                    path,
                    out var cached) &&
                cached.SourceLength ==
                    info.Length &&
                cached.SourceLastWriteUtc ==
                    info.LastWriteTimeUtc)
            {
                var refreshed =
                    cached with
                    {
                        LastUsedGeneration =
                            _generation
                    };

                Cache[path] =
                    refreshed;

                texture =
                    refreshed.Texture;

                return true;
            }
        }

        RuntimeDecodedTexture decoded;

        if (string.Equals(
                Path.GetExtension(
                    path),
                ".tga",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadTga(
                    path,
                    out decoded))
            {
                return false;
            }
        }
        else
        {
            try
            {
                using var factory =
                    new IWICImagingFactory2();

                using var decoder =
                    factory.CreateDecoderFromFileName(
                        path);

                using var frame =
                    decoder.GetFrame(
                        0);

                using var converter =
                    factory.CreateFormatConverter();

                converter.Initialize(
                    frame,
                    WICPixelFormat.Format32bppRGBA);

                var size =
                    converter.Size;

                if (size.Width <=
                        0 ||
                    size.Height <=
                        0 ||
                    size.Width >
                        16_384 ||
                    size.Height >
                        16_384)
                {
                    return false;
                }

                var byteCount =
                    checked(
                        (long)size.Width *
                        size.Height *
                        4L);

                if (byteCount >
                        int.MaxValue ||
                    byteCount >
                        MaximumSingleTextureBytes)
                {
                    return false;
                }

                var pixels =
                    new byte[
                        (int)byteCount];

                converter.CopyPixels(
                    checked(
                        (uint)size.Width *
                        4u),
                    pixels);

                decoded =
                    new RuntimeDecodedTexture(
                        pixels,
                        size.Width,
                        size.Height);
            }
            catch (Exception exception)
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
                return false;
            }
        }

        lock (Gate)
        {
            _generation++;

            if (Cache.TryGetValue(
                    path,
                    out var previous))
            {
                _cacheBytes -=
                    previous.Texture.Pixels.LongLength;
            }

            Cache[path] =
                new CachedTexture(
                    decoded,
                    info.Length,
                    info.LastWriteTimeUtc,
                    _generation);

            _cacheBytes +=
                decoded.Pixels.LongLength;

            while (_cacheBytes >
                       MaximumCacheBytes &&
                   Cache.Count >
                       1)
            {
                string? oldestKey =
                    null;
                CachedTexture? oldestValue =
                    null;

                foreach (var pair in
                         Cache)
                {
                    if (oldestValue is null ||
                        pair.Value.LastUsedGeneration <
                            oldestValue.LastUsedGeneration)
                    {
                        oldestKey =
                            pair.Key;
                        oldestValue =
                            pair.Value;
                    }
                }

                if (oldestKey is null ||
                    oldestValue is null)
                {
                    break;
                }

                _cacheBytes -=
                    oldestValue.Texture.Pixels.LongLength;

                Cache.Remove(
                    oldestKey);
            }
        }

        texture =
            decoded;

        return true;
    }

    private static bool TryReadTga(
        string path,
        out RuntimeDecodedTexture texture)
    {
        texture =
            new RuntimeDecodedTexture(
                Array.Empty<byte>(),
                0,
                0);

        try
        {
            using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            Span<byte> header =
                stackalloc byte[
                    18];

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
                        header[
                            12..
                            14]);

            var height =
                BinaryPrimitives
                    .ReadUInt16LittleEndian(
                        header[
                            14..
                            16]);

            var pixelDepth =
                header[16];

            var descriptor =
                header[17];

            if (colorMapType !=
                    0 ||
                imageType is not
                    (2 or 10) ||
                width ==
                    0 ||
                height ==
                    0 ||
                width >
                    16_384 ||
                height >
                    16_384 ||
                pixelDepth is not
                    (24 or 32))
            {
                return false;
            }

            if (idLength >
                0)
            {
                stream.Seek(
                    idLength,
                    SeekOrigin.Current);
            }

            var bytesPerPixel =
                pixelDepth /
                8;

            var pixelCount =
                checked(
                    (int)width *
                    height);

            var source =
                new byte[
                    checked(
                        pixelCount *
                        bytesPerPixel)];

            if (imageType ==
                2)
            {
                stream.ReadExactly(
                    source);
            }
            else if (!TryDecodeTgaRle(
                         stream,
                         source,
                         bytesPerPixel,
                         pixelCount))
            {
                return false;
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

            for (var sourceIndex = 0;
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
                    (targetY *
                     width +
                     targetX) *
                    4;

                rgba[outputOffset] =
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
                    bytesPerPixel ==
                            4
                        ? source[
                            inputOffset +
                            3]
                        : (byte)255;
            }

            texture =
                new RuntimeDecodedTexture(
                    rgba,
                    width,
                    height);

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

    private static bool TryDecodeTgaRle(
        Stream stream,
        byte[] destination,
        int bytesPerPixel,
        int pixelCount)
    {
        var pixelIndex =
            0;

        Span<byte> pixel =
            stackalloc byte[
                4];

        while (pixelIndex <
               pixelCount)
        {
            var packetHeader =
                stream.ReadByte();

            if (packetHeader <
                0)
            {
                return false;
            }

            var count =
                (packetHeader &
                 0x7F) +
                1;

            if (pixelIndex +
                    count >
                pixelCount)
            {
                return false;
            }

            if ((packetHeader &
                 0x80) !=
                0)
            {
                stream.ReadExactly(
                    pixel[
                        ..bytesPerPixel]);

                for (var repeat = 0;
                     repeat <
                         count;
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
}
