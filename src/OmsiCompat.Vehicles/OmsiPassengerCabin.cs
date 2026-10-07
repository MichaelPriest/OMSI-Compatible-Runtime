using System.Globalization;
using OmsiCompat.Core;

namespace OmsiCompat.Vehicles;

public sealed record OmsiPassengerCabinEntry(
    int PathPoint,
    bool NoTicketSale,
    bool WithButton);

public sealed record OmsiPassengerCabinPosition(
    double X,
    double Y,
    double Z,
    double Height,
    double RotationDegrees,
    int FileIndex,
    bool DriverPosition,
    string? SwitchVariable,
    string? TakenVariable);

public sealed record OmsiPassengerCabinInfo(
    IReadOnlyList<OmsiPassengerCabinEntry> Entries,
    IReadOnlyList<int> Exits,
    IReadOnlyList<OmsiPassengerCabinPosition> PassengerPositions,
    IReadOnlyList<OmsiPassengerCabinPosition> DriverPositions,
    int? LinkToNextVehicle,
    int? LinkToPreviousVehicle)
{
    public static OmsiPassengerCabinInfo Empty { get; } =
        new(
            Array.Empty<OmsiPassengerCabinEntry>(),
            Array.Empty<int>(),
            Array.Empty<OmsiPassengerCabinPosition>(),
            Array.Empty<OmsiPassengerCabinPosition>(),
            null,
            null);
}

public static class OmsiPassengerCabinReader
{
    public static OmsiPassengerCabinInfo ReadFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return OmsiPassengerCabinInfo.Empty;
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
            return OmsiPassengerCabinInfo.Empty;
        }
    }

    internal static OmsiPassengerCabinInfo Parse(
        IReadOnlyList<string> lines)
    {
        var entries =
            new List<MutableEntry>();

        var exits =
            new List<int>();

        var passengerPositions =
            new List<OmsiPassengerCabinPosition>();

        var driverPositions =
            new List<OmsiPassengerCabinPosition>();

        int? linkToNext =
            null;

        int? linkToPrevious =
            null;

        var enabled =
            true;

        MutableEntry? lastEntry =
            null;

        var filePositionIndex =
            0;

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

            if (!enabled)
            {
                continue;
            }

            if (token.Equals(
                    "{noticketsale}",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (lastEntry is not null)
                {
                    lastEntry.NoTicketSale =
                        true;
                }

                continue;
            }

            if (token.Equals(
                    "{withbutton}",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (lastEntry is not null)
                {
                    lastEntry.WithButton =
                        true;
                }

                continue;
            }

            if (!TryKeyword(
                    token,
                    out var keyword))
            {
                continue;
            }

            switch (keyword)
            {
                case "entry":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var entryPoint))
                    {
                        lastEntry =
                            new MutableEntry(
                                entryPoint);

                        entries.Add(
                            lastEntry);
                    }

                    break;

                case "exit":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var exitPoint))
                    {
                        exits.Add(
                            exitPoint);
                    }

                    break;

                case "linktonextveh":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var nextPoint))
                    {
                        linkToNext =
                            nextPoint;
                    }

                    break;

                case "linktoprevveh":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var previousPoint))
                    {
                        linkToPrevious =
                            previousPoint;
                    }

                    break;

                case "passpos":
                case "drivpos":
                    if (!TryReadDouble(
                            lines,
                            ref index,
                            out var x) ||
                        !TryReadDouble(
                            lines,
                            ref index,
                            out var y) ||
                        !TryReadDouble(
                            lines,
                            ref index,
                            out var z) ||
                        !TryReadDouble(
                            lines,
                            ref index,
                            out var height) ||
                        !TryReadDouble(
                            lines,
                            ref index,
                            out var rotation))
                    {
                        break;
                    }

                    string? switchVariable =
                        null;

                    string? takenVariable =
                        null;

                    if (keyword ==
                        "passpos")
                    {
                        switchVariable =
                            ReadOptionalPlaceVariable(
                                lines,
                                ref index);

                        if (switchVariable is not null)
                        {
                            takenVariable =
                                ReadOptionalPlaceVariable(
                                    lines,
                                    ref index);
                        }
                    }

                    var position =
                        new OmsiPassengerCabinPosition(
                            x,
                            y,
                            z,
                            height,
                            rotation,
                            filePositionIndex++,
                            keyword ==
                                "drivpos",
                            switchVariable,
                            takenVariable);

                    if (position.DriverPosition)
                    {
                        driverPositions.Add(
                            position);
                    }
                    else
                    {
                        passengerPositions.Add(
                            position);
                    }

                    break;
            }
        }

        return new OmsiPassengerCabinInfo(
            entries
                .Select(
                    static entry =>
                        new OmsiPassengerCabinEntry(
                            entry.PathPoint,
                            entry.NoTicketSale,
                            entry.WithButton))
                .ToArray(),
            exits.ToArray(),
            passengerPositions.ToArray(),
            driverPositions.ToArray(),
            linkToNext,
            linkToPrevious);
    }

    private static string? ReadOptionalPlaceVariable(
        IReadOnlyList<string> lines,
        ref int index)
    {
        var next =
            index +
            1;

        if (next >=
            lines.Count)
        {
            return null;
        }

        var raw =
            lines[next];

        var trimmed =
            raw.Trim();

        if (trimmed.Length ==
                0 ||
            trimmed.StartsWith(
                "[",
                StringComparison.Ordinal) ||
            trimmed.StartsWith(
                "{",
                StringComparison.Ordinal) ||
            trimmed.Equals(
                "-<DISABLED>-",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(
                "-<ENABLED>-",
                StringComparison.OrdinalIgnoreCase) ||
            IsComment(
                trimmed))
        {
            return null;
        }

        index =
            next;

        return trimmed;
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

    private sealed class MutableEntry(
        int pathPoint)
    {
        public int PathPoint { get; } =
            pathPoint;

        public bool NoTicketSale { get; set; }

        public bool WithButton { get; set; }
    }
}
