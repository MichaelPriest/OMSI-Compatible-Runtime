using System.Globalization;
using System.Text;

namespace OMSICompatible.Multiplayer;

/// <summary>
/// Wire-level constants and text messages used by openOMSI LAN protocol 5.
/// The implementation is intentionally independent from rendering so it can
/// be smoke-tested and later shared by the WinUI launcher and runtime.
/// </summary>
public static class OpenOmsiLanProtocol
{
    public const byte ProtocolVersion = 6;
    public const int DefaultPort = 27015;
    public const int PortRange = 10;
    public const double StateRateHz = 20.0;
    public const double IdleStateRateHz = 5.0;
    public const double HeartbeatSeconds = 1.0;
    public const double InfoEverySeconds = 2.0;
    public const double ClockEverySeconds = 5.0;
    public const double PeerTimeoutSeconds = 15.0;
    public const double JoinTimeoutSeconds = 10.0;
    public const double ReconnectTimeoutSeconds = 60.0;
    public const int MaximumPeers = 32;
    public const int MaximumDatagramBytes = 1400;
    public const int MaximumNameCharacters = 32;
    public const int MaximumFieldCharacters = 64;
    public const int MaximumVehiclePathCharacters = 260;
    public const byte StateMagic = 0xB3;
    public const int StateHeaderBytes = 6;
    public const int MaximumStateBytes = 512;

    public const uint FlagVehicle = 1;
    public const uint FlagEngine = 2;
    public const uint FlagElectrics = 4;
    public const uint FlagHorn = 8;
    public const uint FlagBrake = 16;
    public const uint FlagReverse = 32;
    public const uint FlagFog = 64;
    public const uint FlagKneeling = 128;
    public const uint FlagWipers = 256;
    public const uint FlagStopBrake = 512;

    public const int MaximumDoors = 7;
    public const int MaximumWheels = 15;
    public const int MaximumRearSections = 3;
    public const int MaximumLamps = 127;
    public const int MaximumSwitches = 31;
    public const int MaximumValues = 63;
    public const int MaximumDisplayTexts = 12;
    public const int MaximumFreeTextures = 8;
    public const int MaximumNearFootprints = 28;
    public const double FootprintRadiusMeters = 250.0;

