namespace OMSICompatible.Multiplayer;

public static class OpenOmsiLanStateCodec
{
    private const int FlagBits = 10;

    public static byte[] Encode(
        OpenOmsiLanPose source,
        ushort sequence)
    {
        var pose =
            source.Clone();

        var id =
            (ushort)Math.Min(
                pose.Id,
                ushort.MaxValue);

        Span<byte> header =
        [
            OpenOmsiLanProtocol.StateMagic,
            OpenOmsiLanProtocol.ProtocolVersion,
            (byte)(id & 0xFF),
            (byte)(id >> 8),
            (byte)(sequence & 0xFF),
            (byte)(sequence >> 8)
        ];

        var writer =
            new BitWriter(header);

        var flags =
            pose.Flags &
            ((1u << FlagBits) - 1u);

        writer.Put(flags, FlagBits);

        if ((flags & OpenOmsiLanProtocol.FlagVehicle) == 0)
        {
            PutWalker(writer, pose.Walker);
            PutTail(writer, pose);
            return writer.Finish();
        }

        writer.PutFixed(pose.X, 0.01, 32);
        writer.PutFixed(pose.Y, 0.01, 32);
        writer.PutFixed(pose.Z, 0.01, 24);
        writer.Put(
            QuantizeHeading(pose.HeadingDegrees),
            16);
        writer.PutFixed(pose.PitchDegrees, 0.01, 12);
        writer.PutFixed(pose.BankDegrees, 0.01, 12);
        writer.PutFixed(pose.SpeedKph, 0.05, 14);
        writer.PutFixed(pose.SteeringDegrees, 0.05, 11);
        writer.PutUnsigned(pose.HeadLights, 2);
        writer.PutUnsigned(pose.InteriorLights, 2);
        writer.PutUnsigned(pose.Blinker, 2);
        writer.PutUnsigned(
            float.IsFinite(pose.Rpm)
                ? (long)Math.Round(pose.Rpm / 5.0f)
                : 0,
            10);
        writer.PutUnit(pose.Throttle, 5);
        writer.PutUnit(pose.Brake, 5);
        writer.PutUnsigned(pose.Passengers, 8);

        var doors =
            pose.Doors.Take(OpenOmsiLanProtocol.MaximumDoors).ToArray();

        writer.Put((ulong)doors.Length, 3);

        foreach (var value in doors)
        {
            writer.PutUnit(value, 4);
        }

        var wheels =
            pose.Suspension.Take(OpenOmsiLanProtocol.MaximumWheels).ToArray();

        writer.Put((ulong)wheels.Length, 4);

        foreach (var value in wheels)
        {
            writer.PutFixed(value, 0.005, 7);
        }

        var rear =
            pose.RearSections.Take(OpenOmsiLanProtocol.MaximumRearSections).ToArray();

        writer.Put((ulong)rear.Length, 2);

        var roundedX = RoundCentimeters(pose.X);
        var roundedY = RoundCentimeters(pose.Y);
        var roundedZ = RoundCentimeters(pose.Z);

        foreach (var section in rear)
        {
            writer.PutFixed(section.X - roundedX, 0.01, 16);
            writer.PutFixed(section.Y - roundedY, 0.01, 16);
            writer.PutFixed(section.Z - roundedZ, 0.01, 12);
            writer.Put(QuantizeHeading(section.HeadingDegrees), 16);
        }

        PutUnitList(writer, pose.Lamps, OpenOmsiLanProtocol.MaximumLamps, 7, 2);
        PutSignedList(writer, pose.Switches, OpenOmsiLanProtocol.MaximumSwitches, 5, 4);

        var values =
            pose.Values.Take(OpenOmsiLanProtocol.MaximumValues).ToArray();

        writer.Put((ulong)values.Length, 5);

        foreach (var value in values)
        {
            writer.Put(ToHalfBits(value), 16);
        }

        PutWalker(writer, pose.Walker);
        PutTail(writer, pose);

        var bytes =
            writer.Finish();

        if (bytes.Length > OpenOmsiLanProtocol.MaximumStateBytes)
        {
            throw new InvalidOperationException(
                $"LAN STATE is {bytes.Length} bytes; maximum is {OpenOmsiLanProtocol.MaximumStateBytes}.");
        }

        return bytes;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out ushort sequence,
        out OpenOmsiLanPose pose)
    {
        sequence = 0;
        pose = new OpenOmsiLanPose();

        if (data.Length < OpenOmsiLanProtocol.StateHeaderBytes + 2 ||
            data.Length > OpenOmsiLanProtocol.MaximumStateBytes ||
            data[0] != OpenOmsiLanProtocol.StateMagic ||
            data[1] != OpenOmsiLanProtocol.ProtocolVersion)
        {
            return false;
        }

        pose.Id =
            (uint)(
                data[2] |
                data[3] << 8);

        sequence =
            (ushort)(
                data[4] |
                data[5] << 8);

        var reader =
            new BitReader(
                data[
                    OpenOmsiLanProtocol.StateHeaderBytes..]);

        if (!reader.TryGet(FlagBits, out var flags))
        {
            return false;
        }

        pose.Flags =
            (uint)flags;

        if ((pose.Flags & OpenOmsiLanProtocol.FlagVehicle) == 0)
        {
            pose.Walker =
                GetWalker(reader);

            GetTail(reader, pose);
            return true;
        }

        if (!reader.TryGetFixed(0.01, 32, out var x) ||
            !reader.TryGetFixed(0.01, 32, out var y) ||
            !reader.TryGetFixed(0.01, 24, out var z) ||
            !reader.TryGet(16, out var heading) ||
            !reader.TryGetFixedFloat(0.01, 12, out var pitch) ||
            !reader.TryGetFixedFloat(0.01, 12, out var bank) ||
            !reader.TryGetFixedFloat(0.05, 14, out var speed) ||
            !reader.TryGetFixedFloat(0.05, 11, out var steer) ||
            !reader.TryGet(2, out var head) ||
            !reader.TryGet(2, out var interior) ||
            !reader.TryGet(2, out var blinker) ||
            !reader.TryGet(10, out var rpm) ||
            !reader.TryGetUnit(5, out var throttle) ||
            !reader.TryGetUnit(5, out var brake) ||
            !reader.TryGet(8, out var passengers))
        {
            return false;
        }

        pose.X = x;
        pose.Y = y;
        pose.Z = z;

        pose.HeadingDegrees =
            (float)(
                heading *
                360.0 /
                65536.0);
        pose.PitchDegrees = pitch;
        pose.BankDegrees = bank;
        pose.SpeedKph = speed;
        pose.SteeringDegrees = steer;
        pose.HeadLights = (byte)head;
        pose.InteriorLights = (byte)interior;
        pose.Blinker = (byte)blinker;
        pose.Rpm = (float)rpm * 5.0f;
        pose.Throttle = throttle;
        pose.Brake = brake;
        pose.Passengers = (uint)passengers;

        if (!TryGetUnitList(reader, 3, OpenOmsiLanProtocol.MaximumDoors, 4, pose.Doors) ||
            !TryGetFixedList(reader, 4, OpenOmsiLanProtocol.MaximumWheels, 0.005, 7, pose.Suspension) ||
            !reader.TryGet(2, out var rearCount))
        {
            return false;
        }

        for (var index = 0; index < (int)rearCount; index++)
        {
            if (!reader.TryGetFixed(0.01, 16, out var dx) ||
                !reader.TryGetFixed(0.01, 16, out var dy) ||
                !reader.TryGetFixed(0.01, 12, out var dz) ||
                !reader.TryGet(16, out var rearHeading))
            {
                return false;
            }

            pose.RearSections.Add(
                new OpenOmsiLanPartPose(
                    pose.X + dx,
                    pose.Y + dy,
                    pose.Z + dz,
                    (float)(
                        rearHeading *
                        360.0 /
                        65536.0)));
        }

        if (!TryGetUnitList(reader, 7, OpenOmsiLanProtocol.MaximumLamps, 2, pose.Lamps) ||
            !TryGetFixedList(reader, 5, OpenOmsiLanProtocol.MaximumSwitches, 1.0, 4, pose.Switches) ||
            !reader.TryGet(5, out var valueCount))
        {
            return false;
        }

        for (var index = 0; index < (int)valueCount; index++)
        {
            if (!reader.TryGet(16, out var half))
            {
                return false;
            }

            pose.Values.Add(
                FromHalfBits(
                    (ushort)half));
        }

        pose.Walker =
            GetWalker(reader);

        GetTail(reader, pose);

        return true;
    }

