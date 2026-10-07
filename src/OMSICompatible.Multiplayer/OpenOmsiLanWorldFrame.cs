using System.Buffers.Binary;

namespace OMSICompatible.Multiplayer;

public sealed record OpenOmsiLanWorldCarState(
    uint Id,
    double X,
    double Y,
    double Z,
    float HeadingDegrees,
    float PitchDegrees,
    float BankDegrees,
    float SpeedMetersPerSecond,
    float SteeringDegrees,
    byte Blinker,
    bool Brake,
    bool Lights,
    sbyte AtStation);

public sealed record OpenOmsiLanWorldLightState(
    long ObjectId,
    double PositionSeconds,
    bool Held);

public sealed record OpenOmsiLanWorldFrame(
    ushort Sequence,
    uint HostMilliseconds,
    IReadOnlyList<OpenOmsiLanWorldCarState> Cars,
    IReadOnlyList<OpenOmsiLanWorldLightState> Lights);

public sealed record OpenOmsiLanWorldCarDescription(
    uint Id,
    string VehiclePath,
    int? PaintScheme,
    string Line,
    string Destination);

public static class OpenOmsiLanWorldCodec
{
    public const byte WorldMagic =
        0xB4;
    public const int HeaderBytes =
        18;
    public const int MaximumDatagramBytes =
        1180;
    public const uint MaximumEntityId =
        (1u << 24) -
        1u;

    private const int CarBits =
        132;
    private const int LightBits =
        50;
    private const int CountBits =
        27;