    public static string CleanText(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || maximumCharacters <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumCharacters));

        foreach (var character in value)
        {
            if (builder.Length >= maximumCharacters)
            {
                break;
            }

            builder.Append(
                character == '|' || char.IsControl(character)
                    ? ' '
                    : character);
        }

        return builder.ToString().Trim();
    }

    public static string? NormalizeVehiclePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized =
            value.Trim().Replace('\\', '/');

        if (normalized.Length > MaximumVehiclePathCharacters ||
            normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            normalized.Contains('|') ||
            normalized.Any(char.IsControl))
        {
            return null;
        }

        var parts = normalized.Split('/');

        if (parts.Any(
                part =>
                    string.IsNullOrWhiteSpace(part) ||
                    part.Trim('.', ' ').Length == 0))
        {
            return null;
        }

        var extension =
            Path.GetExtension(parts[^1]);

        return extension.Equals(".bus", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ovh", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    public static bool SequenceIsNewer(ushort candidate, ushort previous) =>
        candidate != previous &&
        unchecked((ushort)(candidate - previous)) < 0x8000;

    public static string SessionHex(ulong session) =>
        (session & 0x0000_FFFF_FFFF_FFFFUL)
            .ToString("X12", CultureInfo.InvariantCulture);

    public static string EncodeWorld(OpenOmsiLanWorld world) =>
        string.Join(
            "|",
            CleanText(world.Map, 260),
            CleanDate(world.Date),
            NormalizeTime(world.TimeSeconds)
                .ToString("0.00", CultureInfo.InvariantCulture),
            CleanText(world.Weather, 260),
            CleanText(world.Season, 16));

    public static OpenOmsiLanWorld DecodeWorld(
        IReadOnlyList<string> fields,
        int first)
    {
        var time =
            TryDouble(Field(fields, first + 2));

        return new OpenOmsiLanWorld(
            CleanText(Field(fields, first), 260),
            CleanDate(Field(fields, first + 1)),
            double.IsFinite(time)
                ? ((time % 86400.0) + 86400.0) % 86400.0
                : 0.0,
            CleanText(Field(fields, first + 3), 260),
            CleanText(Field(fields, first + 4), 16));
    }

    public static string EncodeInfo(OpenOmsiLanPose pose)
    {
        var path =
            NormalizeVehiclePath(pose.VehiclePath) ??
            string.Empty;

        var figure =
            NormalizeHumanPath(pose.FigurePath) ??
            string.Empty;

        var head =
            string.Join(
                "|",
                "INFO",
                pose.Id.ToString(CultureInfo.InvariantCulture),
                CleanText(pose.Name, MaximumNameCharacters),
                path,
                CleanText(pose.Paint, MaximumFieldCharacters),
                CleanText(pose.Line, 16),
                CleanText(pose.Destination, MaximumFieldCharacters),
                ClampFinite(pose.LengthMeters, 0.0f, 60.0f)
                    .ToString("0.00", CultureInfo.InvariantCulture),
                ClampFinite(pose.WidthMeters, 0.0f, 8.0f)
                    .ToString("0.00", CultureInfo.InvariantCulture),
                ClampFinite(pose.BoxOffsetMeters, -40.0f, 40.0f)
                    .ToString("0.00", CultureInfo.InvariantCulture),
                pose.SyncTableHash.ToString("X8", CultureInfo.InvariantCulture),
                CleanText(pose.Tour, MaximumFieldCharacters)) +
            "|";

        var textRoom =
            Math.Max(
                0,
                MaximumDatagramBytes -
                Encoding.UTF8.GetByteCount(
                    head) -
                Encoding.UTF8.GetByteCount(
                    figure) -
                1);

        var texts =
            EncodeTextValues(
                pose.DisplayTexts,
                MaximumDisplayTexts,
                32,
                textRoom);

        var info =
            head +
            texts +
            "|" +
            figure;

        var freeTextureRoom =
            Math.Max(
                0,
                MaximumDatagramBytes -
                Encoding.UTF8.GetByteCount(
                    info) -
                1);

        var freeTextures =
            EncodeTextValues(
                pose.FreeTexturePaths,
                MaximumFreeTextures,
                128,
                freeTextureRoom);

        return info +
               "|" +
               freeTextures;
    }

    public static bool TryDecodeInfo(
        string text,
        out OpenOmsiLanPose pose)
    {
        pose = new OpenOmsiLanPose();

        var fields =
            text.Split('|');

        if (fields.Length < 11 ||
            !fields[0].Equals("INFO", StringComparison.Ordinal))
        {
            return false;
        }

        if (!uint.TryParse(
                fields[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var id))
        {
            return false;
        }

        if (!float.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var length) ||
            !float.TryParse(fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !float.TryParse(fields[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var boxOffset) ||
            !uint.TryParse(fields[10], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var table) ||
            !float.IsFinite(length) ||
            !float.IsFinite(width) ||
            !float.IsFinite(boxOffset))
        {
            return false;
        }

        pose.Id = id;
        pose.Name = CleanText(fields[2], MaximumNameCharacters);
        pose.VehiclePath = NormalizeVehiclePath(fields[3]) ?? string.Empty;
        pose.Paint = CleanText(fields[4], MaximumFieldCharacters);
        pose.Line = CleanText(fields[5], 16);
        pose.Destination = CleanText(fields[6], MaximumFieldCharacters);
        pose.LengthMeters = Math.Clamp(length, 0.0f, 60.0f);
        pose.WidthMeters = Math.Clamp(width, 0.0f, 8.0f);
        pose.BoxOffsetMeters = Math.Clamp(boxOffset, -40.0f, 40.0f);
        pose.SyncTableHash = table;
        pose.Tour = fields.Length > 11
            ? CleanText(fields[11], MaximumFieldCharacters)
            : string.Empty;
        pose.DisplayTexts = fields.Length > 12
            ? DecodeDisplayTexts(fields[12])
            : [];
        pose.FigurePath = fields.Length > 13
            ? NormalizeHumanPath(fields[13]) ?? string.Empty
            : string.Empty;
        pose.FreeTexturePaths = fields.Length > 14
            ? DecodeTextValues(
                fields[14],
                MaximumFreeTextures,
                128)
            : [];

        return true;
    }

    public static string EncodeDisplayTexts(
        IReadOnlyList<string> texts) =>
        EncodeTextValues(
            texts,
            MaximumDisplayTexts,
            32,
            720);

    public static List<string> DecodeDisplayTexts(
        string field) =>
        DecodeTextValues(
            field,
            MaximumDisplayTexts,
            32);

    private static string EncodeTextValues(
        IReadOnlyList<string> values,
        int maximumItems,
        int maximumCharacters,
        int room)
    {
        var result =
            new List<string>(
                Math.Min(
                    values.Count,
                    maximumItems));

        var used =
            0;

        foreach (var value in
                 values.Take(
                     maximumItems))
        {
            var clean =
                new string(
                    (value ??
                     string.Empty)
                        .Where(
                            static character =>
                                !char.IsControl(
                                    character))
                        .Take(
                            maximumCharacters)
                        .ToArray());

            var encoded =
                Convert.ToHexString(
                        Encoding.UTF8.GetBytes(
                            clean))
                    .ToLowerInvariant();

            used +=
                encoded.Length +
                1;

            if (used >
                Math.Min(
                    Math.Max(
                        room,
                        0),
                    720))
            {
                break;
            }

            result.Add(
                encoded);
        }

        return string.Join(
            ",",
            result);
    }

    private static List<string> DecodeTextValues(
        string field,
        int maximumItems,
        int maximumCharacters)
    {
        var result =
            new List<string>();

        if (string.IsNullOrWhiteSpace(
                field))
        {
            return result;
        }

        foreach (var hex in
                 field.Split(',')
                     .Take(
                         maximumItems))
        {
            if ((hex.Length &
                 1) !=
                0)
            {
                continue;
            }

            try
            {
                var text =
                    Encoding.UTF8.GetString(
                        Convert.FromHexString(
                            hex));

                result.Add(
                    new string(
                        text
                            .Where(
                                static character =>
                                    !char.IsControl(
                                        character))
                            .Take(
                                maximumCharacters)
                            .ToArray()));
            }
            catch
            {
            }
        }

        return result;
    }

    private static string? NormalizeHumanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized =
            value.Trim().Replace('\\', '/');

        if (normalized.Length > MaximumVehiclePathCharacters ||
            normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            normalized.Contains('|') ||
            normalized.Any(char.IsControl) ||
            normalized.Split('/').Any(
                part =>
                    string.IsNullOrWhiteSpace(part) ||
                    part.Trim('.', ' ').Length == 0))
        {
            return null;
        }

        return Path.GetExtension(normalized)
            .Equals(".hum", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : null;
    }

    private static string CleanDate(string? value)
    {
        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static double NormalizeTime(double value) =>
        double.IsFinite(value)
            ? ((value % 86400.0) + 86400.0) % 86400.0
            : 0.0;

    private static string Field(
        IReadOnlyList<string> fields,
        int index) =>
        index >= 0 && index < fields.Count
            ? fields[index]
            : string.Empty;

    private static double TryDouble(string text) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : double.NaN;

    private static float ClampFinite(
        float value,
        float minimum,
        float maximum) =>
        float.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : 0.0f;
}

public sealed record OpenOmsiLanWorld(
    string Map,
    string Date,
    double TimeSeconds,
    string Weather,
    string Season)
{
    public static OpenOmsiLanWorld Empty { get; } =
        new(string.Empty, string.Empty, 0.0, string.Empty, string.Empty);
}

public sealed record OpenOmsiLanFootprint(
    double X,
    double Y,
    double Z,
    float HeadingDegrees,
    float LengthMeters,
    float WidthMeters);

public sealed record OpenOmsiLanPartPose(
    double X,
    double Y,
    double Z,
    float HeadingDegrees);

public sealed record OpenOmsiLanAboard(
    uint OwnerId,
    float LocalX,
    float LocalY,
    float LocalZ,
    ushort? SeatIndex);

public sealed record OpenOmsiLanWalker(
    double X,
    double Y,
    double Z,
    float HeadingDegrees,
    float SpeedMetersPerSecond,
    float CourseDegrees,
    bool Seated,
    OpenOmsiLanAboard? Aboard);

public sealed class OpenOmsiLanPose
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string VehiclePath { get; set; } = string.Empty;
    public string Paint { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Tour { get; set; } = string.Empty;
    public List<string> DisplayTexts { get; set; } = [];
    public string FigurePath { get; set; } = string.Empty;
    public List<string> FreeTexturePaths { get; set; } = [];
    public float LengthMeters { get; set; }
    public float WidthMeters { get; set; }
    public float BoxOffsetMeters { get; set; }
    public uint SyncTableHash { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float HeadingDegrees { get; set; }
    public float PitchDegrees { get; set; }
    public float BankDegrees { get; set; }
    public float SpeedKph { get; set; }
    public float SteeringDegrees { get; set; }
    public uint Flags { get; set; }
    public byte HeadLights { get; set; }
    public byte InteriorLights { get; set; }
    public byte Blinker { get; set; }
    public float Rpm { get; set; }
    public float Throttle { get; set; }
    public float Brake { get; set; }
    public uint Passengers { get; set; }
    public List<float> Doors { get; set; } = [];
    public List<float> Suspension { get; set; } = [];
    public List<OpenOmsiLanPartPose> RearSections { get; set; } = [];
    public List<float> Lamps { get; set; } = [];
    public List<float> Switches { get; set; } = [];
    public List<float> Values { get; set; } = [];
    public OpenOmsiLanWalker? Walker { get; set; }
    public uint SentMilliseconds { get; set; }
    public bool RadioKeyed { get; set; }

    public bool HasVehicle =>
        (Flags & OpenOmsiLanProtocol.FlagVehicle) != 0 &&
        !string.IsNullOrWhiteSpace(VehiclePath);

    public OpenOmsiLanPose Clone()
    {
        return new OpenOmsiLanPose
        {
            Id = Id,
            Name = Name,
            VehiclePath = VehiclePath,
            Paint = Paint,
            Line = Line,
            Destination = Destination,
            Tour = Tour,
            DisplayTexts = [.. DisplayTexts],
            FigurePath = FigurePath,
            FreeTexturePaths = [.. FreeTexturePaths],
            LengthMeters = LengthMeters,
            WidthMeters = WidthMeters,
            BoxOffsetMeters = BoxOffsetMeters,
            SyncTableHash = SyncTableHash,
            X = X,
            Y = Y,
            Z = Z,
            HeadingDegrees = HeadingDegrees,
            PitchDegrees = PitchDegrees,
            BankDegrees = BankDegrees,
            SpeedKph = SpeedKph,
            SteeringDegrees = SteeringDegrees,
            Flags = Flags,
            HeadLights = HeadLights,
            InteriorLights = InteriorLights,
            Blinker = Blinker,
            Rpm = Rpm,
            Throttle = Throttle,
            Brake = Brake,
            Passengers = Passengers,
            Doors = [.. Doors],
            Suspension = [.. Suspension],
            RearSections = [.. RearSections],
            Lamps = [.. Lamps],
            Switches = [.. Switches],
            Values = [.. Values],
            Walker = Walker,
            SentMilliseconds = SentMilliseconds,
            RadioKeyed = RadioKeyed
        };
    }
}
