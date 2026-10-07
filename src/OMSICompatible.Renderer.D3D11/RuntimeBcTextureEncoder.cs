using System.Buffers.Binary;

namespace OMSICompatible.Renderer.D3D11;

internal enum RuntimeBcCompressionFormat
{
    Bc1,
    Bc3
}

internal sealed class RuntimeBcPreparedTexture
{
    public RuntimeBcPreparedTexture(
        int width,
        int height,
        RuntimeBcCompressionFormat format,
        byte[][] levels,
        double colorPsnr,
        double alphaPsnr)
    {
        Width = width;
        Height = height;
        Format = format;
        Levels = levels;
        ColorPsnr = colorPsnr;
        AlphaPsnr = alphaPsnr;

        long totalBytes = 0;

        foreach (var level in levels)
        {
            totalBytes += level.LongLength;
        }

        TotalBytes = totalBytes;
    }

    public int Width { get; }

    public int Height { get; }

    public RuntimeBcCompressionFormat Format { get; }

    public byte[][] Levels { get; }

    public double ColorPsnr { get; }

    public double AlphaPsnr { get; }

    public long TotalBytes { get; }
}

/// <summary>
/// Worker-side BC1/BC3 encoder for loose OMSI textures. The fitting strategy
/// follows openOMSI's omsi-texture BC path: luma-weighted principal-axis
/// colour endpoints, BC3 alpha interpolation, a top-level PSNR quality gate,
/// and a linear-light sRGB mip chain. The renderer keeps its RGBA fallback
/// when a texture is too small, cannot be represented safely, or misses the
/// quality threshold.
/// </summary>
internal static class RuntimeBcTextureEncoder
{
    private const int MinimumCompressionTexels =
        64 * 64;

    private const double MinimumColorPsnr =
        33.0;

    private const double MinimumAlphaPsnr =
        30.0;

    private const float WeightRed =
        0.299f;

    private const float WeightGreen =
        0.587f;

    private const float WeightBlue =
        0.114f;

    private static readonly float[] SrgbToLinear =
        BuildSrgbToLinear();

    private static readonly byte[] LinearToSrgb =
        BuildLinearToSrgb();

    public static RuntimeBcPreparedTexture? TryPrepare(
        byte[] rgba,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(
            rgba);

        if (width <=
                0 ||
            height <=
                0 ||
            (long)width *
                height <
                MinimumCompressionTexels ||
            width % 4 !=
                0 ||
            height % 4 !=
                0)
        {
            return null;
        }

        var requiredBytes =
            checked(
                (long)width *
                height *
                4L);

        if (requiredBytes >
                int.MaxValue ||
            rgba.LongLength <
                requiredBytes)
        {
            return null;
        }

        var opaque =
            true;

        for (var index = 3;
             index <
             requiredBytes;
             index +=
                 4)
        {
            if (rgba[index] ==
                255)
            {
                continue;
            }

            opaque =
                false;
            break;
        }

        var format =
            opaque
                ? RuntimeBcCompressionFormat.Bc1
                : RuntimeBcCompressionFormat.Bc3;

        var top =
            EncodeLevel(
                rgba,
                width,
                height,
                format);

        var texels =
            (long)width *
            height;

        var colorPsnr =
            CalculatePsnr(
                top.ColorError,
                texels);

        var alphaPsnr =
            opaque
                ? 99.0
                : CalculatePsnr(
                    top.AlphaError,
                    texels);

        if (colorPsnr <
                MinimumColorPsnr ||
            alphaPsnr <
                MinimumAlphaPsnr)
        {
            return null;
        }

        var levels =
            new List<byte[]>
            {
                top.Blocks
            };

        var current =
            rgba;
        var currentWidth =
            width;
        var currentHeight =
            height;

        while (currentWidth >
                   1 ||
               currentHeight >
                   1)
        {
            current =
                DownsampleLinearLight(
                    current,
                    currentWidth,
                    currentHeight,
                    out currentWidth,
                    out currentHeight);

            levels.Add(
                EncodeLevel(
                        current,
                        currentWidth,
                        currentHeight,
                        format)
                    .Blocks);
        }

        return new RuntimeBcPreparedTexture(
            width,
            height,
            format,
            levels.ToArray(),
            colorPsnr,
            alphaPsnr);
    }