    private static void PutUnitList(
        BitWriter writer,
        IReadOnlyList<float> values,
        int maximum,
        int countBits,
        int valueBits)
    {
        var count =
            Math.Min(values.Count, maximum);

        writer.Put((ulong)count, countBits);

        for (var index = 0; index < count; index++)
        {
            writer.PutUnit(values[index], valueBits);
        }
    }

    private static void PutSignedList(
        BitWriter writer,
        IReadOnlyList<float> values,
        int maximum,
        int countBits,
        int valueBits)
    {
        var count =
            Math.Min(values.Count, maximum);

        writer.Put((ulong)count, countBits);

        for (var index = 0; index < count; index++)
        {
            writer.PutFixed(values[index], 1.0, valueBits);
        }
    }

    private static bool TryGetUnitList(
        BitReader reader,
        int countBits,
        int maximum,
        int valueBits,
        List<float> target)
    {
        if (!reader.TryGet(countBits, out var count) ||
            count > (ulong)maximum)
        {
            return false;
        }

        for (var index = 0; index < (int)count; index++)
        {
            if (!reader.TryGetUnit(valueBits, out var value))
            {
                return false;
            }

            target.Add(value);
        }

        return true;
    }

    private static bool TryGetFixedList(
        BitReader reader,
        int countBits,
        int maximum,
        double step,
        int valueBits,
        List<float> target)
    {
        if (!reader.TryGet(countBits, out var count) ||
            count > (ulong)maximum)
        {
            return false;
        }

        for (var index = 0; index < (int)count; index++)
        {
            if (!reader.TryGetFixedFloat(step, valueBits, out var value))
            {
                return false;
            }

            target.Add(value);
        }

        return true;
    }