    public static IReadOnlyList<byte[]> Encode(
        OpenOmsiLanWorldFrame frame)
    {
        var anchor =
            ResolveAnchor(
                frame.Cars);

        var cars =
            frame.Cars
                .Where(
                    car =>
                        FitsAnchor(
                            car,
                            anchor))
                .ToArray();

        var packets =
            new List<byte[]>();

        var carIndex =
            0;
        var first =
            true;

        do
        {
            var lights =
                first
                    ? frame.Lights
                        .Take(
                            63)
                        .ToArray()
                    : [];

            var budget =
                (
                    MaximumDatagramBytes -
                    HeaderBytes
                ) *
                8 -
                CountBits -
                1 -
                lights.Length *
                LightBits;

            var start =
                carIndex;

            while (carIndex <
                       cars.Length &&
                   carIndex -
                       start <
                       127 &&
                   budget >=
                       CarBits)
            {
                budget -=
                    CarBits;
                carIndex++;
            }

            Span<byte> header =
                stackalloc byte[
                    HeaderBytes];

            header[0] =
                WorldMagic;
            header[1] =
                OpenOmsiLanProtocol.ProtocolVersion;

            BinaryPrimitives.WriteUInt16LittleEndian(
                header[
                    2..4],
                frame.Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(
                header[
                    4..8],
                frame.HostMilliseconds);
            BinaryPrimitives.WriteInt32LittleEndian(
                header[
                    8..12],
                anchor.X);
            BinaryPrimitives.WriteInt32LittleEndian(
                header[
                    12..16],
                anchor.Y);
            BinaryPrimitives.WriteInt16LittleEndian(
                header[
                    16..18],
                anchor.Z);

            var writer =
                new BitWriter(
                    header);

            writer.Put(
                (ulong)(
                    carIndex -
                    start),
                7);

            for (var index = start;
                 index <
                     carIndex;
                 index++)
            {
                WriteCar(
                    writer,
                    cars[index],
                    anchor);
            }

            writer.Put(
                0,
                8);

            writer.Put(
                (ulong)lights.Length,
                6);

            foreach (var light in
                     lights)
            {
                writer.Put(
                    unchecked(
                        (uint)light.ObjectId),
                    32);
                writer.PutSigned(
                    (long)Math.Round(
                        light.PositionSeconds /
                        0.05,
                        MidpointRounding.AwayFromZero),
                    17);
                writer.Put(
                    light.Held
                        ? 1UL
                        : 0UL,
                    1);
            }

            writer.Put(
                0,
                6);
            writer.Put(
                0,
                1);

            packets.Add(
                writer.Finish());

            first =
                false;
        }
        while (carIndex <
               cars.Length);

        if (packets.Count ==
            0)
        {
            packets.Add(
                Encode(
                    frame with
                    {
                        Cars =
                            Array.Empty<
                                OpenOmsiLanWorldCarState>(),
                        Lights =
                            frame.Lights
                    })[0]);
        }

        return packets;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out OpenOmsiLanWorldFrame frame)
    {
        frame =
            new OpenOmsiLanWorldFrame(
                0,
                0,
                Array.Empty<
                    OpenOmsiLanWorldCarState>(),
                Array.Empty<
                    OpenOmsiLanWorldLightState>());

        if (data.Length <
                HeaderBytes ||
            data.Length >
                MaximumDatagramBytes ||
            data[0] !=
                WorldMagic ||
            data[1] !=
                OpenOmsiLanProtocol.ProtocolVersion)
        {
            return false;
        }

        var sequence =
            BinaryPrimitives.ReadUInt16LittleEndian(
                data[
                    2..4]);
        var hostMilliseconds =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data[
                    4..8]);
        var anchorX =
            BinaryPrimitives.ReadInt32LittleEndian(
                data[
                    8..12]);
        var anchorY =
            BinaryPrimitives.ReadInt32LittleEndian(
                data[
                    12..16]);
        var anchorZ =
            BinaryPrimitives.ReadInt16LittleEndian(
                data[
                    16..18]);

        var reader =
            new BitReader(
                data[
                    HeaderBytes..]);

        if (!reader.TryGet(
                7,
                out var carCount))
        {
            return false;
        }

        var cars =
            new List<OpenOmsiLanWorldCarState>(
                (int)carCount);

        for (var index = 0;
             index <
                 (int)carCount;
             index++)
        {
            if (!TryReadCar(
                    reader,
                    anchorX,
                    anchorY,
                    anchorZ,
                    out var car))
            {
                return false;
            }

            cars.Add(
                car);
        }

        if (!reader.TryGet(
                8,
                out var peopleCount))
        {
            return false;
        }

        for (var index = 0;
             index <
                 (int)peopleCount;
             index++)
        {
            if (!SkipPerson(
                    reader))
            {
                return false;
            }
        }

        if (!reader.TryGet(
                6,
                out var lightCount))
        {
            return false;
        }

        var lights =
            new List<OpenOmsiLanWorldLightState>(
                (int)lightCount);

        for (var index = 0;
             index <
                 (int)lightCount;
             index++)
        {
            if (!reader.TryGet(
                    32,
                    out var objectId) ||
                !reader.TryGetSigned(
                    17,
                    out var position) ||
                !reader.TryGet(
                    1,
                    out var held))
            {
                return false;
            }

            lights.Add(
                new OpenOmsiLanWorldLightState(
                    (long)(
                        uint)objectId,
                    position *
                        0.05,
                    held ==
                        1));
        }

        if (!reader.TryGet(
                6,
                out var goneCount))
        {
            return false;
        }

        for (var index = 0;
             index <
                 (int)goneCount;
             index++)
        {
            if (!reader.TryGet(
                    1,
                    out _) ||
                !reader.TryGet(
                    24,
                    out _))
            {
                return false;
            }
        }

        if (reader.TryGet(
                1,
                out var hasParked) &&
            hasParked ==
                1)
        {
            if (!reader.TryGet(
                    1,
                    out _) ||
                !reader.TryGet(
                    7,
                    out var parkedCount))
            {
                return false;
            }

            for (var index = 0;
                 index <
                     (int)parkedCount;
                 index++)
            {
                if (!reader.TryGet(
                        32,
                        out _))
                {
                    return false;
                }
            }
        }

        frame =
            new OpenOmsiLanWorldFrame(
                sequence,
                hostMilliseconds,
                cars,
                lights);

        return true;
    }

    public static string EncodeDescription(
        OpenOmsiLanWorldCarDescription description)
    {
        var path =
            OpenOmsiLanProtocol.NormalizeVehiclePath(
                description.VehiclePath) ??
            string.Empty;

        return string.Join(
            "|",
            "DESC",
            "c",
            Math.Min(
                    description.Id,
                    MaximumEntityId)
                .ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            path,
            description.PaintScheme.HasValue
                ? Math.Clamp(
                        description.PaintScheme.Value,
                        0,
                        255)
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                : "-",
            OpenOmsiLanProtocol.CleanText(
                description.Line,
                16),
            OpenOmsiLanProtocol.CleanText(
                description.Destination,
                64));
    }