    private static (
        byte[] Blocks,
        double ColorError,
        double AlphaError)
        EncodeLevel(
            byte[] rgba,
            int width,
            int height,
            RuntimeBcCompressionFormat format)
    {
        var blocksX =
            Math.Max(
                1,
                (width +
                 3) /
                4);

        var blocksY =
            Math.Max(
                1,
                (height +
                 3) /
                4);

        var blockBytes =
            format ==
                    RuntimeBcCompressionFormat.Bc1
                ? 8
                : 16;

        var output =
            new byte[
                checked(
                    blocksX *
                    blocksY *
                    blockBytes)];

        double colorError =
            0.0;
        double alphaError =
            0.0;

        Span<byte> block =
            stackalloc byte[64];

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
                GatherBlock(
                    rgba,
                    width,
                    height,
                    blockX,
                    blockY,
                    block);

                var outputOffset =
                    (
                        blockY *
                            blocksX +
                        blockX
                    ) *
                    blockBytes;

                if (format ==
                    RuntimeBcCompressionFormat.Bc3)
                {
                    alphaError +=
                        EncodeBc3Alpha(
                            block,
                            output.AsSpan(
                                outputOffset,
                                8));

                    colorError +=
                        EncodeColor(
                            block,
                            output.AsSpan(
                                outputOffset +
                                8,
                                8));
                }
                else
                {
                    colorError +=
                        EncodeColor(
                            block,
                            output.AsSpan(
                                outputOffset,
                                8));
                }
            }
        }

        return (
            output,
            colorError,
            alphaError);
    }

    private static void GatherBlock(
        byte[] rgba,
        int width,
        int height,
        int blockX,
        int blockY,
        Span<byte> destination)
    {
        for (var localY = 0;
             localY <
             4;
             localY++)
        {
            var sourceY =
                Math.Min(
                    blockY *
                        4 +
                    localY,
                    height -
                        1);

            for (var localX = 0;
                 localX <
                 4;
                 localX++)
            {
                var sourceX =
                    Math.Min(
                        blockX *
                            4 +
                        localX,
                        width -
                            1);

                var source =
                    (
                        sourceY *
                            width +
                        sourceX
                    ) *
                    4;

                var target =
                    (
                        localY *
                            4 +
                        localX
                    ) *
                    4;

                destination[target] =
                    rgba[source];
                destination[target + 1] =
                    rgba[source + 1];
                destination[target + 2] =
                    rgba[source + 2];
                destination[target + 3] =
                    rgba[source + 3];
            }
        }
    }

    private static double EncodeColor(
        ReadOnlySpan<byte> rgba,
        Span<byte> destination)
    {
        Span<float> rgb =
            stackalloc float[
                16 *
                3];

        var sqrtRed =
            MathF.Sqrt(
                WeightRed);
        var sqrtGreen =
            MathF.Sqrt(
                WeightGreen);
        var sqrtBlue =
            MathF.Sqrt(
                WeightBlue);

        Span<float> mean =
            stackalloc float[3];

        Span<float> low =
            stackalloc float[3]
            {
                float.MaxValue,
                float.MaxValue,
                float.MaxValue
            };

        Span<float> high =
            stackalloc float[3]
            {
                float.MinValue,
                float.MinValue,
                float.MinValue
            };

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            var source =
                pixel *
                4;

            var r =
                rgba[source];
            var g =
                rgba[source + 1];
            var b =
                rgba[source + 2];

            var target =
                pixel *
                3;

            rgb[target] =
                r;
            rgb[target + 1] =
                g;
            rgb[target + 2] =
                b;

            var weightedR =
                r *
                sqrtRed;
            var weightedG =
                g *
                sqrtGreen;
            var weightedB =
                b *
                sqrtBlue;

            mean[0] +=
                weightedR;
            mean[1] +=
                weightedG;
            mean[2] +=
                weightedB;

            low[0] =
                MathF.Min(
                    low[0],
                    weightedR);
            low[1] =
                MathF.Min(
                    low[1],
                    weightedG);
            low[2] =
                MathF.Min(
                    low[2],
                    weightedB);

            high[0] =
                MathF.Max(
                    high[0],
                    weightedR);
            high[1] =
                MathF.Max(
                    high[1],
                    weightedG);
            high[2] =
                MathF.Max(
                    high[2],
                    weightedB);
        }

        mean[0] /=
            16.0f;
        mean[1] /=
            16.0f;
        mean[2] /=
            16.0f;

        Span<float> covariance =
            stackalloc float[6];

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            var source =
                pixel *
                3;

            var dr =
                rgb[source] *
                    sqrtRed -
                mean[0];
            var dg =
                rgb[source + 1] *
                    sqrtGreen -
                mean[1];
            var db =
                rgb[source + 2] *
                    sqrtBlue -
                mean[2];

            covariance[0] +=
                dr *
                dr;
            covariance[1] +=
                dr *
                dg;
            covariance[2] +=
                dr *
                db;
            covariance[3] +=
                dg *
                dg;
            covariance[4] +=
                dg *
                db;
            covariance[5] +=
                db *
                db;
        }

        Span<float> axis =
            stackalloc float[3]
            {
                high[0] -
                    low[0],
                high[1] -
                    low[1],
                high[2] -
                    low[2]
            };

        for (var round = 0;
             round <
             3;
             round++)
        {
            var vr =
                covariance[0] *
                    axis[0] +
                covariance[1] *
                    axis[1] +
                covariance[2] *
                    axis[2];

            var vg =
                covariance[1] *
                    axis[0] +
                covariance[3] *
                    axis[1] +
                covariance[4] *
                    axis[2];

            var vb =
                covariance[2] *
                    axis[0] +
                covariance[4] *
                    axis[1] +
                covariance[5] *
                    axis[2];

            var maximum =
                MathF.Max(
                    MathF.Abs(
                        vr),
                    MathF.Max(
                        MathF.Abs(
                            vg),
                        MathF.Abs(
                            vb)));

            if (maximum <
                0.000001f)
            {
                break;
            }

            axis[0] =
                vr /
                maximum;
            axis[1] =
                vg /
                maximum;
            axis[2] =
                vb /
                maximum;
        }

        var axisLengthSquared =
            axis[0] *
                axis[0] +
            axis[1] *
                axis[1] +
            axis[2] *
                axis[2];

        if (axisLengthSquared <
            0.000001f)
        {
            axis[0] =
                1.0f;
            axis[1] =
                1.0f;
            axis[2] =
                1.0f;

            axisLengthSquared =
                3.0f;
        }

        var minimumProjection =
            float.MaxValue;
        var maximumProjection =
            float.MinValue;

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            var source =
                pixel *
                3;

            var projection =
                (
                    rgb[source] *
                        sqrtRed -
                    mean[0]
                ) *
                    axis[0] +
                (
                    rgb[source + 1] *
                        sqrtGreen -
                    mean[1]
                ) *
                    axis[1] +
                (
                    rgb[source + 2] *
                        sqrtBlue -
                    mean[2]
                ) *
                    axis[2];

            minimumProjection =
                MathF.Min(
                    minimumProjection,
                    projection);

            maximumProjection =
                MathF.Max(
                    maximumProjection,
                    projection);
        }

        var scaleMax =
            maximumProjection /
            axisLengthSquared;
        var scaleMin =
            minimumProjection /
            axisLengthSquared;

        var end0 =
            ToRgb565(
                (
                    mean[0] +
                    axis[0] *
                        scaleMax
                ) /
                sqrtRed,
                (
                    mean[1] +
                    axis[1] *
                        scaleMax
                ) /
                sqrtGreen,
                (
                    mean[2] +
                    axis[2] *
                        scaleMax
                ) /
                sqrtBlue);

        var end1 =
            ToRgb565(
                (
                    mean[0] +
                    axis[0] *
                        scaleMin
                ) /
                sqrtRed,
                (
                    mean[1] +
                    axis[1] *
                        scaleMin
                ) /
                sqrtGreen,
                (
                    mean[2] +
                    axis[2] *
                        scaleMin
                ) /
                sqrtBlue);

        if (end0 <
            end1)
        {
            (
                end0,
                end1
            ) =
                (
                    end1,
                    end0
                );
        }

        Span<float> palette =
            stackalloc float[
                4 *
                3];

        BuildFourColorPalette(
            end0,
            end1,
            palette);

        uint indices =
            0;
        double totalError =
            0.0;

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            var source =
                pixel *
                3;

            var bestIndex =
                0;
            var bestError =
                double.MaxValue;

            for (var paletteIndex = 0;
                 paletteIndex <
                 4;
                 paletteIndex++)
            {
                var paletteOffset =
                    paletteIndex *
                    3;

                var error =
                    WeightedColorError(
                        rgb[source],
                        rgb[source + 1],
                        rgb[source + 2],
                        palette[paletteOffset],
                        palette[paletteOffset + 1],
                        palette[paletteOffset + 2]);

                if (error >=
                    bestError)
                {
                    continue;
                }

                bestError =
                    error;
                bestIndex =
                    paletteIndex;
            }

            indices |=
                (uint)bestIndex <<
                (
                    pixel *
                    2
                );

            totalError +=
                bestError;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            destination[
                0..
                2],
            end0);

        BinaryPrimitives.WriteUInt16LittleEndian(
            destination[
                2..
                4],
            end1);

        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[
                4..
                8],
            indices);

        return totalError;
    }

    private static double EncodeBc3Alpha(
        ReadOnlySpan<byte> rgba,
        Span<byte> destination)
    {
        byte minimum =
            255;
        byte maximum =
            0;
        byte minimumInner =
            255;
        byte maximumInner =
            0;

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            var alpha =
                rgba[
                    pixel *
                        4 +
                    3];

            minimum =
                Math.Min(
                    minimum,
                    alpha);
            maximum =
                Math.Max(
                    maximum,
                    alpha);

            if (alpha is
                0 or
                255)
            {
                continue;
            }

            minimumInner =
                Math.Min(
                    minimumInner,
                    alpha);
            maximumInner =
                Math.Max(
                    maximumInner,
                    alpha);
        }

        Span<byte> bestIndices =
            stackalloc byte[16];
        Span<byte> candidateIndices =
            stackalloc byte[16];

        var bestA0 =
            maximum;
        var bestA1 =
            minimum;

        var bestError =
            FitBc3Alpha(
                rgba,
                bestA0,
                bestA1,
                bestIndices);

        if (minimumInner >
            maximumInner)
        {
            var candidateError =
                FitBc3Alpha(
                    rgba,
                    0,
                    0,
                    candidateIndices);

            if (candidateError <
                bestError)
            {
                bestError =
                    candidateError;
                bestA0 =
                    0;
                bestA1 =
                    0;

                candidateIndices.CopyTo(
                    bestIndices);
            }
        }
        else if (minimum ==
                     0 ||
                 maximum ==
                     255)
        {
            var candidateError =
                FitBc3Alpha(
                    rgba,
                    minimumInner,
                    maximumInner,
                    candidateIndices);

            if (candidateError <
                bestError)
            {
                bestError =
                    candidateError;
                bestA0 =
                    minimumInner;
                bestA1 =
                    maximumInner;

                candidateIndices.CopyTo(
                    bestIndices);
            }
        }

        destination[0] =
            bestA0;
        destination[1] =
            bestA1;

        ulong packedIndices =
            0;

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            packedIndices |=
                (ulong)(
                    bestIndices[
                        pixel] &
                    0x07) <<
                (
                    pixel *
                    3
                );
        }

        for (var index = 0;
             index <
             6;
             index++)
        {
            destination[
                2 +
                index] =
                (byte)(
                    packedIndices >>
                    (
                        index *
                        8
                    ));
        }

        return bestError;
    }

    private static double FitBc3Alpha(
        ReadOnlySpan<byte> rgba,
        byte alpha0,
        byte alpha1,
        Span<byte> indices)
    {
        Span<int> palette =
            stackalloc int[8];

        palette[0] =
            alpha0;
        palette[1] =
            alpha1;

        if (alpha0 >
            alpha1)
        {
            for (var index = 1;
                 index <=
                 6;
                 index++)
            {
                palette[
                    index +
                    1] =
                    (
                        (
                            7 -
                            index
                        ) *
                            alpha0 +
                        index *
                            alpha1
                    ) /
                    7;
            }
        }
        else
        {
            for (var index = 1;
                 index <=
                 4;
                 index++)
            {
                palette[
                    index +
                    1] =
                    (
                        (
                            5 -
                            index
                        ) *
                            alpha0 +
                        index *
                            alpha1
                    ) /
                    5;
            }

            palette[6] =
                0;
            palette[7] =
                255;
        }

        double totalError =
            0.0;

        for (var pixel = 0;
             pixel <
             16;
             pixel++)
        {
            var alpha =
                rgba[
                    pixel *
                        4 +
                    3];

            var bestIndex =
                0;
            var bestDistance =
                int.MaxValue;

            for (var paletteIndex = 0;
                 paletteIndex <
                 8;
                 paletteIndex++)
            {
                var distance =
                    Math.Abs(
                        alpha -
                        palette[
                            paletteIndex]);

                if (distance >=
                    bestDistance)
                {
                    continue;
                }

                bestDistance =
                    distance;
                bestIndex =
                    paletteIndex;
            }

            indices[pixel] =
                (byte)bestIndex;

            totalError +=
                bestDistance *
                bestDistance;
        }

        return totalError;
    }

    private static void BuildFourColorPalette(
        ushort end0,
        ushort end1,
        Span<float> palette)
    {
        DecodeRgb565(
            end0,
            out var r0,
            out var g0,
            out var b0);

        DecodeRgb565(
            end1,
            out var r1,
            out var g1,
            out var b1);

        palette[0] =
            r0;
        palette[1] =
            g0;
        palette[2] =
            b0;

        palette[3] =
            r1;
        palette[4] =
            g1;
        palette[5] =
            b1;

        palette[6] =
            MathF.Floor(
                (
                    2.0f *
                        r0 +
                    r1
                ) /
                3.0f);

        palette[7] =
            MathF.Floor(
                (
                    2.0f *
                        g0 +
                    g1
                ) /
                3.0f);

        palette[8] =
            MathF.Floor(
                (
                    2.0f *
                        b0 +
                    b1
                ) /
                3.0f);

        palette[9] =
            MathF.Floor(
                (
                    r0 +
                    2.0f *
                        r1
                ) /
                3.0f);

        palette[10] =
            MathF.Floor(
                (
                    g0 +
                    2.0f *
                        g1
                ) /
                3.0f);

        palette[11] =
            MathF.Floor(
                (
                    b0 +
                    2.0f *
                        b1
                ) /
                3.0f);
    }

    private static ushort ToRgb565(
        float red,
        float green,
        float blue)
    {
        var r =
            (ushort)(
                Math.Clamp(
                    red,
                    0.0f,
                    255.0f) *
                    31.0f /
                    255.0f +
                0.5f);

        var g =
            (ushort)(
                Math.Clamp(
                    green,
                    0.0f,
                    255.0f) *
                    63.0f /
                    255.0f +
                0.5f);

        var b =
            (ushort)(
                Math.Clamp(
                    blue,
                    0.0f,
                    255.0f) *
                    31.0f /
                    255.0f +
                0.5f);

        return (ushort)(
            (
                r <<
                11
            ) |
            (
                g <<
                5
            ) |
            b);
    }

    private static void DecodeRgb565(
        ushort packed,
        out float red,
        out float green,
        out float blue)
    {
        var r =
            (
                packed >>
                11
            ) &
            0x1F;

        var g =
            (
                packed >>
                5
            ) &
            0x3F;

        var b =
            packed &
            0x1F;

        red =
            (
                r <<
                3
            ) |
            (
                r >>
                2
            );

        green =
            (
                g <<
                2
            ) |
            (
                g >>
                4
            );

        blue =
            (
                b <<
                3
            ) |
            (
                b >>
                2
            );
    }

    private static double WeightedColorError(
        float red,
        float green,
        float blue,
        float referenceRed,
        float referenceGreen,
        float referenceBlue)
    {
        var deltaRed =
            red -
            referenceRed;
        var deltaGreen =
            green -
            referenceGreen;
        var deltaBlue =
            blue -
            referenceBlue;

        return deltaRed *
                   deltaRed *
                   WeightRed +
               deltaGreen *
                   deltaGreen *
                   WeightGreen +
               deltaBlue *
                   deltaBlue *
                   WeightBlue;
    }

    private static double CalculatePsnr(
        double squaredError,
        long samples)
    {
        if (squaredError <=
                0.0 ||
            samples <=
                0)
        {
            return 99.0;
        }

        return 10.0 *
               Math.Log10(
                   65025.0 /
                   (
                       squaredError /
                       samples
                   ));
    }

    private static byte[] DownsampleLinearLight(
        byte[] rgba,
        int width,
        int height,
        out int outputWidth,
        out int outputHeight)
    {
        outputWidth =
            Math.Max(
                1,
                width /
                2);

        outputHeight =
            Math.Max(
                1,
                height /
                2);

        var output =
            new byte[
                checked(
                    outputWidth *
                    outputHeight *
                    4)];

        for (var y = 0;
             y <
             outputHeight;
             y++)
        {
            for (var x = 0;
                 x <
                 outputWidth;
                 x++)
            {
                var linearRed =
                    0.0f;
                var linearGreen =
                    0.0f;
                var linearBlue =
                    0.0f;
                var alpha =
                    0;

                for (var dy = 0;
                     dy <
                     2;
                     dy++)
                {
                    var sourceY =
                        Math.Min(
                            y *
                                2 +
                            dy,
                            height -
                                1);

                    for (var dx = 0;
                         dx <
                         2;
                         dx++)
                    {
                        var sourceX =
                            Math.Min(
                                x *
                                    2 +
                                dx,
                                width -
                                    1);

                        var source =
                            (
                                sourceY *
                                    width +
                                sourceX
                            ) *
                            4;

                        linearRed +=
                            SrgbToLinear[
                                rgba[
                                    source]];

                        linearGreen +=
                            SrgbToLinear[
                                rgba[
                                    source +
                                    1]];

                        linearBlue +=
                            SrgbToLinear[
                                rgba[
                                    source +
                                    2]];

                        alpha +=
                            rgba[
                                source +
                                3];
                    }
                }

                var target =
                    (
                        y *
                            outputWidth +
                        x
                    ) *
                    4;

                output[target] =
                    LinearToSrgb[
                        Math.Min(
                            4096,
                            (int)(
                                linearRed *
                                    0.25f *
                                    4096.0f +
                                0.5f))];

                output[target + 1] =
                    LinearToSrgb[
                        Math.Min(
                            4096,
                            (int)(
                                linearGreen *
                                    0.25f *
                                    4096.0f +
                                0.5f))];

                output[target + 2] =
                    LinearToSrgb[
                        Math.Min(
                            4096,
                            (int)(
                                linearBlue *
                                    0.25f *
                                    4096.0f +
                                0.5f))];

                output[target + 3] =
                    (byte)(
                        (
                            alpha +
                            2
                        ) /
                        4);
            }
        }

        return output;
    }

    private static float[] BuildSrgbToLinear()
    {
        var table =
            new float[256];

        for (var index = 0;
             index <
             table.Length;
             index++)
        {
            var value =
                index /
                255.0f;

            table[index] =
                value <=
                        0.04045f
                    ? value /
                        12.92f
                    : MathF.Pow(
                        (
                            value +
                            0.055f
                        ) /
                        1.055f,
                        2.4f);
        }

        return table;
    }

    private static byte[] BuildLinearToSrgb()
    {
        var table =
            new byte[4097];

        for (var index = 0;
             index <
             table.Length;
             index++)
        {
            var linear =
                index /
                4096.0f;

            var srgb =
                linear <=
                        0.0031308f
                    ? linear *
                        12.92f
                    : 1.055f *
                        MathF.Pow(
                            linear,
                            1.0f /
                                2.4f) -
                        0.055f;

            table[index] =
                (byte)Math.Clamp(
                    srgb *
                        255.0f +
                    0.5f,
                    0.0f,
                    255.0f);
        }

        return table;
    }
}