    private static void PutWalker(
        BitWriter writer,
        OpenOmsiLanWalker? walker)
    {
        if (walker is null)
        {
            writer.Put(0, 1);
            return;
        }

        writer.Put(1, 1);
        writer.PutFixed(walker.X, 0.01, 32);
        writer.PutFixed(walker.Y, 0.01, 32);
        writer.PutFixed(walker.Z, 0.01, 24);
        writer.Put(QuantizeHeading(walker.HeadingDegrees), 16);
        writer.PutFixed(walker.SpeedMetersPerSecond, 0.05, 9);
        writer.Put(walker.Seated ? 1UL : 0UL, 1);
    }

    private static OpenOmsiLanWalker? GetWalker(
        BitReader reader)
    {
        if (!reader.TryGet(1, out var present) ||
            present == 0)
        {
            return null;
        }

        if (!reader.TryGetFixed(0.01, 32, out var x) ||
            !reader.TryGetFixed(0.01, 32, out var y) ||
            !reader.TryGetFixed(0.01, 24, out var z) ||
            !reader.TryGet(16, out var heading) ||
            !reader.TryGetFixedFloat(0.05, 9, out var speed) ||
            !reader.TryGet(1, out var seated))
        {
            return null;
        }

        return new OpenOmsiLanWalker(
            x,
            y,
            z,
            (float)(heading * 360.0 / 65536.0),
            speed,
            float.NaN,
            seated != 0,
            null);
    }

