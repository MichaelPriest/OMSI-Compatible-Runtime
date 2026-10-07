using System.Globalization;
using OmsiCompat.Core;

namespace OmsiCompat.Vehicles;

public sealed record OmsiHumanDefinition(
    string SourcePath,
    string ModelPath,
    double SeatHeight,
    double FeetDistance,
    double Height,
    IReadOnlyList<double> Links,
    IReadOnlyList<double> WalkParameters,
    string VoicePath,
    double Mass,
    int? Age)
{
    public bool HasCompleteLinks =>
        Links.Count >=
        22;
}

public static class OmsiHumanDefinitionReader
{
    private static readonly double[] DefaultWalkParameters =
    [
        1.4,
        66.0,
        1.0,
        1.0,
        0.0
    ];

    public static OmsiHumanDefinition ReadFile(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        var lines =
            OmsiText.ReadAllLines(
                path);

        var model =
            string.Empty;

        var seatHeight =
            0.0;

        var feetDistance =
            0.0;

        var height =
            0.0;

        var links =
            new List<double>(
                22);

        var walkParameters =
            DefaultWalkParameters
                .ToArray();

        var voice =
            string.Empty;

        var mass =
            0.0;

        int? age =
            null;

        var enabled =
            true;

        for (var index = 0;
             index <
                 lines.Count;
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
                case "model":
                    if (TryReadParameter(
                            lines,
                            ref index,
                            out var modelValue))
                    {
                        model =
                            modelValue
                                .Trim()
                                .Trim('"');
                    }

                    break;

                case "seatheight":
                    TryReadDouble(
                        lines,
                        ref index,
                        out seatHeight);
                    break;

                case "humangeom":
                    TryReadDouble(
                        lines,
                        ref index,
                        out feetDistance);
                    TryReadDouble(
                        lines,
                        ref index,
                        out height);
                    break;

                case "links":
                    links.Clear();

                    for (var link = 0;
                         link <
                             22;
                         link++)
                    {
                        if (!TryReadDouble(
                                lines,
                                ref index,
                                out var value))
                        {
                            break;
                        }

                        links.Add(
                            value);
                    }

                    break;

                case "walk_param":
                    for (var parameter = 0;
                         parameter <
                             walkParameters.Length;
                         parameter++)
                    {
                        if (!TryReadDouble(
                                lines,
                                ref index,
                                out var value))
                        {
                            break;
                        }

                        walkParameters[
                            parameter] =
                            value;
                    }

                    break;

                case "voice":
                    if (TryReadParameter(
                            lines,
                            ref index,
                            out var voiceValue))
                    {
                        voice =
                            voiceValue
                                .Trim()
                                .Trim('"');
                    }

                    break;

                case "mass":
                    TryReadDouble(
                        lines,
                        ref index,
                        out mass);
                    break;

                case "age":
                    if (TryReadInt(
                            lines,
                            ref index,
                            out var parsedAge))
                    {
                        age =
                            parsedAge;
                    }

                    break;
            }
        }

        return new OmsiHumanDefinition(
            Path.GetFullPath(
                path),
            model,
            seatHeight,
            feetDistance,
            height,
            links.ToArray(),
            walkParameters,
            voice,
            mass,
            age);
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
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value) &&
               double.IsFinite(
                   value);
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

    private static bool TryReadParameter(
        IReadOnlyList<string> lines,
        ref int index,
        out string value)
    {
        value =
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
                trimmed.StartsWith(
                    ";",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "#",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "//",
                    StringComparison.Ordinal))
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
            value =
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
}
