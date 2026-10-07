using System.Buffers.Binary;

namespace OMSICompatible.Multiplayer;

public enum OpenOmsiLanWorldPersonActivity : byte { Stand, Walk, Sit, Run }

public sealed record OpenOmsiLanWorldPersonState(
    uint Id,
    OpenOmsiLanWorldPersonActivity Activity,
    bool Aboard,
    bool PlayerBus,
    uint BusId,
    double X,
    double Y,
    double Z,
    float HeadingDegrees,
    float SpeedMetersPerSecond,
    long? WaitingStopObjectId,
    byte? WaitingSpot,
    byte? SeatIndex);

public sealed record OpenOmsiLanWorldPeopleFrame(
    ushort Sequence,
    uint HostMilliseconds,
    IReadOnlyList<OpenOmsiLanWorldPersonState> People,
    IReadOnlyList<OpenOmsiLanWorldGoneEntity>? Gone = null);

public sealed record OpenOmsiLanWorldPersonDescription(
    uint Id,
    string HumanPath);

public static class OpenOmsiLanWorldPeopleCodec
{
    private const int HeaderBytes = 18;
    private const int MaxBytes = 1180;
    private const int CarBits = 132;
    private const int LightBits = 50;
    private const int GoneBits = 25;
    private const uint MaxId = (1u << 24) - 1u;

    public static IReadOnlyList<byte[]> Encode(
        OpenOmsiLanWorldPeopleFrame frame)
    {
        var people =
            frame.People
                .Where(
                    static person =>
                        person.Id <=
                        MaxId)
                .ToArray();

        var gone =
            (frame.Gone ??
             Array.Empty<OpenOmsiLanWorldGoneEntity>())
                .Where(
                    static removed =>
                        removed.Person &&
                        removed.Id <=
                            MaxId)
                .Take(
                    63)
                .ToArray();

        var packets =
            new List<byte[]>();

        var index =
            0;

        do
        {
            var anchor =
                ResolveAnchor(
                    people,
                    index);

            var writer =
                new Writer(
                    BuildHeader(
                        frame.Sequence,
                        frame.HostMilliseconds,
                        anchor));

            writer.Put(
                0,
                7);

            var selected =
                new List<OpenOmsiLanWorldPersonState>();

            var packetGone =
                packets.Count ==
                    0
                    ? gone
                    : Array.Empty<
                        OpenOmsiLanWorldGoneEntity>();

            var budgetBits =
                (
                    MaxBytes -
                    HeaderBytes
                ) *
                8 -
                7 -
                8 -
                6 -
                6 -
                1 -
                packetGone.Length *
                    GoneBits;

            while (index <
                       people.Length &&
                   selected.Count <
                       255)
            {
                var person =
                    people[index];

                var bits =
                    EstimateBits(
                        person);

                if (selected.Count >
                        0 &&
                    bits >
                        budgetBits)
                {
                    break;
                }

                if (!FitsAnchor(
                        person,
                        anchor))
                {
                    index++;
                    continue;
                }

                selected.Add(
                    person);
                budgetBits -=
                    bits;
                index++;
            }

            writer.Put(
                (ulong)selected.Count,
                8);

            foreach (var person in
                     selected)
            {
                WritePerson(
                    writer,
                    person,
                    anchor);
            }

            writer.Put(
                0,
                6);
            writer.Put(
                (ulong)packetGone.Length,
                6);

            foreach (var removed in
                     packetGone)
            {
                writer.Put(
                    1,
                    1);
                writer.Put(
                    removed.Id,
                    24);
            }

            writer.Put(
                0,
                1);

            packets.Add(
                writer.Finish());
        }
        while (index <
               people.Length);

        return packets;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out OpenOmsiLanWorldPeopleFrame frame)
    {
        frame = new(0, 0, Array.Empty<OpenOmsiLanWorldPersonState>());

        if (data.Length < HeaderBytes ||
            data.Length > MaxBytes ||
            data[0] != OpenOmsiLanWorldCodec.WorldMagic ||
            data[1] != OpenOmsiLanProtocol.ProtocolVersion)
        {
            return false;
        }

        var seq = BinaryPrimitives.ReadUInt16LittleEndian(data[2..4]);
        var hostMs = BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);
        var ax = BinaryPrimitives.ReadInt32LittleEndian(data[8..12]);
        var ay = BinaryPrimitives.ReadInt32LittleEndian(data[12..16]);
        var az = BinaryPrimitives.ReadInt16LittleEndian(data[16..18]);
        var r = new Bits(data[HeaderBytes..]);