    private static void PutTail(
        BitWriter writer,
        OpenOmsiLanPose pose)
    {
        writer.Put(1, 1);
        writer.Put(pose.SentMilliseconds, 32);

        var aboard =
            pose.Walker?.Aboard;

        if (aboard is null)
        {
            writer.Put(0, 1);
        }
        else
        {
            writer.Put(1, 1);
            writer.Put(
                Math.Min(
                    aboard.OwnerId,
                    ushort.MaxValue),
                16);
            writer.PutFixed(aboard.LocalX, 0.005, 14);
            writer.PutFixed(aboard.LocalY, 0.005, 14);
            writer.PutFixed(aboard.LocalZ, 0.005, 14);

            if (aboard.SeatIndex.HasValue)
            {
                writer.Put(1, 1);
                writer.Put(aboard.SeatIndex.Value, 10);
            }
            else
            {
                writer.Put(0, 1);
            }
        }

        writer.Put(1, 1);

        foreach (var door in pose.Doors.Take(OpenOmsiLanProtocol.MaximumDoors))
        {
            writer.PutUnit(door, 8);
        }

        if (pose.Walker is null)
        {
            writer.Put(0, 1);
        }
        else
        {
            writer.Put(1, 1);
            writer.Put(
                QuantizeHeading(
                    float.IsFinite(pose.Walker.CourseDegrees)
                        ? pose.Walker.CourseDegrees
                        : pose.Walker.HeadingDegrees),
                16);
        }
    }

    private static void GetTail(
        BitReader reader,
        OpenOmsiLanPose pose)
    {
        if (!reader.TryGet(1, out var tail) ||
            tail == 0 ||
            !reader.TryGet(32, out var sent))
        {
            return;
        }

        pose.SentMilliseconds =
            (uint)sent;

        if (!reader.TryGet(1, out var hasAboard))
        {
            return;
        }

        OpenOmsiLanAboard? aboard =
            null;

        if (hasAboard != 0 &&
            reader.TryGet(16, out var owner) &&
            reader.TryGetFixedFloat(0.005, 14, out var localX) &&
            reader.TryGetFixedFloat(0.005, 14, out var localY) &&
            reader.TryGetFixedFloat(0.005, 14, out var localZ) &&
            reader.TryGet(1, out var hasSeat))
        {
            ushort? seat =
                null;

            if (hasSeat != 0 &&
                reader.TryGet(10, out var seatValue))
            {
                seat =
                    (ushort)seatValue;
            }

            aboard =
                new OpenOmsiLanAboard(
                    (uint)owner,
                    localX,
                    localY,
                    localZ,
                    seat);
        }

        if (pose.Walker is not null &&
            aboard is not null)
        {
            pose.Walker =
                pose.Walker with
                {
                    Aboard = aboard
                };
        }

        if (!reader.TryGet(1, out var fineDoors) ||
            fineDoors == 0)
        {
            return;
        }

        for (var index = 0; index < pose.Doors.Count; index++)
        {
            if (!reader.TryGetUnit(8, out var value))
            {
                return;
            }

            pose.Doors[index] =
                value;
        }

        if (!reader.TryGet(1, out var hasCourse) ||
            hasCourse == 0 ||
            pose.Walker is null ||
            !reader.TryGet(16, out var course))
        {
            return;
        }

        pose.Walker =
            pose.Walker with
            {
                CourseDegrees =
                    (float)(
                        course *
                        360.0 /
                        65536.0)
            };
    }

    private static ulong QuantizeHeading(float value)
    {
        var safe =
            float.IsFinite(value)
                ? value
                : 0.0f;

        var normalized =
            ((safe % 360.0f) + 360.0f) % 360.0f;

        return
            (ulong)Math.Round(
                normalized /
                360.0 *
                65536.0) &
            0xFFFFUL;
    }

    private static double RoundCentimeters(double value) =>
        double.IsFinite(value)
            ? Math.Round(value / 0.01) * 0.01
            : 0.0;

    private static ushort ToHalfBits(float value)
    {
        var safe =
            float.IsNaN(value)
                ? 0.0f
                : Math.Clamp(
                    value,
                    -65504.0f,
                    65504.0f);

        return BitConverter.HalfToUInt16Bits(
            (Half)safe);
    }

