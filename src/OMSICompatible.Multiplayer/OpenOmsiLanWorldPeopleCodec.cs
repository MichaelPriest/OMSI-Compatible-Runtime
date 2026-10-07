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
    IReadOnlyList<OpenOmsiLanWorldPersonState> People);

public sealed record OpenOmsiLanWorldPersonDescription(
    uint Id,
    string HumanPath);

public static class OpenOmsiLanWorldPeopleCodec
{
    private const int HeaderBytes = 18;
    private const int MaxBytes = 1180;
    private const int CarBits = 132;
    private const uint MaxId = (1u << 24) - 1u;

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

        frame = new(seq, hostMs, people);
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