        if (!r.Get(7, out var cars) ||
            !r.Skip(checked((int)cars * CarBits)) ||
            !r.Get(8, out var count))
        {
            return false;
        }

        var people = new List<OpenOmsiLanWorldPersonState>((int)count);

        for (var i = 0; i < (int)count; i++)
        {
            if (!ReadPerson(r, ax, ay, az, out var person))
            {
                return false;
            }

            people.Add(person);
        }

        if (!r.Get(
                6,
                out var lightCount) ||
            !r.Skip(
                checked(
                    (int)lightCount *
                    LightBits)) ||
            !r.Get(
                6,
                out var goneCount))
        {
            return false;
        }

        var gone =
            new List<OpenOmsiLanWorldGoneEntity>(
                (int)goneCount);

        for (var i = 0;
             i < (int)goneCount;
             i++)
        {
            if (!r.Get(
                    1,
                    out var person) ||
                !r.Get(
                    24,
                    out var id))
            {
                return false;
            }

            gone.Add(
                new OpenOmsiLanWorldGoneEntity(
                    person ==
                        1,
                    (uint)id));
        }

        frame =
            new OpenOmsiLanWorldPeopleFrame(
                seq,
                hostMs,
                people,
                gone);

        return true;
    }

    public static string EncodeDescription(
        OpenOmsiLanWorldPersonDescription value) =>
        string.Join(
            "|",
            "DESC",
            "p",
            Math.Min(value.Id, MaxId).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            HumanPath(value.HumanPath) ?? string.Empty);

    public static bool TryDecodeDescription(
        string text,
        out OpenOmsiLanWorldPersonDescription value)
    {
        value = new(0, string.Empty);
        var p = text.Split('|');

        if (p.Length < 4 ||
            p[0] != "DESC" ||
            p[1] != "p" ||
            !uint.TryParse(
                p[2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var id) ||
            id > MaxId)
        {
            return false;
        }

        var path = HumanPath(p[3]);

        if (path is null)
        {
            return false;
        }

        value = new(id, path);
        return true;
    }

    private static (
        int X,
        int Y,
        short Z)
        ResolveAnchor(
            IReadOnlyList<OpenOmsiLanWorldPersonState> people,
            int start)
    {
        for (var index = start;
             index <
                 people.Count;
             index++)
        {
            var person =
                people[index];

            if (person.Aboard)
            {
                continue;
            }

            return (
                RoundInt32(
                    person.X),
                RoundInt32(
                    person.Y),
                (short)Math.Clamp(
                    RoundInt32(
                        person.Z),
                    short.MinValue,
                    short.MaxValue));
        }

        return (
            0,
            0,
            0);
    }

    private static byte[] BuildHeader(
        ushort sequence,
        uint hostMilliseconds,
        (
            int X,
            int Y,
            short Z
        ) anchor)
    {
        var header =
            new byte[
                HeaderBytes];

        header[0] =
            OpenOmsiLanWorldCodec.WorldMagic;
        header[1] =
            OpenOmsiLanProtocol.ProtocolVersion;

        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(
                2,
                2),
            sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(
                4,
                4),
            hostMilliseconds);
        BinaryPrimitives.WriteInt32LittleEndian(
            header.AsSpan(
                8,
                4),
            anchor.X);
        BinaryPrimitives.WriteInt32LittleEndian(
            header.AsSpan(
                12,
                4),
            anchor.Y);
        BinaryPrimitives.WriteInt16LittleEndian(
            header.AsSpan(
                16,
                2),
            anchor.Z);

        return header;
    }

    private static int EstimateBits(
        OpenOmsiLanWorldPersonState person) =>
        person.Aboard
            ? 103
            : 98 +
              (
                  person.WaitingStopObjectId.HasValue
                      ? 40
                      : 0
              );

    private static bool FitsAnchor(
        OpenOmsiLanWorldPersonState person,
        (
            int X,
            int Y,
            short Z
        ) anchor) =>
        person.Aboard ||
        (
            Math.Abs(
                person.X -
                anchor.X) <
            2600.0 &&
            Math.Abs(
                person.Y -
                anchor.Y) <
            2600.0 &&
            Math.Abs(
                person.Z -
                anchor.Z) <
            650.0
        );

    private static void WritePerson(
        Writer writer,
        OpenOmsiLanWorldPersonState person,
        (
            int X,
            int Y,
            short Z
        ) anchor)
    {
        writer.Put(
            Math.Min(
                person.Id,
                MaxId),
            24);

        writer.Put(
            person.Aboard
                ? person.PlayerBus
                    ? 2UL
                    : 1UL
                : 0UL,
            2);

        writer.Put(
            (ulong)Math.Min(
                (byte)person.Activity,
                (byte)3),
            2);

        if (!person.Aboard)
        {
            writer.Fixed(
                person.X -
                    anchor.X,
                0.01,
                19);
            writer.Fixed(
                person.Y -
                    anchor.Y,
                0.01,
                19);
            writer.Fixed(
                person.Z -
                    anchor.Z,
                0.01,
                17);

            writer.Put(
                QuantizeHeading8(
                    person.HeadingDegrees),
                8);

            writer.UnsignedFixed(
                person.SpeedMetersPerSecond,
                0.05,
                6);

            var waiting =
                person.WaitingStopObjectId.HasValue;

            writer.Put(
                waiting
                    ? 1UL
                    : 0UL,
                1);

            if (waiting)
            {
                writer.Put(
                    unchecked(
                        (uint)person.WaitingStopObjectId!.Value),
                    32);
                writer.Put(
                    person.WaitingSpot ??
                        0,
                    8);
            }

            return;
        }

        writer.Put(
            Math.Min(
                person.BusId,
                MaxId),
            24);
        writer.Fixed(
            person.X,
            0.01,
            12);
        writer.Fixed(
            person.Y,
            0.01,
            13);
        writer.Fixed(
            person.Z,
            0.01,
            10);
        writer.Put(
            QuantizeHeading8(
                person.HeadingDegrees),
            8);
        writer.Put(
            person.SeatIndex ??
                byte.MaxValue,
            8);
    }

    private static ulong QuantizeHeading8(
        float value)
    {
        var safe =
            float.IsFinite(
                value)
                ? value
                : 0.0f;

        var normalized =
            (
                safe %
                360.0f +
                360.0f
            ) %
            360.0f;

        return
            (ulong)Math.Round(
                normalized /
                360.0 *
                256.0) &
            0xFFUL;
    }

    private static int RoundInt32(
        double value) =>
        double.IsFinite(
            value)
            ? (int)Math.Clamp(
                Math.Round(
                    value),
                int.MinValue,
                int.MaxValue)
            : 0;

    private static bool ReadPerson(
        Bits r,
        int ax,
        int ay,
        short az,
        out OpenOmsiLanWorldPersonState p)
    {
        p = new(
            0,
            OpenOmsiLanWorldPersonActivity.Stand,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            null);

        if (!r.Get(24, out var id) ||
            !r.Get(2, out var place) ||
            !r.Get(2, out var activity))
        {
            return false;
        }

        var a =
            (OpenOmsiLanWorldPersonActivity)Math.Min(activity, 3UL);

        if (place == 0)
        {
            if (!r.Fixed(.01, 19, out var x) ||
                !r.Fixed(.01, 19, out var y) ||
                !r.Fixed(.01, 17, out var z) ||
                !r.Get(8, out var heading) ||
                !r.Get(6, out var speed) ||
                !r.Get(1, out var waiting))
            {
                return false;
            }

            long? stop = null;
            byte? spot = null;

            if (waiting == 1)
            {
                if (!r.Get(32, out var rawStop) ||
                    !r.Get(8, out var rawSpot))
                {
                    return false;
                }

                stop = (long)(uint)rawStop;
                spot = (byte)rawSpot;
            }

            p = new(
                (uint)id,
                a,
                false,
                false,
                0,
                ax + x,
                ay + y,
                az + z,
                (float)(heading * 360.0 / 256.0),
                (float)speed * .05f,
                stop,
                spot,
                null);
            return true;
        }

        if (place is not (1 or 2) ||
            !r.Get(24, out var bus) ||
            !r.Fixed(.01, 12, out var lx) ||
            !r.Fixed(.01, 13, out var ly) ||
            !r.Fixed(.01, 10, out var lz) ||
            !r.Get(8, out var lh) ||
            !r.Get(8, out var seat))
        {
            return false;
        }

        p = new(
            (uint)id,
            a,
            true,
            place == 2,
            (uint)bus,
            lx,
            ly,
            lz,
            (float)(lh * 360.0 / 256.0),
            0,
            null,
            null,
            seat == 255 ? null : (byte)seat);
        return true;
    }

    private static string? HumanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var p = value.Trim().Replace('\\', '/');

        if (p.Length > 260 ||
            p.StartsWith('/') ||
            p.Contains(':') ||
            p.Contains('|') ||
            p.Any(char.IsControl) ||
            p.Split('/').Any(
                static part =>
                    string.IsNullOrWhiteSpace(part) ||
                    part.Trim('.', ' ').Length == 0))
        {
            return null;
        }

        return Path.GetExtension(p).Equals(
            ".hum",
            StringComparison.OrdinalIgnoreCase)
                ? p
                : null;
    }

    private sealed class Writer
    {
        private readonly List<byte>
            _data;
        private ulong _accumulator;
        private int _bits;

        public Writer(
            ReadOnlySpan<byte> header)
        {
            _data =
                new List<byte>(
                    header.Length +
                    256);

            foreach (var value in
                     header)
            {
                _data.Add(
                    value);
            }
        }

        public void Put(
            ulong value,
            int bits)
        {
            var mask =
                bits ==
                    64
                    ? ulong.MaxValue
                    : (
                        1UL <<
                        bits
                      ) -
                      1UL;

            _accumulator |=
                (
                    value &
                    mask
                ) <<
                _bits;

            _bits +=
                bits;

            while (_bits >=
                   8)
            {
                _data.Add(
                    (byte)_accumulator);

                _accumulator >>=
                    8;
                _bits -=
                    8;
            }
        }

        public void Fixed(
            double value,
            double step,
            int bits)
        {
            var quantized =
                double.IsFinite(
                    value)
                    ? Math.Round(
                        value /
                        step,
                        MidpointRounding.AwayFromZero)
                    : 0.0;

            var maximum =
                (
                    1L <<
                    (
                        bits -
                        1
                    )
                ) -
                1L;

            var signed =
                (long)Math.Clamp(
                    quantized,
                    -maximum -
                        1L,
                    maximum);

            Put(
                unchecked(
                    (ulong)signed),
                bits);
        }

        public void UnsignedFixed(
            float value,
            double step,
            int bits)
        {
            var maximum =
                (
                    1L <<
                    bits
                ) -
                1L;

            var quantized =
                float.IsFinite(
                    value)
                    ? Math.Round(
                        value /
                        step,
                        MidpointRounding.AwayFromZero)
                    : 0.0;

            Put(
                (ulong)Math.Clamp(
                    quantized,
                    0.0,
                    maximum),
                bits);
        }

        public byte[] Finish()
        {
            if (_bits >
                0)
            {
                _data.Add(
                    (byte)_accumulator);
            }

            return _data.ToArray();
        }
    }

    private sealed class Bits
    {
        private readonly byte[] _data;
        private int _bit;

        public Bits(ReadOnlySpan<byte> data) => _data = data.ToArray();

        public bool Skip(int bits)
        {
            if (bits < 0 || _bit + bits > _data.Length * 8)
            {
                return false;
            }

            _bit += bits;
            return true;
        }

        public bool Get(int bits, out ulong value)
        {
            value = 0;
            var end = _bit + bits;

            if (bits <= 0 || bits > 32 || end > _data.Length * 8)
            {
                return false;
            }

            for (var i = 0; i < bits; i++)
            {
                var bit = _bit + i;
                value |=
                    (ulong)((_data[bit / 8] >> (bit % 8)) & 1) << i;
            }

            _bit = end;
            return true;
        }

        public bool Fixed(double step, int bits, out double value)
        {
            value = 0;

            if (!Get(bits, out var raw))
            {
                return false;
            }

            var sign = 1L << (bits - 1);
            var signed = ((long)raw ^ sign) - sign;
            value = signed * step;
            return true;
        }
    }
}