    private static float FromHalfBits(ushort bits)
    {
        var value =
            (float)BitConverter.UInt16BitsToHalf(bits);

        return float.IsFinite(value)
            ? value
            : 0.0f;
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _buffer;
        private ulong _accumulator;
        private int _bits;

        public BitWriter(ReadOnlySpan<byte> header)
        {
            _buffer =
                new List<byte>(
                    header.Length + 128);

            foreach (var value in header)
            {
                _buffer.Add(value);
            }
        }

        public void Put(ulong value, int bits)
        {
            if (bits is <= 0 or > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(bits));
            }

            var mask =
                bits == 32
                    ? uint.MaxValue
                    : (1UL << bits) - 1UL;

            _accumulator |=
                (value & mask) <<
                _bits;

            _bits +=
                bits;

            while (_bits >= 8)
            {
                _buffer.Add(
                    (byte)_accumulator);

                _accumulator >>=
                    8;

                _bits -=
                    8;
            }
        }

        public void PutSigned(long value, int bits)
        {
            var maximum =
                (1L << (bits - 1)) - 1L;

            var clamped =
                Math.Clamp(
                    value,
                    -maximum - 1L,
                    maximum);

            Put(
                unchecked((ulong)clamped),
                bits);
        }

        public void PutUnsigned(long value, int bits)
        {
            var maximum =
                (1L << bits) - 1L;

            Put(
                (ulong)Math.Clamp(
                    value,
                    0L,
                    maximum),
                bits);
        }

        public void PutFixed(
            double value,
            double step,
            int bits)
        {
            var quantized =
                double.IsFinite(value)
                    ? Math.Round(value / step)
                    : 0.0;

            PutSigned(
                quantized >= long.MaxValue
                    ? long.MaxValue
                    : quantized <= long.MinValue
                        ? long.MinValue
                        : (long)quantized,
                bits);
        }

        public void PutUnit(float value, int bits)
        {
            var maximum =
                (1 << bits) - 1;

            var safe =
                float.IsFinite(value)
                    ? Math.Clamp(value, 0.0f, 1.0f)
                    : 0.0f;

            Put(
                (ulong)Math.Round(
                    safe *
                    maximum),
                bits);
        }

        public byte[] Finish()
        {
            if (_bits > 0)
            {
                _buffer.Add(
                    (byte)_accumulator);
            }

            return [.. _buffer];
        }
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bit;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data =
                data.ToArray();
        }

        public bool TryGet(int bits, out ulong value)
        {
            value = 0;

            var end =
                _bit +
                bits;

            if (bits <= 0 ||
                bits > 32 ||
                end > _data.Length * 8)
            {
                return false;
            }

            for (var index = 0; index < bits; index++)
            {
                var bit =
                    _bit +
                    index;

                value |=
                    (ulong)(
                        (_data[bit / 8] >>
                         (bit % 8)) &
                        1) <<
                    index;
            }

            _bit =
                end;

            return true;
        }

        public bool TryGetSigned(int bits, out long value)
        {
            value = 0;

            if (!TryGet(bits, out var raw))
            {
                return false;
            }

            var sign =
                1L << (bits - 1);

            value =
                ((long)raw ^ sign) -
                sign;

            return true;
        }

        public bool TryGetFixed(
            double step,
            int bits,
            out double value)
        {
            value = 0.0;

            if (!TryGetSigned(bits, out var raw))
            {
                return false;
            }

            value =
                raw *
                step;

            return true;
        }

        public bool TryGetFixedFloat(
            double step,
            int bits,
            out float value)
        {
            value = 0.0f;

            if (!TryGetFixed(step, bits, out var raw))
            {
                return false;
            }

            value =
                (float)raw;

            return true;
        }

        public bool TryGetUnit(
            int bits,
            out float value)
        {
            value = 0.0f;

            if (!TryGet(bits, out var raw))
            {
                return false;
            }

            value =
                (float)raw /
                ((1 << bits) - 1);

            return true;
        }
    }
}
