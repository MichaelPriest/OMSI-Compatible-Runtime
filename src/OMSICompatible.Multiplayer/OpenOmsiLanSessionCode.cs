using System.Buffers.Binary;
using System.Net;

namespace OMSICompatible.Multiplayer;

/// <summary>
/// Compatible implementation of openOMSI's human-readable LAN session code.
/// The code carries protocol version, one to three IPv4 addresses, port and a
/// 48-bit session id protected by CRC-16/CCITT-FALSE.
/// </summary>
public sealed record OpenOmsiLanSessionCode(
    byte Protocol,
    IReadOnlyList<IPAddress> Addresses,
    ushort Port,
    ulong SessionId)
{
    private const ushort LayoutMark = 0x4F43;
    private const ushort MultiMark = 0x4D41;
    private const ulong Mask48 = 0x0000FFFFFFFFFFFFUL;
    private const int MaximumAddresses = 3;
    private const string Alphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly ulong[] MixConstants =
    [
        0x00009E3779B97F4BUL,
        0x0000C2B2AE3D27D5UL
    ];

    public string Encode()
    {
        var ips =
            Addresses
                .Where(
                    static address =>
                        address.AddressFamily ==
                        System.Net.Sockets.AddressFamily.InterNetwork)
                .Take(
                    MaximumAddresses)
                .ToArray();

        if (ips.Length ==
            0)
        {
            ips =
            [
                IPAddress.Loopback
            ];
        }

        var mixed =
            Mix48(
                SessionId);

        Span<byte> mixedBytes =
            stackalloc byte[8];

        BinaryPrimitives.WriteUInt64BigEndian(
            mixedBytes,
            mixed);

        var sessionBytes =
            mixedBytes[
                2..]
                .ToArray();

        var plain =
            new List<byte>(
                5 +
                4 *
                ips.Length)
            {
                Protocol
            };

        ushort mark;

        if (ips.Length ==
            1)
        {
            plain.AddRange(
                ips[0].GetAddressBytes());
            plain.Add(
                (byte)(
                    Port >>
                    8));
            plain.Add(
                (byte)Port);
            mark =
                LayoutMark;
        }
        else
        {
            plain.Add(
                (byte)(
                    Port >>
                    8));
            plain.Add(
                (byte)Port);

            foreach (var ip in
                     ips)
            {
                plain.AddRange(
                    ip.GetAddressBytes());
            }

            mark =
                MultiMark;
        }

        var mask =
            CodeMask(
                sessionBytes,
                plain.Count);

        var bytes =
            new List<byte>(
                8 +
                plain.Count);

        bytes.AddRange(
            sessionBytes);

        for (var index = 0;
             index <
                 plain.Count;
             index++)
        {
            bytes.Add(
                (byte)(
                    plain[index] ^
                    mask[index]));
        }

        var crc =
            (ushort)(
                Crc16(
                    bytes) ^
                mark);

        bytes.Add(
            (byte)(
                crc >>
                8));
        bytes.Add(
            (byte)crc);

        return ToText(
            bytes);
    }

    public static bool TryDecode(
        string? text,
        out OpenOmsiLanSessionCode code,
        out string? error)
    {
        code =
            new OpenOmsiLanSessionCode(
                0,
                Array.Empty<IPAddress>(),
                0,
                0);
        error =
            null;

        if (string.IsNullOrWhiteSpace(
                text))
        {
            error =
                "empty session code";
            return false;
        }

        var normalized =
            new string(
                text
                    .Trim()
                    .ToUpperInvariant()
                    .Where(
                        static character =>
                            !char.IsWhiteSpace(
                                character) &&
                            character is not
                                '-' and
                                '_')
                    .ToArray());

        if (normalized.StartsWith(
                "OMSI",
                StringComparison.Ordinal))
        {
            normalized =
                normalized[
                    4..];
        }

        normalized =
            TrimPadding(
                normalized);

        if (normalized.Length is not
            (24 or 31 or 37))
        {
            error =
                "invalid session code length";
            return false;
        }

        var bits =
            new List<byte>(
                normalized.Length *
                5);

        foreach (var character in
                 normalized)
        {
            var value =
                Alphabet.IndexOf(
                    character);

            if (value <
                0)
            {
                error =
                    "invalid session code character";
                return false;
            }

            for (var shift = 4;
                 shift >=
                     0;
                 shift--)
            {
                bits.Add(
                    (byte)(
                        (
                            value >>
                            shift
                        ) &
                        1));
            }
        }

        var byteCount =
            bits.Count /
            8;

        if (byteCount <
            15)
        {
            error =
                "session code is truncated";
            return false;
        }

        var bytes =
            new byte[
                byteCount];

        for (var index = 0;
             index <
                 byteCount;
             index++)
        {
            byte value =
                0;

            for (var bit = 0;
                 bit <
                     8;
                 bit++)
            {
                value =
                    (byte)(
                        (
                            value <<
                            1
                        ) |
                        bits[
                            index *
                                8 +
                            bit]);
            }

            bytes[index] =
                value;
        }

        var crc =
            BinaryPrimitives.ReadUInt16BigEndian(
                bytes.AsSpan(
                    byteCount -
                        2,
                    2));

        var body =
            bytes.AsSpan(
                0,
                byteCount -
                    2);

        var sum =
            Crc16(
                body);

        Span<byte> mixedSession =
            stackalloc byte[8];

        body[
            ..6]
            .CopyTo(
                mixedSession[
                    2..]);

        var mixedId =
            BinaryPrimitives.ReadUInt64BigEndian(
                mixedSession);

        if (byteCount ==
                15 &&
            (ushort)(
                sum ^
                LayoutMark) ==
            crc)
        {
            var plain =
                Unmask(
                    body,
                    6);

            if (plain.Length <
                7)
            {
                error =
                    "session code payload is truncated";
                return false;
            }

            code =
                new OpenOmsiLanSessionCode(
                    plain[0],
                    [
                        new IPAddress(
                            plain
                                .AsSpan(
                                    1,
                                    4)
                                .ToArray())
                    ],
                    BinaryPrimitives.ReadUInt16BigEndian(
                        plain.AsSpan(
                            5,
                            2)),
                    Unmix48(
                        mixedId));

            return true;
        }

        if (byteCount ==
                15 &&
            sum ==
                crc)
        {
            Span<byte> oldSession =
                stackalloc byte[8];

            body[
                7..13]
                .CopyTo(
                    oldSession[
                        2..]);

            code =
                new OpenOmsiLanSessionCode(
                    body[0],
                    [
                        new IPAddress(
                            body[
                                1..5]
                                .ToArray())
                    ],
                    BinaryPrimitives.ReadUInt16BigEndian(
                        body[
                            5..7]),
                    BinaryPrimitives.ReadUInt64BigEndian(
                        oldSession));

            return true;
        }

        if (byteCount >
                15 &&
            (ushort)(
                sum ^
                MultiMark) ==
            crc)
        {
            var plain =
                Unmask(
                    body,
                    6);

            if (plain.Length <
                    7 ||
                (
                    plain.Length -
                    3
                ) %
                4 !=
                    0)
            {
                error =
                    "invalid multi-address session code";
                return false;
            }

            var addresses =
                new List<IPAddress>();

            for (var offset = 3;
                 offset +
                     4 <=
                 plain.Length &&
                 addresses.Count <
                     MaximumAddresses;
                 offset +=
                     4)
            {
                addresses.Add(
                    new IPAddress(
                        plain
                            .AsSpan(
                                offset,
                                4)
                            .ToArray()));
            }

            code =
                new OpenOmsiLanSessionCode(
                    plain[0],
                    addresses,
                    BinaryPrimitives.ReadUInt16BigEndian(
                        plain.AsSpan(
                            1,
                            2)),
                    Unmix48(
                        mixedId));

            return true;
        }

        error =
            "session code checksum does not match";
        return false;
    }

    public static bool LooksLikeCode(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return false;
        }

        var trimmed =
            text.Trim();

        if (trimmed.Contains(
                '.') ||
            trimmed.Contains(
                ':'))
        {
            return false;
        }

        var compact =
            new string(
                trimmed
                    .ToUpperInvariant()
                    .Where(
                        static character =>
                            !char.IsWhiteSpace(
                                character) &&
                            character is not
                                '-' and
                                '_')
                    .ToArray());

        if (compact.StartsWith(
                "OMSI",
                StringComparison.Ordinal))
        {
            compact =
                compact[
                    4..];
        }

        compact =
            TrimPadding(
                compact);

        return compact.Length is
                   24 or 31 or 37 &&
               compact.All(
                   char.IsLetterOrDigit);
    }

    private static byte[] Unmask(
        ReadOnlySpan<byte> body,
        int offset)
    {
        var payload =
            body[
                offset..]
                .ToArray();

        var mask =
            CodeMask(
                body[
                    ..6]
                    .ToArray(),
                payload.Length);

        for (var index = 0;
             index <
                 payload.Length;
             index++)
        {
            payload[index] =
                (byte)(
                    payload[index] ^
                    mask[index]);
        }

        return payload;
    }

    private static string TrimPadding(
        string value)
    {
        if (value.Length ==
                32 &&
            value[^1] ==
                Alphabet[0])
        {
            return value[
                ..31];
        }

        if (value.Length ==
                40 &&
            value[
                37..]
                .All(
                    character =>
                        character ==
                        Alphabet[0]))
        {
            return value[
                ..37];
        }

        return value;
    }

    private static string ToText(
        IReadOnlyList<byte> bytes)
    {
        var characterCount =
            (
                bytes.Count *
                    8 +
                4
            ) /
            5;

        var characters =
            new List<char>(
                characterCount +
                3);

        for (var index = 0;
             index <
                 characterCount;
             index++)
        {
            var value =
                0;

            for (var bitIndex = 0;
                 bitIndex <
                     5;
                 bitIndex++)
            {
                var bit =
                    index *
                        5 +
                    bitIndex;

                var source =
                    bit /
                    8;

                var next =
                    source <
                        bytes.Count
                        ? (
                            bytes[source] >>
                            (
                                7 -
                                bit %
                                8
                            )
                          ) &
                          1
                        : 0;

                value =
                    (
                        value <<
                        1
                    ) |
                    next;
            }

            characters.Add(
                Alphabet[
                    value]);
        }

        while (characters.Count %
               4 !=
               0)
        {
            characters.Add(
                Alphabet[0]);
        }

        return "OMSI-" +
               string.Join(
                   "-",
                   characters
                       .Chunk(
                           4)
                       .Select(
                           static group =>
                               new string(
                                   group)));
    }

    private static byte[] CodeMask(
        IReadOnlyList<byte> session,
        int length)
    {
        ulong z =
            0;

        foreach (var value in
                 session)
        {
            z =
                (
                    z <<
                    8
                ) |
                value;
        }

        z ^=
            0x5DEECE66D1CE4E5BUL;

        var output =
            new byte[
                length];

        for (var index = 0;
             index <
                 output.Length;
             index++)
        {
            z =
                unchecked(
                    z +
                    0x9E3779B97F4A7C15UL);

            var x =
                z;

            x =
                unchecked(
                    (
                        x ^
                        (
                            x >>
                            30
                        )
                    ) *
                    0xBF58476D1CE4E5B9UL);

            x =
                unchecked(
                    (
                        x ^
                        (
                            x >>
                            27
                        )
                    ) *
                    0x94D049BB133111EBUL);

            output[index] =
                (byte)(
                    (
                        x ^
                        (
                            x >>
                            31
                        )
                    ) >>
                    (
                        (
                            index %
                            8
                        ) *
                        3
                    ));
        }

        return output;
    }

    private static ushort Crc16(
        IEnumerable<byte> bytes)
    {
        ushort crc =
            0xFFFF;

        foreach (var value in
                 bytes)
        {
            crc ^=
                (ushort)(
                    value <<
                    8);

            for (var bit = 0;
                 bit <
                     8;
                 bit++)
            {
                crc =
                    (ushort)(
                        (
                            crc &
                            0x8000
                        ) !=
                            0
                            ? (
                                crc <<
                                1
                              ) ^
                              0x1021
                            : crc <<
                              1);
            }
        }

        return crc;
    }

    private static ulong Mix48(
        ulong value)
    {
        var mixed =
            value &
            Mask48;

        foreach (var constant in
                 MixConstants)
        {
            mixed ^=
                mixed >>
                24;

            mixed =
                unchecked(
                    mixed *
                    constant) &
                Mask48;
        }

        return (
                   mixed ^
                   (
                       mixed >>
                       24
                   )
               ) &
               Mask48;
    }

    private static ulong Unmix48(
        ulong value)
    {
        var mixed =
            value &
            Mask48;

        for (var index =
                 MixConstants.Length -
                 1;
             index >=
                 0;
             index--)
        {
            mixed ^=
                mixed >>
                24;

            mixed =
                unchecked(
                    mixed *
                    Inverse48(
                        MixConstants[
                            index])) &
                Mask48;
        }

        return (
                   mixed ^
                   (
                       mixed >>
                       24
                   )
               ) &
               Mask48;
    }

    private static ulong Inverse48(
        ulong value)
    {
        var inverse =
            value;

        for (var iteration = 0;
             iteration <
                 5;
             iteration++)
        {
            inverse =
                unchecked(
                    inverse *
                    (
                        2UL -
                        unchecked(
                            value *
                            inverse)
                    ));
        }

        return inverse &
               Mask48;
    }
}
