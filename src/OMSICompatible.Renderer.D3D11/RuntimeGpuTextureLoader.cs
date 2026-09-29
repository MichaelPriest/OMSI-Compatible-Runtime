using System.Buffers.Binary;
using System.Runtime.InteropServices;
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
        ID3D11ShaderResourceView view,
        long approximateBytes = 0)
    {
        Texture = texture;
        View = view;
        ApproximateBytes =
            Math.Max(
                0,
                approximateBytes);
    }

    public ID3D11Texture2D Texture
    {
        get;
    }

    public ID3D11ShaderResourceView View
    {
        get;
    }

    public long ApproximateBytes
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
    private sealed record CachedTextureFile(
        byte[] Bytes,
        long Length,
        DateTime LastWriteUtc,
        long LastUsedGeneration);

    private sealed record CachedDecodedTexture(
        byte[] Pixels,
        int Width,
        int Height,
        long SourceLength,
        DateTime SourceLastWriteUtc,
        long LastUsedGeneration);

    private static readonly object TextureFileCacheGate =
        new();
    private static readonly Dictionary<string, CachedTextureFile>
        TextureFileCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private static long _textureFileCacheGeneration;
    private static long _textureFileCacheBytes;
    private static long _textureFileCacheHits;
    private static long _textureFileCacheMisses;
    private static long _textureFileCacheEvictions;

    private static readonly object DecodedTextureCacheGate =
        new();
    private static readonly Dictionary<string, CachedDecodedTexture>
        DecodedTextureCache =
            new(
                StringComparer.OrdinalIgnoreCase);
    private static long _decodedTextureCacheGeneration;
    private static long _decodedTextureCacheBytes;
    private static long _decodedTextureCacheHits;
    private static long _decodedTextureCacheMisses;
    private static long _decodedTextureCacheEvictions;
    private static long _directBcTextureUploads;
    private static long _directBcTextureUploadBytes;

    private const long MaximumDecodedTextureCacheBytes =
        256L * 1024L * 1024L;
    private const long MaximumSingleDecodedTextureCacheBytes =
        64L * 1024L * 1024L;

    private const long MaximumTextureFileCacheBytes =
        512L * 1024L * 1024L;
    private const long MaximumSingleTextureFileCacheBytes =
        64L * 1024L * 1024L;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _deviceContext;

    public RuntimeGpuTextureLoader(
        ID3D11Device device,
        ID3D11DeviceContext? deviceContext = null)
    {
        _device =
            device ??
            throw new ArgumentNullException(
                nameof(device));

        _deviceContext =
            deviceContext ??
            _device.ImmediateContext;
    }

    public static int WarmFileCache(
        IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(
            paths);

        var warmed =
            0;

        foreach (var path in
                 paths
                     .Where(
                         static value =>
                             !string.IsNullOrWhiteSpace(
                                 value))
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            var extension =
                Path.GetExtension(
                    path);

            if (!string.Equals(
                    extension,
                    ".dds",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".tga",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryGetCachedFileBytes(
                    path,
                    out _))
            {
                warmed++;
            }
        }

        return warmed;
    }

    public static int WarmDecodedCache(
        IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(
            paths);

        var warmed =
            0;

        foreach (var path in
                 paths
                     .Where(
                         static value =>
                             !string.IsNullOrWhiteSpace(
                                 value))
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            var extension =
                Path.GetExtension(
                    path);

            if (!string.Equals(
                    extension,
                    ".bmp",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".png",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".jpg",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".jpeg",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryReadRgba(
                    path,
                    out _,
                    out _,
                    out _,
                    requireCacheable:
                        true))
            {
                warmed++;
            }
        }

        return warmed;
    }

    public static string GetFileCacheDiagnostics()
    {
        lock (TextureFileCacheGate)
        {
            long decodedHits;
            long decodedMisses;
            long decodedEvictions;
            int decodedEntries;
            long decodedBytes;

            lock (DecodedTextureCacheGate)
            {
                decodedHits =
                    _decodedTextureCacheHits;
                decodedMisses =
                    _decodedTextureCacheMisses;
                decodedEvictions =
                    _decodedTextureCacheEvictions;
                decodedEntries =
                    DecodedTextureCache.Count;
                decodedBytes =
                    _decodedTextureCacheBytes;
            }

            return
                $"fileHits={_textureFileCacheHits}; fileMisses={_textureFileCacheMisses}; fileEvictions={_textureFileCacheEvictions}; fileEntries={TextureFileCache.Count}; fileMB={_textureFileCacheBytes / (1024.0 * 1024.0):0.0}/{MaximumTextureFileCacheBytes / (1024.0 * 1024.0):0}; rgbaHits={decodedHits}; rgbaMisses={decodedMisses}; rgbaEvictions={decodedEvictions}; rgbaEntries={decodedEntries}; rgbaMB={decodedBytes / (1024.0 * 1024.0):0.0}/{MaximumDecodedTextureCacheBytes / (1024.0 * 1024.0):0}; bcUploads={Interlocked.Read(ref _directBcTextureUploads)}; bcMB={Interlocked.Read(ref _directBcTextureUploadBytes) / (1024.0 * 1024.0):0.0}";
        }
    }

    private static Stream OpenCachedReadStream(
        string path)
    {
        if (TryGetCachedFileBytes(
                path,
                out var bytes))
        {
            return new MemoryStream(
                bytes,
                writable:
                    false);
        }

        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
    }

    private static bool TryGetCachedFileBytes(
        string path,
        out byte[] bytes)
    {
        bytes =
            Array.Empty<byte>();

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

            if (info.Length <=
                    0 ||
                info.Length >
                    MaximumSingleTextureFileCacheBytes)
            {
                return false;
            }
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

        lock (TextureFileCacheGate)
        {
            _textureFileCacheGeneration++;

            if (TextureFileCache.TryGetValue(
                    path,
                    out var cached) &&
                cached.Length ==
                    info.Length &&
                cached.LastWriteUtc ==
                    info.LastWriteTimeUtc)
            {
                TextureFileCache[
                    path] =
                    cached with
                    {
                        LastUsedGeneration =
                            _textureFileCacheGeneration
                    };

                _textureFileCacheHits++;

                bytes =
                    cached.Bytes;

                return true;
            }
        }

        lock (TextureFileCacheGate)
        {
            _textureFileCacheMisses++;
        }

        byte[] loaded;

        try
        {
            loaded =
                File.ReadAllBytes(
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

        lock (TextureFileCacheGate)
        {
            _textureFileCacheGeneration++;

            if (TextureFileCache.TryGetValue(
                    path,
                    out var previous))
            {
                _textureFileCacheBytes -=
                    previous.Bytes.LongLength;
            }

            var entry =
                new CachedTextureFile(
                    loaded,
                    info.Length,
                    info.LastWriteTimeUtc,
                    _textureFileCacheGeneration);

            TextureFileCache[
                path] =
                entry;

            _textureFileCacheBytes +=
                loaded.LongLength;

            while (_textureFileCacheBytes >
                       MaximumTextureFileCacheBytes &&
                   TextureFileCache.Count >
                       1)
            {
                string? oldestKey =
                    null;
                CachedTextureFile? oldestValue =
                    null;

                foreach (var pair in
                         TextureFileCache)
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

                _textureFileCacheBytes -=
                    oldestValue.Bytes.LongLength;

                TextureFileCache.Remove(
                    oldestKey);

                _textureFileCacheEvictions++;
            }

            bytes =
                entry.Bytes;

            return true;
        }
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
                OpenCachedReadStream(
                    path);

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

            var mipMapCount =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        span.Slice(
                            28,
                            4));

            mipMapCount =
                Math.Clamp(
                    mipMapCount <=
                            0
                        ? 1
                        : mipMapCount,
                    1,
                    16);

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

        if (TryReadRgba(
                path,
                out var pixels,
                out var width,
                out var height))
        {
            return CreateRgbaTexture(
                pixels,
                width,
                height);
        }

        return null;
    }

    private RuntimeGpuTexture? TryLoadDds(

        string path)
    {
        try
        {
            using var stream =
                OpenCachedReadStream(
                    path);

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

            var mipMapCount =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        span.Slice(
                            28,
                            4));

            mipMapCount =
                Math.Clamp(
                    mipMapCount <=
                            0
                        ? 1
                        : mipMapCount,
                    1,
                    16);

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

            var bcFormat =
                fourCc switch
                {
                    dxt1 =>
                        BcFormat.Bc1,
                    dxt2 or dxt3 =>
                        BcFormat.Bc2,
                    dxt4 or dxt5 =>
                        BcFormat.Bc3,
                    _ =>
                        (BcFormat?)null
                };

            if (bcFormat.HasValue)
            {
                var compressed =
                    TryCreateBcTexture(
                        stream,
                        width,
                        height,
                        bcFormat.Value,
                        mipMapCount);

                if (compressed is not null)
                {
                    return compressed;
                }

                // Preserve the established RGBA decoder as a compatibility
                // fallback for drivers/content that reject a compressed upload.
                stream.Position =
                    dataOffset;
            }

            byte[]? rgba =
                bcFormat.HasValue
                    ? DecodeBcTexture(
                        stream,
                        width,
                        height,
                        bcFormat.Value)
                    : null;

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

    private RuntimeGpuTexture? TryCreateBcTexture(
        Stream stream,
        int width,
        int height,
        BcFormat format,
        int requestedMipLevels)
    {
        var blockBytes =
            format ==
                BcFormat.Bc1
                ? 8
                : 16;

        var gpuFormat =
            format switch
            {
                BcFormat.Bc1 =>
                    Format.BC1_UNorm,
                BcFormat.Bc2 =>
                    Format.BC2_UNorm,
                BcFormat.Bc3 =>
                    Format.BC3_UNorm,
                _ =>
                    Format.Unknown
            };

        if (gpuFormat ==
            Format.Unknown)
        {
            return null;
        }

        var maximumMipLevels =
            1 +
            (int)Math.Floor(
                Math.Log2(
                    Math.Max(
                        width,
                        height)));

        var mipLevels =
            Math.Clamp(
                requestedMipLevels,
                1,
                maximumMipLevels);

        var mipData =
            new List<byte[]>(
                mipLevels);

        var mipWidth =
            width;
        var mipHeight =
            height;
        long totalCompressedBytes =
            0;

        for (var mip = 0;
             mip < mipLevels;
             mip++)
        {
            var blocksX =
                Math.Max(
                    1,
                    (mipWidth +
                     3) /
                    4);

            var blocksY =
                Math.Max(
                    1,
                    (mipHeight +
                     3) /
                    4);

            var requiredBytes =
                checked(
                    blocksX *
                    blocksY *
                    blockBytes);

            if (stream.Length -
                    stream.Position <
                requiredBytes)
            {
                break;
            }

            var compressed =
                new byte[
                    requiredBytes];

            stream.ReadExactly(
                compressed);

            mipData.Add(
                compressed);

            totalCompressedBytes +=
                compressed.LongLength;

            mipWidth =
                Math.Max(
                    1,
                    mipWidth /
                    2);

            mipHeight =
                Math.Max(
                    1,
                    mipHeight /
                    2);
        }

        if (mipData.Count ==
            0)
        {
            return null;
        }

        var handles =
            new GCHandle[
                mipData.Count];

        try
        {
            var initialData =
                new SubresourceData[
                    mipData.Count];

            mipWidth =
                width;
            mipHeight =
                height;

            for (var mip = 0;
                 mip < mipData.Count;
                 mip++)
            {
                handles[mip] =
                    GCHandle.Alloc(
                        mipData[mip],
                        GCHandleType.Pinned);

                var blocksX =
                    Math.Max(
                        1,
                        (mipWidth +
                         3) /
                        4);

                var blocksY =
                    Math.Max(
                        1,
                        (mipHeight +
                         3) /
                        4);

                var rowPitch =
                    checked(
                        (uint)(
                            blocksX *
                            blockBytes));

                var slicePitch =
                    checked(
                        rowPitch *
                        (uint)blocksY);

                initialData[mip] =
                    new SubresourceData(
                        handles[mip]
                            .AddrOfPinnedObject(),
                        rowPitch,
                        slicePitch);

                mipWidth =
                    Math.Max(
                        1,
                        mipWidth /
                        2);

                mipHeight =
                    Math.Max(
                        1,
                        mipHeight /
                        2);
            }

            var texture =
                _device.CreateTexture2D(
                    gpuFormat,
                    (uint)width,
                    (uint)height,
                    mipLevels:
                        (uint)mipData.Count,
                    initialData:
                        initialData,
                    bindFlags:
                        BindFlags
                            .ShaderResource);

            var view =
                _device.CreateShaderResourceView(
                    texture);

            Interlocked.Increment(
                ref _directBcTextureUploads);

            Interlocked.Add(
                ref _directBcTextureUploadBytes,
                totalCompressedBytes);

            return new RuntimeGpuTexture(
                texture,
                view,
                totalCompressedBytes);
        }
        catch (Exception exception)
            when (
                exception is
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
        finally
        {
            foreach (var handle in
                     handles)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
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
                OpenCachedReadStream(
                    path);

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

    public static bool TryReadRgba(
        string path,
        out byte[] pixels,
        out int width,
        out int height,
        bool requireCacheable =
            false)
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

        lock (DecodedTextureCacheGate)
        {
            _decodedTextureCacheGeneration++;

            if (DecodedTextureCache.TryGetValue(
                    path,
                    out var cached) &&
                cached.SourceLength ==
                    info.Length &&
                cached.SourceLastWriteUtc ==
                    info.LastWriteTimeUtc)
            {
                DecodedTextureCache[
                    path] =
                    cached with
                    {
                        LastUsedGeneration =
                            _decodedTextureCacheGeneration
                    };

                _decodedTextureCacheHits++;

                pixels =
                    cached.Pixels;
                width =
                    cached.Width;
                height =
                    cached.Height;

                return true;
            }

            _decodedTextureCacheMisses++;
        }

        byte[] decodedPixels;
        int decodedWidth;
        int decodedHeight;

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

            var decodedByteCount =
                checked(
                    (long)size.Width *
                    size.Height *
                    4L);

            if (decodedByteCount >
                    int.MaxValue ||
                requireCacheable &&
                decodedByteCount >
                    MaximumSingleDecodedTextureCacheBytes)
            {
                return false;
            }

            var stride =
                checked(
                    (uint)size.Width *
                    4u);

            decodedPixels =
                new byte[
                    (int)decodedByteCount];

            converter.CopyPixels(
                stride,
                decodedPixels);

            decodedWidth =
                size.Width;
            decodedHeight =
                size.Height;
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

        if (decodedPixels.LongLength <=
            MaximumSingleDecodedTextureCacheBytes)
        {
            lock (DecodedTextureCacheGate)
            {
                _decodedTextureCacheGeneration++;

                if (DecodedTextureCache.TryGetValue(
                        path,
                        out var previous))
                {
                    _decodedTextureCacheBytes -=
                        previous.Pixels.LongLength;
                }

                var entry =
                    new CachedDecodedTexture(
                        decodedPixels,
                        decodedWidth,
                        decodedHeight,
                        info.Length,
                        info.LastWriteTimeUtc,
                        _decodedTextureCacheGeneration);

                DecodedTextureCache[
                    path] =
                    entry;

                _decodedTextureCacheBytes +=
                    decodedPixels.LongLength;

                while (_decodedTextureCacheBytes >
                           MaximumDecodedTextureCacheBytes &&
                       DecodedTextureCache.Count >
                           1)
                {
                    string? oldestKey =
                        null;
                    CachedDecodedTexture? oldestValue =
                        null;

                    foreach (var pair in
                             DecodedTextureCache)
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

                    _decodedTextureCacheBytes -=
                        oldestValue.Pixels.LongLength;

                    DecodedTextureCache.Remove(
                        oldestKey);

                    _decodedTextureCacheEvictions++;
                }
            }
        }

        pixels =
            decodedPixels;
        width =
            decodedWidth;
        height =
            decodedHeight;

        return true;
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
            _device.CreateTexture2D(
                Format.R8G8B8A8_UNorm,
                (uint)width,
                (uint)height,
                mipLevels:
                    0,
                bindFlags:
                    BindFlags.ShaderResource |
                    BindFlags.RenderTarget,
                miscFlags:
                    ResourceOptionFlags.GenerateMips);

        _deviceContext.UpdateSubresource(
            pixels,
            texture,
            subresource:
                0,
            rowPitch:
                checked(
                    (uint)width *
                    4u),
            depthPitch:
                checked(
                    (uint)width *
                    (uint)height *
                    4u));

        var view =
            _device.CreateShaderResourceView(
                texture);

        _deviceContext.GenerateMips(
            view);

        var baseBytes =
            checked(
                (long)width *
                height *
                4L);

        // Full mip chains converge to ~4/3 of the base level for
        // two-dimensional textures.
        var approximateBytes =
            checked(
                baseBytes +
                baseBytes /
                    3L);

        return new RuntimeGpuTexture(
            texture,
            view,
            approximateBytes);
    }


}
