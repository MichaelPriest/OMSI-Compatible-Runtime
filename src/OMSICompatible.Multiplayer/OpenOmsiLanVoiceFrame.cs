using System.Buffers.Binary;

namespace OMSICompatible.Multiplayer;

public sealed record OpenOmsiLanVoiceFrame(
    uint SenderId,
    ushort Sequence,
    byte[] Pcm16Mono8Khz);

public static class OpenOmsiLanVoiceCodec
{
    public const byte Magic0 = (byte)'O';
    public const byte Magic1 = (byte)'V';
    public const byte Magic2 = (byte)'C';
    public const byte Version = 1;

    public const int HeaderSize = 12;
    public const int MaximumPcmBytes = 960;

    public static bool LooksLike(
        ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize &&
        data[0] == Magic0 &&
        data[1] == Magic1 &&
        data[2] == Magic2 &&
        data[3] == Version;

    public static byte[] Encode(
        uint senderId,
        ushort sequence,
        ReadOnlySpan<byte> pcm16Mono8Khz)
    {
        if (pcm16Mono8Khz.Length <= 0 ||
            pcm16Mono8Khz.Length > MaximumPcmBytes ||
            (pcm16Mono8Khz.Length & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pcm16Mono8Khz),
                "Voice PCM must be 16-bit mono data and fit in one UDP frame.");
        }

        var result =
            new byte[
                HeaderSize +
                pcm16Mono8Khz.Length];

        result[0] = Magic0;
        result[1] = Magic1;
        result[2] = Magic2;
        result[3] = Version;

        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(4, 4),
            senderId);
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(8, 2),
            sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(10, 2),
            checked((ushort)pcm16Mono8Khz.Length));

        pcm16Mono8Khz.CopyTo(
            result.AsSpan(HeaderSize));

        return result;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out OpenOmsiLanVoiceFrame frame)
    {
        frame =
            new OpenOmsiLanVoiceFrame(
                0,
                0,
                []);

        if (!LooksLike(data))
        {
            return false;
        }

        var senderId =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(4, 4));

        var sequence =
            BinaryPrimitives.ReadUInt16LittleEndian(
                data.Slice(8, 2));

        var length =
            BinaryPrimitives.ReadUInt16LittleEndian(
                data.Slice(10, 2));

        if (senderId == 0 ||
            length <= 0 ||
            length > MaximumPcmBytes ||
            HeaderSize + length != data.Length ||
            (length & 1) != 0)
        {
            return false;
        }

        frame =
            new OpenOmsiLanVoiceFrame(
                senderId,
                sequence,
                data.Slice(
                        HeaderSize,
                        length)
                    .ToArray());

        return true;
    }
}