    public static bool TryDecodeDescription(
        string text,
        out OpenOmsiLanWorldCarDescription description)
    {
        description =
            new OpenOmsiLanWorldCarDescription(
                0,
                string.Empty,
                null,
                string.Empty,
                string.Empty);

        var fields =
            text.Split(
                '|');

        if (fields.Length <
                7 ||
            !fields[0].Equals(
                "DESC",
                StringComparison.Ordinal) ||
            !fields[1].Equals(
                "c",
                StringComparison.Ordinal) ||
            !uint.TryParse(
                fields[2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var id) ||
            id >
                MaximumEntityId)
        {
            return false;
        }

        var path =
            OpenOmsiLanProtocol.NormalizeVehiclePath(
                fields[3]);

        if (path is null)
        {
            return false;
        }

        int? scheme =
            null;

        if (!fields[4].Equals(
                "-",
                StringComparison.Ordinal) &&
            int.TryParse(
                fields[4],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedScheme))
        {
            scheme =
                Math.Clamp(
                    parsedScheme,
                    0,
                    255);
        }

        description =
            new OpenOmsiLanWorldCarDescription(
                id,
                path,
                scheme,
                OpenOmsiLanProtocol.CleanText(
                    fields[5],
                    16),
                OpenOmsiLanProtocol.CleanText(
                    fields[6],
                    64));

        return true;
    }

    private static (
        int X,
        int Y,
        short Z)
        ResolveAnchor(
            IReadOnlyList<OpenOmsiLanWorldCarState> cars)
    {
        if (cars.Count ==
            0)
        {
            return (
                0,
                0,
                0);
        }

        var first =
            cars[0];

        return (
            RoundInt32(
                first.X),
            RoundInt32(
                first.Y),
            (short)Math.Clamp(
                RoundInt32(
                    first.Z),
                short.MinValue,
                short.MaxValue));
    }

    private static bool FitsAnchor(
        OpenOmsiLanWorldCarState car,
        (
            int X,
            int Y,
            short Z
        ) anchor) =>
        Math.Abs(
            car.X -
            anchor.X) <
        2600.0 &&
        Math.Abs(
            car.Y -
            anchor.Y) <
        2600.0 &&
        Math.Abs(
            car.Z -
            anchor.Z) <
        650.0;

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

    private static void WriteCar(
        BitWriter writer,
        OpenOmsiLanWorldCarState car,
        (
            int X,
            int Y,
            short Z
        ) anchor)
    {
        writer.Put(
            Math.Min(
                car.Id,
                MaximumEntityId),
            24);
        writer.PutFixed(
            car.X -
                anchor.X,
            0.01,
            19);
        writer.PutFixed(
            car.Y -
                anchor.Y,
            0.01,
            19);
        writer.PutFixed(
            car.Z -
                anchor.Z,
            0.01,
            17);

        var heading =
            float.IsFinite(
                car.HeadingDegrees)
                ? (
                    (
                        car.HeadingDegrees %
                        360.0f
                    ) +
                    360.0f
                  ) %
                  360.0f
                : 0.0f;

        writer.Put(
            (ulong)(
                Math.Round(
                    heading /
                    360.0 *
                    4096.0) %
                4096),
            12);
        writer.PutFixed(
            car.PitchDegrees,
            0.1,
            8);
        writer.PutFixed(
            car.BankDegrees,
            0.1,
            8);
        writer.PutFixed(
            car.SpeedMetersPerSecond,
            0.05,
            11);
        writer.PutFixed(
            car.SteeringDegrees,
            0.5,
            8);
        writer.Put(
            Math.Min(
                car.Blinker,
                (byte)3),
            2);
        writer.Put(
            car.Brake
                ? 1UL
                : 0UL,
            1);
        writer.Put(
            car.Lights
                ? 1UL
                : 0UL,
            1);
        writer.Put(
            car.AtStation switch
            {
                1 => 1UL,
                -1 => 2UL,
                _ => 0UL
            },
            2);
    }

    private static bool TryReadCar(
        BitReader reader,
        int anchorX,
        int anchorY,
        short anchorZ,
        out OpenOmsiLanWorldCarState car)
    {
        car =
            new OpenOmsiLanWorldCarState(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                0);

        if (!reader.TryGet(
                24,
                out var id) ||
            !reader.TryGetFixed(
                0.01,
                19,
                out var x) ||
            !reader.TryGetFixed(
                0.01,
                19,
                out var y) ||
            !reader.TryGetFixed(
                0.01,
                17,
                out var z) ||
            !reader.TryGet(
                12,
                out var heading) ||
            !reader.TryGetFixed(
                0.1,
                8,
                out var pitch) ||
            !reader.TryGetFixed(
                0.1,
                8,
                out var bank) ||
            !reader.TryGetFixed(
                0.05,
                11,
                out var speed) ||
            !reader.TryGetFixed(
                0.5,
                8,
                out var steering) ||
            !reader.TryGet(
                2,
                out var blinker) ||
            !reader.TryGet(
                1,
                out var brake) ||
            !reader.TryGet(
                1,
                out var lights) ||
            !reader.TryGet(
                2,
                out var atStation))
        {
            return false;
        }

        car =
            new OpenOmsiLanWorldCarState(
                (uint)id,
                anchorX +
                    x,
                anchorY +
                    y,
                anchorZ +
                    z,
                (float)(
                    heading *
                    360.0 /
                    4096.0),
                (float)pitch,
                (float)bank,
                (float)speed,
                (float)steering,
                (byte)blinker,
                brake ==
                    1,
                lights ==
                    1,
                atStation switch
                {
                    1 => (sbyte)1,
                    2 => (sbyte)-1,
                    _ => (sbyte)0
                });

        return true;
    }

    private static bool SkipPerson(
        BitReader reader)
    {
        if (!reader.TryGet(
                24,
                out _) ||
            !reader.TryGet(
                2,
                out var place) ||
            !reader.TryGet(
                2,
                out _))
        {
            return false;
        }

        if (place ==
            0)
        {
            if (!reader.TryGet(
                    19,
                    out _) ||
                !reader.TryGet(
                    19,
                    out _) ||
                !reader.TryGet(
                    17,
                    out _) ||
                !reader.TryGet(
                    8,
                    out _) ||
                !reader.TryGet(
                    6,
                    out _) ||
                !reader.TryGet(
                    1,
                    out var waiting))
            {
                return false;
            }

            return waiting ==
                       0 ||
                   (
                       reader.TryGet(
                           32,
                           out _) &&
                       reader.TryGet(
                           8,
                           out _)
                   );
        }

        if (place is
            1 or 2)
        {
            return reader.TryGet(
                       24,
                       out _) &&
                   reader.TryGet(
                       12,
                       out _) &&
                   reader.TryGet(
                       13,
                       out _) &&
                   reader.TryGet(
                       10,
                       out _) &&
                   reader.TryGet(
                       8,
                       out _) &&
                   reader.TryGet(
                       8,
                       out _);
        }

        return false;
    }

    private sealed class BitWriter
    {
        private readonly List<byte>
            _buffer;
        private ulong _accumulator;
        private int _bits;

        public BitWriter(
            ReadOnlySpan<byte> header)
        {
            _buffer =
                new List<byte>(
                    header.Length +
                    256);

            foreach (var value in
                     header)
            {
                _buffer.Add(
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
                _buffer.Add(
                    (byte)_accumulator);
                _accumulator >>=
                    8;
                _bits -=
                    8;
            }
        }

        public void PutSigned(
            long value,
            int bits)
        {
            var maximum =
                (
                    1L <<
                    (
                        bits -
                        1
                    )
                ) -
                1L;

            Put(
                unchecked(
                    (ulong)Math.Clamp(
                        value,
                        -maximum -
                            1,
                        maximum)),
                bits);
        }

        public void PutFixed(
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

            PutSigned(
                (long)Math.Clamp(
                    quantized,
                    long.MinValue,
                    long.MaxValue),
                bits);
        }

        public byte[] Finish()
        {
            if (_bits >
                0)
            {
                _buffer.Add(
                    (byte)_accumulator);
            }

            return _buffer.ToArray();
        }
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bit;

        public BitReader(
            ReadOnlySpan<byte> data)
        {
            _data =
                data.ToArray();
        }

        public bool TryGet(
            int bits,
            out ulong value)
        {
            value =
                0;

            var end =
                _bit +
                bits;

            if (end >
                _data.Length *
                8)
            {
                return false;
            }

            for (var index = 0;
                 index <
                     bits;
                 index++)
            {
                var bit =
                    _bit +
                    index;

                value |=
                    (ulong)(
                        (
                            _data[
                                bit /
                                8] >>
                            (
                                bit %
                                8
                            )
                        ) &
                        1) <<
                    index;
            }

            _bit =
                end;

            return true;
        }

        public bool TryGetSigned(
            int bits,
            out long value)
        {
            value =
                0;

            if (!TryGet(
                    bits,
                    out var raw))
            {
                return false;
            }

            var sign =
                1L <<
                (
                    bits -
                    1
                );

            value =
                (
                    (long)raw ^
                    sign
                ) -
                sign;

            return true;
        }

        public bool TryGetFixed(
            double step,
            int bits,
            out double value)
        {
            value =
                0.0;

            if (!TryGetSigned(
                    bits,
                    out var raw))
            {
                return false;
            }

            value =
                raw *
                step;

            return true;
        }
    }
}
