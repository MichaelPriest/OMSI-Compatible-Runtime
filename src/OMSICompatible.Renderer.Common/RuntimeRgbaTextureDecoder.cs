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
                var oldest =
                    Cache
                        .OrderBy(
                            static pair =>
                                pair.Value
                                    .LastUsedGeneration)
                        .First();

                _cacheBytes -=
                    oldest.Value.Texture.Pixels.LongLength;

                Cache.Remove(
                    oldest.Key);
            }
        }

        texture =
            decoded;

        return true;
    }
}
