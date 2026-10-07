using System.Globalization;
using OmsiCompat.Core;

namespace OmsiCompat.Vehicles;

public sealed record OmsiVehiclePathPoint(
    double X,
    double Y,
    double Z);

public sealed record OmsiVehiclePathLink(
    int From,
    int To,
    bool OneWay,
    int StepSoundPack,
    double RoomHeight);

public sealed record OmsiVehiclePathNetwork(
    IReadOnlyList<OmsiVehiclePathPoint> Points,
    IReadOnlyList<OmsiVehiclePathLink> Links)
{
    public static OmsiVehiclePathNetwork Empty { get; } =
        new(
            Array.Empty<OmsiVehiclePathPoint>(),
            Array.Empty<OmsiVehiclePathLink>());
}

public static class OmsiVehiclePathReader
{
    public static OmsiVehiclePathNetwork ReadFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return OmsiVehiclePathNetwork.Empty;
        }

        try
        {
            return Parse(
                OmsiText.ReadAllLines(
                    path));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException)
        {
            return OmsiVehiclePathNetwork.Empty;
        }
    }

    internal static OmsiVehiclePathNetwork Parse(
        IReadOnlyList<string> lines)
    {
        var points =
            new List<OmsiVehiclePathPoint>();

        var links =
            new List<OmsiVehiclePathLink>();

        var enabled =
            true;

        var nextStepSound =
            -1;

        var nextRoomHeight =
            2.0;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            var token =
                lines[index]
                    .Trim()
                    .TrimStart(
                        '\uFEFF');

            if (token.Equals(
                    "-<DISABLED>-",
                    StringComparison.OrdinalIgnoreCase))
            {
                enabled =
                    false;
                continue;
            }

            if (token.Equals(
                    "-<ENABLED>-",
                    StringComparison.OrdinalIgnoreCase))
            {
                enabled =
                    true;
                continue;
            }

            if (!enabled ||
                !TryKeyword(
                    token,
                    out var keyword))
            {
                continue;
            }

            switch (keyword)
            {
                case "next_stepsound":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var stepSound))
                    {
                        nextStepSound =
                            stepSound;
                    }

                    break;

                case "next_roomheight":
                    if (TryReadDouble(
                            lines,
                            ref index,
                            out var roomHeight))
                    {
                        nextRoomHeight =
                            roomHeight;
                    }

                    break;

                case "pathpnt":
                    if (TryReadDouble(
                            lines,
                            ref index,
                            out var x) &&
                        TryReadDouble(
                            lines,
                            ref index,
                            out var y) &&
                        TryReadDouble(
                            lines,
                            ref index,
                            out var z))
                    {
                        points.Add(
                            new OmsiVehiclePathPoint(
                                x,
                                y,
                                z));
                    }

                    break;

                case "pathlink":
                case "pathlink_oneway":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var from) &&
                        TryReadInt(
                            lines,
                            ref index,
                            out var to))
                    {
                        links.Add(
                            new OmsiVehiclePathLink(
                                from,
                                to,
                                keyword ==
                                    "pathlink_oneway",
                                nextStepSound,
                                nextRoomHeight));
                    }

                    break;
            }
        }

        return new OmsiVehiclePathNetwork(
            points.ToArray(),
            links.ToArray());
    }

    private static bool TryReadInt(
        IReadOnlyList<string> lines,
        ref int index,
        out int value)
    {
        value =
            0;

        if (!TryReadParameter(
                lines,
                ref index,
                out var parameter))
        {
            return false;
        }

        return int.TryParse(
            parameter,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadDouble(
        IReadOnlyList<string> lines,
        ref int index,
        out double value)
    {
        value =
            0.0;

        if (!TryReadParameter(
                lines,
                ref index,
                out var parameter))
        {
            return false;
        }

        return double.TryParse(
                   parameter,
                   NumberStyles.Float |
                   NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture,
                   out value) &&
               double.IsFinite(
                   value);
    }

    private static bool TryReadParameter(
        IReadOnlyList<string> lines,
        ref int index,
        out string parameter)
    {
        parameter =
            string.Empty;

        for (var candidate =
                 index +
                 1;
             candidate <
                 lines.Count;
             candidate++)
        {
            var trimmed =
                lines[candidate]
                    .Trim();

            if (trimmed.Length ==
                    0 ||
                IsComment(
                    trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith(
                    "[",
                    StringComparison.Ordinal) ||
                trimmed.Equals(
                    "-<DISABLED>-",
                    StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals(
                    "-<ENABLED>-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            index =
                candidate;
            parameter =
                trimmed;
            return true;
        }

        return false;
    }

    private static bool TryKeyword(
        string token,
        out string keyword)
    {
        keyword =
            string.Empty;

        if (token.Length <
                3 ||
            token[0] !=
                '[' ||
            token[^1] !=
                ']')
        {
            return false;
        }

        keyword =
            token[1..^1]
                .Trim()
                .ToLowerInvariant();

        return keyword.Length >
               0;
    }

    private static bool IsComment(
        string value) =>
        value.StartsWith(
            ";",
            StringComparison.Ordinal) ||
        value.StartsWith(
            "#",
            StringComparison.Ordinal) ||
        value.StartsWith(
            "//",
            StringComparison.Ordinal);
}
